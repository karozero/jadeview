import struct
import unittest

from audit_pc_function_registry import inspect_registry


class FunctionRegistryChecks(unittest.TestCase):
    def setUp(self):
        self.image = bytes(16) + struct.pack('<IIII', 8050, 0x401010, 12103, 0x700000)
        self.sections = [dict(address=0x500000, raw_size=16, size=16, offset=16, executable=False),
                         dict(address=0x401000, raw_size=16, size=32, offset=0, executable=True)]

    def test_virtual_mapping_filter_and_callback_bounds(self):
        result = inspect_registry(self.image, self.sections, 0x500000, 2, [8050, 12103, 53])
        self.assertEqual(result['missingSelectors'], [53])
        self.assertEqual(result['rows'][0]['callback'], '00401010')
        self.assertTrue(result['rows'][0]['callbackInExecutableSection'])
        self.assertFalse(result['rows'][1]['callbackInExecutableSection'])
        self.assertEqual(result['rows'][1]['index'], 1)
        self.assertEqual(result['rows'][1]['rowAddress'], '00500008')

    def test_reject_truncation_and_cross_section_reads(self):
        for count in (-1, 3):
            with self.assertRaises(ValueError):
                inspect_registry(self.image, self.sections, 0x500000, count, [])
        with self.assertRaises(ValueError):
            inspect_registry(self.image[:-1], self.sections, 0x500000, 2, [])


if __name__ == '__main__':
    unittest.main()
