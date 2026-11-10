"""Tests of Tools/research-client.py: read-only guard, client strings, Panorama localization, powerup descriptions.
Run: python3 -m unittest discover -s Tools/tests
Design: .kiro/specs/dota-research-client/design.md §6. A fake Dota install is built in a temporary directory;
powerup data comes from Research/Json, VData localization and Panorama files from the real NestOfThorns copies.
"""
import builtins
import contextlib
import hashlib
import importlib.util
import io
import json
import os
import shutil
import struct
import sys
import tempfile
import unittest
import zlib
from unittest import mock
TOOLS = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJECT = os.path.dirname(TOOLS)
sys.path.insert(0, TOOLS)
_spec = importlib.util.spec_from_file_location("research_client", os.path.join(TOOLS, "research-client.py"))
rc = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(rc)
NEST = os.path.join(PROJECT, "NestOfThorns")
VDATA_LOC = [f"resource/localization/vdata/survivors_{kind}/survivors_{kind}_{language}.txt"
             for kind in ("powerups", "attributes") for language in ("english", "russian")]
DOTA_LOC = {
    "english": '\ufeff"lang"\r\n{\r\n\t"Language" "English"\r\n\t"Tokens"\r\n\t{\r\n'
               '\t\t"DOTA_Survivors_DisableDialogue"\t\t"Disable dialogue"\r\n'
               '\t\t"DOTA_Survivors_Quote"\t\t"say \\"hi\\""\r\n'
               '\t\t"DOTA_Survivors_Twice"\t\t"first"\r\n'
               '\t\t"DOTA_Survivors_Twice"\t\t"second"\r\n'
               '\t\t"DOTA_Event_Level"\t\t"Level"\r\n'
               '\t\t"DOTA_FromBinary"\t\t"From binary"\r\n'
               '\t\t"DOTA_Unrelated"\t\t"Unrelated"\r\n'
               '\t}\r\n}\r\n',
    "russian": '"lang"\n{\n\t"Language" "russian"\n\t"Tokens"\n\t{\n'
               '\t\t"DOTA_Survivors_DisableDialogue"\t\t"Отключить реплики"\n'
               '\t\t"DOTA_Event_Level"\t\t"Уровень"\n'
               '\t\t"DOTA_Unrelated"\t\t"Не относится"\n'
               '\t}\n}\n',
}
CLIENT = [b"CSurvivorsFoo", b"_ZN21CSurvivorsPowerUp_Bar5ThinkEv", b"k_eSurvivorsAttribute_X",
          b"filler01", b"m_flNear", b"survivors_near", b"#DOTA_FromBinary in survivors ui"] + \
         [b"padding%02d" % i for i in range(30)] + [b"m_flFar"] + [b"filling%02d" % i for i in range(30)] + \
         [b"m_unPowerUpID", b"m_flFallDamageMult"]
SERVER = [b"CDOTA_Survivors_GameRules", b"ESurvivorsBossAbility", b"m_bServerSide", b"survivors_server"]
def panorama_paths():
    with open(os.path.join(NEST, "manifest.tsv"), encoding="utf-8") as f:
        return sorted(line.split("\t")[0] for line in f
                      if line.startswith(("panorama/layout/", "panorama/styles/")))
def write_vpk(path, files):
    """Single VPK v1 with all data embedded (archive 0x7FFF)."""
    tree, blob = bytearray(), bytearray()
    by_ext = {}
    for name, data in sorted(files.items()):
        folder, _, base = name.rpartition("/")
        stem, _, ext = base.rpartition(".")
        by_ext.setdefault(ext, {}).setdefault(folder or " ", []).append((stem, data))
    for ext, folders in sorted(by_ext.items()):
        tree += ext.encode() + b"\0"
        for folder, items in sorted(folders.items()):
            tree += folder.encode() + b"\0"
            for stem, data in items:
                tree += stem.encode() + b"\0"
                tree += struct.pack("<IHHIIH", zlib.crc32(data) & 0xFFFFFFFF, 0, 0x7FFF, len(blob), len(data), 0xFFFF)
                blob += data
            tree += b"\0"
        tree += b"\0"
    tree += b"\0"
    with open(path, "wb") as f:
        f.write(struct.pack("<III", 0x55AA1234, 1, len(tree)) + bytes(tree) + bytes(blob))
