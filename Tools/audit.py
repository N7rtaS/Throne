#!/usr/bin/env python3
"""Stage-1 audit: content registry, sound registry and source versions.

Reads Research/Json (decode-data.sh), NestOfThorns (extract-resources.py), the Dota client binary
(class names only, via `strings`) and the Unity prototype sources (to record what it covers).

Writes:
  Research/FullGame/ContentRegistry.json
  Research/FullGame/SoundRegistry.json
  Research/FullGame/SourceVersions.json

Usage: python3 Tools/audit.py [--dota PATH]
"""
import argparse
import collections
import glob
import hashlib
import json
import os
import platform
import re
import subprocess
import sys

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
JSON_DIR = os.path.join(PROJECT, "Research", "Json")
SOURCE = os.path.join(PROJECT, "NestOfThorns")
OUT = os.path.join(PROJECT, "Research", "FullGame")
UNITY = os.path.join(PROJECT, "UnityProject")
DEFAULT_DOTA = os.path.expanduser(
    "~/Library/Application Support/Steam/steamapps/common/dota 2 beta/game/dota")
CATEGORIES = ["game_modes", "levels", "difficulty", "heroes", "powerups", "enemies", "spawners", "pickups",
              "attributes"]
ID_FIELDS = {"powerups": "m_unPowerUpID", "enemies": "m_unEnemyID", "pickups": "m_unPickupID",
             "heroes": "m_unHeroID", "levels": "m_unLevelID", "game_modes": "m_unGameModeID"}
ATTRIBUTE = re.compile(r"k_eSurvivorsAttribute_(\w+)")


def load(category):
    data = json.load(open(os.path.join(JSON_DIR, f"survivors_{category}.json"), encoding="utf-8"))
    return data.get("generic_data_type"), {k: v for k, v in data.items() if isinstance(v, dict) and not k.startswith("_")}


def localization():
    """Russian strings for all survivors_* VData localization files."""
    result = {}
    for path in glob.glob(os.path.join(SOURCE, "resource/localization/vdata/survivors_*/*_russian.txt")):
        text = open(path, encoding="utf-8-sig").read()
        for key, value in re.findall(r'^\s*"([^"]+)"\s+"((?:\\.|[^"\\])*)"', text, re.M):
            result[key] = value
    return result


def walk(value):
    if isinstance(value, dict):
        yield value
        for item in value.values():
            yield from walk(item)
    elif isinstance(value, list):
        for item in value:
            yield from walk(item)


def resources(value):
    found = set()
    for node in walk(value):
        if node.get("$type") == "resource_name" and isinstance(node.get("$value"), str) and node["$value"]:
            found.add(node["$value"])
    return sorted(found)


def native_classes(dota):
    binary = os.path.join(dota, "bin", "osx64", "libclient.dylib")
    if not os.path.exists(binary):
        return None, binary
    output = subprocess.run(["strings", "-a", binary], capture_output=True, text=True, errors="replace").stdout
    return NativeStrings(output), binary


class NativeStrings:
    """Strings of the client binary. `in` matches whole lines; class() finds C++ class names,
    including inside mangled symbols (followed by digits or upper-case method names)."""

    def __init__(self, text):
        self.text = text
        self.lines = set(text.splitlines())

    def __contains__(self, item):
        return item in self.lines

    def has_class(self, name):
        return re.search(re.escape(name) + r"(?![a-z_])", self.text) is not None

    def runtime_class(self, definition_class):
        if not definition_class or "Definition" not in definition_class:
            return None
        base, _, suffix = definition_class.partition("Definition")
        if not suffix:  # CSurvivorsEnemyDefinition -> CSurvivorsEnemy, CSurvivorsSpawnerGolemDefinition -> CSurvivorsSpawnerGolem
            return base if self.has_class(base) else None
        kind = base.replace("CSurvivors", "")  # PowerUp, Enemy, ...
        name = suffix.lstrip("_")
        candidates = [base + suffix, f"CSurvivors{kind}{name}", f"CSurvivors{name}"]
        return next((c for c in candidates if self.has_class(c)), None)


