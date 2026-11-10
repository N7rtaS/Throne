#!/usr/bin/env python3
"""Read-only research of the installed Dota 2 client for Nest of Thorns.
Collects strings of bin/osx64/libclient.dylib and libserver.dylib, Panorama localization and menu texts,
and Russian/English descriptions of all powerups. Design: .kiro/specs/dota-research-client/design.md.
Writes (under --out, default: the project root):
  Research/Decoded/client/strings.json
  Research/Decoded/panorama/{layout,styles}/**, localization_{english,russian}.json, menu_texts.md
  Research/FullGame/Descriptions.md
  Research/research-client-report.json
Read-only guarantee: the whole Steam directory (~/Library/Application Support/Steam and the Steam library that
holds Dota) is a forbidden zone. Files there are opened only with mode "rb"; outputs and Source2Viewer arguments
in the zone are refused; the tool refuses to run if the project itself is inside the zone. Source2Viewer decodes
only the copies in NestOfThorns/, after their CRC32 is checked against pak01_dir.vpk.
Usage: python3 Tools/research-client.py [--dota PATH] [--out DIR] [--check-readonly]
Exit codes: 0 ok; 1 missing input; 2 data violation; 3 output or project inside the forbidden zone;
4 the Dota install changed during the run (--check-readonly).
"""
import argparse
import glob
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from vpk import Vpk, crc32  # noqa: E402
import audit  # noqa: E402
PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STEAM_HOME = os.path.expanduser("~/Library/Application Support/Steam")
DEFAULT_DOTA = os.path.join(STEAM_HOME, "steamapps", "common", "dota 2 beta", "game", "dota")
DEFAULT_S2V = os.path.join(PROJECT, "Tools", "Source2Viewer", "cli-macos", "Source2Viewer-CLI")
BINARIES = ("libclient.dylib", "libserver.dylib")
LANGUAGES = ("english", "russian")
STRINGS_OUT = "Research/Decoded/client/strings.json"
PANORAMA_OUT = "Research/Decoded/panorama"
DESCRIPTIONS_OUT = "Research/FullGame/Descriptions.md"
REPORT_OUT = "Research/research-client-report.json"
DOTA_LOCALIZATION = "resource/localization/dota_{}.txt"
VDATA_LOCALIZATION = "resource/localization/vdata/survivors_{0}/survivors_{0}_{1}.txt"
WINDOW = 10  # design.md §3.2, decision Р4
POWERUP_COUNT = 47
ROLE_GROUPS = [("active-ability", "Активные способности"), ("passive-item", "Пассивные предметы"),
               ("recipe", "Рецепты"), ("innate", "Врождённые"), ("service", "Служебные")]
class ToolError(Exception):
    code = 1
class InputError(ToolError):
    code = 1
class DataError(ToolError):
    code = 2
class GuardError(ToolError):
    code = 3
def _inside(path, root):
    return path == root or path.startswith(root.rstrip(os.sep) + os.sep)
class Guard:
    """The forbidden zone (requirements.md, «Запретная зона»; decision Р2): the known Steam directory, the Steam
    library holding Dota (parent of the nearest `steamapps`) or, without `steamapps`, the Dota install root."""
    def __init__(self, dota, steam_home=STEAM_HOME):
        self.dota = os.path.realpath(dota)
        game = os.path.dirname(self.dota)
        self.install_root = os.path.dirname(game) if os.path.basename(game) == "game" else self.dota
        roots = {os.path.realpath(steam_home)}
        parts = self.dota.split(os.sep)
        if "steamapps" in parts:
            index = len(parts) - 1 - parts[::-1].index("steamapps")
            roots.add(os.sep.join(parts[:index]) or os.sep)
        else:
            roots.add(self.install_root)
        self.roots = sorted(roots)
    def forbidden(self, path):
        real = os.path.realpath(path)
        return any(_inside(real, root) for root in self.roots)
    def check_output(self, path):
        if self.forbidden(path):
            raise GuardError(f"output path is inside the Steam forbidden zone: {path}")
    def check_project(self, project):
        if self.forbidden(project):
            raise GuardError(f"the project is inside the Steam forbidden zone ({', '.join(self.roots)}): {project}. "
                             "Check --dota / THORN_DOTA_DIR.")
    def check_external(self, paths):
        for path in paths:
            if self.forbidden(path):
                raise GuardError(f"external program argument is inside the Steam forbidden zone: {path}")
    def open_readonly(self, path, mode="rb"):
        if mode != "rb":
            raise ValueError(f"only mode 'rb' is allowed for {path}, got {mode!r}")
        return open(path, "rb")
