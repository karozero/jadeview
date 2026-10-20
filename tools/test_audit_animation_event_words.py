import json
from pathlib import Path
import struct
import unittest
from unittest.mock import patch

from audit_animation_event_words import inspect_folder


class ContainerEventChecks(unittest.TestCase):
    def test_manifest_preserves_occurrences_and_ignores_stale_files(self):
        files = {}
        with patch.object(Path, "read_text", lambda path, **kwargs: files[path.name]):
            folder = Path("audit-fixture")
            payload = struct.pack("<III", 1, 0, 0x03001F78).hex()
            report = {"Actor": "clip", "Decoded": 1, "Failures": [], "Events": [
                {"Kind": "word-pair-list", "Bytes": payload, "Clip": "12345678",
                 "Time": 1, "HeaderWord": "0000009C", "SourceOffset": 16}]}
            records = []
            for index in range(2):
                filename = f"clip-{index}.json"
                files[filename] = json.dumps(report)
                records.append({"File": filename, "Container": "scene", "Offset": index, "Sha256": "hash"})
            files["stale-animation-events.json"] = "invalid"
            files["index.json"] = json.dumps({"Container": "scene", "Records": records})
            result = inspect_folder(folder, 8056)
            self.assertEqual(result["category3SelectorCounts"], {8056: 2})
            self.assertEqual([match["recordOffset"] for match in result["matches"]], [0, 1])
            self.assertEqual(result["matches"][0]["sourceOffset"], 20)

    def test_rejects_manifest_path_escape(self):
        manifest = json.dumps({"Records": [{"File": "../outside.json"}]})
        with patch.object(Path, "read_text", return_value=manifest):
            folder = Path("audit-fixture")
            with self.assertRaises(ValueError):
                inspect_folder(folder, 8056)


if __name__ == "__main__":
    unittest.main()
