"""Read PC OVA descriptor names from ActorScriptAudit JSON; never execute code."""
import argparse
import json
from pathlib import Path
import struct

STRIDES = {37: 12, 41: 52, 44: 8,
           **{kind: 4 for kind in (32, 33, 34, 38, 39, 40, 42, 43, 45, 46, 132)}}
FACE_NAMES = ("expr", "blink", "speak", "bouche", "module_tete", "module_pup")


def read_variables(payload):
    def require(offset, size):
        if offset < 0 or size < 0 or offset + size > len(payload):
            raise ValueError("OVA field outside retained payload")

    def word(offset):
        require(offset, 4)
        return struct.unpack_from("<I", payload, offset)[0]

    descriptor_bytes = word(0)
    if descriptor_bytes % 12:
        raise ValueError("OVA descriptor byte count is not divisible by 12")
    count = descriptor_bytes // 12
    names_offset = 8 + descriptor_bytes
    values_offset = names_offset + count * 30
    require(4, descriptor_bytes)
    require(names_offset - 4, 4 + count * 30)
    values_present = word(values_offset)
    position = values_offset + 4
    variables = []
    for index in range(count):
        descriptor_offset = 4 + index * 12
        first_word, packed_length, kind, flags = struct.unpack_from("<IIHH", payload, descriptor_offset)
        name_bytes = payload[names_offset + index * 30:names_offset + (index + 1) * 30]
        name = name_bytes.split(b"\0", 1)[0].decode("latin1")
        length = packed_length & 0x3FFFFFFF
        row = {"index": index, "name": name, "descriptorOffset": descriptor_offset,
               "descriptorFirstWord": f"{first_word:08X}", "type": kind, "flags": flags,
               "packedLength": f"{packed_length:08X}", "elementCount": length}
        if values_present:
            if kind not in STRIDES:
                raise ValueError(f"Unknown OVA value stride for type {kind}")
            prefix_size = (packed_length >> 30) * 4 if packed_length != 1 else 0
            require(position, prefix_size)
            position += prefix_size
            size = length * STRIDES[kind]
            require(position, size)
            # Follow the existing loader's consumption only. Descriptor order
            # has not been proven to map sequential serialized values to names.
            position += size
        variables.append(row)
    # Match the loader's five fixed and optional counted code references.
    require(position, 20)
    position += 20
    if position < len(payload):
        references = word(position)
        position += 4
        require(position, references * 4)
        position += references * 4
    if position != len(payload):
        raise ValueError("OVA cursor mismatch")
    return variables


def inspect(report):
    matches = []
    code_sizes = []
    for record in report["Records"]:
        payload = bytes.fromhex(record["Bytes"])
        if record["Kind"] == "t":
            variables = read_variables(payload)
            matches.extend({"resourceKey": record["Key"], **variable} for variable in variables
                           if any(part in variable["name"].lower() for part in FACE_NAMES))
        elif record["Kind"] == "r":
            code_sizes.append({"key": record["Key"], "retainedBytes": len(payload),
                               "headerHex": payload[:12].hex().upper()})
    return {"actor": report["Actor"], "brainKey": report["BrainKey"],
            "missingReferences": report["Missing"], "facialNameCandidates": matches,
            "codeResources": code_sizes,
            "limits": "Names are source metadata hints. Default-value assignment order and descriptor first-word semantics are unverified. No variable-to-native-writer or event-to-morph relationship is established."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    result = inspect(json.loads(arguments.report.read_text(encoding="utf-8-sig")))
    arguments.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(f"{result['actor']}: {len(result['facialNameCandidates'])} facial-name candidates, "
          f"{len(result['codeResources'])} code resources")


if __name__ == "__main__":
    main()
