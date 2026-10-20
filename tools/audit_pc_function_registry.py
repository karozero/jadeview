"""Read an explicitly selected PC selector/callback registration table.

Table address/count must come from native analysis of the exact input carrier.
This does not execute events or prove that a registered callback was invoked.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

from audit_pc_script_bindings import pe_sections

ROW_SIZE = 8


def inspect_registry(image, sections, address, count, selectors):
    if count < 0:
        raise ValueError('Table count must be nonnegative')
    size = count * ROW_SIZE
    region = next((section for section in sections
                   if section['address'] <= address
                   and address + size <= section['address'] + section['raw_size']), None)
    if region is None:
        raise ValueError('Table must fit within one retained PE section')
    offset = region['offset'] + address - region['address']
    if offset < 0 or offset + size > len(image):
        raise ValueError('Table extends beyond the input carrier')
    selected = set(selectors)
    rows = []
    for index in range(count):
        descriptor, callback = struct.unpack_from('<II', image, offset + index * ROW_SIZE)
        selector = descriptor & 0xFFFF
        if selected and selector not in selected:
            continue
        executable = any(section['executable'] and section['address'] <= callback
                         < section['address'] + section['size'] for section in sections)
        rows.append(dict(selector=selector, descriptor=f'{descriptor:08X}', index=index,
                         rowAddress=f'{address + index * ROW_SIZE:08X}',
                         callback=f'{callback:08X}', callbackInExecutableSection=executable))
    return dict(tableAddress=f'{address:08X}', tableCount=count, rows=rows,
                missingSelectors=sorted(selected - {row['selector'] for row in rows}),
                limits='Registration only; no event evaluation, argument binding or runtime execution established.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('image', type=Path)
    parser.add_argument('--table-address', type=lambda value: int(value, 0), required=True)
    parser.add_argument('--count', type=lambda value: int(value, 0), required=True)
    parser.add_argument('--selector', type=lambda value: int(value, 0), action='append', default=[])
    parser.add_argument('--output', type=Path, required=True)
    arguments = parser.parse_args()
    image = arguments.image.read_bytes()
    result = inspect_registry(image, pe_sections(image), arguments.table_address,
                              arguments.count, arguments.selector)
    result['sourceSha256'] = hashlib.sha256(image).hexdigest()
    arguments.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f"{len(result['rows'])} selected registration rows")


if __name__ == '__main__':
    main()
