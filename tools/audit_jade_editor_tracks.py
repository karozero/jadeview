"""Inspect unoptimized editor TRL records in place; save metadata, not assets."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import struct

from audit_jade_editor_archive import BigIndex, cstring


def inspect(data):
    position = 0

    def read(format):
        nonlocal position
        values = struct.unpack_from('<' + format, data, position)
        position += struct.calcsize('<' + format)
        return values[0] if len(values) == 1 else values

    count, flags = read('HH')
    tracks, functions, timing_curves = [], [], []
    for index in range(count):
        track_flags, gizmo, data_length = read('HhI')
        if track_flags & 0xBF00:
            raise ValueError(f'Unsupported optimized/animation track at {index}: {track_flags:04X}')
        target = read('I')
        name = cstring(data[position:position + 15])
        position += 16  # 15-byte editor name plus color.
        event_count = read('I')
        if event_count > 100000:
            raise ValueError('Event budget exceeded')
        frames = 0
        types = Counter()
        for event_index in range(event_count):
            duration, event_flags = read('HH')
            kind = event_flags & 0x7C0
            types[f'{kind:03X}'] += 1
            if kind == 0x40:
                nodes = read('I')
                if nodes > 100000 or position + nodes * 8 > len(data):
                    raise ValueError('AI nodes outside record')
                rows = [read('II') for unused in range(nodes)]
                functions.append(dict(track=index, event=event_index, time=frames / 60,
                                      duration=duration / 60, target=f'{target:08X}', rows=rows))
            elif kind == 0x80:
                if event_flags & 0x20:
                    read('I')
                else:
                    size, format = struct.unpack_from('<HH', data, position)
                    if size < 4:
                        raise ValueError('Invalid transform payload size')
                    translation = format & 3
                    if format & 8 and translation and not format & 0x40:
                        control_offset = position + 4 + translation * 12
                        points = struct.unpack_from('<I', data, control_offset)[0]
                        control_size = points * 8
                        if not points or control_offset + control_size != position + size:
                            raise ValueError('Time curve does not fill its transform payload')
                        controls = data[control_offset:control_offset + control_size]
                        curve_points = [(0.0, struct.unpack_from('<f', controls, 4)[0])]
                        curve_points.extend(struct.unpack_from('<ff', controls, point * 8)
                                            for point in range(1, points))
                        sample_times = {duration / 60 * fraction for fraction in (0, .25, .5, .75, 1)}
                        sample_times.update(time for time, factor in curve_points if 0 <= time <= duration / 60)
                        samples = [dict(elapsed=time, warped=integrate(curve_points, time))
                                   for time in sorted(sample_times)]
                        timing_curves.append(dict(track=index, event=event_index,
                                                  time=frames / 60, duration=duration / 60,
                                                  target=f'{target:08X}', points=points,
                                                  sha256=hashlib.sha256(controls).hexdigest(), samples=samples))
                    position += size
            elif kind != 0:
                raise ValueError(f'Unsupported event type {kind:03X}')
            if position > len(data):
                raise ValueError('Event outside record')
            frames += duration
        tracks.append(dict(index=index, target=f'{target:08X}', name=name,
                           flags=f'{track_flags:04X}', gizmo=gizmo, dataLength=data_length,
                           events=event_count, duration=frames / 60, eventTypes=types))
    if position != len(data):
        raise ValueError(f'Cursor mismatch: {position}/{len(data)}')
    return dict(trackCount=count, flags=flags, tracks=tracks, functions=functions, timingCurves=timing_curves)


def integrate(points, elapsed):
    # Independent interval-area reference for the source's held-factor integral.
    total = 0.0
    for index, (start, factor) in enumerate(points):
        end = points[index + 1][0] if index + 1 < len(points) else elapsed
        total += max(0.0, min(elapsed, end) - start) * factor
    return total


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    parser.add_argument('--key', action='append', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    archive = BigIndex(args.archive)
    keys = {key.upper() for key in args.key}
    reports = []
    for entry in archive.entries:
        if entry['key'] not in keys:
            continue
        try:
            result = inspect(archive.read(entry['offset'], entry['size']))
            reports.append(dict(key=entry['key'], name=entry['name'], **result))
            print(entry['key'], result['trackCount'], 'tracks', len(result['functions']), 'AI events')
        except (ValueError, struct.error) as error:
            reports.append(dict(key=entry['key'], error=str(error)))
            print(entry['key'], 'BLOCKED', error)
    archive.stream.close()
    args.output.write_text(json.dumps(reports, indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()
