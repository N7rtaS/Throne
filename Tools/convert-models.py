#!/usr/bin/env python3
"""Convert models used by the mobile build to glTF (.glb) with Source2Viewer CLI.

Output: UnityProject/Assets/Game/Art/Converted/<original path>.glb (+ PNG textures).
The folder is generated from the local Dota 2 install and is not versioned (like NestOfThorns/).

Models (stage 2 trial, extended as content is added):
  * hero body + cosmetic set from m_vecEconItems (Research/extract-report.json);
  * enemy models from m_vecModelNames of the listed enemies.

Glb units are metres (Source units × 0.0254); the game scales them to its own units.

Usage: python3 Tools/convert-models.py [--dota PATH] [--heroes dragonus] [--enemies skeleton]
"""
import argparse
import json
import os
import subprocess
import sys

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(PROJECT, "UnityProject", "Assets", "Game", "Art", "Converted")
CLI = os.environ.get("THORN_S2V_CLI", os.path.join(PROJECT, "Tools", "Source2Viewer", "cli-macos", "Source2Viewer-CLI"))
DEFAULT_DOTA = os.path.expanduser("~/Library/Application Support/Steam/steamapps/common/dota 2 beta/game/dota")
HERO_MODELS = {101: "models/heroes/skywrath_mage/skywrath_mage.vmdl_c", 20: "models/heroes/vengeful/vengeful.vmdl_c",
               145: "models/heroes/kez/kez_base.vmdl_c"}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dota", default=os.environ.get("THORN_DOTA_DIR", DEFAULT_DOTA))
    parser.add_argument("--heroes", nargs="*", default=["dragonus"])
    parser.add_argument("--enemies", nargs="*", default=None,
                        help="enemy keys; default: every enemy reachable in Research/FullGame/ContentRegistry.json")
    args = parser.parse_args()
    if args.enemies is None:
        registry = json.load(open(os.path.join(PROJECT, "Research/FullGame/ContentRegistry.json"), encoding="utf-8"))
        args.enemies = sorted(e["key"] for e in registry["definitions"].values()
                              if e["category"] == "enemies" and e["reachable"])

    heroes = json.load(open(os.path.join(PROJECT, "Research/Json/survivors_heroes.json"), encoding="utf-8"))
    enemies = json.load(open(os.path.join(PROJECT, "Research/Json/survivors_enemies.json"), encoding="utf-8"))
    report = json.load(open(os.path.join(PROJECT, "Research/extract-report.json"), encoding="utf-8"))

    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from vpk import Vpk
    import re
    pak_vpk = Vpk(os.path.join(args.dota, "pak01_dir.vpk"))
    items_game = pak_vpk.read(pak_vpk.entries["scripts/items/items_game.txt"]).decode("utf-8", "replace")

    def worn_model(item_id):
        """model_player of an econ item: the mesh the hero actually wears."""
        start = re.search(r'\n\t\t"%d"\s*\n\t\t\{' % item_id, items_game)
        if not start:
            return None
        block = items_game[start.end():items_game.find("\n\t\t}", start.end())]
        found = re.search(r'"model_player"\s+"([^"]+)"', block)
        return found.group(1) if found else None

    models, looks = [], {}
    for key in args.heroes:
        hero = heroes[key]
        body = HERO_MODELS[hero["m_nDOTAHeroID"]]
        models.append(body)
        worn = []
        for item in hero.get("m_vecEconItems", []):
            assets = report["econItems"].get(str(item), {}).get("assets", [])
            models += [a + "_c" for a in assets if a.endswith(".vmdl")]
            model = worn_model(item)
            if model:
                worn.append(model[:-len(".vmdl")] + ".glb")
        looks[key] = {"body": body[:-len(".vmdl_c")] + ".glb", "wearables": worn, "style": hero.get("m_unStyleIndex", 0)}
    # Enemies drawn with a hero model (Imperia: Vengeful Spirit with the Resurrection of Shen, style 2).
    for key, enemy in enemies.items():
        if not isinstance(enemy, dict) or not enemy.get("m_bUseHeroModel"):
            continue
        body = HERO_MODELS.get(enemy.get("m_nDOTAHeroID"))
        if not body:
            continue
        models.append(body)
        worn = []
        for item in enemy.get("m_vecEconItems", []):
            assets = report["econItems"].get(str(item), {}).get("assets", [])
            models += [a + "_c" for a in assets if a.endswith(".vmdl")]
            model = worn_model(item)
            if model:
                worn.append(model[:-len(".vmdl")] + ".glb")
        looks[key] = {"body": body[:-len(".vmdl_c")] + ".glb", "wearables": worn, "style": enemy.get("m_unStyleIndex", 0),
                      "scale": enemy.get("m_flModelScale", 1)}
    enemy_looks = {}
    for key in args.enemies:
        names = []
        for name in enemies[key].get("m_vecModelNames", []):
            path = name["$value"] if isinstance(name, dict) else name
            models.append(path + "_c")
            names.append(path[:-len(".vmdl")] + ".glb")
        if names:
            enemy_looks[key] = {"models": names, "scale": enemies[key].get("m_flModelScale", 1),
                                "skin": enemies[key].get("m_sSkinName")}
    models = sorted(set(models))

    os.makedirs(OUT, exist_ok=True)
    pak = os.path.join(args.dota, "pak01_dir.vpk")
    # One CLI call: VRF resolves materials and textures from the VPK.
    command = [CLI, "-i", pak, "-f", ",".join(models), "-o", OUT, "-d",
               "--gltf_export_format", "glb", "--gltf_export_materials", "--gltf_textures_adapt",
               "--gltf_export_animations"]
    result = subprocess.run(command, capture_output=True, text=True)
    failures = [line for line in result.stdout.splitlines() + result.stderr.splitlines() if "Failed" in line or "rror" in line]
    written = [m for m in models if os.path.exists(os.path.join(OUT, m[:-len(".vmdl_c")] + ".glb"))]
    print(f"models requested {len(models)}, written {len(written)}, warnings {len(failures)}")
    for line in failures[:20]:
        print("  " + line)
    # Arena (only used in the Editor to bake ground tiles, see ArenaBaker).
    arena_vpk = os.path.join(args.dota, "maps/scenes/crownfall/survivors_arena.vpk")
    arena_glb = os.path.join(OUT, "maps/scenes/crownfall/survivors_arena/world.glb")
    if not os.path.exists(arena_glb):
        subprocess.run([CLI, "-i", arena_vpk, "-f", "maps/scenes/crownfall/survivors_arena/world.vwrld_c", "-o", OUT, "-d",
                        "--gltf_export_format", "glb", "--gltf_export_materials", "--gltf_textures_adapt"],
                       capture_output=True, text=True)
    print("arena:", "ok" if os.path.exists(arena_glb) else "MISSING")

    with open(os.path.join(OUT, "looks.json"), "w", encoding="utf-8") as f:
        json.dump(looks, f, ensure_ascii=False, indent=1)
    with open(os.path.join(OUT, "enemies.json"), "w", encoding="utf-8") as f:
        json.dump(enemy_looks, f, ensure_ascii=False, indent=1)
    export_materials(pak_vpk, pak, models)
    missing = sorted(set(models) - set(written))
    if missing:
        print("missing:", missing)
        sys.exit(1)


