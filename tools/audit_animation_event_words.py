"""Inspect retained PC event words without executing or naming VM handlers.

Input: *-animation-events.json produced by tests/ExportVerification.
Tag bit fields follow the candidate VM storage layout; source IDs are not
runtime dispatch indices. Operand float views are bit reinterpretations only.
"""
import argparse
from collections import Counter
import json
from pathlib import Path
import struct


def inspect(report, selector):
    selector_counts = Counter()
    matches = []
    for event in report["Events"]:
        if event["Kind"] != "word-pair-list":
            continue
        payload = bytes.fromhex(event["Bytes"])
        if len(payload) < 4:
            raise ValueError("Missing event row count")
        count, = struct.unpack_from("<I", payload)
        if len(payload) != 4 + count * 8:
            raise ValueError("Event row count does not match retained bytes")
        rows = list(struct.iter_unpack("<II", payload[4:]))
        for row_index, (operand, tag) in enumerate(rows):
            category = tag >> 24
            source_selector = tag & 0xFFFF
            if category != 3:
                continue
            selector_counts[source_selector] += 1
            if source_selector != selector:
                continue
            preceding = [
                {"operandHex": f"{value:08X}", "tagHex": f"{kind:08X}"}
                for value, kind in rows[max(0, row_index - 4):row_index]
            ]
            matches.append({
                "clip": event["Clip"], "time": event["Time"],
                "trackHeader": event["HeaderWord"], "rowIndex": row_index,
                "sourceOffset": event["SourceOffset"] + 4 + row_index * 8,
                "operandHex": f"{operand:08X}", "tagHex": f"{tag:08X}",
                "precedingRows": preceding,
            })
    return {
        "actor": report["Actor"], "decodedClips": report["Decoded"],
        "decodeFailures": report["Failures"],
        "limits": "Raw source-tag census; no runtime remap, argument evaluation or facial attribution.",
        "category3SelectorCounts": dict(sorted(selector_counts.items())),
        "selectedSourceSelector": selector, "matches": matches,
    }


def inspect_folder(folder, selector):
    """Read manifest-listed records only; preserve each source occurrence."""
    manifest = json.loads((folder / "index.json").read_text(encoding="utf-8-sig"))
    counts = Counter()
    matches = []
    failures = []
    decoded = 0
    for record in manifest["Records"]:
        filename = record["File"]
        if Path(filename).name != filename:
            raise ValueError("Manifest event filename must be local to its folder")
        report = json.loads((folder / filename).read_text(encoding="utf-8-sig"))
        result = inspect(report, selector)
        counts.update(result["category3SelectorCounts"])
        decoded += result["decodedClips"]
        failures.extend({"file": filename, "failure": failure} for failure in result["decodeFailures"])
        for match in result["matches"]:
            matches.append({"file": filename, "container": record["Container"],
                            "recordOffset": record["Offset"], "sha256": record["Sha256"], **match})
    return {"actor": manifest["Container"], "decodedClips": decoded,
            "decodeFailures": failures, "recordCount": len(manifest["Records"]),
            "limits": "Manifest-listed source occurrences, including fixed resources; no argument execution or deduplication.",
            "category3SelectorCounts": dict(sorted(counts.items())),
            "selectedSourceSelector": selector, "matches": matches}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report", type=Path)
    parser.add_argument("--selector", type=lambda value: int(value, 0), default=0x2F47)
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    if arguments.report.is_dir():
        result = inspect_folder(arguments.report, arguments.selector)
    else:
        report = json.loads(arguments.report.read_text(encoding="utf-8-sig"))
        result = inspect(report, arguments.selector)
    arguments.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(f"{result['actor']}: {len(result['matches'])} matching rows; {result['category3SelectorCounts']}")


if __name__ == "__main__":
    main()
