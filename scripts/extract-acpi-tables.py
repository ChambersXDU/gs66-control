#!/usr/bin/env python3
"""Carve checksum-valid DSDT/SSDT candidates from UEFIExtract body.bin files.

Offline only. Candidate presence does not establish runtime installation.
Offsets belong to decompressed section bodies, never to original flash bytes.
"""

import argparse
import hashlib
import json
from pathlib import Path
import struct


def candidates(data):
    for signature in (b"DSDT", b"SSDT"):
        position = 0
        while True:
            offset = data.find(signature, position)
            if offset < 0:
                break
            position = offset + 4
            if offset + 36 > len(data):
                continue
            length = struct.unpack_from("<I", data, offset + 4)[0]
            if not 36 <= length <= len(data) - offset:
                continue
            table = data[offset:offset + length]
            if table[8] not in (1, 2) or sum(table) % 256:
                continue
            yield offset, table


def extract(root, output):
    root = root.resolve(strict=True)
    bodies = sorted(path for path in root.rglob("body.bin") if path.is_file() and not path.is_symlink())
    if not bodies:
        raise ValueError("No UEFIExtract body.bin files found")
    # Refuse existing destinations, including symlinks; never overwrite evidence.
    output.mkdir(parents=False, exist_ok=False)
    tables = {}
    for path in bodies:
        data = path.read_bytes()
        for offset, table in candidates(data):
            digest = hashlib.sha256(table).hexdigest()
            record = tables.setdefault(digest, {
                "sha256": digest,
                "file": f"{table[:4].decode()}-{digest[:12]}.aml",
                "bytes": len(table),
                "oemId": table[10:16].decode("ascii", errors="replace"),
                "oemTableId": table[16:24].decode("ascii", errors="replace"),
                "checksumValid": True,
                "locations": [],
            })
            record["locations"].append({
                "section": str(path.relative_to(root)),
                "sectionBytes": len(data),
                "sectionSha256": hashlib.sha256(data).hexdigest(),
                "offset": offset,
                "offsetBasis": "UEFIExtract decompressed body; original flash offset unknown",
            })
            if len(record["locations"]) == 1:
                (output / record["file"]).write_bytes(table)
    for record in tables.values():
        record["locations"].sort(key=lambda item: (item["sectionBytes"], item["section"], item["offset"]))
    index = {
        "schemaVersion": 1,
        "mode": "offline-static",
        "bodyFilesScanned": len(bodies),
        "limitations": [
            "Only checksum-valid DSDT/SSDT candidates in extracted body.bin files are included.",
            "Firmware may include alternative tables that are never installed at runtime.",
            "Decompression breaks correspondence with original flash byte offsets.",
        ],
        "tables": sorted(tables.values(), key=lambda item: item["file"]),
    }
    (output / "index.json").write_text(json.dumps(index, indent=2) + "\n", encoding="utf-8")
    return index


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("extracted_root", type=Path)
    parser.add_argument("new_output_directory", type=Path)
    args = parser.parse_args()
    index = extract(args.extracted_root, args.new_output_directory)
    print(f"Scanned {index['bodyFilesScanned']} bodies; extracted {len(index['tables'])} unique ACPI candidates.")


if __name__ == "__main__":
    main()
