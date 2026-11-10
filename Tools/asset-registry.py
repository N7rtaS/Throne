#!/usr/bin/env python3
"""Asset registry: graph of original resources, their Unity outputs and statuses.
Reads only project files: NestOfThorns/ (+ manifest.tsv, map VPK), Research/FullGame/{ContentRegistry,SoundRegistry,
SourceVersions}.json, Research/extract-report.json, Research/Json/survivors_*.json,
UnityProject/Assets/Game/Art/Converted/** with .meta, Tools/asset-registry-overrides.json.
Never opens the Dota/Steam folders and runs no external programs. Design: .kiro/specs/asset-registry/design.md.
Writes:
  Research/FullGame/AssetRegistry.json
Prints a summary and a Markdown table of model views (reference triangle counts, no budget check).
Usage: python3 Tools/asset-registry.py [--out DIR] [--root DIR]
Exit codes: 0 ok; 1 input violation (missing file, CRC32 mismatch, output without .meta/guid, invalid override);
nothing is written on 1.
"""
import argparse
import collections
import hashlib
import json
import os
import re
import struct
import sys
import zlib

TOOLS = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(TOOLS)
sys.path.insert(0, TOOLS)
import vpk  # noqa: E402

CONVERTED = "UnityProject/Assets/Game/Art/Converted"
ASSETS_PREFIX = "UnityProject/"
OVERRIDES = "Tools/asset-registry-overrides.json"
STATUS_VALUES = ["converted", "baked", "recreated", "none", "unused"]
REQUIRED_CATEGORIES = {"heroes", "enemies", "pickups", "levels"}
REQUIRED_POWERUP_ROLES = {"active-ability", "passive-item", "recipe", "innate"}
# The only level of the mode; its map is not named in survivors_levels.json (design §5, Р3).
LEVEL_MAPS = {"levels/skywrath": "maps/scenes/crownfall/survivors_arena.vpk"}
GLTFAST_SCRIPT = "715df9372183c47e389bb6e19fbc3b52"
PLATFORMS = ("DefaultTexturePlatform", "Standalone", "Android")
# UnityEditor.TextureImporterFormat / TextureImporterCompression.
TEXTURE_FORMATS = {
    -1: "Automatic", 1: "Alpha8", 2: "ARGB16", 3: "RGB24", 4: "RGBA32", 5: "ARGB32", 7: "RGB16", 9: "R16",
    10: "DXT1", 12: "DXT5", 13: "RGBA16", 17: "RGBAHalf", 24: "BC6H", 25: "BC7", 26: "BC4", 27: "BC5",
    28: "DXT1Crunched", 29: "DXT5Crunched", 34: "ETC_RGB4", 41: "EAC_R", 43: "EAC_RG", 45: "ETC2_RGB4",
    46: "ETC2_RGB4_PUNCHTHROUGH_ALPHA", 47: "ETC2_RGBA8", 48: "ASTC_4x4", 49: "ASTC_5x5", 50: "ASTC_6x6",
    51: "ASTC_8x8", 52: "ASTC_10x10", 53: "ASTC_12x12", 62: "RG16", 63: "R8", 64: "ETC_RGB4Crunched",
    65: "ETC2_RGBA8Crunched", 66: "ASTC_HDR_4x4", 67: "ASTC_HDR_5x5", 68: "ASTC_HDR_6x6", 69: "ASTC_HDR_8x8",
    70: "ASTC_HDR_10x10", 71: "ASTC_HDR_12x12",
}
COMPRESSION = {0: "Uncompressed", 1: "Compressed", 2: "CompressedHQ", 3: "CompressedLQ"}
TOOL_CONVERT = "Tools/convert-models.py (Source2Viewer CLI, glb)"
TOOL_MATERIALS = "MobileBuilder.BuildDotaMaterials"
TOOL_ENEMY_BAKER = "EnemyBaker"
TOOL_ARENA_BAKER = "ArenaBaker"
ORM_SUFFIX = re.compile(r"_orm_\d+$")
CUBE_FACE = re.compile(r"_(up|dn|lf|rt|ft|bk)$")


def is_required(definition):
    """Required entity for the coverage check (design §5, Р2)."""
    if not (definition.get("required") and definition.get("reachable")):
        return False
    category = definition.get("category")
    if category == "powerups":
        return definition.get("role") in REQUIRED_POWERUP_ROLES
    return category in REQUIRED_CATEGORIES


