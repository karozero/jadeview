"""Inspect PC model defaults and equal-layout instance overrides by variable name.

Qualified from 00471560 / 00470E30. This is initial source state only: no native
initialization, pointer resolution, saved-state restoration or script execution.
"""
import argparse
import json
import struct
from pathlib import Path

from audit_actor_facial_variables import read_variables, STRIDES


def read_block(payload):
    variables = read_variables(payload)
    start = 12 + len(variables) * 42
    size = struct.unpack_from('<I', payload, start - 4)[0]
    if start + size > len(payload):
        raise ValueError('Variable storage extends beyond the retained payload')
    data = payload[start:start + size]
    fields = []
    for variable in variables:
        packed = int(variable['packedLength'], 16)
        prefix = (packed >> 30) * 4 if packed != 1 else 0
        count = variable['elementCount']
        stride = STRIDES[variable['type']]
        offset = int(variable['descriptorFirstWord'], 16)
        end = offset + prefix + count * stride
        if end > size:
            raise ValueError(f"Variable {variable['name']} exceeds native storage")
        fields.append(dict(variable, prefixBytes=prefix, rawBytes=data[offset:end].hex().upper()))
    return fields


def merge_defaults(model, instance):
    merged = []
    model_names = {field['name'].lower() for field in model}
    for field in model:
        target = dict(field, source='model')
        # Native 00470E30 searches from the start and takes the first matching name.
        override = next((item for item in instance if item['name'].lower() == field['name'].lower()), None)
        if override is not None:
            same_layout = override['type'] == target['type'] and override['packedLength'] == target['packedLength']
            if not same_layout:
                raise ValueError('Unequal-layout override requires separate native qualification')
            target['rawBytes'] = override['rawBytes']
            target['source'] = 'instance'
            target['instanceDescriptorFirstWord'] = override['descriptorFirstWord']
        merged.append(target)
    unmatched = [field['name'] for field in instance if field['name'].lower() not in model_names]
    return dict(variables=merged, unmatchedInstanceNames=unmatched)


def inspect(report, brain_key):
    def record(key, kind):
        matches = [item for item in report['Records'] if item['Key'] == key and item['Kind'] == kind]
        payloads = {item['Bytes'] for item in matches}
        if len(payloads) != 1:
            raise ValueError(f'Missing or ambiguous {kind} resource {key}; use an actor-scoped report')
        return bytes.fromhex(next(iter(payloads)))

    brain = record(brain_key, 'q')
    if len(brain) < 8:
        raise ValueError('Truncated brain resource')
    model_key, instance_key = struct.unpack_from('<II', brain)
    model = record(f'{model_key:08X}', 's')
    if len(model) % 8:
        raise ValueError('Malformed script model rows')
    defaults = {struct.unpack_from('<I', model, offset)[0] for offset in range(0, len(model), 8)
                if model[offset + 4:offset + 8] == b'.ova'}
    if len(defaults) != 1:
        raise ValueError('Expected one model OVA resource')
    default_key = next(iter(defaults))
    result = merge_defaults(read_block(record(f'{default_key:08X}', 't')),
                            read_block(record(f'{instance_key:08X}', 't')))
    result.update(brainKey=brain_key, modelKey=f'{model_key:08X}', modelVariables=f'{default_key:08X}',
                  instanceVariables=f'{instance_key:08X}',
                  limits='Initial source defaults and equal-layout name overrides only; object references remain raw, runtime initialization/timing are not executed.')
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('report', type=Path)
    parser.add_argument('--brain')
    parser.add_argument('--output', type=Path, required=True)
    arguments = parser.parse_args()
    report = json.loads(arguments.report.read_text(encoding='utf-8-sig'))
    brain = arguments.brain or report.get('BrainKey')
    if not brain:
        parser.error('An explicit brain key is required for a scene-wide report')
    result = inspect(report, brain.upper())
    arguments.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f"{result['brainKey']}: {len(result['variables'])} variables")


if __name__ == '__main__':
    main()