def prototype_coverage():
    """What the friend's prototype implements, read from its sources."""
    game = open(os.path.join(UNITY, "Assets/Game/Scripts/Gameplay/PrototypeGame.cs"), encoding="utf-8").read()
    build = open(os.path.join(UNITY, "Assets/Game/Scripts/Gameplay/DragonusBuild.cs"), encoding="utf-8").read()
    enemies = set(re.findall(r'"(\w+)"', re.search(r"supportedEnemies = new HashSet<string> \{(.*?)\};", game, re.S).group(1)))
    passives = set(re.findall(r'"(\w+)"', re.search(r"Passives = \{(.*?)\};", build, re.S).group(1)))
    recipes = set(re.findall(r'"(\w+)"', re.search(r"Recipes = \{(.*?)\};", build, re.S).group(1)))
    attributes = set(re.findall(r'(?:Hero|Global|Spell)\("(\w+)"\)', build))
    return enemies, passives | recipes | {"arcane_bolt", "skywrath_mage_innate"}, attributes


def powerup_role(value):
    if value.get("m_bIsGold"):
        return "service:gold"
    if value.get("m_bRollable") is False:
        return "service:not-rollable"
    if value.get("m_bIsInnate"):
        return "innate"
    if value.get("m_vecRecipeItems"):
        return "recipe"
    if value.get("m_bIsPassive"):
        return "passive-item"
    if value["_class"] != "CSurvivorsPowerUpDefinition":
        return "active-ability"
    return "unknown"


