"""Inventory literal cinematic messages from the archive-wide FF census.

This only recognizes the bounded 19-row 8056 template already decoded by
audit_pc_cinematic_messages.py. It does not execute animation tracks or scripts.
"""
import argparse
import json
import struct
from collections import Counter, defaultdict
from pathlib import Path

from audit_pc_cinematic_messages import decode_template

MESSAGE_TYPE_OFFSET = 0x30
LINE_SELECTOR_OFFSET = 0x28
SECONDARY_INTEGER_OFFSET = 0x2C
DIALOGUE_MESSAGE_TYPE = 30


def decode_message(event):
    decoded = decode_template(bytes.fromhex(event["Bytes"]))
    if decoded is None:
        return None

    return bytes.fromhex(decoded["messageBytes"])


def message_type(message):
    return struct.unpack_from("<I", message, MESSAGE_TYPE_OFFSET)[0]


def dialogue_event(container, event, message, target_name):
    return {
        "container": container["Container"],
        "world": event["WorldName"],
        "clip": event["Clip"],
        "time": event["Time"],
        "trackTarget": event["Target"],
        "targetName": target_name,
        "lineSelector": struct.unpack_from("<i", message, LINE_SELECTOR_OFFSET)[0],
        "secondaryInteger": struct.unpack_from("<i", message, SECONDARY_INTEGER_OFFSET)[0],
    }


def inspect_census(path):
    census = json.loads(path.read_text(encoding="utf-8-sig"))
    type_counts = Counter()
    targets = defaultdict(Counter)
    dialogue_events = []

    for container in census["Containers"]:
        for event in container["LiteralMessageTemplates"]:
            message = decode_message(event)
            if message is None:
                continue

            type_id = message_type(message)
            type_counts[type_id] += 1
            target_name = event["TargetRecordName"] or "<unresolved>"
            targets[type_id][target_name] += 1

            if type_id == DIALOGUE_MESSAGE_TYPE:
                dialogue_events.append(dialogue_event(container, event, message, target_name))

    return {
        "archive": census["Archive"],
        "containersScanned": census["ContainersScanned"],
        "recognizedLiteralMessages": sum(type_counts.values()),
        "messageTypes": [
            {
                "id": message_type,
                "occurrences": count,
                "targets": [
                    {"name": name, "occurrences": target_count}
                    for name, target_count in targets[message_type].most_common()
                ],
            }
            for message_type, count in sorted(type_counts.items())
        ],
        "dialogueType30Events": dialogue_events,
        "limits": (
            "Static inventory of literal messages matching one observed 19-row "
            "selector-8056 template. It does not cover dynamic sends, prove track "
            "execution or delivery, decode localized text/audio, or establish that "
            "other message IDs are facial/dialogue events."
        ),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("census", type=Path, help="archive-cinematic-census.json")
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()

    report = inspect_census(arguments.census)
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"{report['recognizedLiteralMessages']} literal messages; "
          f"{len(report['dialogueType30Events'])} type-30 dialogue events")


if __name__ == "__main__":
    main()
