import struct
import unittest

from audit_pc_variable_defaults import read_block, merge_defaults


def payload(fields, data):
    descriptors = b''.join(struct.pack('<IIHH', offset, packed, kind, 0)
                           for name, offset, kind, packed in fields)
    names = b''.join(name.encode('ascii').ljust(30, b'\0') for name, *_ in fields)
    return (struct.pack('<I', len(descriptors)) + descriptors + struct.pack('<I', len(names))
            + names + struct.pack('<I', len(data)) + data + bytes(20))


class VariableDefaultsChecks(unittest.TestCase):
    def test_storage_offsets_are_not_descriptor_order(self):
        fields = [('later', 4, 33, 1), ('earlier', 0, 33, 1)]
        result = read_block(payload(fields, struct.pack('<II', 11, 22)))
        self.assertEqual([field['rawBytes'] for field in result], ['16000000', '0B000000'])

    def test_name_override_preserves_model_offset_and_first_match(self):
        model = read_block(payload([('Friend', 4, 33, 1), ('friend', 0, 33, 1)], bytes(8)))
        instance = read_block(payload([('FRIEND', 0, 33, 1), ('friend', 4, 33, 1)],
                                      struct.pack('<II', 1, 2)))
        result = merge_defaults(model, instance)
        self.assertEqual([field['rawBytes'] for field in result['variables']], ['01000000'] * 2)
        self.assertEqual([field['descriptorFirstWord'] for field in result['variables']], ['00000004', '00000000'])
        self.assertTrue(all(field['source'] == 'instance' for field in result['variables']))

    def test_array_prefix_and_nonfinite_bits_are_retained(self):
        data = struct.pack('<III', 2, 0x7FC00001, 0x80000000)
        field = read_block(payload([('speed', 0, 34, 0x40000002)], data))[0]
        self.assertEqual(field['prefixBytes'], 4)
        self.assertEqual(field['rawBytes'], data.hex().upper())

    def test_out_of_bounds_and_unequal_layout_are_rejected(self):
        with self.assertRaises(ValueError):
            read_block(payload([('bad', 8, 33, 1)], bytes(4)))
        model = read_block(payload([('value', 0, 33, 1)], bytes(4)))
        instance = read_block(payload([('value', 0, 34, 1)], bytes(4)))
        with self.assertRaises(ValueError):
            merge_defaults(model, instance)


if __name__ == '__main__':
    unittest.main()