def write_text(guard, path, text):
    """Writes UTF-8 text with `\\n` via a temporary file; keeps the file untouched if content is equal."""
    guard.check_output(path)
    data = text.encode("utf-8")
    if os.path.exists(path):
        with open(path, "rb") as f:
            if f.read() == data:
                return False
    folder = os.path.dirname(path)
    guard.check_output(folder)
    os.makedirs(folder, exist_ok=True)
    temporary = path + ".tmp"
    guard.check_output(temporary)
    with open(temporary, "wb") as f:
        f.write(data)
    os.replace(temporary, path)
    return True
def snapshot(root):
    result = {}
    for folder, dirs, names in os.walk(root, followlinks=False):
        dirs.sort()
        for name in names:
            path = os.path.join(folder, name)
            info = os.lstat(path)
            result[os.path.relpath(path, root)] = (info.st_size, info.st_mtime_ns)
    return result
def dumps(value):
    return json.dumps(value, ensure_ascii=False, indent=1, sort_keys=True) + "\n"
# --- Client strings (requirements Т2, design §3.1–3.3) ---
def extract_strings(data):
    return [m.group().decode("ascii") for m in re.finditer(rb"[\x20-\x7e]{4,}", data)]
_CLASS_HEAD = r"[CI](?:DOTA_(?:UI_)?)?Survivors"
_ENUM_HEAD = r"ESurvivors"
def _symbols(text, head):
    """Names starting with `head`; a C++ mangling length prefix (`21CSurvivorsFoo...`) cuts the name."""
    for m in re.finditer(r"(\d*)(" + head + r"\w*)", text):
        digits, name, start = m.group(1), m.group(2), m.start(2)
        if digits:
            for k in range(1, len(digits) + 1):
                n = int(digits[-k:])
                if len("CSurvivors") <= n <= len(name) and re.fullmatch(head + r"\w*", name[:n]):
                    yield name[:n]
                    break
        elif start == 0 or not (text[start - 1].isalnum() or text[start - 1] == "_"):
            yield name
def client_strings(binaries, data_fields, read):
    """binaries: name -> strings in file order; data_fields: all m_* keys of the data; read: fields read by the
    simulation."""
    survivors, classes, enums, client_only, present = {}, set(), set(), set(), set()
    for name, strings in sorted(binaries.items()):
        present.update(strings)
        survivors[name] = sorted({s for s in strings if "survivors" in s.lower()})
        anchors = [i for i, s in enumerate(strings) if "survivors" in s.lower() or s in data_fields]
        near = set()
        for i in anchors:
            near.update(range(i - WINDOW, i + WINDOW + 1))
        for i, s in enumerate(strings):
            if "Survivors" in s:
                classes.update(_symbols(s, _CLASS_HEAD))
                enums.update(_symbols(s, _ENUM_HEAD))
                enums.update(re.findall(r"(?<![\w])(k_eSurvivors\w+)", s))
                enums.update(re.findall(r"(?<![\w])(E?Survivors\w*_t)(?!\w)", s))
            if i in near and s not in data_fields and re.fullmatch(r"m_\w+", s):
                client_only.add(s)
    fields = {f: {"inClient": f in present, "readBySimulation": f in read} for f in sorted(data_fields)}
    return {
        "binaries": {name: {"strings": len(strings)} for name, strings in sorted(binaries.items())},
        "survivors": survivors,
        "classes": sorted(classes),
        "enums": sorted(enums),
        "dataFields": fields,
        "unreadFields": sorted(f for f, v in fields.items() if not v["readBySimulation"]),
        "clientOnlyFields": {
            "evidence": "К-кандидат",
            "rule": f"m_* string absent from the data within {WINDOW} strings of a 'survivors' string or a data "
                    "field (design.md §3.2). The linker merges equal strings of different modules: membership in "
                    "the mode is not proven.",
            "all": sorted(client_only),
            "m_fl": sorted(s for s in client_only if s.startswith("m_fl")),
        },
    }
def data_fields(json_dir=audit.JSON_DIR):
    fields = set()
    for path in sorted(glob.glob(os.path.join(json_dir, "survivors_*.json"))):
        with open(path, encoding="utf-8") as f:
            for node in audit.walk(json.load(f)):
                fields.update(k for k in node if k.startswith("m_"))
    return fields