def load_json(path, default=None):
    if not os.path.exists(path):
        return default
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_manifest(root):
    rows = {}
    with open(os.path.join(root, "NestOfThorns", "manifest.tsv"), encoding="utf-8") as f:
        header = f.readline().rstrip("\n").split("\t")
        for line in f:
            if not line.strip():
                continue
            row = dict(zip(header, line.rstrip("\n").split("\t")))
            rows[row["path"]] = row
    return rows


def kind_of(path):
    name = os.path.basename(path)
    return name.rsplit(".", 1)[-1] if "." in name else ""


def read_sources(root, rows, errors):
    """Records for manifest rows and map VPK entries; verifies CRC32. Returns (records, map_index)."""
    records, map_index = {}, {}
    for path, row in sorted(rows.items()):
        full = os.path.join(root, "NestOfThorns", path)
        if not os.path.exists(full):
            errors.append(f"missing in NestOfThorns: {path}")
            continue
        with open(full, "rb") as f:
            data = f.read()
        actual = format(zlib.crc32(data) & 0xFFFFFFFF, "08x")
        if actual != row["crc32"].lower():
            errors.append(f"CRC32 mismatch: {path} (manifest {row['crc32']}, file {actual})")
            continue
        records[path] = {"kind": kind_of(path), "crc32": row["crc32"].lower(), "bytes": int(row["bytes"]),
                         "archive": row.get("archive"), "reason": row.get("reason"),
                         "_refs": vpk.external_references(data) if path.endswith("_c") else []}
        if path.endswith(".vpk"):
            archive = vpk.Vpk(full)
            children = []
            for entry_path, entry in sorted(archive.entries.items()):
                key = f"{path}:{entry_path}"
                try:
                    entry_data = archive.read(entry)
                except IOError as error:
                    errors.append(f"map entry: {path}:{error}")
                    continue
                map_index[entry_path] = key
                children.append(key)
                records[key] = {"kind": kind_of(entry_path), "crc32": format(entry.crc, "08x"), "bytes": entry.size,
                                "archive": path, "reason": "map-entry",
                                "_refs": vpk.external_references(entry_data) if entry_path.endswith("_c") else []}
            records[path]["_children"] = children
    return records, map_index


def resolver(records, map_index):
    def resolve(reference, inside_map=False):
        path = vpk.compiled_path(reference)
        if inside_map and path in map_index:
            return map_index[path]
        if path in records:
            return path
        return map_index.get(path)
    return resolve


def link_graph(records, map_index, unresolved):
    resolve = resolver(records, map_index)
    for key, record in records.items():
        inside_map = ":" in key
        references = set(record.pop("_children", []))
        for reference in record.pop("_refs"):
            target = resolve(reference, inside_map)
            if target is None:
                target = vpk.compiled_path(reference)
                unresolved[key].add(target)
            references.add(target)
        record["references"] = sorted(references)
    return resolve


def strings_in(value):
    if isinstance(value, str):
        yield value
    elif isinstance(value, dict):
        for item in value.values():
            yield from strings_in(item)
    elif isinstance(value, list):
        for item in value:
            yield from strings_in(item)


def entity_roots(root, definitions, records, extract, sounds):
    """Entity key → root resource paths (design §3), unresolved paths are kept as compiled paths."""
    roots = collections.defaultdict(set)
    data = {}
    for category in {d.get("category") for d in definitions.values()}:
        data[category] = load_json(os.path.join(root, "Research", "Json", f"survivors_{category}.json"), {})
    folders = extract.get("heroModelFolders", {})
    econ = extract.get("econItems", {})
    referenced_events = set()
    for key, definition in definitions.items():
        for resource in definition.get("resources", []):
            roots[key].add(resource)
        raw = data.get(definition.get("category"), {}).get(definition.get("key"))
        if not isinstance(raw, dict):
            continue
        if definition.get("category") == "heroes" or raw.get("m_bUseHeroModel"):
            folder = folders.get(str(raw.get("m_nDOTAHeroID")))
            if folder:
                roots[key].update(p for p in records if p.startswith(folder))
            for item in raw.get("m_vecEconItems", []):
                roots[key].update(econ.get(str(item), {}).get("assets", []))
        for text in strings_in(raw):
            if text in sounds:
                referenced_events.add(text)
                roots[key].update(sounds[text].get("vsnd", []))
    modes = sorted(k for k, d in definitions.items() if d.get("category") == "game_modes")
    if modes:
        for event, value in sounds.items():
            if event not in referenced_events:
                roots[modes[0]].update(value.get("vsnd", []))
        roots[modes[0]].update(p for p in records if p.endswith(".vsndevts_c") and ":" not in p)
    for key, path in LEVEL_MAPS.items():
        if key in definitions:
            roots[key].add(path)
    return roots


