from __future__ import annotations

import importlib.util
import unittest
from pathlib import Path


def load_worker():
    path = Path(__file__).with_name("worker.py")
    spec = importlib.util.spec_from_file_location("tg_melotts_worker", path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


worker = load_worker()


class WorkerTextTests(unittest.TestCase):
    def test_long_text_splits_without_losing_content(self):
        text = ("欢迎参观TGZhanTing-2026.09版本，系统包含12个主题。" * 20).strip()
        chunks = worker.split_long_text(text, max_chars=80)
        self.assertGreater(len(chunks), 1)
        self.assertTrue(all(len(chunk) <= 80 for chunk in chunks))
        self.assertEqual(text, "".join(chunks))

    def test_alphanumeric_token_is_not_split_when_it_fits(self):
        token = "TGZhanTing-2026.09"
        text = f"系统版本为{token}，欢迎参观。"
        chunks = worker.split_long_text(text, max_chars=24)
        self.assertTrue(any(token in chunk for chunk in chunks))

    def test_control_characters_are_removed_deterministically(self):
        self.assertEqual("甲 乙\n丙", worker.normalize_text("甲\x00 乙\n丙"))


if __name__ == "__main__":
    unittest.main()