def read_fields(root=audit.SIMULATION):
    fields = set()
    for path in sorted(glob.glob(os.path.join(root, "**", "*.cs"), recursive=True)):
        with open(path, encoding="utf-8") as f:
            fields.update(re.findall(r'"(m_\w+)"', f.read()))
    return fields
# --- Localization (Т3, design §3.4) ---
def parse_kv(text):
    """Key/value pairs of a KeyValues localization file; the last repeat wins (as in the engine)."""
    text = text.lstrip("\ufeff").replace("\r\n", "\n")
    result = {}
    for key, value in re.findall(r'^[ \t]*"((?:\\.|[^"\\\n])*)"[ \t]+"((?:\\.|[^"\\])*)"', text, re.M):
        result[key] = re.sub(r'\\(["\\])', r"\1", value)
    return result
def layout_texts(xml):
    """`#Key` references and literal `text="..."` values of a decoded Panorama layout."""
    refs = {key for key in re.findall(r"[\"']#(\w+)[\"']", xml) if not re.fullmatch(r"[0-9a-fA-F]{3,8}", key)}
    literals = {t for t in re.findall(r'\btext="([^"]*)"', xml) if t and not t.startswith("#")}
    return sorted(refs), sorted(literals)
def select_localization(tokens, refs):
    selected = {k: v for k, v in tokens.items()
                if k.startswith("DOTA_Survivors_") or "Survivors" in k or "NestOfThorns" in k or k in refs}
    return selected, sorted(r for r in refs if r not in tokens)
def _cell(text):
    return text.replace("|", "\\|").replace("\n", " ")
def menu_texts(layouts, english, russian):
    lines = ["# Тексты меню Panorama режима Nest of Thorns",
             "Сгенерировано `Tools/research-client.py` из разметок `Research/Decoded/panorama/layout` и "
             "`resource/localization/dota_{english,russian}.txt`. Не править вручную.", ""]
    for path, xml in sorted(layouts.items()):
        refs, literals = layout_texts(xml)
        lines += [f"## `{path}`", ""]
        if refs:
            lines += ["| Ключ | English | Русский |", "|---|---|---|"]
            for key in refs:
                en = english.get(key, "нет в локализации")
                ru = russian.get(key, "нет в локализации")
                lines.append(f"| `{key}` | {_cell(en)} | {_cell(ru)} |")
            lines.append("")
        if literals:
            lines.append("Литеральные тексты: " + ", ".join(f"`{_cell(t)}`" for t in literals))
            lines.append("")
        if not refs and not literals:
            lines += ["Текстов нет.", ""]
    return "\n".join(lines).rstrip("\n") + "\n"
# --- Descriptions (Т4, design §3.5) ---
def load_powerups():
    _, defs = audit.load("powerups")
    return {k: v for k, v in defs.items() if "_class" in v}
def _role_group(value):
    role = audit.powerup_role(value)
    return "service" if role.startswith("service:") else role
def _number(value):
    return format(value, "g") if isinstance(value, float) else str(value)
def _attribute(enum):
    return enum.replace("k_eSurvivorsAttribute_", "")
