"""Build a static-analysis PE carrier from captured module sections. Never launch it.

Usage: python extract_module_image.py saved.dmp --output analysis.exe
This copies only the selected module, not arbitrary heap or stack regions.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct

from minidump_reader import MinidumpReader


def extract(source, output, module_name="BGE.exe"):
    if Path(source).resolve() == Path(output).resolve():
        raise ValueError("Output must not replace the source dump")
    with MinidumpReader(source) as reader:
        matches = [module for module in reader.modules
                   if module.name.replace("\\", "/").rsplit("/", 1)[-1].lower() == module_name.lower()]
        if len(matches) != 1:
            raise ValueError("Expected exactly one matching captured module")
        module, = matches
        header = reader.memory(module.address, 4096)
        if header is None or header[:2] != b"MZ":
            raise ValueError("Missing captured PE header")
        pe_offset, = struct.unpack_from("<I", header, 60)
        if pe_offset > len(header) - 24 or header[pe_offset:pe_offset + 4] != b"PE\0\0":
            raise ValueError("Invalid PE header location")
        section_count, = struct.unpack_from("<H", header, pe_offset + 6)
        optional_size, = struct.unpack_from("<H", header, pe_offset + 20)
        optional_offset = pe_offset + 24
        section_offset = optional_offset + optional_size
        if section_offset + section_count * 40 > len(header):
            raise ValueError("Section table outside captured header")
        magic, = struct.unpack_from("<H", header, optional_offset)
        if magic != 0x10B or optional_size < 136:
            raise ValueError("Only PE32 captured modules are supported")
        file_alignment, = struct.unpack_from("<I", header, optional_offset + 36)
        if file_alignment < 512 or file_alignment > 65536 or file_alignment & (file_alignment - 1):
            raise ValueError("Unsupported PE file alignment")
        image = bytearray(module.size)
        image[:len(header)] = header
        sections = []
        for index in range(section_count):
            offset = section_offset + index * 40
            virtual_size, address = struct.unpack_from("<II", header, offset + 8)
            aligned_size = (virtual_size + file_alignment - 1) & ~(file_alignment - 1)
            if address < len(header) or address + aligned_size > len(image):
                raise ValueError("Section outside captured module")
            payload = reader.memory(module.address + address, virtual_size)
            if payload is None:
                raise ValueError(f"Missing captured section at RVA {address:X}")
            image[address:address + virtual_size] = payload
            struct.pack_into("<II", image, offset + 16, aligned_size, address)
            sections.append({"name": header[offset:offset + 8].rstrip(b"\0").decode("ascii"),
                             "rva": f"{address:08X}", "capturedBytes": virtual_size,
                             "sha256": hashlib.sha256(payload).hexdigest()})
        struct.pack_into("<I", image, optional_offset + 64, 0)
        struct.pack_into("<II", image, optional_offset + 96 + 4 * 8, 0, 0)
        report = {"sourceDump": str(source), "sourceDumpSize": Path(source).stat().st_size,
                  "moduleBase": f"{module.address:08X}", "moduleSize": module.size,
                  "sections": sections, "outputSha256": hashlib.sha256(image).hexdigest(),
                  "limits": "Derived captured-memory PE carrier. Section disk offsets remapped to RVAs; checksum and certificate directory cleared. Original entry point and import state may be unusable. Static analysis only; never launch or distribute."}
        Path(output).write_bytes(image)
        Path(output).with_suffix(".json").write_text(json.dumps(report, indent=2), encoding="utf-8")
        return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("dump", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--module", default="BGE.exe")
    arguments = parser.parse_args()
    print(json.dumps(extract(arguments.dump, arguments.output, arguments.module), indent=2))
