"""Tests of Tools/import-localization.py (KeyValues localization → JSON for Unity).
Run: python3 -m unittest discover -s Tools/tests
Design: .kiro/specs/screen-flow/design.md Ф12. Uses the real survivors_difficulty_russian.txt from NestOfThorns.
"""
import importlib.util
import json
import os
import tempfile
import unittest

TOOLS = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJECT = os.path.dirname(TOOLS)
_spec = importlib.util.spec_from_file_location("import_localization", os.path.join(TOOLS, "import-localization.py"))
il = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(il)
SOURCE = os.path.join(PROJECT, "NestOfThorns", "resource", "localization", "vdata", "survivors_difficulty",
                      "survivors_difficulty_russian.txt")
UNITY = os.path.join(PROJECT, "UnityProject", "Assets", "Game", "Data", "Localization", "difficulty_ru.json")
DIFFICULTIES = ["story", "harder_punches", "sticky_situation", "footmen_frenzy", "spooky_follower",
                "endurance_run", "meteor_mayhem", "echelon_mastery"]


class ImportLocalizationTests(unittest.TestCase):
    def convert(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = os.path.join(tmp, "out.json")
            self.assertEqual(il.main(["import-localization.py", SOURCE, out]), 0)
            with open(out, encoding="utf-8") as f:
                return json.load(f)

    def test_difficulty_file_has_name_and_description_of_every_difficulty(self):
        data = self.convert()
        self.assertEqual(len(data), 16)
        for key in DIFFICULTIES:
            prefix = f"DOTA_VData_survivors_difficulty_{key}_LocDifficulty"
            self.assertIn(prefix + "Name", data)
            self.assertIn(prefix + "Description", data)
        self.assertEqual(data["DOTA_VData_survivors_difficulty_story_LocDifficultyName"], "Полёт фантазии")
        self.assertEqual(data["DOTA_VData_survivors_difficulty_endurance_run_LocDifficultyDescription"],
                         "Выживать нужно на <b>1 минуту больше</b>.<br/>Империя <b>сильнее</b>.")

    def test_keys_match_difficulty_data(self):
        with open(os.path.join(PROJECT, "Research", "Json", "survivors_difficulty.json"), encoding="utf-8") as f:
            difficulties = json.load(f)
        data = self.convert()
        text = json.dumps(difficulties)
        for key in data:
            self.assertIn("#" + key, text)

    def test_unity_copy_is_up_to_date(self):
        with open(UNITY, encoding="utf-8") as f:
            self.assertEqual(json.load(f), self.convert())

    def test_escapes_duplicates_and_bom(self):
        text = '\ufeff"file"\r\n{\r\n\t"a"\t\t"say \\"hi\\""\r\n\t"a"\t\t"second"\r\n\t// note\r\n}\r\n'
        self.assertEqual(il.parse(text), {"a": 'say "hi"'})

    def test_unreadable_line_and_empty_file_fail(self):
        with self.assertRaises(ValueError):
            il.parse('"file"\n{\n\t"a" broken\n}\n')
        with self.assertRaises(ValueError):
            il.parse('"file"\n{\n}\n')
        with tempfile.TemporaryDirectory() as tmp:
            self.assertEqual(il.main(["x", os.path.join(tmp, "absent.txt"), os.path.join(tmp, "o.json")]), 1)


if __name__ == "__main__":
    unittest.main()
