#!/usr/bin/env python3
"""Stage-1 audit: content registry, sound registry and source versions.

Reads Research/Json (decode-data.sh), NestOfThorns (extract-resources.py), the Dota client binary
(class names only, via `strings`), the simulation sources (what is implemented) and DotNet tests (what is
checked). Rules of the implementation field: .kiro/specs/audit-simulation-coverage/design.md §3.

Writes:
  Research/FullGame/ContentRegistry.json
  Research/FullGame/SoundRegistry.json
  Research/FullGame/SourceVersions.json

Usage: python3 Tools/audit.py [--dota PATH] [--out DIR]
Exit codes: 0 ok; 1 a required entry has no role (nothing is written); 2 a simulation construct is not parsed.
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




IMPLEMENTATION_LEVELS = ("none", "partial", "implemented", "verified")
SIMULATION = os.path.join(UNITY, "Assets", "Game", "Scripts", "Simulation")
TESTS = os.path.join(PROJECT, "DotNet")
# Prototype coverage as recorded before the prototype was retired (its sources are in Удалить/ and are not read).
# Every other entry is "none". Frozen historical value, design.md Р3 (.kiro/specs/audit-simulation-coverage).
PROTOTYPE_PARTIAL = frozenset({
    "attributes/k_eSurvivorsAttribute_Cooldown", "attributes/k_eSurvivorsAttribute_CooldownReductionMultiplier",
    "attributes/k_eSurvivorsAttribute_DamageMultiplier_Magical", "attributes/k_eSurvivorsAttribute_Damage_Magical",
    "attributes/k_eSurvivorsAttribute_ExpMultiplier", "attributes/k_eSurvivorsAttribute_HPRegen",
    "attributes/k_eSurvivorsAttribute_KnockbackDistance", "attributes/k_eSurvivorsAttribute_MaxHP",
    "attributes/k_eSurvivorsAttribute_MovementSpeed", "attributes/k_eSurvivorsAttribute_PickupRadius",
    "attributes/k_eSurvivorsAttribute_ProjectileAttackInterval",
    "attributes/k_eSurvivorsAttribute_ProjectileHitCount", "attributes/k_eSurvivorsAttribute_ProjectileSpeed",
    "attributes/k_eSurvivorsAttribute_Range", "attributes/k_eSurvivorsAttribute_TargetCount", "enemies/eidolon",
    "enemies/eidolon_2", "enemies/flyer", "enemies/flyer_2", "enemies/ghost", "enemies/hellbear", "enemies/hulk",
    "enemies/imperia", "enemies/mud_golem", "enemies/mud_golem_shard", "enemies/ogre", "enemies/satyr",
    "enemies/satyr_boss", "enemies/satyr_boss_2", "enemies/skeleton", "enemies/skeleton_boss",
    "enemies/skeleton_boss_2", "enemies/skeleton_gold", "enemies/skeleton_intro", "enemies/spider",
    "enemies/zombie", "enemies/zombie_2", "enemies/zombie_torso", "enemies/zombie_torso_2",
    "game_modes/crownfall_act4", "heroes/dragonus", "levels/skywrath", "pickups/experience",
    "powerups/arcane_bolt", "powerups/heart_of_tarrasque", "powerups/kaya", "powerups/octarine_core",
    "powerups/reaver", "powerups/ring_of_tarrasque", "powerups/skywrath_mage_innate", "powerups/tome_of_knowledge",
    "powerups/yasha", "powerups/yasha_and_kaya", "spawners/eidolon", "spawners/eidolon_2", "spawners/hellbear",
    "spawners/hulk", "spawners/mud_golem", "spawners/ogre", "spawners/satyr", "spawners/satyr_boss",
    "spawners/satyr_boss_2", "spawners/skeleton", "spawners/skeleton_boss", "spawners/skeleton_gold",
    "spawners/skeleton_intro", "spawners/zombie", "spawners/zombie_2", "spawners/zombie_torso",
    "spawners/zombie_torso_2",
})


class CoverageError(Exception):
    """A construct the audit relies on was not found in the simulation sources, or cannot be mapped to data."""


Coverage = collections.namedtuple("Coverage", [
    "ability_classes", "hero_attributes", "used_attributes", "enemy_handled", "movements",
    "pickup_conditions", "spawn_behaviors", "difficulty_unsupported", "field_maps", "ability_targeting"])
FieldMaps = collections.namedtuple("FieldMaps", ["fields", "computed"])


def _text(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def _read(root, relative):
    path = os.path.join(root, relative)
    if not os.path.exists(path):
        raise CoverageError(f"simulation source {relative} not found in {root}")
    return _text(path)


def _search(pattern, text, what, flags=re.S):
    match = re.search(pattern, text, flags)
    if not match:
        raise CoverageError(f"{what} not found in the simulation sources")
    return match


def _balanced(text, start, opening="(", closing=")"):
    """Index just after the bracket that closes text[start] (which must be `opening`)."""
    depth = 0
    for i in range(start, len(text)):
        if text[i] == opening:
            depth += 1
        elif text[i] == closing:
            depth -= 1
            if depth == 0:
                return i + 1
    raise CoverageError(f"unbalanced {opening}{closing} in the simulation sources")


def _method_body(text, signature, what):
    match = _search(signature, text, what)
    brace = text.index("{", match.end())
    return text[brace + 1:_balanced(text, brace, "{", "}") - 1]


def _if_branches(body):
    """(condition, statement) for every `if (...)` of an if/else-if chain of single statements."""
    branches = []
    matches = list(re.finditer(r"\bif\s*\(", body))
    for i, match in enumerate(matches):
        start = match.end() - 1
        end = _balanced(body, start)
        stop = matches[i + 1].start() if i + 1 < len(matches) else len(body)
        branches.append((body[start + 1:end - 1].strip(), body[end:stop]))
    return branches


def _enum_map(game_data, parser, enum):
    """Data string -> enum member, from a `case "DATA": return Enum.Member;` switch in GameData.cs."""
    body = _method_body(game_data, rf"private static {enum} {parser}\(string name\)", f"GameData.{parser}")
    pairs = re.findall(rf'case "(\w+)": return {enum}\.(\w+);', body)
    if not pairs:
        raise CoverageError(f"GameData.{parser} has no cases")
    return {member: data for data, member in pairs}


def _field_maps(game_data, definitions):
    """C# property -> JSON field, from the loaders in GameData.cs and computed properties in Definitions.cs."""
    fields = {}
    readers = list(re.finditer(r"private static (\w+Def) Read\w+\(string key, JObject v\)", game_data))
    if not readers:
        raise CoverageError("GameData Read*(string key, JObject v) loaders not found")
    for i, reader in enumerate(readers):
        stop = readers[i + 1].start() if i + 1 < len(readers) else len(game_data)
        block = game_data[reader.end():stop]
        stop = block.find("private static")
        block = block if stop < 0 else block[:stop]
        props = fields.setdefault(reader.group(1), {"Key": ("@key", "S", None)})
        for prop, kind, field, default in re.findall(
                r'\b(\w+)\s*=\s*(NF|NI|F|I|B|S)\(v,\s*"([^"]+)"(?:,\s*([^)]+))?\)', block):
            props[prop] = (field, kind, _literal(default.strip()) if default else _DEFAULTS[kind])
    computed = {}
    classes = list(re.finditer(r"public sealed class (\w+)", definitions))
    for i, cls in enumerate(classes):
        stop = classes[i + 1].start() if i + 1 < len(classes) else len(definitions)
        for prop, expr in re.findall(r"public bool (\w+) => ([^;]+);", definitions[cls.end():stop]):
            computed.setdefault(cls.group(1), {})[prop] = expr.strip()
    return FieldMaps(fields, computed)


