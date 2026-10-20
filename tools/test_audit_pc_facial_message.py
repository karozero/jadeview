import struct
import unittest

from audit_pc_facial_message import MESSAGE_SIZE, inspect_message


class FacialMessageChecks(unittest.TestCase):
    def test_field_offsets_and_uninterpreted_words(self):
        words = list(range(13))
        words[2], words[3], words[6] = 0x80000000, 0x7FC00001, 0xFFFFFFFF
        words[8], words[10], words[11], words[12] = 0x41200000, 0xFFFFFFFE, 7, 53
        report = inspect_message(struct.pack('<13I', *words))
        self.assertEqual([command['channel'] for command in report['commands']], [0, 1, 4])
        self.assertEqual([command['packedBits'] for command in report['commands']],
                         ['80000000', '7FC00001', 'FFFFFFFF'])
        self.assertEqual(report['sharedSpeedBits'], '41200000')
        self.assertEqual(report['mouthSpeedInt'], -2)
        self.assertEqual(report['forcedBlinkWord'], '00000007')
        self.assertEqual(report['words'][1], '00000001')

    @staticmethod
    def packet(first=0, second=0, mouth=0):
        payload = bytearray(MESSAGE_SIZE)
        for offset, value in ((0x08, first), (0x0C, second), (0x18, mouth)):
            struct.pack_into('<f', payload, offset, value)
        struct.pack_into('<I', payload, 0x30, 53)
        return payload

    def test_signed_commands_and_zero_mouth_preserve_mode(self):
        result = inspect_message(self.packet(mouth=-52515))['controllerInterpretation']
        self.assertTrue(result['qualified'])
        self.assertTrue(result['automaticMouthUpdate'])
        self.assertEqual(result['commands'][2]['nativeForm'], -52)
        self.assertEqual(result['commands'][2]['targetFactor'], 0.149993896484375)
        unchanged = inspect_message(self.packet(first=-1550))['controllerInterpretation']
        self.assertIsNone(unchanged['automaticMouthUpdate'])

    def test_rounding_boundary_and_residual_reuse(self):
        result = inspect_message(self.packet(first=999.5, second=1000.5))['controllerInterpretation']
        for command in result['commands'][:2]:
            self.assertEqual(command['nativeForm'], 1)
            self.assertEqual(command['targetFactor'], -5)
        reused = inspect_message(self.packet(first=-1550, mouth=.5))['controllerInterpretation']
        self.assertTrue(reused['commands'][2]['noOp'])
        self.assertTrue(reused['automaticMouthUpdate'])
        positive = inspect_message(self.packet(first=1550, mouth=.5))['controllerInterpretation']
        self.assertFalse(positive['automaticMouthUpdate'])

    def test_nonfinite_retained_without_inventing_conversion(self):
        result = inspect_message(self.packet(mouth=float('inf')))
        self.assertFalse(result['controllerInterpretation']['qualified'])
        self.assertEqual(result['commands'][2]['packedBits'], '7F800000')

    def test_wrong_size_and_type(self):
        for size in (0, MESSAGE_SIZE - 1, MESSAGE_SIZE + 1):
            with self.assertRaises(ValueError):
                inspect_message(bytes(size))
        with self.assertRaises(ValueError):
            inspect_message(bytes(MESSAGE_SIZE))


if __name__ == '__main__':
    unittest.main()