def make_install(root, client=CLIENT, server=SERVER, vpk_override=None, library=("Steam", "steamapps", "common")):
    """Fake `<root>/Steam/steamapps/common/dota 2 beta/game/dota`; returns the Dota directory."""
    dota = os.path.join(root, *library, "dota 2 beta", "game", "dota")
    os.makedirs(os.path.join(dota, "bin", "osx64"))
    os.makedirs(os.path.join(root, *library[:1], "config"), exist_ok=True)
    files = {f"resource/localization/dota_{language}.txt": text.encode("utf-8") for language, text in DOTA_LOC.items()}
    for path in VDATA_LOC + panorama_paths():
        with open(os.path.join(NEST, path), "rb") as f:
            files[path] = f.read()
    files.update(vpk_override or {})
    write_vpk(os.path.join(dota, "pak01_dir.vpk"), files)
    for name, strings in (("libclient.dylib", client), ("libserver.dylib", server)):
        if strings is not None:
            with open(os.path.join(dota, "bin", "osx64", name), "wb") as f:
                f.write(b"\x00\x01".join(strings) + b"\x00")
    with open(os.path.join(dota, "steam.inf"), "w") as f:
        f.write("ClientVersion=1\nSourceRevision=2\nVersionDate=Jan 01 2026\n")
    return dota
class FakeDecoder:
    """Stands in for Source2Viewer: writes a deterministic text built from the compiled file."""
    calls = []
    def version(self):
        return "fake 1.0"
    def decode(self, source, target):
        FakeDecoder.calls.append((source, target))
        with open(source, "rb") as f:
            digest = hashlib.sha256(f.read()).hexdigest()
        text = f"<!-- {os.path.basename(source)} {digest} -->\n"
        if source.endswith(".vxml_c"):
            text += ('<root><Label text="#DOTA_Survivors_DisableDialogue" /><Label text="#DOTA_Event_Level" />'
                     '<Label text="#DOTA_Missing_Key" /><Label text="Literal" /><Panel style="color: #ffffff;" />'
                     '<Label text="#fff" /></root>\n')
        with open(target, "w", encoding="utf-8") as f:
            f.write(text)
def run(argv, **kwargs):
    err = io.StringIO()
    with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(err):
        code = rc.main(argv, decoder=FakeDecoder(), **kwargs)
    return code, err.getvalue()
def tree_files(root):
    result = {}
    for folder, _, names in os.walk(root):
        for name in names:
            path = os.path.join(folder, name)
            with open(path, "rb") as f:
                result[os.path.relpath(path, root)] = (hashlib.sha256(f.read()).hexdigest(), os.stat(path).st_mtime_ns)
    return result
class TempCase(unittest.TestCase):
    def setUp(self):
        self.tmp = os.path.realpath(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp)
        self.out = os.path.join(self.tmp, "out")