def descriptions(defs, english, russian):
    errors = []
    if len(defs) != POWERUP_COUNT:
        errors.append(f"expected {POWERUP_COUNT} powerups, found {len(defs)}")
    for key, value in sorted(defs.items()):
        for field, ref in sorted(value.items()):
            if field.startswith("m_sLoc") and isinstance(ref, str):
                for language, tokens in (("english", english), ("russian", russian)):
                    if ref.lstrip("#") not in tokens:
                        errors.append(f"{key}.{field}: {ref} is missing in {language} localization")
        if _role_group(value) not in dict(ROLE_GROUPS):
            errors.append(f"{key}: unknown role {audit.powerup_role(value)}")
    if errors:
        raise DataError("powerup descriptions:\n  " + "\n  ".join(errors))
    by_id = {v["m_unPowerUpID"]: k for k, v in defs.items()}
    def text(value, field, tokens):
        ref = value.get(field)
        return tokens[ref.lstrip("#")] if isinstance(ref, str) else "нет в данных"
    def attribute_name(enum):
        key = f"DOTA_VData_survivors_attributes_{enum}_LocName"
        return russian.get(key, "нет в локализации")
    lines = ["# Описания powerups режима Nest of Thorns",
             "Сгенерировано `Tools/research-client.py` из `Research/Json/survivors_powerups.json` и локализации "
             "`resource/localization/vdata/survivors_{powerups,attributes}_{russian,english}.txt` клиента. "
             "Не править вручную.",
             "Тексты приведены как в оригинале, вместе с разметкой `<b>`. Плейсхолдеры `{f:…}` не подставлены: "
             "их значение подставляет клиент. Числа атрибутов — значения из данных без форматирования клиента "
             "(масштаб процентов в `{f:0:attribute_value}` не восстановлен).", ""]
    for group, title in ROLE_GROUPS:
        members = sorted((k for k, v in defs.items() if _role_group(v) == group),
                         key=lambda k: defs[k]["m_unPowerUpID"])
        lines += [f"## {title} ({len(members)})", ""]
        for key in members:
            value = defs[key]
            name_ru = text(value, "m_sLocAbilityName", russian)
            name_en = text(value, "m_sLocAbilityName", english)
            lines.append(f"### {key} — {name_ru} / {name_en}")
            lines.append(f"- ID {value['m_unPowerUpID']}; класс `{value['_class']}`; роль `{audit.powerup_role(value)}`; "
                         f"максимальный ранг {_number(value['m_nMaxLevel']) if 'm_nMaxLevel' in value else 'нет в данных'}")
            if "m_sLocHeroName" in value:
                lines.append(f"- Герой-источник: {text(value, 'm_sLocHeroName', russian)} / "
                             f"{text(value, 'm_sLocHeroName', english)}")
            placeholders = set()
            for label, field in (("Описание", "m_sLocAbilityDesc"), ("Aghanim", "m_sLocScepterAbilityDesc")):
                if field not in value:
                    lines.append(f"- {label}: нет в данных")
                    continue
                for language, tokens in (("ru", russian), ("en", english)):
                    body = text(value, field, tokens)
                    placeholders.update(re.findall(r"\{[a-z]:\d+:\w+\}", body))
                    lines.append(f"- {label} ({language}): {body}")
            if placeholders:
                lines.append("- Плейсхолдеры, значение подставляет клиент: "
                             + ", ".join(f"`{p}`" for p in sorted(placeholders)))
            base = value.get("m_vecBaseAttributes") or []
            if base:
                lines.append("- Базовые атрибуты: " + "; ".join(
                    f"{_attribute(a['m_eType'])} = {_number(a['m_flValue'])} ({attribute_name(a['m_eType'])})"
                    for a in base))
            tooltip = value.get("m_vecTooltipAttributes") or []
            if tooltip:
                lines.append("- Атрибуты подсказки: " + "; ".join(
                    f"{_attribute(a)} ({attribute_name(a)})" for a in tooltip))
            upgrades = []
            for choice in value.get("m_vecMinorUpgradeChoices") or []:
                attributes = choice.get("m_vecGlobalUpgradeAttributes") or []
                if attributes:
                    rarity = str(choice.get("m_unRarity", "нет в данных")).replace("RARITY_", "")
                    upgrades.append(f"[{rarity}] " + ", ".join(
                        f"{_attribute(a['m_eType'])} = {_number(a['m_flValue'])} ({attribute_name(a['m_eType'])})"
                        for a in attributes))
            if upgrades:
                lines.append("- Глобальные атрибуты малых улучшений: " + "; ".join(upgrades))
            recipe = value.get("m_vecRecipeItems") or []
            if recipe:
                lines.append("- Рецепт: " + " + ".join(
                    f"`{by_id.get(i, '?')}` (ID {i})" for i in recipe))
            lines.append("")
    return "\n".join(lines).rstrip("\n") + "\n"
# --- Source2Viewer ---
class Source2Viewer:
    def __init__(self, cli=None):
        self.cli = cli or os.environ.get("THORN_S2V_CLI", DEFAULT_S2V)
    def _check(self):
        if not os.access(self.cli, os.X_OK):
            raise InputError(f"Source2Viewer CLI not found: {self.cli} (set THORN_S2V_CLI)")
    def version(self):
        self._check()
        output = subprocess.run([self.cli, "--version"], capture_output=True, text=True).stdout
        match = re.search(r"Version:\s*(\S+)", output)
        return match.group(1) if match else output.strip()
    def decode(self, source, target):
        self._check()
        result = subprocess.run([self.cli, "-i", source, "-o", target, "-d"], capture_output=True, text=True)
        if result.returncode != 0 or not os.path.exists(target):
            raise InputError(f"Source2Viewer failed on {source}: {result.stderr.strip() or result.stdout.strip()}")
