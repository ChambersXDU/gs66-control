"""Offline parser checks with generated tables; no firmware or hardware needed."""
import importlib.util
from pathlib import Path
import struct
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location("extract_acpi", Path(__file__).parents[1] / "scripts/extract-acpi-tables.py")
extract_acpi = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(extract_acpi)


def table(signature=b"DSDT", payload=b"\x00\x01\x02"):
    data = bytearray(struct.pack("<4sIBB6s8sI4sI", signature, 36 + len(payload), 2, 0,
                                 b"TEST  ", b"FIXTURE ", 1, b"TEST", 1) + payload)
    data[9] = -sum(data) % 256
    return bytes(data)


class ExtractionTests(unittest.TestCase):
    def test_rejects_bad_checksum_and_out_of_bounds_header(self):
        corrupt = bytearray(table())
        corrupt[-1] ^= 1
        oversized = bytearray(table())
        struct.pack_into("<I", oversized, 4, 100000)
        self.assertEqual([], list(extract_acpi.candidates(bytes(corrupt))))
        self.assertEqual([], list(extract_acpi.candidates(bytes(oversized))))
        self.assertEqual([], list(extract_acpi.candidates(b"DSDT\x00")))

    def test_embedded_tables_preserve_offsets_and_deduplicate_by_content(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "sections"
            (root / "parent").mkdir(parents=True)
            (root / "leaf").mkdir()
            data = table()
            (root / "parent/body.bin").write_bytes(b"prefix" + data + b"suffix")
            (root / "leaf/body.bin").write_bytes(data)
            output = Path(directory) / "acpi"
            index = extract_acpi.extract(root, output)
            self.assertEqual(1, len(index["tables"]))
            record = index["tables"][0]
            self.assertEqual([0, 6], [location["offset"] for location in record["locations"]])
            self.assertEqual(data, (output / record["file"]).read_bytes())
            with self.assertRaises(FileExistsError):
                extract_acpi.extract(root, output)


if __name__ == "__main__":
    unittest.main()