def reach(records, roots, resolve, unresolved):
    used_by = collections.defaultdict(set)
    for entity, paths in sorted(roots.items()):
        queue = []
        for path in sorted(paths):
            target = path if path in records else resolve(path)
            if target is None:
                unresolved[entity].add(vpk.compiled_path(path))
            else:
                queue.append(target)
        seen = set(queue)
        while queue:
            key = queue.pop()
            used_by[key].add(entity)
            for reference in records[key]["references"]:
                if reference in records and reference not in seen:
                    seen.add(reference)
                    queue.append(reference)
    return used_by


def read_meta(path):
    """GUID, importer and texture platform settings of a Unity .meta file."""
    with open(path, encoding="utf-8", errors="replace") as f:
        text = f.read()
    guid = re.search(r"^guid: *(\w+)", text, re.M)
    importer = re.search(r"^(\w+):\s*$", text, re.M)
    name = importer.group(1) if importer else None
    if name == "ScriptedImporter" and GLTFAST_SCRIPT in text:
        name = "ScriptedImporter:glTFast"
    compression = {}
    if name == "TextureImporter" and "platformSettings:" in text:
        section = text[text.index("platformSettings:"):]
        for block in re.split(r"\n  - ", section)[1:]:
            fields = dict(re.findall(r"^\s*(\w+): *(-?\w+)\s*$", block, re.M))
            target = fields.get("buildTarget")
            if target in PLATFORMS:
                code = int(fields.get("textureFormat", -1))
                level = int(fields.get("textureCompression", 1))
                compression[target] = {"maxTextureSize": int(fields.get("maxTextureSize", 0)),
                                       "textureFormat": TEXTURE_FORMATS.get(code, f"code:{code}"),
                                       "textureCompression": COMPRESSION.get(level, f"code:{level}")}
    return (guid.group(1) if guid else None), name, compression


def glb_document(path):
    with open(path, "rb") as f:
        head = f.read(20)
        if len(head) < 20:
            return {}
        magic, _, _, length, chunk = struct.unpack("<IIIII", head)
        if magic != 0x46546C67 or chunk != 0x4E4F534A:
            return {}
        return json.loads(f.read(length))


def glb_triangles(path, style=0):
    """(all triangles, triangles of meshes shown for the style) — the filter of HeroView.IsStyleMesh (Р4)."""
    doc = glb_document(path)
    accessors, meshes = doc.get("accessors", []), doc.get("meshes", [])

    def triangles(mesh):
        total = 0
        for primitive in mesh.get("primitives", []):
            index = primitive.get("indices", primitive.get("attributes", {}).get("POSITION"))
            count = accessors[index]["count"] if index is not None else 0
            mode = primitive.get("mode", 4)
            total += count // 3 if mode == 4 else max(count - 2, 0) if mode in (5, 6) else 0
        return total

    parts = []
    for node in doc.get("nodes", []):
        if "mesh" in node:
            mesh = meshes[node["mesh"]]
            parts.append((node.get("name") or mesh.get("name") or "", triangles(mesh)))
    if style > 0 and not any(f"_style{style}" in name for name, _ in parts):
        style = 0

    def shown(name):
        if name.endswith("_dummy"):
            return False
        return "_style" not in name if style == 0 else f"_style{style}" in name

    return sum(t for _, t in parts), sum(t for name, t in parts if shown(name))


def unity_outputs(root, errors):
    base = os.path.join(root, CONVERTED)
    outputs = []
    for folder, dirs, files in os.walk(base):
        dirs.sort()
        for name in sorted(files):
            if name.endswith(".meta") or name.startswith("."):
                continue
            full = os.path.join(folder, name)
            rel = os.path.relpath(full, base).replace(os.sep, "/")
            meta = full + ".meta"
            if not os.path.exists(meta):
                errors.append(f"output without .meta: {rel}")
                continue
            guid, importer, compression = read_meta(meta)
            if not guid:
                errors.append(f".meta without guid: {rel}")
                continue
            entry = {"path": os.path.relpath(full, os.path.join(root, ASSETS_PREFIX)).replace(os.sep, "/"),
                     "guid": guid, "importer": importer, "_rel": rel, "_full": full}
            if compression:
                entry["compression"] = compression
            outputs.append(entry)
    return outputs