class GuardTests(TempCase):
    def setUp(self):
        super().setUp()
        self.dota = make_install(self.tmp)
        self.guard = rc.Guard(self.dota)
    def test_library_root_is_parent_of_steamapps(self):
        self.assertIn(os.path.join(self.tmp, "Steam"), self.guard.roots)
        self.assertIn(os.path.realpath(rc.STEAM_HOME), self.guard.roots)
    def test_paths_anywhere_in_steam_are_forbidden(self):
        for path in (self.dota, os.path.join(self.dota, "x"), os.path.dirname(self.dota),
                     os.path.join(self.tmp, "Steam", "config", "out"), os.path.join(rc.STEAM_HOME, "logs", "x")):
            self.assertTrue(self.guard.forbidden(path), path)
        self.assertFalse(self.guard.forbidden(os.path.join(self.tmp, "SteamOther")))
        self.assertFalse(self.guard.forbidden(self.out))
    def test_symlink_into_steam_is_forbidden(self):
        link = os.path.join(self.tmp, "link")
        os.symlink(os.path.join(self.tmp, "Steam", "config"), link)
        self.assertTrue(self.guard.forbidden(os.path.join(link, "new", "file.json")))
    def test_install_root_without_steamapps(self):
        dota = make_install(self.tmp, library=("Games",))
        guard = rc.Guard(dota)
        install = os.path.join(self.tmp, "Games", "dota 2 beta")
        self.assertIn(install, guard.roots)
        self.assertEqual(guard.install_root, install)
        self.assertTrue(guard.forbidden(os.path.join(install, "game", "dota_russian")))
        self.assertFalse(guard.forbidden(os.path.join(self.tmp, "Games", "other")))
    def test_open_readonly_rejects_write_modes_without_opening(self):
        target = os.path.join(self.dota, "new.txt")
        for mode in ("wb", "w", "ab", "r+b", "r"):
            with self.assertRaises(ValueError):
                self.guard.open_readonly(target, mode)
        self.assertFalse(os.path.exists(target))
        with self.guard.open_readonly(os.path.join(self.dota, "steam.inf")) as f:
            self.assertTrue(f.read().startswith(b"ClientVersion"))
    def test_write_text_refuses_forbidden_path(self):
        target = os.path.join(self.tmp, "Steam", "config", "x.json")
        with self.assertRaises(rc.GuardError):
            rc.write_text(self.guard, target, "x")
        self.assertFalse(os.path.exists(target))
    def test_write_text_keeps_file_when_content_is_equal(self):
        target = os.path.join(self.out, "a", "b.txt")
        self.assertTrue(rc.write_text(self.guard, target, "текст\n"))
        stamp = os.stat(target).st_mtime_ns
        self.assertFalse(rc.write_text(self.guard, target, "текст\n"))
        self.assertEqual(os.stat(target).st_mtime_ns, stamp)
        with open(target, "rb") as f:
            self.assertEqual(f.read(), "текст\n".encode("utf-8"))
    def test_output_inside_dota_or_steam_exits_with_3_and_writes_nothing(self):
        before = tree_files(self.tmp)
        for out in (os.path.join(self.dota, "out"), os.path.join(os.path.dirname(self.dota), "out"),
                    os.path.join(self.tmp, "Steam", "config", "out")):
            code, err = run(["--dota", self.dota, "--out", out])
            self.assertEqual(code, 3, err)
        self.assertEqual(tree_files(self.tmp), before)
    def test_project_inside_forbidden_zone_exits_with_3(self):
        dota = make_install(self.tmp, library=("steamapps", "common"))
        project = os.path.join(self.tmp, "project")
        os.makedirs(project)
        other = os.path.realpath(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, other)
        code, err = run(["--dota", dota, "--out", other], project=project)
        self.assertEqual(code, 3, err)
        self.assertIn("project", err.lower())
        self.assertEqual(os.listdir(other), [])