_DEFAULTS = {"F": 0.0, "I": 0, "B": False, "S": None, "NF": None, "NI": None}


def _literal(text):
    if text in ("true", "false"):
        return text == "true"
    if text == "null":
        return None
    if text.startswith('"'):
        return json.loads(text)
    try:
        return float(text.rstrip("fF"))
    except ValueError:
        raise CoverageError(f"unsupported literal {text!r} in the simulation sources")


_TOKEN = re.compile(r'\s*(?:(?P<str>"(?:[^"\\]|\\.)*")|(?P<num>\d+(?:\.\d+)?[fF]?)'
                    r'|(?P<op>&&|\|\||==|!=|>=|<=|[!<>(),.])|(?P<id>[A-Za-z_]\w*))')


class CsCondition:
    """A boolean C# expression of the simulation evaluated on a JSON definition (no eval).
    Supports && || ! == != > < >= <= parentheses, literals, `subject.Prop`, bare properties (inside computed
    properties) and string .EndsWith/.StartsWith. Properties are mapped to JSON fields through FieldMaps."""

    def __init__(self, expr, cls, subject, maps):
        self.expr, self.cls, self.subject, self.maps = expr, cls, subject, maps
        self.tokens = self._tokenize(expr)
        self.pos = 0
        self.tree = self._or()
        if self.pos != len(self.tokens):
            raise CoverageError(f"cannot parse condition {expr!r} at token {self.tokens[self.pos][1]!r}")

    def evaluate(self, key, record):
        return bool(self.tree(key, record))

    @staticmethod
    def _tokenize(expr):
        tokens, pos = [], 0
        expr = expr.strip()
        while pos < len(expr):
            match = _TOKEN.match(expr, pos)
            if not match or match.end() == pos:
                raise CoverageError(f"cannot tokenize condition {expr!r} at {expr[pos:]!r}")
            kind = match.lastgroup
            tokens.append((kind, match.group(kind)))
            pos = match.end()
        return tokens

    def _peek(self):
        return self.tokens[self.pos][1] if self.pos < len(self.tokens) else None

    def _take(self, expected=None):
        if self.pos >= len(self.tokens):
            raise CoverageError(f"unexpected end of condition {self.expr!r}")
        token = self.tokens[self.pos]
        if expected is not None and token[1] != expected:
            raise CoverageError(f"expected {expected!r} in condition {self.expr!r}, got {token[1]!r}")
        self.pos += 1
        return token

    def _or(self):
        left = self._and()
        while self._peek() == "||":
            self._take()
            right = self._and()
            left = (lambda a, b: lambda k, r: a(k, r) or b(k, r))(left, right)
        return left

    def _and(self):
        left = self._unary()
        while self._peek() == "&&":
            self._take()
            right = self._unary()
            left = (lambda a, b: lambda k, r: a(k, r) and b(k, r))(left, right)
        return left

    def _unary(self):
        if self._peek() == "!":
            self._take()
            inner = self._unary()
            return lambda k, r: not inner(k, r)
        return self._compare()

    _COMPARE = {
        "==": lambda a, b: a == b, "!=": lambda a, b: a != b,
        ">": lambda a, b: a is not None and b is not None and a > b,
        "<": lambda a, b: a is not None and b is not None and a < b,
        ">=": lambda a, b: a is not None and b is not None and a >= b,
        "<=": lambda a, b: a is not None and b is not None and a <= b,
    }

    def _compare(self):
        left = self._primary()
        op = self._peek()
        if op in self._COMPARE:
            self._take()
            right = self._primary()
            fn = self._COMPARE[op]
            return lambda k, r: fn(left(k, r), right(k, r))
        return left

    def _primary(self):
        kind, value = self._take()
        if value == "(":
            inner = self._or()
            self._take(")")
            return inner
        if kind == "str":
            text = json.loads(value)
            return lambda k, r: text
        if kind == "num":
            number = float(value.rstrip("fF"))
            return lambda k, r: number
        if kind != "id":
            raise CoverageError(f"unexpected {value!r} in condition {self.expr!r}")
        if value in ("true", "false", "null"):
            constant = _literal(value)
            return lambda k, r: constant
        if value == self.subject:
            self._take(".")
            value = self._take()[1]
        node = self._property(value)
        while self._peek() == ".":
            self._take()
            method = self._take()[1]
            if method not in ("EndsWith", "StartsWith"):
                raise CoverageError(f"unsupported method {method} in condition {self.expr!r}")
            self._take("(")
            kind, argument = self._take()
            if kind != "str":
                raise CoverageError(f"{method} needs a string literal in condition {self.expr!r}")
            if self._peek() == ",":  # StringComparison.Ordinal
                self._take()
                self._take()
                self._take(".")
                self._take()
            self._take(")")
            text, ends = json.loads(argument), method == "EndsWith"
            node = (lambda n, t, e: lambda k, r: isinstance(n(k, r), str)
                    and (n(k, r).endswith(t) if e else n(k, r).startswith(t)))(node, text, ends)
        return node

    def _property(self, name):
        fields = self.maps.fields.get(self.cls, {})
        if name in fields:
            field, _, default = fields[name]
            if field == "@key":
                return lambda k, r: k
            return lambda k, r: r.get(field, default)
        computed = self.maps.computed.get(self.cls, {})
        if name in computed:
            return CsCondition(computed[name], self.cls, None, self.maps).tree
        raise CoverageError(f"property {self.cls}.{name} in condition {self.expr!r} is not loaded from data")


