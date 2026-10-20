"""Match retained OFC keys to candidate key/function rows in a captured PE.

Read-only: never execute the image. A row is a lead, not proof that a native
function is called for this actor or that it implements facial behavior.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct


def pe_sections(image):
    if image[:2] != b'MZ':
        raise ValueError('Expected a PE carrier')
    header = struct.unpack_from('<I', image, 0x3c)[0]
    if image[header:header + 4] != b'PE\0\0':
        raise ValueError('Invalid PE signature')
    count, optional_size = (struct.unpack_from('<H', image, header + offset)[0]
                            for offset in (6, 20))
    optional = header + 24
    if struct.unpack_from('<H', image, optional)[0] != 0x10b:
        raise ValueError('Expected a 32-bit PC image')
    base = struct.unpack_from('<I', image, optional + 28)[0]
    sections = []
    for index in range(count):
        position = optional + optional_size + index * 40
        size, rva, raw_size, raw_offset = struct.unpack_from('<IIII', image, position + 8)
        flags = struct.unpack_from('<I', image, position + 36)[0]
        if raw_offset + raw_size > len(image):
            raise ValueError('Section extends beyond PE carrier')
        sections.append(dict(name=image[position:position + 8].rstrip(b'\0').decode('ascii'),
                             address=base + rva, size=size, raw_size=raw_size,
                             offset=raw_offset, executable=bool(flags & 0x20000000)))
    return sections


def match_bindings(image, report):
    sections = pe_sections(image)
    code = [section for section in sections if section['executable']]
    rows = []
    records = {int(record['Key'], 16): record for record in report['Records'] if record['Kind'] == 'r'}
    for section in sections:
        if section['executable']:
            continue
        end = section['offset'] + section['raw_size'] - 7
        for offset in range(section['offset'], end, 4):
            key, function = struct.unpack_from('<II', image, offset)
            if key not in records:
                continue
            executable = any(block['address'] <= function < block['address'] +
                             min(block['size'], block['raw_size']) for block in code)
            if not executable:
                continue
            rows.append(dict(key=f'{key:08X}', function=f'{function:08X}',
                             row=f"{section['address'] + offset - section['offset']:08X}",
                             section=section['name']))
    return dict(actor=report['Actor'], imageSha256=hashlib.sha256(image).hexdigest(),
                codeKeyCount=len(records), matchedKeyCount=len({row['key'] for row in rows}),
                candidates=rows,
                limits='Aligned non-executable PE rows with an executable-range pointer only. '
                       'Call dispatch, function entry, actor execution and facial meaning need independent verification.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('image', type=Path)
    parser.add_argument('report', type=Path)
    parser.add_argument('--output', required=True, type=Path)
    arguments = parser.parse_args()
    report = json.loads(arguments.report.read_text(encoding='utf-8-sig'))
    result = match_bindings(arguments.image.read_bytes(), report)
    arguments.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(f"{result['actor']}: {result['matchedKeyCount']}/{result['codeKeyCount']} keys have candidate native bindings")


if __name__ == '__main__':
    main()