# --- Main ---
def _sha256(guard, path):
    digest = hashlib.sha256()
    with guard.open_readonly(path) as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()
def _project_sha256(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()
def panorama_files(project):
    with open(os.path.join(project, "NestOfThorns", "manifest.tsv"), encoding="utf-8") as f:
        return sorted(line.split("\t")[0] for line in f
                      if line.startswith(("panorama/layout/", "panorama/styles/")))
def client_version(guard, dota, project):
    path = os.path.join(dota, "steam.inf")
    if os.path.exists(path):
        with guard.open_readonly(path) as f:
            values = dict(line.split("=", 1) for line in f.read().decode("utf-8", "replace").splitlines() if "=" in line)
        result = {k: values[k] for k in ("ClientVersion", "SourceRevision", "VersionDate", "VersionTime") if k in values}
        result["source"] = "steam.inf"
        return result
    with open(os.path.join(project, "Research", "FullGame", "SourceVersions.json"), encoding="utf-8") as f:
        result = dict(json.load(f)["dota"])
    result["source"] = "Research/FullGame/SourceVersions.json"
    return result
def parse_args(argv):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dota", default=os.environ.get("THORN_DOTA_DIR", DEFAULT_DOTA),
                        help="path to 'dota 2 beta/game/dota'")
    parser.add_argument("--out", default=PROJECT, help="root for Research/... outputs (default: project root)")
    parser.add_argument("--check-readonly", action="store_true",
                        help="compare size and mtime of every file of the Dota install before and after the run")
    return parser.parse_args(argv)
