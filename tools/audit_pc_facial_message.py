"""Inspect one explicitly selected 52-byte PC actor message without executing it.

This decodes the raw message carrier and the fields read by 005B20D0.
It does not locate messages, resolve actor pointers, or infer dialogue timing.
"""
import argparse
import json
import math
import struct
from pathlib import Path

MESSAGE_SIZE = 0x34
MESSAGE_TYPE_OFFSET = 0x30
FACIAL_MESSAGE_TYPE = 53
COMMAND_FIELDS = ((0, 0x08), (1, 0x0C), (4, 0x18))


NATIVE_INTEGER_BIAS = 6755399441055744.0
INTEGER_LIMIT = 2147483648


def float32(value):
    return struct.unpack('<f', struct.pack('<f', value))[0]


def interpret_commands(payload):
    """Bounded arithmetic from 005B4913..005B4D46, assuming nearest/even FPU mode."""
    packed_values = [struct.unpack_from('<f', payload, offset)[0] for _, offset in COMMAND_FIELDS]
    supported = all(math.isfinite(value) and -INTEGER_LIMIT <= value < INTEGER_LIMIT
                    for value in packed_values)
    if not supported:
        return dict(qualified=False, reason='Non-finite or out-of-range conversion remains uninterpreted')
    residual = 0.0
    commands = []
    for (channel, _), packed in zip(COMMAND_FIELDS, packed_values):
        # Match 0040CF80: add the captured double bias and read the low signed word.
        value = struct.unpack('<i', struct.pack('<d', packed + NATIVE_INTEGER_BIAS)[:4])[0]
        if value == 0:
            commands.append(dict(channel=channel, noOp=True))
            continue
        group = abs(value) // 1000 * (-1 if value < 0 else 1)
        residual = float32(float32(float32(value) / 1000.0) - float32(group))
        weight = float32(float32(abs(residual) - 0.5) * 10.0)
        commands.append(dict(channel=channel, noOp=False, nativeForm=group, targetFactor=weight))
    # Native code tests the original mouth float, then the shared residual local.
    # A nonzero value rounding to zero can therefore reuse a preceding slot's residual.
    automatic_update = residual < 0 if packed_values[-1] != 0 else None
    return dict(qualified=True, rounding='nearest-even', commands=commands,
                automaticMouthUpdate=automatic_update,
                limits='Controller arithmetic only; native form IDs are not validated mesh targets.')


def inspect_message(payload):
    if len(payload) != MESSAGE_SIZE:
        raise ValueError('Expected exactly one 52-byte actor message')
    words = struct.unpack('<13I', payload)
    message_type = words[MESSAGE_TYPE_OFFSET // 4]
    if message_type != FACIAL_MESSAGE_TYPE:
        raise ValueError(f'Expected facial message type 53, got {message_type}')
    # Always preserve raw bits, even when the bounded arithmetic view is unavailable.
    commands = [dict(channel=channel, offset=f'{offset:02X}',
                     packedBits=f'{words[offset // 4]:08X}')
                for channel, offset in COMMAND_FIELDS]
    return dict(messageType=message_type, words=[f'{word:08X}' for word in words],
                commands=commands, controllerInterpretation=interpret_commands(payload),
                sharedSpeedBits=f'{words[0x20 // 4]:08X}',
                mouthSpeedInt=struct.unpack_from('<i', payload, 0x28)[0],
                forcedBlinkWord=f'{words[0x2C // 4]:08X}',
                limits='Static record inspection only; no source, scheduling, emotion names or target mesh binding established.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input', type=Path)
    parser.add_argument('--offset', type=lambda value: int(value, 0), default=0)
    parser.add_argument('--output', type=Path)
    arguments = parser.parse_args()
    if arguments.offset < 0:
        parser.error('Offset must be nonnegative')
    with arguments.input.open('rb') as source:
        source.seek(arguments.offset)
        result = inspect_message(source.read(MESSAGE_SIZE))
    result['source'] = str(arguments.input)
    result['fileOffset'] = arguments.offset
    report = json.dumps(result, indent=2)
    if arguments.output:
        arguments.output.write_text(report + '\n', encoding='utf-8')
    else:
        print(report)


if __name__ == '__main__':
    main()
