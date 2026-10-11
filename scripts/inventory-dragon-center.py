#!/usr/bin/env python3
"""Offline inventory of an extracted vendor package. Never loads target code.
Requires Python 3.11+ and pefile, dnfile; binaries remain outside the repository.
"""
import argparse
import hashlib
import json
from pathlib import Path
import pefile
import dnfile


def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as source:
        for block in iter(lambda: source.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def version(row):
    return '.'.join(str(getattr(row, x)) for x in ('MajorVersion', 'MinorVersion', 'BuildNumber', 'RevisionNumber'))


def inspect(path, root):
    result = {'path': path.relative_to(root).as_posix(), 'bytes': path.stat().st_size, 'sha256': digest(path)}
    with path.open('rb') as source:
        if source.read(2) != b'MZ':
            result['kind'] = 'data'
            return result
    try:
        pe = pefile.PE(str(path), fast_load=False)
        result.update(kind='PE', machine=hex(pe.FILE_HEADER.Machine), image_base=hex(pe.OPTIONAL_HEADER.ImageBase),
                      entry_rva=hex(pe.OPTIONAL_HEADER.AddressOfEntryPoint))
        result['imports'] = [{'dll': item.dll.decode(errors='replace'),
                              'symbols': [entry.name.decode(errors='replace') if entry.name else f'ordinal:{entry.ordinal}' for entry in item.imports]}
                             for item in getattr(pe, 'DIRECTORY_ENTRY_IMPORT', [])]
        result['exports'] = [{'name': entry.name.decode(errors='replace') if entry.name else None,
                              'ordinal': entry.ordinal, 'rva': hex(entry.address)}
                             for entry in getattr(getattr(pe, 'DIRECTORY_ENTRY_EXPORT', None), 'symbols', [])]
        versions = {}
        for group in getattr(pe, 'FileInfo', []):
            for entry in group:
                for table in getattr(entry, 'StringTable', []):
                    versions.update({k.decode(errors='replace'): v.decode(errors='replace') for k, v in table.entries.items()})
        result['version_resources'] = versions
        cli = pe.OPTIONAL_HEADER.DATA_DIRECTORY[14]
        result['cli_directory'] = {'rva': hex(cli.VirtualAddress), 'bytes': cli.Size}
        result['kind'] = 'managed-PE' if cli.VirtualAddress else 'native-PE'
        pe.close()
        if cli.VirtualAddress:
            managed = dnfile.dnPE(str(path), clr_lazy_load=True)
            tables = managed.net.mdtables
            assemblies = getattr(tables, 'Assembly', None)
            result['assemblies'] = [{'name': str(row.Name), 'version': version(row)} for row in assemblies.rows] if assemblies else []
            references = getattr(tables, 'AssemblyRef', None)
            result['assembly_references'] = [{'name': str(row.Name), 'version': version(row)} for row in references.rows] if references else []
            result['clr_flags'] = hex(managed.net.struct.Flags)
            managed.close()
    except Exception as error:
        result['parse_error'] = str(error)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    root = args.root.resolve()
    output = args.output.resolve()
    files = [inspect(p, root) for p in sorted(root.rglob('*')) if p.is_file() and not p.is_symlink() and p.resolve() != output]
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({'schema': 1, 'mode': 'offline-static-no-target-execution', 'files': files}, ensure_ascii=False, indent=2) + '\n')
    print(f'Inventoried {len(files)} files -> {output}')


if __name__ == '__main__':
    main()