def run(args, project, decoder):
    guard = Guard(args.dota)
    out = os.path.realpath(args.out)
    guard.check_project(project)
    for relative in ("", STRINGS_OUT, PANORAMA_OUT, DESCRIPTIONS_OUT, REPORT_OUT):
        guard.check_output(os.path.join(out, relative))
    dota = guard.dota
    pak_path = os.path.join(dota, "pak01_dir.vpk")
    binaries = [os.path.join(dota, "bin", "osx64", name) for name in BINARIES]
    for path in [pak_path] + binaries:
        if not os.path.isfile(path):
            raise InputError(f"Dota input not found: {path}\nPass --dota or set THORN_DOTA_DIR.")
    before = snapshot(guard.install_root) if args.check_readonly else None
    counts = {}
    vpk_inputs = {}
    pak = Vpk(pak_path)
    def vpk_text(path):
        entry = pak.entries.get(path)
        if entry is None:
            raise InputError(f"{path} not found in pak01_dir.vpk")
        vpk_inputs[path] = {"path": path, "bytes": entry.size, "crc32": f"{entry.crc:08x}"}
        return pak.read(entry).decode("utf-8", "replace")
    # Client strings.
    strings = {}
    for path in binaries:
        with guard.open_readonly(path) as f:
            strings[os.path.basename(path)] = extract_strings(f.read())
    result = client_strings(strings, data_fields(), read_fields())
    write_text(guard, os.path.join(out, STRINGS_OUT), dumps(result))
    counts[STRINGS_OUT] = {"classes": len(result["classes"]), "enums": len(result["enums"]),
                           "dataFields": len(result["dataFields"]), "unreadFields": len(result["unreadFields"]),
                           "clientOnlyFields": len(result["clientOnlyFields"]["all"]),
                           "survivors": {k: len(v) for k, v in result["survivors"].items()}}
    # Panorama layouts and styles: decode project copies after a CRC32 check against the VPK (decision Р1).
    nest = os.path.join(project, "NestOfThorns")
    layouts = {}
    with tempfile.TemporaryDirectory() as temporary:
        guard.check_output(temporary)
        for index, path in enumerate(panorama_files(project)):
            copy = os.path.join(nest, path)
            entry = pak.entries.get(path)
            if entry is None or not os.path.isfile(copy):
                raise InputError(f"{path}: missing in pak01_dir.vpk or NestOfThorns. "
                                 "Run python3 Tools/extract-resources.py")
            with open(copy, "rb") as f:
                if crc32(f.read()) != entry.crc:
                    raise InputError(f"{path}: NestOfThorns copy differs from pak01_dir.vpk (CRC32). "
                                     "Run python3 Tools/extract-resources.py")
            vpk_inputs[path] = {"path": path, "bytes": entry.size, "crc32": f"{entry.crc:08x}"}
            relative = (path[:-2] if path.endswith("_c") else path)[len("panorama/"):]
            target = os.path.join(temporary, f"{index}{os.path.splitext(relative)[1]}")
            guard.check_external([copy, target])
            decoder.decode(copy, target)
            with open(target, encoding="utf-8") as f:
                decoded = f.read().replace("\r\n", "\n")
            write_text(guard, os.path.join(out, PANORAMA_OUT, relative), decoded)
            if relative.startswith("layout/"):
                layouts[relative] = decoded
    counts[PANORAMA_OUT + "/layout+styles"] = len(panorama_files(project))
    # Panorama localization and menu texts.
    refs = set()
    for xml in layouts.values():
        refs.update(layout_texts(xml)[0])
    for values in result["survivors"].values():
        for s in values:
            refs.update(re.findall(r"#(DOTA_\w+)", s))
    selected = {}
    for language in LANGUAGES:
        source = DOTA_LOCALIZATION.format(language)
        tokens = parse_kv(vpk_text(source))
        keys, missing = select_localization(tokens, refs)
        selected[language] = keys
        name = f"{PANORAMA_OUT}/localization_{language}.json"
        write_text(guard, os.path.join(out, name), dumps({"source": source, "keys": keys,
                                                          "missingReferences": missing}))
        counts[name] = len(keys)
    menu = menu_texts(layouts,
                      selected["english"], selected["russian"])
    write_text(guard, os.path.join(out, PANORAMA_OUT, "menu_texts.md"), menu)
    counts[PANORAMA_OUT + "/menu_texts.md"] = len(layouts)
    # Powerup descriptions.
    vdata = {}
    for language in LANGUAGES:
        tokens = {}
        for kind in ("powerups", "attributes"):
            tokens.update(parse_kv(vpk_text(VDATA_LOCALIZATION.format(kind, language))))
        vdata[language] = tokens
    defs = load_powerups()
    write_text(guard, os.path.join(out, DESCRIPTIONS_OUT), descriptions(defs, vdata["english"], vdata["russian"]))
    counts[DESCRIPTIONS_OUT] = len(defs)
    # Report.
    dota_inputs = [{"path": os.path.relpath(p, dota), "bytes": os.path.getsize(p), "sha256": _sha256(guard, p)}
                   for p in [pak_path] + binaries]
    project_files = sorted(glob.glob(os.path.join(project, "Research", "Json", "survivors_*.json")))
    simulation = hashlib.sha256()
    for path in sorted(glob.glob(os.path.join(audit.SIMULATION, "**", "*.cs"), recursive=True)):
        simulation.update(os.path.relpath(path, project).encode() + b"\0" + _project_sha256(path).encode() + b"\n")
    report = {
        "tool": "Tools/research-client.py",
        "client": client_version(guard, dota, project),
        "source2viewer": decoder.version(),
        "inputs": {
            "dota": dota_inputs,
            "vpkEntries": [vpk_inputs[k] for k in sorted(vpk_inputs)],
            "project": [{"path": os.path.relpath(p, project), "bytes": os.path.getsize(p),
                         "sha256": _project_sha256(p)} for p in project_files],
            "simulationSourcesSha256": simulation.hexdigest(),
        },
        "outputs": counts,
    }
    write_text(guard, os.path.join(out, REPORT_OUT), dumps(report))
    if before is not None:
        after = snapshot(guard.install_root)
        changed = sorted(set(before) ^ set(after) | {k for k in set(before) & set(after) if before[k] != after[k]})
        if changed:
            print("Dota install changed during the run (close Dota and Steam updates, decision Р7):", file=sys.stderr)
            for path in changed:
                print(f"  {path}", file=sys.stderr)
            return 4
        print(f"read-only check: {len(after)} files unchanged under {guard.install_root}")
    print(f"written under {out}: " + ", ".join(sorted(counts)))
    return 0
def main(argv=None, project=PROJECT, decoder=None):
    args = parse_args(argv)
    try:
        return run(args, os.path.realpath(project), decoder or Source2Viewer())
    except ToolError as error:
        print(f"error: {error}", file=sys.stderr)
        return error.code
if __name__ == "__main__":
    sys.exit(main())
