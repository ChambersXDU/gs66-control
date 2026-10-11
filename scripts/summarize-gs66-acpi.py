#!/usr/bin/env python3
"""Extract SCM0 WDG/EC/index evidence from an iasl-decompiled GS66 DSDT.

This is a bounded reader for the studied iasl output, not a general ASL parser.
It emits static mappings, not a hardware compatibility or control profile.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import uuid


def block(source, pattern, start=0):
    match = re.search(pattern, source[start:])
    if not match:
        raise ValueError(f"Missing expected declaration: {pattern}")
    begin = start + match.start()
    opening = source.index("{", start + match.end())
    # iasl comments may include arbitrary punctuation; preserve byte positions.
    clean = re.sub(r"/\*.*?\*/|//[^\n]*", lambda item: " " * len(item.group()), source, flags=re.S)
    depth = 1
    closing = opening + 1
    while depth:
        if closing >= len(clean):
            raise ValueError("Unterminated declaration")
        depth += (clean[closing] == "{") - (clean[closing] == "}")
        closing += 1
    return begin, closing, source[opening + 1:closing - 1]


def number(value):
    return {"Zero": 0, "One": 1}.get(value) if value in ("Zero", "One") else int(value, 0)


def summarize(path):
    raw = path.read_bytes()
    source = raw.decode("utf-8")
    scm_begin, scm_end, _ = block(source, r"Device \(SCM0\)")
    scm = source[scm_begin:scm_end]
    wdg_begin, wdg_end, wdg = block(scm, r"Name \(_WDG, Buffer \(0xC8\)")
    wdg = re.sub(r"/\*.*?\*/|//[^\n]*", "", wdg, flags=re.S)
    descriptor = bytes(int(value, 16) for value in re.findall(r"0x([0-9A-Fa-f]{2})\b", wdg))
    if len(descriptor) != 200:
        raise ValueError("Expected exactly 10 WDG descriptors")
    descriptors = []
    for offset in range(0, len(descriptor), 20):
        item = descriptor[offset:offset + 20]
        descriptors.append({
            "guid": str(uuid.UUID(bytes_le=item[:16])),
            "objectIdHex": item[16:18].hex(),
            "objectId": item[16:18].decode("ascii") if not item[19] & 8 else None,
            "instanceCount": item[18], "flags": item[19],
        })
    field_begin, field_end, field = block(scm, r"Field \(EC, ByteAcc, NoLock, Preserve\)")
    bit = 0
    fields = {}
    for match in re.finditer(r"Offset \((0x[0-9A-Fa-f]+)\)|\b([A-Z0-9_]+),\s+(\d+)", field):
        if match[1]:
            bit = int(match[1], 16) * 8
        else:
            width = int(match[3])
            fields[match[2]] = {"byteOffset": bit // 8, "bitOffset": bit % 8, "bits": width}
            bit += width
    constants = {name: number(value) for name, value in re.findall(
        r"Name \((TDVR), (Zero|One|0x[0-9A-Fa-f]+)\)", scm)}
    if "TDVR" not in constants:
        raise ValueError("Missing TDVR constant")
    methods = []
    excerpts = []

    def add_excerpt(name, begin, end):
        absolute_begin, absolute_end = scm_begin + begin, scm_begin + end
        excerpts.append({
            "name": name,
            "firstLine": source[:absolute_begin].count("\n") + 1,
            "lastLine": source[:absolute_end].count("\n") + 1,
            "text": source[absolute_begin:absolute_end],
        })

    add_excerpt("SCM0 header", 0, wdg_begin)
    add_excerpt("_WDG", wdg_begin, wdg_end)
    add_excerpt("EmbeddedControl region / Field", wdg_end, field_end)
    for prefix in ("WQ", "WS"):
        for suffix in (f"A{letter}" for letter in "ABCDEFGHI"):
            name = prefix + suffix
            begin, end, body = block(scm, rf"Method \({name},")
            branches = []
            for match in re.finditer(r"If \(\(Arg0 == (Zero|One|0x[0-9A-Fa-f]+)\)\)", body):
                _, _, case = block(body, re.escape(match.group()), match.start())
                read = re.search(r"Local\d+ = ([A-Z0-9_]+)", case)
                store = re.search(r"\b([A-Z0-9_]+) = Arg1", case)
                target = (read or store)[1] if read or store else None
                if target not in fields:
                    raise ValueError(f"Unresolved field in {name}: {target}")
                writes = re.findall(r"\b(T[A-Z0-9_]+) = (?!Arg1\b)([A-Z0-9_]+)", case)
                branches.append({
                    "index": number(match[1]), "field": target, **fields[target],
                    "querySideEffects": [{"field": left, "valueExpression": right} for left, right in writes],
                })
            methods.append({"name": name, "branches": branches})
            if name in ("WQAF", "WQAG", "WQAH", "WQAI", "WSAF", "WSAG", "WSAH"):
                add_excerpt(name, begin, end)
    return {
        "schemaVersion": 1, "mode": "offline-static",
        "dslFile": path.name, "dslSha256": hashlib.sha256(raw).hexdigest(),
        "device": "\\_SB.PCI0.LPCB.EC.SCM0",
        "region": {"addressSpace": "EmbeddedControl", "offset": 0, "bytes": 256},
        "namedConstants": constants,
        "descriptors": descriptors, "fields": fields, "methods": methods, "excerpts": excerpts,
        "limitations": [
            "Only the studied SCM0 declaration and simple constant-index branches are parsed.",
            "Indices here are ACPI Arg0 values, not independently verified Windows enumeration order.",
            "Method bodies are decompiled static evidence; no firmware method is invoked.",
            "No sensor units, defaults, physical fan routing or live compatibility are inferred.",
        ],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("dsl", type=Path)
    parser.add_argument("new_output_json", type=Path)
    args = parser.parse_args()
    result = summarize(args.dsl)
    with args.new_output_json.open("x", encoding="utf-8") as output:
        json.dump(result, output, ensure_ascii=False, indent=2)
        output.write("\n")
    print(f"Recovered {len(result['descriptors'])} descriptors and {len(result['methods'])} method maps.")


if __name__ == "__main__":
    main()