def link_outputs(root, records, map_index, outputs):
    """Attaches outputs to records (requirements Т2.1); returns paths of outputs without a source."""
    by_stem = collections.defaultdict(list)
    for key, record in records.items():
        if record["kind"] in ("vtex_c", "vmat_c"):
            by_stem[(record["kind"], os.path.basename(key.split(":")[-1]).rsplit(".", 1)[0])].append(key)
    enemy_looks = load_json(os.path.join(root, CONVERTED, "enemies.json"), {}) or {}
    baked_glb = collections.defaultdict(set)
    for look in enemy_looks.values():
        for model in look.get("models", []):
            baked_glb[os.path.basename(model)[:-len(".glb")]].add(model)

    def model_source(rel):
        stem = rel[:-len(".glb")]
        candidates = [stem + ".vmdl_c"]
        if stem.endswith("_physics"):
            candidates.append(stem[:-len("_physics")] + ".vmdl_c")
        candidates.append(stem + ".vwrld_c")
        for candidate in candidates:
            if candidate in records:
                return [candidate]
            if candidate in map_index:
                return [map_index[candidate]]
        return []

    world = map_index.get("maps/scenes/crownfall/survivors_arena/world.vwrld_c")
    orphans = []
    for output in outputs:
        rel = output.pop("_rel")
        full = output.pop("_full")
        name = os.path.basename(rel)
        stem, extension = os.path.splitext(name)
        sources, tool, stage = [], TOOL_CONVERT, "converted"
        if rel.startswith("Baked/") and extension == ".asset":
            tool, stage = TOOL_ENEMY_BAKER, "baked"
            models = baked_glb.get(stem, set())
            if len(models) == 1:
                sources = model_source(next(iter(models)))
        elif rel.startswith("Arena/"):
            tool, stage = TOOL_ARENA_BAKER, "baked"
            sources = [world] if world else []
        elif extension == ".glb":
            sources = model_source(rel)
            output["triangles"], output["visibleTriangles"] = glb_triangles(full, 0)
        elif extension == ".png":
            base = ORM_SUFFIX.sub("", stem)
            sources = by_stem.get(("vtex_c", base)) or by_stem.get(("vtex_c", CUBE_FACE.sub("", base)), [])
        elif extension == ".mat" and rel.startswith("materials/") and rel.count("/") == 1:
            tool = TOOL_MATERIALS
            sources = by_stem.get(("vmat_c", stem), [])
        if not sources:
            orphans.append(output["path"])
            continue
        output["tool"], output["stage"] = tool, stage
        for source in sources:
            records[source].setdefault("outputs", []).append(dict(output))
    return sorted(orphans)


def read_overrides(root, records, errors):
    overrides = load_json(os.path.join(root, OVERRIDES), {}) or {}
    for key, value in sorted(overrides.items()):
        if key not in records:
            errors.append(f"override of unknown source: {key}")
        elif value.get("status") not in ("recreated", "unused"):
            errors.append(f"override status must be recreated or unused: {key}")
        elif value["status"] == "unused" and not value.get("reason"):
            errors.append(f"unused override without reason: {key}")
        elif value["status"] == "recreated" and not value.get("unity"):
            errors.append(f"recreated override without unity path: {key}")
    return overrides


def model_views(root, definitions):
    """Reference model views: looks.json — body + wearables with the style (sum); enemies.json — max (Т4)."""
    base = os.path.join(root, CONVERTED)

    def part(glb, style):
        full = os.path.join(base, glb)
        if not os.path.exists(full):
            return {"glb": glb, "missing": True}
        triangles, visible = glb_triangles(full, style)
        return {"glb": glb, "triangles": triangles, "visibleTriangles": visible}

    def entity(key):
        for category in ("heroes", "enemies"):
            if f"{category}/{key}" in definitions:
                return f"{category}/{key}"
        return f"enemies/{key}"

    views = {}
    for key, look in sorted((load_json(os.path.join(base, "enemies.json"), {}) or {}).items()):
        parts = [part(m, 0) for m in look.get("models", [])]
        views[entity(key)] = {"parts": parts, "combine": "max",
                              "visibleTriangles": max([p.get("visibleTriangles", 0) for p in parts] or [0])}
    for key, look in sorted((load_json(os.path.join(base, "looks.json"), {}) or {}).items()):
        style = look.get("style", 0) or 0
        parts = [part(m, style) for m in [look["body"]] + look.get("wearables", [])]
        views[entity(key)] = {"parts": parts, "combine": "sum", "style": style,
                              "visibleTriangles": sum(p.get("visibleTriangles", 0) for p in parts)}
    for key, view in views.items():
        view["role"] = definitions.get(key, {}).get("role")
    return views


