"""Bounded, read-only access to streams and memory in a Windows minidump."""
import mmap
import struct

from minidump_models import DumpModule, DumpThread, MemoryRegion


class MinidumpReader:
    REGISTER_OFFSETS = {
        'EDI': 156, 'ESI': 160, 'EBX': 164, 'EDX': 168, 'ECX': 172,
        'EAX': 176, 'EBP': 180, 'EIP': 184, 'ESP': 196,
    }

    def __init__(self, path):
        self.path = path
        self.source = None
        self.data = None

    def __enter__(self):
        self.source = open(self.path, 'rb')
        try:
            self.data = mmap.mmap(self.source.fileno(), 0, access=mmap.ACCESS_READ)
            if self.data[:4] != b'MDMP':
                raise ValueError('Not a minidump')
            self.streams = self._read_streams()
            self.regions = self._read_regions()
            self.modules = self._read_modules()
            return self
        except Exception:
            self.__exit__(None, None, None)
            raise

    def __exit__(self, *_exception):
        if self.data is not None:
            self.data.close()
        if self.source is not None:
            self.source.close()

    def unpack(self, layout, offset):
        return struct.unpack_from('<' + layout, self.data, offset)

    def _read_streams(self):
        count, directory = self.unpack('II', 8)
        streams = {}
        for index in range(count):
            kind, size, offset = self.unpack('III', directory + index * 12)
            streams[kind] = (offset, size)
        return streams

    def _read_regions(self):
        if 9 in self.streams:
            return self._read_full_memory_regions(self.streams[9][0])
        if 5 not in self.streams:
            return []
        offset = self.streams[5][0]
        regions = []
        for index in range(self.unpack('I', offset)[0]):
            address, size, payload = self.unpack('QII', offset + 4 + index * 16)
            regions.append(MemoryRegion(address, size, payload))
        return regions

    def _read_full_memory_regions(self, offset):
        count, payload = self.unpack('QQ', offset)
        regions = []
        for index in range(count):
            address, size = self.unpack('QQ', offset + 16 + index * 16)
            regions.append(MemoryRegion(address, size, payload))
            payload += size
        return regions

    def _read_modules(self):
        if 4 not in self.streams:
            return []
        offset = self.streams[4][0]
        modules = []
        for index in range(self.unpack('I', offset)[0]):
            item = offset + 4 + index * 108
            address, size = self.unpack('QI', item)
            name_offset = self.unpack('I', item + 20)[0]
            name_size = self.unpack('I', name_offset)[0]
            name = self.data[name_offset + 4:name_offset + 4 + name_size].decode('utf-16-le', errors='replace')
            modules.append(DumpModule(address, size, name))
        return modules

    def fault_thread(self):
        return self.unpack('I', self.streams[6][0])[0] if 6 in self.streams else 0

    def threads(self):
        offset = self.streams[3][0]
        for index in range(self.unpack('I', offset)[0]):
            item = offset + 4 + index * 48
            thread_id = self.unpack('I', item)[0]
            context_size, context_offset = self.unpack('II', item + 40)
            yield DumpThread(thread_id, context_size, context_offset)

    def registers(self, thread, recovered_context=None):
        size = thread.context_size if recovered_context is None else 716
        offset = thread.context_offset if recovered_context is None else recovered_context
        if size < 716 or not (self.unpack('I', offset)[0] & 0x10000):
            raise ValueError(f'Unsupported context format: thread={thread.thread_id}, bytes={size}')
        return {name: self.unpack('I', offset + register_offset)[0]
                for name, register_offset in self.REGISTER_OFFSETS.items()}

    def module_name(self, address):
        for module in self.modules:
            if module.address <= address < module.address + module.size:
                return f'{module.name}+0x{address-module.address:X}'
        return ''

    def memory(self, address, size):
        chunks = []
        while size:
            region = next((item for item in self.regions
                           if item.address <= address < item.address + item.size), None)
            if region is None:
                return None
            part_size = min(size, region.address + region.size - address)
            offset = region.payload + address - region.address
            chunks.append(self.data[offset:offset + part_size])
            address += part_size
            size -= part_size
        return b''.join(chunks)