class ClientStringsTests(unittest.TestCase):
    def setUp(self):
        binaries = {name: rc.extract_strings(b"\x00".join(strings)) for name, strings in
                    (("libclient.dylib", CLIENT), ("libserver.dylib", SERVER))}
        self.result = rc.client_strings(binaries, {"m_unPowerUpID", "m_flUnused"}, {"m_unPowerUpID"})
    def test_extract_strings_keeps_printable_ascii_of_four_or_more(self):
        self.assertEqual(rc.extract_strings(b"abc\x00abcd\x01\xffxyz12\x00"), ["abcd", "xyz12"])
    def test_survivors_strings_by_binary(self):
        self.assertEqual(self.result["survivors"]["libclient.dylib"],
                         ["#DOTA_FromBinary in survivors ui", "CSurvivorsFoo", "_ZN21CSurvivorsPowerUp_Bar5ThinkEv",
                          "k_eSurvivorsAttribute_X", "survivors_near"])
        self.assertEqual(self.result["survivors"]["libserver.dylib"],
                         ["CDOTA_Survivors_GameRules", "ESurvivorsBossAbility", "survivors_server"])
    def test_classes_strip_mangling_length(self):
        self.assertEqual(self.result["classes"],
                         ["CDOTA_Survivors_GameRules", "CSurvivorsFoo", "CSurvivorsPowerUp_Bar"])
    def test_enums(self):
        self.assertEqual(self.result["enums"], ["ESurvivorsBossAbility", "k_eSurvivorsAttribute_X"])
    def test_data_fields_and_unread_fields(self):
        self.assertEqual(self.result["dataFields"], {
            "m_flUnused": {"inClient": False, "readBySimulation": False},
            "m_unPowerUpID": {"inClient": True, "readBySimulation": True}})
        self.assertEqual(self.result["unreadFields"], ["m_flUnused"])
    def test_client_only_fields_use_window_of_ten(self):
        fields = self.result["clientOnlyFields"]
        self.assertEqual(fields["evidence"], "К-кандидат")
        self.assertIn("m_flNear", fields["all"])
        self.assertIn("m_bServerSide", fields["all"])
        self.assertIn("m_flFallDamageMult", fields["all"])  # next to the data field m_unPowerUpID
        self.assertNotIn("m_flFar", fields["all"])
        self.assertNotIn("m_unPowerUpID", fields["all"])
        self.assertEqual(fields["m_fl"], ["m_flFallDamageMult", "m_flNear"])
    def test_lists_are_sorted_and_unique(self):
        for values in [self.result["classes"], self.result["enums"], self.result["unreadFields"],
                       self.result["clientOnlyFields"]["all"]] + list(self.result["survivors"].values()):
            self.assertEqual(values, sorted(set(values)))
    def test_read_fields_come_from_simulation_literals(self):
        fields = rc.read_fields()
        self.assertIn("m_unPowerUpID", fields)
        self.assertTrue(all(f.startswith("m_") for f in fields))
class LocalizationTests(unittest.TestCase):
    def test_parse_kv_handles_bom_crlf_quotes_and_repeats(self):
        tokens = rc.parse_kv(DOTA_LOC["english"])
        self.assertEqual(tokens["DOTA_Survivors_Quote"], 'say "hi"')
        self.assertEqual(tokens["DOTA_Survivors_Twice"], "second")
        self.assertEqual(tokens["DOTA_Survivors_DisableDialogue"], "Disable dialogue")
    def test_layout_references_skip_hex_colors(self):
        refs, literals = rc.layout_texts('<a text="#DOTA_A" /><b text="#fff" /><c text="Hello" />'
                                         '<d onactivate="$.Localize(\'#DOTA_B\')" />')
        self.assertEqual(refs, ["DOTA_A", "DOTA_B"])
        self.assertEqual(literals, ["Hello"])
    def test_selection_rules(self):
        tokens = rc.parse_kv(DOTA_LOC["english"])
        tokens["Crownfall_NestOfThorns_Title"] = "Nest"
        selected, missing = rc.select_localization(tokens, {"DOTA_Event_Level", "DOTA_Missing_Key"})
        self.assertEqual(sorted(selected), ["Crownfall_NestOfThorns_Title", "DOTA_Event_Level",
                                            "DOTA_Survivors_DisableDialogue", "DOTA_Survivors_Quote",
                                            "DOTA_Survivors_Twice"])
        self.assertEqual(missing, ["DOTA_Missing_Key"])
class DescriptionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.defs = rc.load_powerups()
        cls.loc = {}
        for language in ("english", "russian"):
            tokens = {}
            for path in VDATA_LOC:
                if path.endswith(f"_{language}.txt"):
                    with open(os.path.join(NEST, path), encoding="utf-8-sig") as f:
                        tokens.update(rc.parse_kv(f.read()))
            cls.loc[language] = tokens
        cls.text = rc.descriptions(cls.defs, cls.loc["english"], cls.loc["russian"])
    def test_all_47_powerups_grouped_by_role(self):
        self.assertEqual(self.text.count("\n### "), 47)
        for heading in ("## Активные способности (17)", "## Пассивные предметы (16)", "## Рецепты (9)",
                        "## Врождённые (3)", "## Служебные (2)"):
            self.assertIn(heading, self.text)
    def test_magic_missile_has_both_languages_and_data(self):
        section = self.text.split("### magic_missile")[1].split("\n### ")[0]
        self.assertIn(self.loc["russian"]["DOTA_VData_survivors_powerups_magic_missile_LocAbilityDesc"], section)
        self.assertIn(self.loc["english"]["DOTA_VData_survivors_powerups_magic_missile_LocAbilityDesc"], section)
        self.assertIn("`CSurvivorsPowerUpDefinition_MagicMissile`", section)
        self.assertIn("ID 10", section)
        self.assertIn("максимальный ранг 9", section)
        self.assertIn("ProjectileBounces = 1", section)
    def test_placeholders_are_kept_and_listed(self):
        section = self.text.split("### counter_helix")[1].split("\n### ")[0]
        self.assertIn("{f:0:counter_chance}%", section)
        self.assertIn("значение подставляет клиент: `{f:0:counter_chance}`", section)
    def test_recipe_lists_components_and_global_attributes(self):
        section = self.text.split("### sange_and_yasha")[1].split("\n### ")[0]
        self.assertIn("Рецепт: `yasha` (ID 1) + `sange` (ID 91)", section)
        self.assertIn("MovementSpeed = 100", section)
    def test_missing_loc_field_is_written_as_absent(self):
        section = self.text.split("### yasha ")[1].split("\n### ")[0]
        self.assertIn("Описание: нет в данных", section)
    def test_unknown_loc_key_is_a_data_error(self):
        defs = json.loads(json.dumps(self.defs))
        defs["yasha"]["m_sLocAbilityName"] = "#DOTA_No_Such_Key"
        with self.assertRaises(rc.DataError) as error:
            rc.descriptions(defs, self.loc["english"], self.loc["russian"])
        self.assertIn("DOTA_No_Such_Key", str(error.exception))
    def test_wrong_powerup_count_is_a_data_error(self):
        defs = {k: v for k, v in self.defs.items() if k != "yasha"}
        with self.assertRaises(rc.DataError) as error:
            rc.descriptions(defs, self.loc["english"], self.loc["russian"])
        self.assertIn("46", str(error.exception))
