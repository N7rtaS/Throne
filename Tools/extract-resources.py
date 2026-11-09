#!/usr/bin/env python3
"""Extract Nest of Thorns resources from a local Dota 2 install into NestOfThorns/.

Collects:
  * every pak01 path containing "survivors" and every path already in the manifest;
  * resource references from Research/resource-dependencies.json;
  * hero models for the heroes used by the mode (HeroID -> scripts/npc/heroes/*.txt),
    including all models in the hero's model folder (default wearables);
  * cosmetic item assets from m_vecEconItems (scripts/items/items_game.txt);
  * soundevents_crownfall and every .vsnd named in decoded sound events (Research/Json/soundevents_*.json);
  * the arena map VPK maps/scenes/crownfall/survivors_arena.vpk;
  * the transitive closure of RERL references from all of the above.

Every written file is verified by CRC32 against the VPK. Writes NestOfThorns/manifest.tsv
and Research/extract-report.json.

Usage:
  python3 Tools/extract-resources.py [--dota PATH] [--dry-run]
Dota path: --dota, then $THORN_DOTA_DIR, then the default Steam location on macOS.
"""
import argparse
import collections
import json
import os
import re
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from vpk import Vpk, compiled_path, crc32, external_references  # noqa: E402

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUTPUT = os.path.join(PROJECT, "NestOfThorns")
MANIFEST = os.path.join(OUTPUT, "manifest.tsv")
REPORT = os.path.join(PROJECT, "Research", "extract-report.json")
DEFAULT_DOTA = os.path.expanduser(
    "~/Library/Application Support/Steam/steamapps/common/dota 2 beta/game/dota")
MAPS = ["maps/scenes/crownfall/survivors_arena.vpk"]


def parse_args():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dota", default=os.environ.get("THORN_DOTA_DIR", DEFAULT_DOTA),
                        help="path to 'dota 2 beta/game/dota'")
    parser.add_argument("--dry-run", action="store_true", help="report only, write nothing")
    return parser.parse_args()


def read_manifest():
    rows = {}
    if os.path.exists(MANIFEST):
        with open(MANIFEST, encoding="utf-8") as f:
            next(f, None)
            for line in f:
                parts = line.rstrip("\n").split("\t")
                if parts and parts[0]:
                    rows[parts[0]] = parts
    return rows


def hero_models(pak):
    """HeroID -> base model path from scripts/npc/heroes/npc_dota_hero_*.txt."""
    result = {}
    for path, entry in pak.entries.items():
        if not re.fullmatch(r"scripts/npc/heroes/npc_dota_hero_\w+\.txt", path):
            continue
        text = pak.read(entry).decode("utf-8", "replace")
        hero_id = re.search(r'"HeroID"\s+"(\d+)"', text)
        model = re.search(r'"Model"\s+"([^"]+)"', text)
        if hero_id and model:
            result[int(hero_id.group(1))] = model.group(1)
    return result