def simulation_coverage(root=SIMULATION):
    """What the simulation implements, read from its sources (design.md §2, §3)."""
    run = _read(root, "Run.cs")
    runs = run + "".join(_text(p) for p in sorted(glob.glob(os.path.join(root, "Run.*.cs"))))
    abilities = _read(root, "Abilities/Abilities.cs")
    attributes = _read(root, "Stats/Attributes.cs")
    game_data = _read(root, "Content/GameData.cs")
    definitions = _read(root, "Content/Definitions.cs")
    maps = _field_maps(game_data, definitions)

    factory = _method_body(abilities, r"public static class AbilityFactory", "AbilityFactory")
    ability_classes = set(re.findall(r'\{\s*"(\w+)",', factory))
    if not ability_classes:
        raise CoverageError("AbilityFactory has no registered classes")
    # Handlers that implement only some m_eTargeting values of their class (AbilityFactory.supports, Mechanics §26 Р15).
    ability_targeting = {}
    for cls, handler in re.findall(r'\{\s*"(\w+)",\s*(\w+)\.Supports\s*\}', factory):
        source = _read(root, f"Abilities/{handler}.cs")
        body = _search(r"public static bool Supports\(PowerUpDef def\) =>(.*?);", source, f"{handler}.Supports").group(1)
        if "def.Targeting" not in body:
            raise CoverageError(f"{handler}.Supports does not check def.Targeting")
        constants = dict(re.findall(r'const string (\w+) = "(\w+)";', source))
        values = set(re.findall(r'"(\w+)"', body)) | {constants[n] for n in re.findall(r"\b(\w+)\b", body) if n in constants}
        if not values:
            raise CoverageError(f"{handler}.Supports names no targeting value")
        ability_targeting[cls] = values

    declared = dict(re.findall(r"public static readonly int (\w+) = Id\(\"(\w+)\"\);", attributes))
    if not declared:
        raise CoverageError("Attr attribute declarations not found in Stats/Attributes.cs")
    supported = _search(r"SupportedHeroAttributes = new HashSet<int>\s*\{(.*?)\};", run, "Run.SupportedHeroAttributes")
    hero_attributes = set()
    for member in re.findall(r"Attr\.(\w+)", supported.group(1)):
        if member not in declared:
            raise CoverageError(f"Attr.{member} in SupportedHeroAttributes is not declared")
        hero_attributes.add(declared[member])
    used_attributes = set()
    for path in sorted(glob.glob(os.path.join(root, "**", "*.cs"), recursive=True)):
        if os.path.basename(path) == "Attributes.cs":
            continue
        for member in re.findall(r"\bAttr\.(\w+)", _text(path)):
            if member in declared:
                used_attributes.add(declared[member])

    enemy_method = _search(r"private void ReportUnsupportedEnemy\(EnemyDef (\w+)\)", run, "Run.ReportUnsupportedEnemy")
    enemy_body = _method_body(run, r"private void ReportUnsupportedEnemy\(EnemyDef \w+\)", "Run.ReportUnsupportedEnemy")
    handled = _search(r"bool handled = (.*?);", enemy_body, "`bool handled` in Run.ReportUnsupportedEnemy").group(1)
    enemy_handled = CsCondition(" ".join(handled.split()), "EnemyDef", enemy_method.group(1), maps)

    movement_names = _enum_map(game_data, "ParseMovement", "MovementBehavior")
    movements = {movement_names[m] for m in re.findall(r"MovementBehavior\.(\w+)", runs) if m in movement_names}
    if not movements:
        raise CoverageError("no MovementBehavior is used in Run*.cs")

    aliases = dict((var, name) for name, var in re.findall(r'TryGetValue\("(\w+)", out (\w+Def)\)', run))
    drop = _search(r"private void Drop\(PickupDef (\w+),", run, "Run.Drop")
    pickup_conditions = []
    for condition, statement in _if_branches(_method_body(run, r"private void Drop\(PickupDef \w+,", "Run.Drop")):
        if "Diagnostics.Report" in statement:
            continue
        for var, name in aliases.items():
            condition = re.sub(rf"\b(\w+) == {var}\b", rf'\1.Key == "{name}"', condition)
        pickup_conditions.append(CsCondition(condition, "PickupDef", drop.group(1), maps))
    if not pickup_conditions:
        raise CoverageError("Run.Drop has no implemented branches")

    spawn_names = _enum_map(game_data, "ParseSpawn", "SpawnBehavior")
    spawn_body = _method_body(run, r"private bool SpawnPosition\(", "Run.SpawnPosition")
    spawn_behaviors = set()
    for member in re.findall(r"case SpawnBehavior\.(\w+):", spawn_body):
        if member not in spawn_names:
            raise CoverageError(f"SpawnBehavior.{member} is not parsed from data")
        spawn_behaviors.add(spawn_names[member])
    if not spawn_behaviors:
        raise CoverageError("Run.SpawnPosition handles no SpawnBehavior")

    rules_body = _method_body(run, r"private void ReportUnsupportedRules\(\)", "Run.ReportUnsupportedRules")
    difficulty_unsupported = [CsCondition(c, "DifficultyDef", "Difficulty", maps)
                              for c, s in _if_branches(rules_body) if "Diagnostics.Report" in s]
    return Coverage(ability_classes, hero_attributes, used_attributes, enemy_handled, movements,
                    pickup_conditions, spawn_behaviors, difficulty_unsupported, maps, ability_targeting)