class FullRunTests(TempCase):
    def setUp(self):
        super().setUp()
        self.dota = make_install(self.tmp)
        FakeDecoder.calls = []
    def test_full_run_is_read_only_and_reproducible(self):
        install = os.path.dirname(os.path.dirname(self.dota))
        real_open = builtins.open
        modes = []
        def spy(file, mode="r", *args, **kwargs):
            if isinstance(file, str) and os.path.realpath(file).startswith(install + os.sep):
                modes.append((file, mode))
            return real_open(file, mode, *args, **kwargs)
        before = tree_files(install)
        with mock.patch("builtins.open", spy):
            code, err = run(["--dota", self.dota, "--out", self.out, "--check-readonly"])
        self.assertEqual(code, 0, err)
        self.assertEqual(tree_files(install), before)
        self.assertTrue(modes)
        self.assertEqual({mode for _, mode in modes}, {"rb"})
        for source, target in FakeDecoder.calls:
            self.assertTrue(source.startswith(NEST + os.sep), source)
            self.assertFalse(target.startswith(self.tmp + os.sep + "Steam"), target)
        first = tree_files(self.out)
        code, err = run(["--dota", self.dota, "--out", self.out, "--check-readonly"])
        self.assertEqual(code, 0, err)
        self.assertEqual(tree_files(self.out), first)
        for name in ("Research/Decoded/client/strings.json", "Research/FullGame/Descriptions.md",
                     "Research/Decoded/panorama/localization_english.json",
                     "Research/Decoded/panorama/localization_russian.json",
                     "Research/Decoded/panorama/menu_texts.md", "Research/research-client-report.json",
                     "Research/Decoded/panorama/layout/battle_pass/crownfall/survivors_powerup_tooltip.vxml",
                     "Research/Decoded/panorama/styles/banners/front_page_nest_of_thorns.vcss"):
            self.assertIn(name, first)
    def test_outputs_content(self):
        code, err = run(["--dota", self.dota, "--out", self.out])
        self.assertEqual(code, 0, err)
        def read(name):
            with open(os.path.join(self.out, name), encoding="utf-8") as f:
                return f.read()
        english = json.loads(read("Research/Decoded/panorama/localization_english.json"))
        self.assertEqual(sorted(english["keys"]), ["DOTA_Event_Level", "DOTA_FromBinary",
                                                   "DOTA_Survivors_DisableDialogue", "DOTA_Survivors_Quote",
                                                   "DOTA_Survivors_Twice"])
        self.assertEqual(english["missingReferences"], ["DOTA_Missing_Key"])
        menu = read("Research/Decoded/panorama/menu_texts.md")
        self.assertIn("Отключить реплики", menu)
        self.assertIn("нет в локализации", menu)
        self.assertIn("Literal", menu)
        report = json.loads(read("Research/research-client-report.json"))
        self.assertEqual(report["client"]["ClientVersion"], "1")
        self.assertEqual(report["source2viewer"], "fake 1.0")
        paths = [item["path"] for item in report["inputs"]["dota"]]
        self.assertIn("bin/osx64/libclient.dylib", paths)
        self.assertNotIn(self.tmp, json.dumps(report))
        strings = json.loads(read("Research/Decoded/client/strings.json"))
        self.assertIn("CSurvivorsPowerUp_Bar", strings["classes"])
        self.assertEqual(read("Research/FullGame/Descriptions.md").count("\n### "), 47)
    def test_check_readonly_detects_a_change(self):
        original = rc.client_strings
        def touching(*args, **kwargs):
            with open(os.path.join(self.dota, "touched.txt"), "w") as f:  # the test, not the tool, writes here
                f.write("x")
            return original(*args, **kwargs)
        with mock.patch.object(rc, "client_strings", touching):
            code, err = run(["--dota", self.dota, "--out", self.out, "--check-readonly"])
        self.assertEqual(code, 4, err)
        self.assertIn("touched.txt", err)
    def test_missing_binary_exits_with_1(self):
        dota = make_install(os.path.join(self.tmp, "b"), server=None)
        code, err = run(["--dota", dota, "--out", self.out])
        self.assertEqual(code, 1, err)
        self.assertIn("libserver.dylib", err)
    def test_copy_differing_from_vpk_exits_with_1(self):
        path = panorama_paths()[0]
        dota = make_install(os.path.join(self.tmp, "c"), vpk_override={path: b"changed"})
        code, err = run(["--dota", dota, "--out", self.out])
        self.assertEqual(code, 1, err)
        self.assertIn("extract-resources.py", err)
    def test_unknown_loc_key_exits_with_2(self):
        path = "resource/localization/vdata/survivors_powerups/survivors_powerups_english.txt"
        dota = make_install(os.path.join(self.tmp, "d"), vpk_override={path: b'"x"\n{\n}\n'})
        code, err = run(["--dota", dota, "--out", self.out])
        self.assertEqual(code, 2, err)
        self.assertIn("magic_missile", err)
if __name__ == "__main__":
    unittest.main()
