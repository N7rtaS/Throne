"""Tests of Tools/asset-registry.py: resource graph, Unity outputs, statuses, reference triangle counts.
Run: python3 -m unittest discover -s Tools/tests
Design: .kiro/specs/asset-registry/design.md §7. A synthetic project is built in a temporary directory;
the last class runs on the real project data (skipped when NestOfThorns/manifest.tsv is absent).
"""
import contextlib
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

TOOLS = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJECT = os.path.dirname(TOOLS)
sys.path.insert(0, TOOLS)
_spec = importlib.util.spec_from_file_location("asset_registry", os.path.join(TOOLS, "asset-registry.py"))
ar = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ar)
CONVERTED = "UnityProject/Assets/Game/Art/Converted"


def read_bytes(path):
    with open(path, "rb") as f:
        return f.read()


def read_json(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def resource(references):
    """Minimal compiled Source 2 resource with a RERL block (layout read by vpk.external_references)."""
    names = [r.encode() + b"\0" for r in references]
    first = 36
    strings = first + 16 * len(references)
    entries, position = b"", strings
    for j, name in enumerate(names):
        entry = first + 16 * j
        entries += struct.pack("<QII", 0, position - (entry + 8), 0)
        position += len(name)
    body = struct.pack("<4sII", b"RERL", 8, 0) + struct.pack("<II", 8, len(references)) + entries + b"".join(names)
    return struct.pack("<IHHII", 16 + len(body), 12, 0, 8, 1) + body


def glb(meshes):
    """glb with one node per (name, index count); every mesh has one triangle-list primitive."""
    doc = {"asset": {"version": "2.0"}, "nodes": [], "meshes": [], "accessors": []}
    for i, (name, count) in enumerate(meshes):
        doc["accessors"].append({"count": count, "componentType": 5123, "type": "SCALAR"})
        doc["meshes"].append({"name": name, "primitives": [{"attributes": {}, "indices": i}]})
        doc["nodes"].append({"name": name, "mesh": i})
    data = json.dumps(doc).encode()
    data += b" " * (-len(data) % 4)
    return struct.pack("<III", 0x46546C67, 2, 20 + len(data)) + struct.pack("<II", len(data), 0x4E4F534A) + data


def meta(guid, importer="NativeFormatImporter", android_format=None):
    text = f"fileFormatVersion: 2\nguid: {guid}\n{importer}:\n  externalObjects: {{}}\n"
    if android_format is not None:
        text += "  textureFormat: 1\n  platformSettings:\n"
        for target, code in (("DefaultTexturePlatform", -1), ("Standalone", -1), ("Android", android_format)):
            text += (f"  - serializedVersion: 4\n    buildTarget: {target}\n    maxTextureSize: 1024\n"
                     f"    resizeAlgorithm: 0\n    textureFormat: {code}\n    textureCompression: 1\n")
    if importer == "ScriptedImporter":
        text += "  script: {fileID: 11500000, guid: 715df9372183c47e389bb6e19fbc3b52, type: 3}\n"
    return text


class Project:
    """Synthetic project: NestOfThorns + manifest, registries, Converted outputs with .meta."""

    def __init__(self):
        self.root = tempfile.mkdtemp(prefix="thorn-assets-")
        self.files = {}
        self.definitions = {}
        self.json = {}
        self.extract = {"heroModelFolders": {}, "econItems": {}}
        self.sounds = {}
        self.outputs = {}
        self.overrides = None

    def source(self, path, data=b"x", crc=None):
        self.files[path] = (data, crc)

    def define(self, key, role, resources=(), required=True, reachable=True):
        category = key.split("/")[0]
        self.definitions[key] = {"category": category, "key": key.split("/")[1], "role": role,
                                 "resources": list(resources), "required": required, "reachable": reachable}

    def output(self, path, data=b"o", meta_text="guid"):
        self.outputs[path] = (data, meta_text)

    def write(self):
        def put(rel, data):
            full = os.path.join(self.root, rel)
            os.makedirs(os.path.dirname(full), exist_ok=True)
            with open(full, "wb") as f:
                f.write(data if isinstance(data, bytes) else data.encode())
        lines = ["path\tbytes\tarchive\tcrc32\treason"]
        for path, (data, crc) in sorted(self.files.items()):
            if data is not None:
                put("NestOfThorns/" + path, data)
            real = zlib.crc32(data or b"") & 0xFFFFFFFF
            lines.append(f"{path}\t{len(data or b'')}\t1\t{crc or format(real, '08x')}\tmanifest")
        put("NestOfThorns/manifest.tsv", "\n".join(lines) + "\n")
        put("Research/FullGame/ContentRegistry.json", json.dumps({"definitions": self.definitions}))
        put("Research/FullGame/SoundRegistry.json", json.dumps({"definitions": self.sounds}))
        put("Research/FullGame/SourceVersions.json", json.dumps({"tools": {"source2viewer": "Version: 20.1"}}))
        put("Research/extract-report.json", json.dumps(self.extract))
        for name, value in self.json.items():
            put(f"Research/Json/{name}.json", json.dumps(value))
        for path, (data, meta_text) in self.outputs.items():
            put(f"{CONVERTED}/{path}", data)
            if meta_text is not None:
                put(f"{CONVERTED}/{path}.meta", meta_text)
        if self.overrides is not None:
            put("Tools/asset-registry-overrides.json", json.dumps(self.overrides))

    def run(self):
        self.write()
        out = os.path.join(self.root, "out")
        stdout = io.StringIO()
        with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(io.StringIO()):
            code = ar.main(["--root", self.root, "--out", out])
        self.stdout = stdout.getvalue()
        path = os.path.join(out, "AssetRegistry.json")
        self.registry = read_json(path) if os.path.exists(path) else None
        return code

    def cleanup(self):
        shutil.rmtree(self.root, ignore_errors=True)


class SyntheticCase(unittest.TestCase):
    def setUp(self):
        self.p = Project()
        self.addCleanup(self.p.cleanup)
        self.p.source("models/a/a.vmdl_c", resource(["materials/a.vmat", "materials/gone.vmat"]))
        self.p.source("materials/a.vmat_c", resource(["materials/a_color_png_1.vtex"]))
        self.p.source("materials/a_color_png_1.vtex_c", resource([]))
        self.p.source("models/b/b.vmdl_c", resource([]))
        self.p.define("enemies/a", "horde", ["models/a/a.vmdl"])
        self.p.define("game_modes/mode", "game_mode")

    def asset(self, path):
        return self.p.registry["assets"][path]


class GraphTests(SyntheticCase):
    def test_references_are_compiled_paths_from_rerl(self):
        self.assertEqual(self.p.run(), 0)
        self.assertEqual(self.asset("models/a/a.vmdl_c")["references"], ["materials/a.vmat_c", "materials/gone.vmat_c"])
        self.assertNotIn("referencedBy", self.asset("models/a/a.vmdl_c"))

    def test_used_by_is_transitive_from_definition_resources(self):
        self.p.run()
        self.assertEqual(self.asset("materials/a_color_png_1.vtex_c")["usedBy"], ["enemies/a"])

    def test_every_manifest_row_has_a_record_with_crc(self):
        self.p.run()
        self.assertEqual(sorted(self.p.registry["assets"]),
                         ["materials/a.vmat_c", "materials/a_color_png_1.vtex_c", "models/a/a.vmdl_c", "models/b/b.vmdl_c"])
        data = resource([])
        self.assertEqual(self.asset("models/b/b.vmdl_c")["crc32"], format(zlib.crc32(data) & 0xFFFFFFFF, "08x"))
        self.assertEqual(self.asset("models/b/b.vmdl_c")["bytes"], len(data))

    def test_missing_target_goes_to_unresolved(self):
        self.p.run()
        self.assertEqual(self.p.registry["summary"]["unresolved"], {"models/a/a.vmdl_c": ["materials/gone.vmat_c"]})

    def test_crc_mismatch_fails_with_code_1_and_writes_nothing(self):
        self.p.source("models/b/b.vmdl_c", resource([]), crc="deadbeef")
        self.assertEqual(self.p.run(), 1)
        self.assertIsNone(self.p.registry)

    def test_missing_source_file_fails_with_code_1(self):
        self.p.source("models/c.vmdl_c", None)
        self.assertEqual(self.p.run(), 1)

    def test_hero_roots_come_from_hero_model_folder_and_econ_items(self):
        self.p.source("models/heroes/h/h.vmdl_c", resource([]))
        self.p.source("models/items/h/wings.vmdl_c", resource([]))
        self.p.define("heroes/h", "hero")
        self.p.json["survivors_heroes"] = {"h": {"m_nDOTAHeroID": 7, "m_vecEconItems": [5]}}
        self.p.extract = {"heroModelFolders": {"7": "models/heroes/h/"},
                          "econItems": {"5": {"usedBy": ["h"], "assets": ["models/items/h/wings.vmdl"]}}}
        self.p.run()
        self.assertEqual(self.asset("models/heroes/h/h.vmdl_c")["usedBy"], ["heroes/h"])
        self.assertEqual(self.asset("models/items/h/wings.vmdl_c")["usedBy"], ["heroes/h"])

    def test_sound_events_go_to_referencing_definition_or_game_mode(self):
        self.p.source("sounds/hit.vsnd_c", b"s1")
        self.p.source("sounds/music.vsnd_c", b"s2")
        self.p.define("heroes/h", "hero")
        self.p.json["survivors_heroes"] = {"h": {"m_pszPlayerHitSoundEvent": "hit"}}
        self.p.sounds = {"hit": {"vsnd": ["sounds/hit.vsnd"]}, "music": {"vsnd": ["sounds/music.vsnd"]}}
        self.p.run()
        self.assertEqual(self.asset("sounds/hit.vsnd_c")["usedBy"], ["heroes/h"])
        self.assertEqual(self.asset("sounds/music.vsnd_c")["usedBy"], ["game_modes/mode"])


class OutputTests(SyntheticCase):
    def test_glb_png_and_material_are_linked_with_guid_and_importer(self):
        self.p.output("models/a/a.glb", glb([("a_lod0", 30)]), meta("g1", "ScriptedImporter"))
        self.p.output("models/a/a_color_png_1.png", b"png", meta("g2", "TextureImporter", 51))
        self.p.output("materials/a.mat", b"mat", meta("g3"))
        self.p.run()
        out = self.asset("models/a/a.vmdl_c")["outputs"][0]
        self.assertEqual((out["path"], out["guid"], out["importer"], out["stage"]),
                         ("Assets/Game/Art/Converted/models/a/a.glb", "g1", "ScriptedImporter:glTFast", "converted"))
        png = self.asset("materials/a_color_png_1.vtex_c")["outputs"][0]
        self.assertEqual(png["guid"], "g2")
        self.assertEqual(png["compression"]["Android"],
                         {"maxTextureSize": 1024, "textureFormat": "ASTC_8x8", "textureCompression": "Compressed"})
        self.assertEqual(png["compression"]["Standalone"]["textureFormat"], "Automatic")
        self.assertEqual(self.asset("materials/a.vmat_c")["outputs"][0]["guid"], "g3")
        self.assertEqual(self.asset("models/a/a.vmdl_c")["status"], "converted")

    def test_derived_orm_png_and_physics_glb_link_to_their_source(self):
        self.p.output("models/a/a_color_png_1_orm_123.png", b"png", meta("g4", "TextureImporter", -1))
        self.p.output("models/a/a_physics.glb", glb([]), meta("g5", "ScriptedImporter"))
        self.p.run()
        self.assertEqual(self.asset("materials/a_color_png_1.vtex_c")["outputs"][0]["guid"], "g4")
        self.assertEqual(self.asset("models/a/a.vmdl_c")["outputs"][0]["guid"], "g5")

    def test_baked_asset_links_through_enemies_json(self):
        self.p.output("models/a/a.glb", glb([("a_lod0", 3)]), meta("g1", "ScriptedImporter"))
        self.p.output("Baked/a.asset", b"asset", meta("g6"))
        self.p.output("enemies.json", json.dumps({"a": {"models": ["models/a/a.glb"]}}).encode(), meta("g7", "TextScriptImporter"))
        self.p.run()
        stages = [o["stage"] for o in self.asset("models/a/a.vmdl_c")["outputs"]]
        self.assertEqual(sorted(stages), ["baked", "converted"])
        self.assertEqual(self.asset("models/a/a.vmdl_c")["status"], "baked")

    def test_output_without_meta_fails_with_code_1(self):
        self.p.output("models/a/a.glb", glb([]), None)
        self.assertEqual(self.p.run(), 1)

    def test_unlinked_output_is_reported_as_orphan(self):
        self.p.output("misc/notes.png", b"png", meta("g8", "TextureImporter", -1))
        self.p.run()
        self.assertIn("Assets/Game/Art/Converted/misc/notes.png", self.p.registry["summary"]["orphanOutputs"])


class StatusTests(SyntheticCase):
    def test_required_without_output_is_none_and_unreachable_is_unused(self):
        self.p.run()
        self.assertEqual(self.asset("materials/a.vmat_c")["status"], "none")
        self.assertEqual(self.asset("models/b/b.vmdl_c")["status"], "unused")
        self.assertEqual(self.asset("models/b/b.vmdl_c")["verification"], "unverified")

    def test_reachable_only_from_non_required_entity_is_unused(self):
        self.p.define("enemies/b", "horde", ["models/b/b.vmdl"], required=True, reachable=False)
        self.p.run()
        self.assertEqual(self.asset("models/b/b.vmdl_c")["usedBy"], ["enemies/b"])
        self.assertEqual(self.asset("models/b/b.vmdl_c")["status"], "unused")

    def test_override_marks_recreated_and_verified(self):
        self.p.overrides = {"materials/a.vmat_c": {"status": "recreated", "unity": "Assets/Game/Art/X.mat",
                                                   "verified": "Research/check.md"}}
        self.p.run()
        self.assertEqual(self.asset("materials/a.vmat_c")["status"], "recreated")
        self.assertEqual(self.asset("materials/a.vmat_c")["verification"], "verified")

    def test_unused_override_without_reason_fails(self):
        self.p.overrides = {"materials/a.vmat_c": {"status": "unused"}}
        self.assertEqual(self.p.run(), 1)

    def test_override_of_unknown_source_fails(self):
        self.p.overrides = {"materials/zzz.vmat_c": {"status": "unused", "reason": "r"}}
        self.assertEqual(self.p.run(), 1)

    def test_required_entity_without_records_is_reported(self):
        self.p.define("powerups/item", "passive-item")
        self.p.define("attributes/hp", "attribute")
        self.p.define("powerups/gold", "service:gold")
        self.p.define("enemies/ghost", "horde", ["models/none.vmdl"])
        self.assertEqual(self.p.run(), 0)
        self.assertEqual(self.p.registry["summary"]["missingRequired"], ["enemies/ghost", "powerups/item"])
        self.assertIn("missingRequired: 2", self.p.stdout)

    def test_two_runs_write_identical_bytes(self):
        self.p.run()
        path = os.path.join(self.p.root, "out", "AssetRegistry.json")
        first = read_bytes(path)
        self.p.run()
        self.assertEqual(read_bytes(path), first)
        self.assertIn("NestOfThorns/manifest.tsv", self.p.registry["inputs"])
        self.assertEqual(self.p.registry["tools"]["source2viewer"], "Version: 20.1")


class TriangleTests(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, self.dir, True)
        self.path = os.path.join(self.dir, "m.glb")
        with open(self.path, "wb") as f:
            f.write(glb([("m_lod0", 30), ("m_style1_lod0", 6), ("m._dummy", 3)]))

    def test_all_and_visible_triangles_by_style(self):
        self.assertEqual(ar.glb_triangles(self.path, 0), (13, 10))
        self.assertEqual(ar.glb_triangles(self.path, 1), (13, 2))

    def test_style_without_own_meshes_falls_back_to_default(self):
        self.assertEqual(ar.glb_triangles(self.path, 2), (13, 10))

    def test_models_section_sums_looks_and_takes_max_for_enemies(self):
        p = Project()
        self.addCleanup(p.cleanup)
        p.define("heroes/h", "hero")
        p.define("enemies/e", "horde")
        p.output("m.glb", read_bytes(self.path), meta("g1", "ScriptedImporter"))
        p.output("n.glb", glb([("n_lod0", 60)]), meta("g2", "ScriptedImporter"))
        p.output("looks.json", json.dumps({"h": {"body": "m.glb", "wearables": ["n.glb"], "style": 1}}).encode(), meta("g3"))
        p.output("enemies.json", json.dumps({"e": {"models": ["m.glb", "n.glb"]}}).encode(), meta("g4"))
        self.assertEqual(p.run(), 0)
        self.assertEqual(p.registry["models"]["heroes/h"]["visibleTriangles"], 2 + 20)
        self.assertEqual(p.registry["models"]["enemies/e"]["visibleTriangles"], 20)
        self.assertEqual(p.registry["models"]["enemies/e"]["role"], "horde")
        self.assertIn("| heroes/h | hero | 2 | 22 |", p.stdout)


@unittest.skipUnless(os.path.exists(os.path.join(PROJECT, "NestOfThorns", "manifest.tsv")), "no NestOfThorns")
class RealDataTests(unittest.TestCase):
    registry = None

    @classmethod
    def setUpClass(cls):
        cls.out = tempfile.mkdtemp()
        with contextlib.redirect_stdout(io.StringIO()):
            cls.code = ar.main(["--out", cls.out])
        cls.registry = read_json(os.path.join(cls.out, "AssetRegistry.json"))

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.out, ignore_errors=True)

    def test_exit_code_is_zero(self):
        self.assertEqual(self.code, 0)

    def test_records_cover_manifest_and_map(self):
        rows = len(read_bytes(os.path.join(PROJECT, "NestOfThorns", "manifest.tsv")).splitlines()) - 1
        self.assertGreaterEqual(len(self.registry["assets"]), rows + 1)
        self.assertIn("maps/scenes/crownfall/survivors_arena.vpk:maps/scenes/crownfall/survivors_arena/world.vwrld_c",
                      self.registry["assets"])

    def test_dragonus_body_is_linked_to_its_glb_guid(self):
        record = self.registry["assets"]["models/heroes/skywrath_mage/skywrath_mage.vmdl_c"]
        guids = {o["path"]: o["guid"] for o in record["outputs"]}
        self.assertEqual(guids["Assets/Game/Art/Converted/models/heroes/skywrath_mage/skywrath_mage.glb"],
                         "9ba625c5010514a5b96c0e165405d258")
        self.assertIn("heroes/dragonus", record["usedBy"])
        self.assertEqual(record["status"], "converted")

    def test_model_views_exist_for_looks_and_enemies(self):
        enemies = read_json(os.path.join(PROJECT, CONVERTED, "enemies.json"))
        for key in ["heroes/dragonus", "enemies/imperia"] + [f"enemies/{k}" for k in enemies]:
            self.assertGreater(self.registry["models"][key]["visibleTriangles"], 0, key)

    def test_required_entities_have_records_unless_reported(self):
        registry = read_json(os.path.join(PROJECT, "Research/FullGame/ContentRegistry.json"))
        used = {key for record in self.registry["assets"].values() for key in record["usedBy"]}
        missing = set(self.registry["summary"]["missingRequired"])
        for key, d in registry["definitions"].items():
            if ar.is_required(d):
                self.assertTrue(key in used or key in missing, key)
                self.assertFalse(key in used and key in missing, key)


if __name__ == "__main__":
    unittest.main()
