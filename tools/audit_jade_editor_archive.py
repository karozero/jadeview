"""Read-only BIG 0x22/0x24 directory/resource census; never extracts game assets."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import struct


INVALID = 0xFFFFFFFF


def cstring(value):
    return value.split(b'\0', 1)[0].decode('cp1252', errors='replace')


class BigIndex:
    def __init__(self, path):
        self.path = path
        self.stream = path.open('rb')
        self.length = path.stat().st_size
        header = struct.unpack('<17I', self.read(0, 68))
        magic, self.version, count, directory_count = header[:4]
        capacity, fats = header[8:10]
        if magic != 0x00474942 or self.version not in (0x22, 0x24):
            raise ValueError('Unsupported BIG signature/version')
        if fats != 1 or not 0 <= count <= capacity <= 1000000:
            raise ValueError('This bounded audit requires a single FAT')
        if header[11] != count or header[12] != directory_count:
            raise ValueError('Header/FAT counts disagree')
        fat = header[13]
        extension_size = 88 if self.version == 0x24 else 84
        names = fat + capacity * 8
        directories = names + capacity * extension_size
        self.dirs = []
        for index in range(directory_count):
            raw = self.read(directories + index * 84, 84)
            self.dirs.append((cstring(raw[20:84]), struct.unpack_from('<I', raw, 16)[0]))
        self.entries = []
        for index in range(count):
            position, key = struct.unpack('<II', self.read(fat + index * 8, 8))
            raw = self.read(names + index * extension_size, extension_size)
            name = cstring(raw[20:84])
            parent = struct.unpack_from('<I', raw, 12)[0]
            if position == INVALID or key == INVALID or not name:
                continue
            size = struct.unpack('<I', self.read(position, 4))[0]
            self.check(position + 4, size)
            self.entries.append(dict(index=index, key=f'{key:08X}', name=name,
                                     folder=self.folder(parent), offset=position + 4, size=size))

    def check(self, offset, size):
        if offset < 0 or size < 0 or offset > self.length - size:
            raise ValueError(f'Archive range outside file: {offset}+{size}')

    def read(self, offset, size):
        self.check(offset, size)
        self.stream.seek(offset)
        value = self.stream.read(size)
        if len(value) != size:
            raise ValueError('Truncated archive range')
        return value

    def folder(self, index):
        parts, seen = [], set()
        while index != INVALID and index < len(self.dirs):
            if index in seen:
                raise ValueError('Directory parent cycle')
            seen.add(index)
            name, parent = self.dirs[index]
            if index == 0:
                break
            parts.append(name)
            index = parent
        return '/'.join(reversed(parts)) or 'ROOT'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--key', action='append', default=[])
    args = parser.parse_args()
    archive = BigIndex(args.archive)
    selected_keys = {key.upper().removeprefix('0X').zfill(8) for key in args.key}
    bge = [entry for entry in archive.entries if 'BGE' in entry['folder'].split('/')]
    terms = ('sally', 'peyj', 'secundo', 'lips', 'dialog', 'cinem', 'entercode')
    named = [entry for entry in archive.entries
             if any(term in entry['name'].lower() for term in terms)]
    selected = [entry.copy() for entry in archive.entries if entry['key'] in selected_keys]
    gao_headers = []
    for entry in bge:
        if not entry['name'].lower().endswith('.gao') or entry['size'] < 16:
            continue
        raw = archive.read(entry['offset'], 16)
        if raw[:4] != b'.gao':
            raise ValueError(f'Unexpected GAO header at {entry["key"]}')
        identity = struct.unpack_from('<I', raw, 12)[0]
        gao_headers.append(dict(key=entry['key'], hasAi=bool(identity & 0x20)))
    for entry in selected:
        data = archive.read(entry['offset'], entry['size'])
        entry['sha256'] = hashlib.sha256(data).hexdigest()
        entry['firstBytes'] = data[:80].hex()
        if entry['name'].lower().endswith('.gao'):
            identity = struct.unpack_from('<I', data, 12)[0]
            entry['identityFlags'] = f'{identity:08X}'
            entry['hasAiIdentityFlag'] = bool(identity & 0x20)
    report = dict(archive=str(args.archive.resolve()), bytes=archive.length,
                  version=f'{archive.version:02X}', entries=len(archive.entries),
                  directories=len(archive.dirs), bgeEntries=len(bge),
                  types=Counter(Path(entry['name']).suffix.lower() for entry in archive.entries),
                  bgeTypes=Counter(Path(entry['name']).suffix.lower() for entry in bge),
                  bgeGaoHeaders=len(gao_headers), bgeGaoWithAiFlag=sum(entry['hasAi'] for entry in gao_headers),
                  missingSelectedKeys=sorted(selected_keys - {entry['key'] for entry in selected}),
                  bgeDirectories=[archive.folder(index) for index in range(len(archive.dirs))
                                  if 'BGE' in archive.folder(index).split('/')],
                  selected=selected, namedResources=named,
                  limits='Directory/key census and selected raw payload hashes only. No runtime behavior or BGE retail identity inferred from a name/key alone.')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2), encoding='utf-8')
    archive.stream.close()
    print(json.dumps({key: report[key] for key in ('version', 'entries', 'directories', 'bgeEntries')}))
    print('SELECTED', len(selected), 'NAMED', len(named), 'REPORT', args.output)


if __name__ == '__main__':
    main()