def build(root, errors):
    rows = read_manifest(root)
    records, map_index = read_sources(root, rows, errors)
    registry_path = os.path.join(root, "Research", "FullGame", "ContentRegistry.json")
    definitions = load_json(registry_path, {}).get("definitions", {})
    sounds = load_json(os.path.join(root, "Research", "FullGame", "SoundRegistry.json"), {}).get("definitions", {})
    extract = load_json(os.path.join(root, "Research", "extract-report.json"), {})
    versions = load_json(os.path.join(root, "Research", "FullGame", "SourceVersions.json"), {})
    unresolved = collections.defaultdict(set)
    resolve = link_graph(records, map_index, unresolved)
    roots = entity_roots(root, definitions, records, extract, sounds)
    used_by = reach(records, roots, resolve, unresolved)
    outputs = unity_outputs(root, errors)
    orphans = link_outputs(root, records, map_index, outputs)
    overrides = read_overrides(root, records, errors)
    if errors:
        return None
    required = {k for k, d in definitions.items() if is_required(d)}
    for key, record in records.items():
        record["usedBy"] = sorted(used_by.get(key, ()))
        record["outputs"] = sorted(record.get("outputs", []), key=lambda o: o["path"])
        override = overrides.get(key, {})
        stages = {o["stage"] for o in record["outputs"]}
        if override.get("status"):
            record["status"] = override["status"]
            record.update({k: override[k] for k in ("reason", "unity") if k in override})
        elif "baked" in stages:
            record["status"] = "baked"
        elif stages:
            record["status"] = "converted"
        elif required & set(record["usedBy"]):
            record["status"] = "none"
        else:
            record["status"] = "unused"
        record["verification"] = "verified" if override.get("verified") else "unverified"
        if override.get("verified"):
            record["verifiedBy"] = override["verified"]
    used = {e for r in records.values() for e in r["usedBy"]}
    inputs = {}
    for rel in ["NestOfThorns/manifest.tsv", "Research/FullGame/ContentRegistry.json",
                "Research/FullGame/SoundRegistry.json", "Research/FullGame/SourceVersions.json",
                "Research/extract-report.json", f"{CONVERTED}/looks.json", f"{CONVERTED}/enemies.json", OVERRIDES] + \
            sorted(f"Research/Json/survivors_{c}.json" for c in {d.get("category") for d in definitions.values()}):
        if os.path.exists(os.path.join(root, rel)):
            inputs[rel] = sha256(os.path.join(root, rel))
    summary = {
        "records": len(records),
        "byStatus": dict(collections.Counter(r["status"] for r in records.values())),
        "byKind": dict(collections.Counter(r["kind"] for r in records.values())),
        "unresolved": {k: sorted(v) for k, v in unresolved.items() if v},
        "orphanOutputs": orphans,
        "requiredEntities": len(required),
        "missingRequired": sorted(required - used),
    }
    return {
        "schema": 1,
        "inputs": inputs,
        "tools": {"source2viewer": versions.get("tools", {}).get("source2viewer"), "converter": TOOL_CONVERT,
                  "registry": "Tools/asset-registry.py"},
        "statusValues": STATUS_VALUES,
        "summary": summary,
        "models": model_views(root, definitions),
        "assets": records,
    }


def table(models):
    lines = ["| сущность | роль | модели | видимые треугольники |", "|---|---|---|---|"]
    for key, view in sorted(models.items()):
        lines.append(f"| {key} | {view.get('role') or '—'} | {len(view['parts'])} | {view['visibleTriangles']} |")
    return "\n".join(lines)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", default=PROJECT, help="project root (default: this repository)")
    parser.add_argument("--out", default=None, help="output directory (default Research/FullGame)")
    args = parser.parse_args(argv)
    root = os.path.abspath(args.root)
    out = args.out or os.path.join(root, "Research", "FullGame")
    errors = []
    document = build(root, errors)
    if errors:
        print("input violations (AssetRegistry.json is not written):", file=sys.stderr)
        for error in errors:
            print("  " + error, file=sys.stderr)
        return 1
    os.makedirs(out, exist_ok=True)
    path = os.path.join(out, "AssetRegistry.json")
    with open(path + ".tmp", "w", encoding="utf-8") as f:
        json.dump(document, f, ensure_ascii=False, indent=1, sort_keys=True)
        f.write("\n")
    os.replace(path + ".tmp", path)
    summary = document["summary"]
    print(f"records: {summary['records']}, by status: {json.dumps(summary['byStatus'], sort_keys=True)}")
    print(f"unresolved: {sum(len(v) for v in summary['unresolved'].values())}, "
          f"orphanOutputs: {len(summary['orphanOutputs'])}")
    print(f"missingRequired: {len(summary['missingRequired'])} of {summary['requiredEntities']}")
    for key in summary["missingRequired"]:
        print("  " + key)
    print(table(document["models"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
