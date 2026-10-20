"""Decode a bounded literal cinematic send template, without executing its VM.

Only the observed reference/context + type + two references + two literal
vectors + two floats + two integers + send + terminator shape is supported.
Context flags and track targets are retained, never interpreted as dispatch.
"""
import argparse
import json
from pathlib import Path
import struct

from audit_pc_facial_message import inspect_message


def decode_template(payload):
    if len(payload) != 4 + 19 * 8 or struct.unpack_from('<I', payload)[0] != 19:
        return None
    rows = list(struct.iter_unpack('<II', payload[4:]))
    literal_tags = {0: 0x01000028, 2: 0x01000021, 3: 0x01000028, 4: 0x01000028,
                    **{index: 0x01000022 for index in (5, 6, 7, 9, 10, 11, 13, 14)},
                    15: 0x01000021, 16: 0x01000021}
    if any(rows[index][1] != tag for index, tag in literal_tags.items()):
        return None
    for index, category, selector in ((1, 2, 2054), (8, 3, 6000), (12, 3, 6000), (17, 3, 8056)):
        operand, tag = rows[index]
        if operand != 0 or tag >> 24 != category or tag & 0xFFFF != selector:
            return None
    if rows[18][0] != 0 or rows[18][1] >> 24 != 31:
        return None
    # 6000 pops z/y/x and pushes x/y/z. 8056 pops in reverse argument order:
    # int2, int1, float2, float1, vector2, vector1, ref2, ref1, type.
    words = [rows[index][0] for index in (3, 4, 5, 6, 7, 9, 10, 11, 13, 14, 15, 16, 2)]
    return {"messageBytes": struct.pack('<13I', *words).hex().upper(),
            "destinationSourceReference": f'{rows[0][0]:08X}',
            "contextTag": f'{rows[1][1]:08X}', "sendTag": f'{rows[17][1]:08X}',
            "terminatorTag": f'{rows[18][1]:08X}'}


def describe_track_context(decoded, event):
    """Static binding rule from AF0970 + 45FE10, not a delivery assertion."""
    null_prefix = decoded['destinationSourceReference'] == '00000000'
    object_track = event['Bone'] == -1
    if not null_prefix:
        binding = 'explicit-reference'
        source_key = decoded['destinationSourceReference']
    elif not object_track:
        binding = 'unresolved-bone-object'
        source_key = None
    elif event['Target'] == '00000000':
        binding = 'animation-owner'
        source_key = None
    else:
        binding = 'track-object'
        source_key = event['Target']
    flags = event['Flags']
    return {"binding": binding, "sourceKey": source_key,
            "nullContextPrefixSkipped": null_prefix,
            "bone": event['Bone'], "trackFlags": event['ChannelFlags'],
            "normalizedKeyFlags": flags,
            "eventKeyKind": flags & 0x7C0 == 0x40,
            "oncePerKeyEntry": bool(flags & 1),
            "hasConditionalStop": bool(flags & 0x18),
            "limits": 'Binding rule conditional on track reference resolution and native dispatch; active-track state, suppression, direction, key entry and queue capacity are not evaluated.'}


def inspect_folder(folder):
    manifest = json.loads((folder / 'index.json').read_text(encoding='utf-8-sig'))
    matches = []
    for record in manifest['Records']:
        filename = record['File']
        if Path(filename).name != filename:
            raise ValueError('Manifest filename must be local to its folder')
        report = json.loads((folder / filename).read_text(encoding='utf-8-sig'))
        for event in report['Events']:
            if event['Kind'] != 'word-pair-list':
                continue
            decoded = decode_template(bytes.fromhex(event['Bytes']))
            if decoded is None:
                continue
            message = bytes.fromhex(decoded['messageBytes'])
            if struct.unpack_from('<I', message, 0x30)[0] != 53:
                continue
            matches.append({"file": filename, "recordSha256": record['Sha256'],
                            "recordOffset": record['Offset'], "sourceOffset": event['SourceOffset'],
                            "time": event['Time'], "duration": event['Duration'],
                            "trackTargetSourceKey": event['Target'], **decoded,
                            "sourceContext": describe_track_context(decoded, event),
                            "message": inspect_message(message)})
    return {"container": manifest['Container'], "matches": matches,
            "limits": 'Literal template and conditional object-track binding only. No active playback, actor delivery or audio association established.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    arguments = parser.parse_args()
    result = inspect_folder(arguments.folder)
    arguments.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f"{len(result['matches'])} literal type-53 template occurrences")


if __name__ == '__main__':
    main()
