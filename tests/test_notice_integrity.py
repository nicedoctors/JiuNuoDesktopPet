import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
from public_privacy import audit


class NoticeIntegrityTests(unittest.TestCase):
    def fixture(self, root, text):
        notice = root / "licenses/example.txt"
        notice.parent.mkdir()
        notice.write_text(text, encoding="utf-8")
        (root / "packaging").mkdir()
        (root / "packaging/third-party-notices.json").write_text(json.dumps({"files": [{
            "file": "licenses/example.txt", "sha256": hashlib.sha256(notice.read_bytes()).hexdigest()
        }]}), encoding="utf-8")
        return notice

    def test_exact_upstream_copyright_email_is_preserved(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            notice = self.fixture(root, "Copyright Example <license" + "@example.invalid>")
            self.assertEqual(audit(root, [notice])["findings"], [])

    def test_modified_notice_cannot_bypass_email_check(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            notice = self.fixture(root, "Copyright Example")
            notice.write_text("contact" + "@example.invalid", encoding="utf-8")
            categories = [c for f in audit(root, [notice])["findings"] for c in f["categories"]]
            self.assertIn("notice-integrity", categories)
            self.assertIn("email", categories)

    def test_pinned_notice_does_not_bypass_secret_check(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            notice = self.fixture(root, "ghp_" + "x" * 36)
            self.assertEqual(audit(root, [notice])["findings"][0]["categories"], ["credential"])

    def test_missing_notice_blocks_release(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            notice = self.fixture(root, "Example")
            notice.unlink()
            self.assertEqual(audit(root, [])["findings"][0]["categories"], ["notice-integrity"])


if __name__ == "__main__":
    unittest.main()