def main():
    args = parse_args()
    pak_path = os.path.join(args.dota, "pak01_dir.vpk")
    if not os.path.exists(pak_path):
        sys.exit(f"Dota 2 not found: {pak_path}\nPass --dota or set THORN_DOTA_DIR.")
    pak = Vpk(pak_path)
    print(f"pak01: {len(pak.entries)} entries")

    reasons = {}

    def seed(path, reason):
        reasons.setdefault(path, reason)

    old_manifest = read_manifest()
    for path, row in old_manifest.items():
        if len(row) < 3 or row[2] != "map":  # map VPKs are handled separately
            seed(path, row[4] if len(row) > 4 else "manifest")
    for path in pak.entries:
        if "survivors" in path.lower():
            seed(path, "survivors")
    deps_file = os.path.join(PROJECT, "Research", "resource-dependencies.json")
    for item in json.load(open(deps_file, encoding="utf-8")):
        seed(compiled_path(item["path"]), "vdata-reference")

    # Heroes and hero-model enemies used by the mode.
    json_dir = os.path.join(PROJECT, "Research", "Json")
    hero_ids = set()
    for name, field in (("survivors_heroes.json", None), ("survivors_enemies.json", "m_bUseHeroModel")):
        for key, value in json.load(open(os.path.join(json_dir, name), encoding="utf-8")).items():
            if isinstance(value, dict) and "m_nDOTAHeroID" in value and (field is None or value.get(field)):
                hero_ids.add(value["m_nDOTAHeroID"])
    models_by_id = hero_models(pak)
    hero_folders = {}
    for hero_id in sorted(hero_ids):
        model = models_by_id.get(hero_id)
        if not model:
            print(f"warning: no model for DOTA hero {hero_id}")
            continue
        folder = os.path.dirname(model) + "/"
        hero_folders[hero_id] = folder
        for path in pak.entries:
            if path.startswith(folder) and path.endswith(".vmdl_c"):
                seed(path, f"hero-{hero_id}")

    # Cosmetic sets worn in the mode (m_vecEconItems -> items_game.txt). Takes every model, particle
    # and material path in the item block, including style and visual-modifier assets.
    econ = collections.defaultdict(set)
    for name in ("survivors_heroes.json", "survivors_enemies.json"):
        for key, value in json.load(open(os.path.join(json_dir, name), encoding="utf-8")).items():
            if isinstance(value, dict):
                for item in value.get("m_vecEconItems") or []:
                    econ[int(item)].add(key)
    econ_items = {}
    if econ:
        items_game = pak.read(pak.entries["scripts/items/items_game.txt"]).decode("utf-8", "replace")
        starts = {int(m.group(1)): m.end() for m in re.finditer(r'\n\t\t"(\d+)"\s*\n\t\t\{', items_game)}
        for item_id, users in sorted(econ.items()):
            start = starts.get(item_id)
            if start is None:
                print(f"warning: econ item {item_id} not found in items_game.txt")
                continue
            block = items_game[start:items_game.find("\n\t\t}", start)]
            name = re.search(r'"name"\s+"([^"]+)"', block)
            assets = sorted(set(re.findall(r'"((?:models|particles|materials)/[^"]+\.(?:vmdl|vpcf|vmat))"', block)))
            econ_items[item_id] = {"name": name.group(1) if name else None, "usedBy": sorted(users), "assets": assets}
            for asset in assets:
                seed(compiled_path(asset), f"econ-{item_id}")

    # Sound events: the shared Crownfall file, plus every .vsnd named in decoded sound events
    # (Research/Json/soundevents_*.json, produced by decode-data.sh; run extraction again after decoding).
    seed("soundevents/soundevents_crownfall.vsndevts_c", "soundevents")
    sound_files = set()
    for name in sorted(os.listdir(json_dir)):
        if name.startswith("soundevents_") and name.endswith(".json"):
            text = open(os.path.join(json_dir, name), encoding="utf-8").read()
            sound_files.update(re.findall(r'"(sounds/[^"]+\.vsnd)"', text))
    for path in sorted(sound_files):
        seed(compiled_path(path), "sound")

    # Map VPKs: copied whole; their internal files contribute references.
    maps = {}
    for map_path in MAPS:
        full = os.path.join(args.dota, map_path)
        if not os.path.exists(full):
            print(f"warning: map not found: {full}")
            continue
        maps[map_path] = Vpk(full)

    # Transitive closure over RERL references.
    queue = collections.deque(reasons)
    for map_vpk in maps.values():
        for entry in map_vpk.entries.values():
            if entry.path.endswith("_c"):
                for ref in external_references(map_vpk.read(entry)):
                    queue.append(compiled_path(ref))
                    reasons.setdefault(compiled_path(ref), "map-dependency")
    seen, unresolved, referenced_by, inside_maps = set(), set(), {}, set()
    while queue:
        path = queue.popleft()
        if path in seen:
            continue
        seen.add(path)
        source, entry = pak, pak.entries.get(path)
        if entry is None:
            source, entry = next(((m, m.entries[path]) for m in maps.values() if path in m.entries), (None, None))
            if entry is None:
                unresolved.add(path)
                continue
            inside_maps.add(path)
        if not path.endswith("_c"):
            continue
        for ref in external_references(source.read(entry)):
            target = compiled_path(ref)
            referenced_by.setdefault(target, path)
            reasons.setdefault(target, "dependency")
            if target not in seen:
                queue.append(target)

    files = sorted(p for p in seen if p in pak.entries)
    new_files = [p for p in files if p not in old_manifest]
    total = sum(pak.entries[p].size for p in files) + sum(os.path.getsize(m.path) for m in maps.values())
    print(f"files: {len(files)} (new {len(new_files)}), maps: {len(maps)}, "
          f"total {total / 2**20:.1f} MiB, unresolved references: {len(unresolved)}")

    written = verified = 0
    if not args.dry_run:
        for path in files:
            entry = pak.entries[path]
            target = os.path.join(OUTPUT, path)
            if os.path.exists(target) and os.path.getsize(target) == entry.size:
                with open(target, "rb") as f:
                    if crc32(f.read()) == entry.crc:
                        verified += 1
                        continue
            os.makedirs(os.path.dirname(target), exist_ok=True)
            data = pak.read(entry)  # raises on CRC mismatch
            with open(target + ".tmp", "wb") as f:
                f.write(data)
            os.replace(target + ".tmp", target)
            written += 1
        for map_path, map_vpk in maps.items():
            target = os.path.join(OUTPUT, map_path)
            os.makedirs(os.path.dirname(target), exist_ok=True)
            if not (os.path.exists(target) and os.path.getsize(target) == os.path.getsize(map_vpk.path)):
                shutil.copyfile(map_vpk.path, target + ".tmp")
                os.replace(target + ".tmp", target)
                written += 1
            for entry in map_vpk.entries.values():
                map_vpk.read(entry)  # verify the copy's source CRCs
        with open(MANIFEST + ".tmp", "w", encoding="utf-8", newline="\n") as f:
            f.write("path\tbytes\tarchive\tcrc32\treason\n")
            for path in files:
                entry = pak.entries[path]
                f.write(f"{path}\t{entry.size}\t{entry.archive}\t{entry.crc:08x}\t{reasons.get(path, 'dependency')}\n")
            for map_path, map_vpk in maps.items():
                with open(map_vpk.path, "rb") as m:
                    f.write(f"{map_path}\t{os.path.getsize(map_vpk.path)}\tmap\t{crc32(m.read()):08x}\tmap\n")
        os.replace(MANIFEST + ".tmp", MANIFEST)
        print(f"written: {written}, already present and verified: {verified}")

    by_reason = collections.Counter(reasons.get(p, "dependency") for p in files)
    by_ext = collections.Counter(p.rsplit(".", 1)[-1] for p in new_files)
    report = {
        "dota": args.dota,
        "dryRun": args.dry_run,
        "files": len(files),
        "newFiles": len(new_files),
        "bytes": total,
        "byReason": dict(by_reason),
        "newByExtension": dict(by_ext.most_common()),
        "heroModelFolders": {str(k): v for k, v in hero_folders.items()},
        "econItems": {str(k): v for k, v in econ_items.items()},
        "maps": {k: {"entries": len(v.entries), "bytes": os.path.getsize(v.path)} for k, v in maps.items()},
        "referencesResolvedInsideMaps": len(inside_maps),
        "unresolved": sorted(unresolved),
        "unresolvedReferencedBy": {p: referenced_by.get(p) for p in sorted(unresolved)},
        "soundFilesNamed": len(sound_files),
        "notCovered": [
            "Sound files are taken only from decoded soundevents_survivors and soundevents_crownfall "
            "(run decode-data.sh, then this script again). Events defined in other soundevents files are not followed.",
            "Econ item styles (m_unStyleIndex) are not interpreted: all assets named in the item block are "
            "taken, but which of them a style uses must be resolved during conversion.",
        ],
    }
    if not args.dry_run:
        with open(REPORT, "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=1)
        print(f"report: {os.path.relpath(REPORT, PROJECT)}")
    else:
        print(json.dumps({k: v for k, v in report.items() if k not in ("unresolved", "unresolvedReferencedBy")},
                         ensure_ascii=False, indent=1))


if __name__ == "__main__":
    main()
