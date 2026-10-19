import importlib.util
from pathlib import Path
import struct
import unittest

MODULE_PATH = Path(__file__).resolve().parents[2] / "tools/audit_actor_facial_variables.py"
SPEC = importlib.util.spec_from_file_location("facial_variables", MODULE_PATH)
AUDIT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(AUDIT)


class VariableAuditChecks(unittest.TestCase):
    def fixture(self):
        descriptor = struct.pack("<IIHH", 0x188, 1, 34, 9)
        name = b"f_blink_progress".ljust(30, b"\0")
        return struct.pack("<I", 12) + descriptor + struct.pack("<I", 30) + name + struct.pack("<If", 1, 0.5) + bytes(20)

    def test_descriptor_identity_without_guessing_defaults(self):
        variable, = AUDIT.read_variables(self.fixture())
        self.assertEqual(variable["name"], "f_blink_progress")
        self.assertEqual(variable["descriptorFirstWord"], "00000188")
        self.assertEqual(variable["type"], 34)
        self.assertNotIn("valueHex", variable)

    def test_reject_truncation_and_trailing_bytes(self):
        for payload in (self.fixture()[:-1], self.fixture() + b"\0"):
            with self.assertRaises(ValueError):
                AUDIT.read_variables(payload)

    def test_reject_partial_descriptor(self):
        payload = bytearray(self.fixture())
        struct.pack_into("<I", payload, 0, 11)
        with self.assertRaises(ValueError):
            AUDIT.read_variables(payload)

    def test_reject_unknown_value_stride(self):
        payload = bytearray(self.fixture())
        struct.pack_into("<H", payload, 12, 999)
        with self.assertRaises(ValueError):
            AUDIT.read_variables(payload)


if __name__ == "__main__":
    unittest.main()