def enemy_role(key, value, split_targets):
    if value["_class"].endswith("ImperiaBoss"):
        return "final-boss"
    if value.get("m_bPlayerFriendly"):
        return "friendly"
    if value.get("m_bIsDestructible"):
        return "destructible"
    if value.get("m_bIsElite"):
        return "elite"
    if value.get("m_eMovementBehavior") == "ENEMY_MOVEMENT_BEHAVIOR_STATIONARY":
        return "stationary"
    if value.get("m_unEnemyID") in split_targets:
        return "split-spawn"
    return "horde"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dota", default=os.environ.get("THORN_DOTA_DIR", DEFAULT_DOTA))
    args = parser.parse_args()
    os.makedirs(OUT, exist_ok=True)

    types, defs = {}, {}
    for category in CATEGORIES:
        types[category], defs[category] = load(category)
    loc = localization()
    native, binary = native_classes(args.dota)
    proto_enemies, proto_powerups, proto_attributes = prototype_coverage()
    by_id = {c: {v[f]: k for k, v in defs[c].items() if f in v} for c, f in ID_FIELDS.items()}

    registry = {}
    for category in CATEGORIES:
        for key, value in defs[category].items():
            cls = value.get("_class", types[category])
            registry[f"{category}/{key}"] = {
                "category": category, "key": key, "id": value.get(ID_FIELDS.get(category, ""), None),
                "class": cls,
                "nativeRuntimeClass": native.runtime_class(cls) if native else None,
                "nativeDefinitionClassFound": bool(native and cls and native.has_class(cls)),
                "role": None, "name": None, "references": set(), "referencedBy": set(),
                "attributes": sorted(set(ATTRIBUTE.findall(json.dumps(value)))),
                "resources": resources(value), "reachable": False,
                "prototype": "none", "testScenario": None, "notes": [],
            }

    def ref(source, category, key, note=None):
        target = f"{category}/{key}"
        if target not in registry:
            registry[source]["notes"].append(f"unresolved reference {target}" + (f" ({note})" if note else ""))
            return
        registry[source]["references"].add(target)
        registry[target]["referencedBy"].add(source)

    # References.
    for key, value in defs["game_modes"].items():
        src = f"game_modes/{key}"
        ref(src, "levels", value["m_sLevelName"])
        for room in value.get("m_vecEliteRoomChoices", []):
            ref(src, "spawners", room, "elite room")
        for node in walk(value.get("m_luckyLootTable", {})):
            if "m_unPickupID" in node:
                ref(src, "pickups", by_id["pickups"].get(node["m_unPickupID"], node["m_unPickupID"]))
        if value.get("m_nInitialMagnetSpawns"):
            ref(src, "pickups", "magnet", "m_nInitialMagnetSpawns")
        for d in defs["difficulty"]:
            ref(src, "difficulty", d, "selectable difficulty")
        for h in defs["heroes"]:
            ref(src, "heroes", h, "selectable hero")
    for key, value in defs["levels"].items():
        for event in value.get("m_vecEvents", []) + value.get("m_vecBossEvents", []):
            ref(f"levels/{key}", "spawners", event["m_sSpawnerName"])
    for key, value in defs["difficulty"].items():
        for event in value.get("m_vecAdditionalEnemyEvents", []):
            ref(f"difficulty/{key}", "spawners", event["m_sSpawnerName"])
    for key, value in defs["heroes"].items():
        for field in ("m_vecStartingPowerUps", "m_vecInnatePowerUps"):
            for pid in value.get(field, []):
                ref(f"heroes/{key}", "powerups", by_id["powerups"].get(pid, pid), field)
    for key, value in defs["spawners"].items():
        ref(f"spawners/{key}", "enemies", value["m_sEnemyName"])
    split_targets = set()
    for key, value in defs["enemies"].items():
        src = f"enemies/{key}"
        for chance in value.get("m_vecPickupChances", []):
            ref(src, "pickups", chance["m_sPickupName"])
        for node in walk([value.get("m_fullLootTable"), value.get("m_vecLootTable")]):
            if "m_unPickupID" in node:
                ref(src, "pickups", by_id["pickups"].get(node["m_unPickupID"], node["m_unPickupID"]))
        for spawner in value.get("m_vecDemonPortalSpawners", []):
            ref(src, "spawners", spawner, "boss demon portal")
        if "m_unSplitOnDeathEnemyID" in value:
            split_targets.add(value["m_unSplitOnDeathEnemyID"])
            ref(src, "enemies", by_id["enemies"].get(value["m_unSplitOnDeathEnemyID"], value["m_unSplitOnDeathEnemyID"]), "split on death")
    for key, value in defs["powerups"].items():
        src = f"powerups/{key}"
        for pid in value.get("m_vecRecipeItems", []):
            ref(src, "powerups", by_id["powerups"].get(pid, pid), "recipe component")
        if "m_unSpawnPickupOnKillID" in value:
            ref(src, "pickups", by_id["pickups"].get(value["m_unSpawnPickupOnKillID"]), "spawn on kill")
        if "m_unHeroID" in value:  # a Dota hero ID (14 = Pudge), not a survivors hero
            registry[src]["notes"].append(f"m_unHeroID {value['m_unHeroID']} is a Dota hero ID")
    for entry in registry.values():
        for attr in entry["attributes"]:
            if f"attributes/k_eSurvivorsAttribute_{attr}" in registry:
                ref(f"{entry['category']}/{entry['key']}", "attributes", f"k_eSurvivorsAttribute_{attr}")

    # Reachability from the game mode; rollable powerups are reachable through level-up choices.
    rollable = {f"powerups/{k}" for k, v in defs["powerups"].items()
                if powerup_role(v) in ("active-ability", "passive-item", "recipe")}
    # Roots without a data reference but with a known source:
    #  * meta-progression attributes -> permanent upgrades;
    #  * the boss class (ImperiaBoss) -> spawned by the mode after m_flFirstFloorTimeLimit;
    #  * enemies sharing a specific class with a powerup (Snotty) -> summoned by that powerup.
    meta = {f"attributes/{k}" for k, v in defs["attributes"].items() if v.get("m_vecMetaProgressionTiers")}
    bosses = {f"enemies/{k}" for k, v in defs["enemies"].items() if v["_class"].endswith("ImperiaBoss")}
    for boss in bosses:
        registry[boss]["notes"].append("spawned by the mode after m_flFirstFloorTimeLimit (no spawner in data)")
    for pkey, pvalue in defs["powerups"].items():
        suffix = pvalue["_class"].partition("Definition")[2]
        for ekey, evalue in defs["enemies"].items():
            if suffix and evalue["_class"].endswith(suffix):
                ref(f"powerups/{pkey}", "enemies", ekey, "summoned by the powerup (shared class)")
    queue = [f"game_modes/{k}" for k in defs["game_modes"]] + sorted(rollable) + sorted(meta) + sorted(bosses)
    while queue:
        node = queue.pop()
        if registry[node]["reachable"]:
            continue
        registry[node]["reachable"] = True
        queue.extend(registry[node]["references"])
    for entry in registry.values():
        entry["nameInClientBinary"] = bool(native and entry["key"] in native)
        if not entry["reachable"]:
            entry["notes"].append(
                "no reference from the game mode in data; "
                + ("the name appears in the client binary, likely spawned/used by native code"
                   if entry["nameInClientBinary"] else "possibly unused or created by native code under another name")
                + " - verify by observation")

    # Roles, names, prototype coverage.
    for entry in registry.values():
        c, k, v = entry["category"], entry["key"], defs[entry["category"]][entry["key"]]
        if c == "powerups":
            entry["role"] = powerup_role(v)
            entry["name"] = loc.get(v.get("m_sLocAbilityName", "").lstrip("#"))
            entry["source"] = v.get("m_sSource")
            entry["prototype"] = "partial" if k in proto_powerups else "none"
        elif c == "enemies":
            entry["role"] = enemy_role(k, v, split_targets)
            entry["name"] = loc.get(v.get("m_sDisplayName", "").lstrip("#")) or v.get("m_sDisplayName")
            entry["movement"] = v.get("m_eMovementBehavior")
            entry["prototype"] = "partial" if k in proto_enemies or k == "imperia" else "none"
        elif c == "spawners":
            entry["role"] = "elite-room" if f"spawners/{k}" in registry["game_modes/crownfall_act4"]["references"] else "spawner"
            entry["spawnBehavior"] = v.get("m_eSpawnBehavior")
            entry["prototype"] = "partial" if v.get("m_sEnemyName") in proto_enemies and v.get("m_eSpawnBehavior") == "RANDOM_DIRECTION" else "none"
        elif c == "attributes":
            name = k.replace("k_eSurvivorsAttribute_", "")
            entry["role"] = "meta-progression" if v.get("m_vecMetaProgressionTiers") else "attribute"
            entry["name"] = loc.get(v.get("m_sLocName", "").lstrip("#"))
            entry["prototype"] = "partial" if name in proto_attributes else "none"
        elif c == "heroes":
            entry["role"] = "hero"
            entry["name"] = loc.get(v.get("m_sLocDisplayName", "").lstrip("#"))
            entry["prototype"] = "partial" if k == "dragonus" else "none"
        elif c == "difficulty":
            entry["role"] = "difficulty"
            entry["name"] = loc.get(v.get("m_sLocDifficultyName", "").lstrip("#"))
        elif c == "pickups":
            entry["role"] = "pickup"
            entry["prototype"] = "partial" if k == "experience" else "none"
        else:
            entry["role"] = c[:-1]
            entry["prototype"] = "partial"
        entry["missingResources"] = [r for r in entry["resources"]
                                     if not os.path.exists(os.path.join(SOURCE, r + "_c")) and not os.path.exists(os.path.join(SOURCE, r))]
        entry["references"] = sorted(entry["references"])
        entry["referencedBy"] = sorted(entry["referencedBy"])

    # Attribute types used in data but without a definition.
    used_types = set()
    for c in CATEGORIES:
        if c != "attributes":
            used_types |= set(ATTRIBUTE.findall(json.dumps(defs[c])))
    defined = {k.replace("k_eSurvivorsAttribute_", "") for k in defs["attributes"]}
    undefined = sorted(used_types - defined)

    summary = collections.defaultdict(lambda: collections.Counter())
    for e in registry.values():
        summary[e["category"]][f"role:{e['role']}"] += 1
        summary[e["category"]][f"prototype:{e['prototype']}"] += 1
        summary[e["category"]]["reachable" if e["reachable"] else "unreachable"] += 1
    with open(os.path.join(OUT, "ContentRegistry.json"), "w", encoding="utf-8") as f:
        json.dump({
            "schema": 1,
            "statusValues": {"prototype": ["none", "partial", "implemented", "verified"]},
            "summary": {c: dict(v) for c, v in summary.items()},
            "attributesUsedWithoutDefinition": undefined,
            "nativeBinary": binary if native else None,
            "definitions": registry,
        }, f, ensure_ascii=False, indent=1)

    # Sound registry.
    sounds = {}
    for name in ("soundevents_survivors", "soundevents_crownfall"):
        path = os.path.join(JSON_DIR, f"{name}.json")
        if not os.path.exists(path):
            continue
        for event, value in json.load(open(path, encoding="utf-8")).items():
            files = value.get("vsnd_files", [])
            files = [files] if isinstance(files, str) else files
            if name == "soundevents_crownfall" and not re.search(r"surviv|nest", event, re.I):
                continue
            sounds[event] = {"file": name, "type": value.get("type"), "mixgroup": value.get("mixgroup"),
                             "vsnd": files,
                             "missing": [s for s in files if not os.path.exists(os.path.join(SOURCE, s + "_c"))]}
    data_events = set()
    for c in CATEGORIES:
        data_events |= set(re.findall(r'"m_\w*Sound\w*": "([^"]+)"', json.dumps(defs[c])))
    with open(os.path.join(OUT, "SoundRegistry.json"), "w", encoding="utf-8") as f:
        json.dump({"events": len(sounds), "files": len({s for e in sounds.values() for s in e["vsnd"]}),
                   "eventsReferencedByData": sorted(data_events),
                   "eventsReferencedByDataButUndefined": sorted(data_events - set(sounds)),
                   "definitions": sounds}, f, ensure_ascii=False, indent=1)

    # Source versions.
    def sha256(path):
        return hashlib.sha256(open(path, "rb").read()).hexdigest()

    steam = {}
    inf = os.path.join(args.dota, "steam.inf")
    if os.path.exists(inf):
        steam = dict(line.strip().split("=", 1) for line in open(inf) if "=" in line)

    def tool(cmd):
        try:
            return subprocess.run(cmd, capture_output=True, text=True).stdout.strip().splitlines()[0]
        except (OSError, IndexError):
            return None

    unity_version = open(os.path.join(UNITY, "ProjectSettings/ProjectVersion.txt")).read().split()
    with open(os.path.join(OUT, "SourceVersions.json"), "w", encoding="utf-8") as f:
        json.dump({
            "dota": {k: steam.get(k) for k in ("ClientVersion", "SourceRevision", "VersionDate", "VersionTime")},
            "dotaPak01DirSha256": sha256(os.path.join(args.dota, "pak01_dir.vpk")),
            "unity": unity_version[1] if len(unity_version) > 1 else None,
            "unityPackages": json.load(open(os.path.join(UNITY, "Packages/manifest.json")))["dependencies"],
            "tools": {
                "source2viewer": tool([os.path.join(PROJECT, "Tools/Source2Viewer/cli-macos/Source2Viewer-CLI"), "--version"]),
                "python": platform.python_version(), "node": tool(["node", "--version"]),
            },
            "inputs": {os.path.relpath(p, PROJECT): sha256(p) for p in sorted(glob.glob(os.path.join(JSON_DIR, "*.json")))},
            "manifest": {"path": "NestOfThorns/manifest.tsv",
                         "sha256": sha256(os.path.join(SOURCE, "manifest.tsv"))},
        }, f, ensure_ascii=False, indent=1)

    print(json.dumps({c: dict(v) for c, v in summary.items()}, ensure_ascii=False, indent=1))
    print("attributes used without definition:", undefined)
    print("sound events:", len(sounds), "referenced by data but undefined:", sorted(data_events - set(sounds)))
    notes = [(k, n) for k, e in registry.items() for n in e["notes"]]
    print("notes:", notes[:30])
    print("unreachable:", sorted(k for k, e in registry.items() if not e["reachable"]))
    print("missing resources:", sorted({r for e in registry.values() for r in e["missingResources"]})[:30])


if __name__ == "__main__":
    main()
