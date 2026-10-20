"""Print saved x86 contexts and raw stack candidates without executing dump code.

Usage: python inspect_dump.py dump.dmp fault-thread-id [context-offset] [address:size ...]
Stack addresses are candidates, not an unwound call stack.
"""
import struct
import sys

from minidump_reader import MinidumpReader


def print_thread(reader, thread, recovered_context, memory_specs):
    registers = reader.registers(thread, recovered_context)
    print(f'Thread {thread.thread_id}:', ' '.join(f'{name}={value:08X}' for name, value in registers.items()))
    print('Fault location:', reader.module_name(registers['EIP']))
    for name in ('EAX', 'EDX', 'ESI'):
        raw = reader.memory(registers[name], 32)
        print(name, 'memory:', raw.hex(' ') if raw else 'not captured/readable')
    stack = reader.memory(registers['ESP'], 512)
    if stack:
        print('Raw stack candidates (not unwound):')
        for index, (value,) in enumerate(struct.iter_unpack('<I', stack)):
            print(f'ESP+{index*4:03X}: {value:08X} {reader.module_name(value)}')
    for specification in memory_specs:
        address, length = (int(part, 0) for part in specification.split(':'))
        raw = reader.memory(address, length)
        print(f'MEMORY {address:08X}:', raw.hex(' ') if raw else 'unavailable')


def inspect(path, fault_thread, recovered_context=None, memory_specs=None):
    specifications = sys.argv[4:] if memory_specs is None else memory_specs
    with MinidumpReader(path) as reader:
        selected_thread = fault_thread or reader.fault_thread()
        for thread in reader.threads():
            if selected_thread != 0 and thread.thread_id != selected_thread:
                continue
            print_thread(reader, thread, recovered_context, specifications)
            if selected_thread != 0:
                return
        if selected_thread != 0:
            raise ValueError('Fault thread is not present in the dump')


if __name__ == '__main__':
    inspect(sys.argv[1], int(sys.argv[2], 0) if len(sys.argv) > 2 else 0,
            int(sys.argv[3], 0) if len(sys.argv) > 3 else None)