def export_materials(pak_vpk, pak, models):
    """Dota hero.vfx materials: parameters and compiled (channel-packed) textures for ThornThrone/DotaHero.

    Writes Converted/materials.json: { material name: { path, shader, ints, floats, vectors, textures } },
    texture paths are relative to Converted/ and point to PNGs exported from the compiled vtex files.
    """
    import re
    import tempfile
    from vpk import external_references
    vmats = set()
    for model in models:
        entry = pak_vpk.entries.get(model)
        if entry:
            vmats |= {r + "_c" for r in external_references(pak_vpk.read(entry)) if r.endswith(".vmat")}
    vmats = sorted(v for v in vmats if v in pak_vpk.entries)

    decoded = tempfile.mkdtemp(prefix="thorn-vmat-")
    subprocess.run([CLI, "-i", pak, "-f", ",".join(vmats), "-o", decoded, "-d"], capture_output=True, text=True)
    materials, textures = {}, set()
    for vmat in vmats:
        text_path = os.path.join(decoded, vmat[:-2])
        if not os.path.exists(text_path):
            print("  material not decoded:", vmat)
            continue
        text = open(text_path, encoding="utf-8", errors="replace").read()
        pairs = dict(re.findall(r'^\s*"([^"]+)"\s+"([^"]*)"', text, re.M))
        compiled = text[text.find('"Compiled Textures"'):] if '"Compiled Textures"' in text else ""
        tex = dict(re.findall(r'"(g_t\w+)"\s+"([^"]+\.vtex)"', compiled))
        entry = {"path": vmat[:-2], "shader": pairs.get("shader"), "ints": {}, "floats": {}, "vectors": {}, "textures": {}}
        for key, value in pairs.items():
            if key.startswith("F_"):
                entry["ints"][key] = int(float(value))
            elif key.startswith("g_fl") or key.startswith("g_n") or key.startswith("g_b"):
                try:
                    entry["floats"][key] = float(value)
                except ValueError:
                    pass
            elif key.startswith("g_v") and value.startswith("["):
                entry["vectors"][key] = [float(x) for x in value.strip("[]").split()]
        for slot, path in tex.items():
            entry["textures"][slot] = path[:-len(".vtex")] + ".png"
            textures.add(path + "_c")
        materials[os.path.splitext(os.path.basename(vmat[:-2]))[0]] = entry

    textures = sorted(t for t in textures if t in pak_vpk.entries)
    subprocess.run([CLI, "-i", pak, "-f", ",".join(textures), "-o", OUT, "-d"], capture_output=True, text=True)
    missing = []
    for entry in materials.values():
        for slot, png in list(entry["textures"].items()):
            if not os.path.exists(os.path.join(OUT, png)):
                missing.append(png)
                del entry["textures"][slot]
    with open(os.path.join(OUT, "materials.json"), "w", encoding="utf-8") as f:
        json.dump(materials, f, ensure_ascii=False, indent=1)
    print(f"materials {len(materials)}, textures {len(textures)}, missing pngs {len(missing)}")
    for m in missing[:10]:
        print("  missing", m)


if __name__ == "__main__":
    main()