_METHOD = re.compile(r"^\s*(?:public|private|internal|protected)\b[^=;{]*?\b(\w+)\s*\(")


def test_references(root=TESTS):
    """String literals and Attr.X in DotNet/**/*.cs -> sorted ["File.cs:Method", ...]."""
    found = collections.defaultdict(set)
    for path in sorted(glob.glob(os.path.join(root, "**", "*.cs"), recursive=True)):
        parts = os.path.relpath(path, root).split(os.sep)
        if "bin" in parts or "obj" in parts:
            continue
        method, pending = None, []
        for line in _text(path).splitlines():
            if line.strip().startswith("["):  # [TestCase("...")] belongs to the method declared below it
                pending.append(line)
                continue
            declaration = _METHOD.match(line)
            if declaration and not re.search(r"\b(?:class|struct|interface|enum|new)\b", line.split("(")[0]):
                method = declaration.group(1)
            lines, pending = pending + [line], []
            if method is None:
                continue
            place = f"{os.path.basename(path)}:{method}"
            for text in lines:
                for literal in re.findall(r'"((?:[^"\\]|\\.)*)"', text):
                    found[literal].add(place)
                for member in re.findall(r"\bAttr\.(\w+)", text):
                    found["Attr." + member].add(place)
    return {k: sorted(v) for k, v in found.items()}


