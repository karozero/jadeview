import struct
import unittest

from audit_pc_cinematic_messages import decode_template, describe_track_context
from audit_pc_facial_message import inspect_message


def template():
    tags = [0x01000028, 0x02000806, 0x01000021, 0x01000028, 0x01000028,
            0x01000022, 0x01000022, 0x01000022, 0x03001770,
            0x01000022, 0x01000022, 0x01000022, 0x03001770,
            0x01000022, 0x01000022, 0x01000021, 0x01000021, 0x035C1F78, 0x1F050004]
    rows = [(0, tag) for tag in tags]
    rows[2] = (53, tags[2])
    rows[10] = (0x43FA0000, tags[10])
    rows[15] = (8, tags[15])
    return rows


def pack(rows):
    return struct.pack('<I', len(rows)) + b''.join(struct.pack('<II', *row) for row in rows)


class CinematicMessageChecks(unittest.TestCase):
    def test_mouth_reset_fields_and_controller_interpretation(self):
        result = decode_template(pack(template()))
        message = inspect_message(bytes.fromhex(result['messageBytes']))
        self.assertEqual(message['mouthSpeedInt'], 8)
        self.assertEqual(message['forcedBlinkWord'], '00000000')
        controller = message['controllerInterpretation']
        self.assertEqual(controller['commands'][2]['nativeForm'], 0)
        self.assertEqual(controller['commands'][2]['targetFactor'], 0)
        self.assertFalse(controller['automaticMouthUpdate'])
        self.assertEqual(result['destinationSourceReference'], '00000000')

    def test_vector_and_scalar_order(self):
        rows = template()
        indices = (3, 4, 5, 6, 7, 9, 10, 11, 13, 14, 15, 16, 2)
        for value, index in enumerate(indices, 1):
            rows[index] = (value, rows[index][1])
        result = decode_template(pack(rows))
        self.assertEqual(struct.unpack('<13I', bytes.fromhex(result['messageBytes'])), tuple(range(1, 14)))

    def test_negative_mouth_command_enables_automatic_cycle(self):
        rows = template()
        packed, = struct.unpack('<I', struct.pack('<f', -580))
        rows[10] = (packed, rows[10][1])
        rows[15] = (11, rows[15][1])
        result = decode_template(pack(rows))
        message = inspect_message(bytes.fromhex(result['messageBytes']))
        self.assertTrue(message['controllerInterpretation']['automaticMouthUpdate'])
        self.assertAlmostEqual(message['controllerInterpretation']['commands'][2]['targetFactor'], 0.8, places=5)
        self.assertEqual(message['mouthSpeedInt'], 11)

    def test_dynamic_operands_and_other_shapes_remain_unsupported(self):
        rows = template()
        rows[10] = (0, 0x01000026)
        self.assertIsNone(decode_template(pack(rows)))
        self.assertIsNone(decode_template(pack(template())[:-1]))
        rows = template()
        rows[8] = (0, 0x03001771)
        self.assertIsNone(decode_template(pack(rows)))

    def test_null_prefix_inherits_object_track_context(self):
        decoded = decode_template(pack(template()))
        event = {'Bone': -1, 'Target': '71003F5A', 'ChannelFlags': 52, 'Flags': 65}
        context = describe_track_context(decoded, event)
        self.assertEqual(context['binding'], 'track-object')
        self.assertEqual(context['sourceKey'], '71003F5A')
        self.assertTrue(context['nullContextPrefixSkipped'])
        self.assertTrue(context['oncePerKeyEntry'])
        self.assertTrue(context['eventKeyKind'])
        self.assertFalse(context['hasConditionalStop'])

    def test_owner_fallback_and_unresolved_bone_context(self):
        decoded = decode_template(pack(template()))
        event = {'Bone': -1, 'Target': '00000000', 'ChannelFlags': 52, 'Flags': 65}
        self.assertEqual(describe_track_context(decoded, event)['binding'], 'animation-owner')
        event['Bone'] = 3
        context = describe_track_context(decoded, event)
        self.assertEqual(context['binding'], 'unresolved-bone-object')
        self.assertIsNone(context['sourceKey'])

    def test_explicit_reference_is_not_replaced_by_track_target(self):
        rows = template()
        rows[0] = (0x12345678, rows[0][1])
        decoded = decode_template(pack(rows))
        event = {'Bone': -1, 'Target': '71003F5A', 'ChannelFlags': 52, 'Flags': 65}
        context = describe_track_context(decoded, event)
        self.assertEqual(context['binding'], 'explicit-reference')
        self.assertEqual(context['sourceKey'], '12345678')
        self.assertFalse(context['nullContextPrefixSkipped'])


if __name__ == '__main__':
    unittest.main()
