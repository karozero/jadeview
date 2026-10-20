"""Classify literal type-53 cinematic send candidates from an archive census.

This decodes a narrowly observed 19-row source template. It does not execute
the script VM or claim that a message reaches a live actor/controller.
"""
import argparse
import json
from collections import Counter
from pathlib import Path
import struct

from audit_pc_cinematic_messages import decode_template, describe_track_context
from audit_pc_facial_message import inspect_message


def inspect_census(path):
    census = json.loads(path.read_text(encoding="utf-8-sig"))
    matches = []
    for container in census["Containers"]:
        for event in container["LiteralMessageTemplates"]:
            decoded = decode_template(bytes.fromhex(event["Bytes"]))
            if decoded is None:
                continue
            message_bytes = bytes.fromhex(decoded["messageBytes"])
            if struct.unpack_from("<I", message_bytes, 0x30)[0] != 53:
                continue
            event_context = {
                "Bone": event["Bone"],
                "ChannelFlags": event["ChannelFlags"],
                "Flags": event["Flags"],
                "Target": event["Target"],
            }
            matches.append({
                "container": container["Container"],
                "clip": event["Clip"],
                "animationWorldKey": event["WorldKey"],
                "animationWorld": event["WorldName"],
                "recordSha256": event["RecordSha256"],
                "recordOffset": event["RecordOffset"],
                "sourceOffset": event["SourceOffset"],
                "time": event["Time"],
                "duration": event["Duration"],
                "trackTarget": event["Target"],
                "targetRecordKind": event["TargetRecordKind"],
                "targetRecordName": event["TargetRecordName"],
                "targetHasBrain": event["TargetHasBrain"],
                "clipOwners": event["ClipOwners"],
                "clipLinks": event["ClipLinks"],
                **decoded,
                "sourceContext": describe_track_context(decoded, event_context),
                "message": inspect_message(message_bytes),
            })

    containers = Counter(match["container"] for match in matches)
    clips = {(match["container"], match["clip"]) for match in matches}
    targets = Counter(match["trackTarget"] for match in matches)
    return {
        "archive": census["Archive"],
        "containersScanned": census["ContainersScanned"],
        "literalType53Occurrences": len(matches),
        "containersWithMatches": len(containers),
        "containerClipPairs": len(clips),
        "distinctTrackTargets": len(targets),
        "targets": [{"key": key, "occurrences": count}
                    for key, count in targets.most_common()],
        "matches": matches,
        "limits": (
            "Static archive candidates matching the observed literal template and message type 53. "
            "Target names are same-container key matches, not proven runtime recipients. "
            "No VM execution, message delivery, dialogue/audio association, shared-resource "
            "deduplication or non-FF resource completeness is inferred."
        ),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("census", type=Path, help="archive-cinematic-census.json")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    report = inspect_census(args.census)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(
        f"{report['literalType53Occurrences']} type-53 candidates across "
        f"{report['containersWithMatches']} containers, "
        f"{report['containerClipPairs']} container/clip pairs, "
        f"{report['distinctTrackTargets']} track targets"
    )


if __name__ == "__main__":
    main()