def arena_targets():
    path = os.path.join(JSON_DIR, "survivors_arena_entities.json")
    if not os.path.exists(path):
        return set()
    return set(json.loads(_text(path)).get("targets", {}))


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




def load_all():
    types, defs = {}, {}
    for category in CATEGORIES:
        types[category], defs[category] = load(category)
    return types, defs


def _min_level(levels):
    """The lowest level; `verified` of a component counts as `implemented` (design.md §3.3, §3.7)."""
    lowest = min((IMPLEMENTATION_LEVELS.index(l) for l in levels), default=IMPLEMENTATION_LEVELS.index("implemented"))
    return IMPLEMENTATION_LEVELS[min(lowest, IMPLEMENTATION_LEVELS.index("implemented"))]


def _at_least_implemented(level):
    return IMPLEMENTATION_LEVELS.index(level) >= IMPLEMENTATION_LEVELS.index("implemented")


def _attribute_names(attributes):
    return [a["m_eType"].replace("k_eSurvivorsAttribute_", "") for a in attributes or [] if "m_eType" in a]


def implementation_levels(defs, registry, coverage, targets):
    """Base implementation level per entry from the simulation sources (design.md §3.1-§3.7, all Р)."""
    supported = coverage.hero_attributes
    levels = {}
    by_id = {v["m_unPowerUpID"]: k for k, v in defs["powerups"].items() if "m_unPowerUpID" in v}

    def passive(value):
        # Minor and authored upgrades both give hero attributes (Mechanics §25 Р1).
        choices = (value.get("m_vecMinorUpgradeChoices") or []) + (value.get("m_vecAuthoredUpgradeChoices") or [])
        return bool(choices) and all(name in supported for c in choices
                                     for name in _attribute_names(c.get("m_vecGlobalUpgradeAttributes")))

    def powerup(key, stack=()):
        entry_key = f"powerups/{key}"
        if entry_key in levels:
            return levels[entry_key]
        value, role = defs["powerups"][key], registry[entry_key]["role"]
        cls = value.get("_class")
        if role == "active-ability" or (role == "innate" and cls != "CSurvivorsPowerUpDefinition"):
            allowed = coverage.ability_targeting.get(cls)
            handled = cls in coverage.ability_classes and (allowed is None or value.get("m_eTargeting") in allowed)
            level = "implemented" if handled else "none"
        elif role == "passive-item":
            level = "implemented" if passive(value) else "none"
        elif role == "recipe":
            parts = [by_id.get(pid) for pid in value.get("m_vecRecipeItems", [])]
            ok = passive(value) and all(p in defs["powerups"] and p not in stack
                                        and _at_least_implemented(powerup(p, stack + (key,))) for p in parts)
            level = "implemented" if ok else "none"
        elif role == "innate":
            choices = value.get("m_vecMinorUpgradeChoices") or [{}]
            names = set(_attribute_names(value.get("m_vecBaseAttributes"))
                        + _attribute_names(choices[0].get("m_vecGlobalUpgradeAttributes")))
            hits = len(names & supported)
            level = "implemented" if names and hits == len(names) else "partial" if hits else "none"
        else:  # service:* and unknown: the simulation does not give them
            level = "none"
        levels[entry_key] = level
        return level

    for key in defs["powerups"]:
        powerup(key)
    for key, value in defs["enemies"].items():
        entry = registry[f"enemies/{key}"]
        if value.get("m_eMovementBehavior") not in coverage.movements:
            level = "none"
        elif coverage.enemy_handled.evaluate(key, value):
            level = "implemented"
        elif any(r.startswith(("spawners/", "enemies/")) for r in entry["referencedBy"]):
            level = "partial"
        else:
            level = "none"
        levels[f"enemies/{key}"] = level
    for key, value in defs["spawners"].items():
        behavior = value.get("m_eSpawnBehavior")
        handled = behavior in coverage.spawn_behaviors
        if handled and behavior.startswith("STATIC_IN_MAP"):
            handled = value.get("m_sSpawnInfoTargetName") in targets
        enemy = levels.get(f"enemies/{value.get('m_sEnemyName')}", "none")
        levels[f"spawners/{key}"] = _min_level(["implemented" if handled else "none", enemy])
    for key, value in defs["pickups"].items():
        ok = any(c.evaluate(key, value) for c in coverage.pickup_conditions)
        levels[f"pickups/{key}"] = "implemented" if ok else "none"
    used = supported | coverage.used_attributes
    for key in defs["attributes"]:
        entry_key = f"attributes/{key}"
        level = "implemented" if key.replace("k_eSurvivorsAttribute_", "") in used else "none"
        if registry[entry_key]["role"] == "meta-progression" and level == "implemented":
            level = "partial"  # permanent upgrades are not implemented yet (roadmap block E)
        levels[entry_key] = level
    for key, value in defs["difficulty"].items():
        entry = registry[f"difficulty/{key}"]
        unsupported = any(c.evaluate(key, value) for c in coverage.difficulty_unsupported)
        spawners = [levels[r] for r in entry["references"] if r.startswith("spawners/")]
        levels[f"difficulty/{key}"] = "partial" if unsupported or not _at_least_implemented(_min_level(spawners)) \
            else "implemented"
    for key, value in defs["heroes"].items():
        parts = [levels.get(f"powerups/{by_id.get(pid)}", "none")
                 for field in ("m_vecStartingPowerUps", "m_vecInnatePowerUps") for pid in value.get(field, [])]
        level = _min_level(parts)
        levels[f"heroes/{key}"] = "partial" if level == "none" else level
    for category in ("levels", "game_modes"):
        for key in defs[category]:
            entry = registry[f"{category}/{key}"]
            parts = [levels.get(r, "none") for r in entry["references"]]
            levels[f"{category}/{key}"] = "implemented" if _at_least_implemented(_min_level(parts)) else "partial"
    return levels


