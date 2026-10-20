"""Compare editor TRL events with retained retail motion-audit metadata."""
import argparse
from collections import Counter
import json
import hashlib
from pathlib import Path
import struct


def signature(target, time, rows):
    # Match semantic node operands/category/type; compiler metadata and the
    # unused END operand are explicitly excluded, not assumed byte-identical.
    nodes = tuple((0, 31, 0) if tag >> 24 == 31 else (value, tag >> 24, tag & 0xFFFF)
                  for value, tag in rows)
    return target, round(time * 60), nodes


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('editor_tracks', type=Path)
    parser.add_argument('retail_directory', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    reports = []
    for clip in json.loads(args.editor_tracks.read_text()):
        if 'error' in clip:
            continue
        candidates = list(args.retail_directory.glob(f'*-{clip["key"]}-motion.json'))
        if len(candidates) != 1:
            raise ValueError(f'Expected one retail audit for {clip["key"]}')
        retail = json.loads(candidates[0].read_text(encoding='utf-8-sig'))
        editor_events = Counter(signature(event['target'], event['time'], event['rows'])
                                for event in clip['functions'] if event['rows'])
        retail_events = Counter()
        retail_curves = Counter()
        for event in retail['Events']:
            data = bytes.fromhex(event['Bytes'])
            if len(data) < 4:
                continue
            count = struct.unpack_from('<I', data)[0]
            if count and len(data) == count * 8:
                retail_curves[(event['Target'], round(event['Time'] * 60), hashlib.sha256(data).hexdigest())] += 1
            if not count or len(data) != 4 + count * 8:
                continue
            rows = list(struct.iter_unpack('<II', data[4:]))
            retail_events[signature(event['Target'], event['Time'], rows)] += 1
        shared = editor_events & retail_events
        editor_curves = Counter((curve['target'], round(curve['time'] * 60), curve['sha256'])
                                for curve in clip.get('timingCurves', []))
        result = dict(key=clip['key'], name=clip['name'], tracks=clip['trackCount'],
                      editorEvents=editor_events.total(), retailEvents=retail_events.total(),
                      matchingSemanticEvents=shared.total(),
                      editorOnly=(editor_events - retail_events).total(),
                      retailOnly=(retail_events - editor_events).total(),
                      editorTimingCurves=editor_curves.total(),
                      matchingTimingCurvePayloads=(editor_curves & retail_curves).total(),
                      editorSelectors=Counter(str(tag & 0xFFFF)
                                              for event in clip['functions']
                                              for value, tag in event['rows'] if tag >> 24 == 3))
        reports.append(result)
        print(json.dumps({key: value for key, value in result.items() if key != 'editorSelectors'}))
    args.output.write_text(json.dumps(reports, indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()