def build_registry(types, defs, loc, native, coverage, tests, binary=None, targets=None):
    """ContentRegistry.json document. References, reachability and roles are the stage-1 rules unchanged;
    implementation, tests, required and the frozen prototype field follow design.md §3."""
    targets = arena_targets() if targets is None else targets
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
    # Roles and names.
    for entry in registry.values():
        c, k, v = entry["category"], entry["key"], defs[entry["category"]][entry["key"]]
        if c == "powerups":
            entry["role"] = powerup_role(v)
            entry["name"] = loc.get(v.get("m_sLocAbilityName", "").lstrip("#"))
            entry["source"] = v.get("m_sSource")
        elif c == "enemies":
            entry["role"] = enemy_role(k, v, split_targets)
            entry["name"] = loc.get(v.get("m_sDisplayName", "").lstrip("#")) or v.get("m_sDisplayName")
            entry["movement"] = v.get("m_eMovementBehavior")
        elif c == "spawners":
            entry["role"] = "elite-room" if f"spawners/{k}" in registry["game_modes/crownfall_act4"]["references"] else "spawner"
            entry["spawnBehavior"] = v.get("m_eSpawnBehavior")
        elif c == "attributes":
            entry["role"] = "meta-progression" if v.get("m_vecMetaProgressionTiers") else "attribute"
            entry["name"] = loc.get(v.get("m_sLocName", "").lstrip("#"))
        elif c == "heroes":
            entry["role"] = "hero"
            entry["name"] = loc.get(v.get("m_sLocDisplayName", "").lstrip("#"))
        elif c == "difficulty":
            entry["role"] = "difficulty"
            entry["name"] = loc.get(v.get("m_sLocDifficultyName", "").lstrip("#"))
        elif c == "pickups":
            entry["role"] = "pickup"
        else:
            entry["role"] = c[:-1]
        entry["prototype"] = "partial" if f"{c}/{k}" in PROTOTYPE_PARTIAL else "none"
        entry["missingResources"] = [r for r in entry["resources"]
                                     if not os.path.exists(os.path.join(SOURCE, r + "_c")) and not os.path.exists(os.path.join(SOURCE, r))]
        entry["references"] = sorted(entry["references"])
        entry["referencedBy"] = sorted(entry["referencedBy"])
    # Coverage by the simulation (design.md §3). Phase 1 scope is every definition of the data (Р2).
    levels = implementation_levels(defs, registry, coverage, targets)
    for key, entry in registry.items():
        k = entry["key"]
        names = [k] + ([f"Attr.{k.replace('k_eSurvivorsAttribute_', '')}"] if entry["category"] == "attributes" else [])
        entry["tests"] = sorted({t for n in names for t in tests.get(n, [])})
        level = levels[key]
        entry["implementation"] = "verified" if level == "implemented" and entry["tests"] else level
        entry["required"] = True
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
        summary[e["category"]][f"implementation:{e['implementation']}"] += 1
        summary[e["category"]]["reachable" if e["reachable"] else "unreachable"] += 1
    return {
        "schema": 2,
        "statusValues": {"prototype": list(IMPLEMENTATION_LEVELS), "implementation": list(IMPLEMENTATION_LEVELS)},
        "summary": {c: dict(v) for c, v in summary.items()},
        "attributesUsedWithoutDefinition": undefined,
        "nativeBinary": binary if native else None,
        "definitions": registry,
    }


def role_errors(registry):
    """Required entries without a role (ТЗ §3: an unknown required class is a coverage error)."""
    return sorted(k for k, e in registry.items() if e.get("required") and e.get("role") in (None, "", "unknown"))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dota", default=os.environ.get("THORN_DOTA_DIR", DEFAULT_DOTA))
    parser.add_argument("--out", default=OUT, help="output directory (default Research/FullGame)")
    args = parser.parse_args(argv)
    out = args.out
    types, defs = load_all()
    loc = localization()
    native, binary = native_classes(args.dota)
    try:
        coverage = simulation_coverage()
    except CoverageError as error:
        print(f"coverage error: {error}", file=sys.stderr)
        return 2
    document = build_registry(types, defs, loc, native, coverage, test_references(), binary)
    registry = document["definitions"]
    missing_roles = role_errors(registry)
    if missing_roles:
        print("required entries without a role (registries are not written):", file=sys.stderr)
        for key in missing_roles:
            print("  " + key, file=sys.stderr)
        return 1
    summary = document["summary"]
    undefined = document["attributesUsedWithoutDefinition"]
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, "ContentRegistry.json"), "w", encoding="utf-8") as f:
        json.dump(document, f, ensure_ascii=False, indent=1)
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
    with open(os.path.join(out, "SoundRegistry.json"), "w", encoding="utf-8") as f:
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
    with open(os.path.join(out, "SourceVersions.json"), "w", encoding="utf-8") as f:
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
    return 0


if __name__ == "__main__":
    sys.exit(main())
