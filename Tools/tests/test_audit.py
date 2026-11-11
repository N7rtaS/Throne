"""Tests of Tools/audit.py: simulation coverage and the implementation field of ContentRegistry.json.
Run: python3 -m unittest discover -s Tools/tests
Expected values come from Research/Json and the simulation sources (design: .kiro/specs/audit-simulation-coverage).
"""
import contextlib
import io
import json
import os
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import audit  # noqa: E402

LEVELS = audit.IMPLEMENTATION_LEVELS


class Shared:
    """Coverage and registry are built once: parsing the sources and data takes a few seconds."""
    _registry = None

    @classmethod
    def registry(cls):
        if cls._registry is None:
            types, defs = audit.load_all()
            cls._registry = audit.build_registry(types, defs, audit.localization(), None,
                                                 audit.simulation_coverage(), audit.test_references())
        return cls._registry


def level(key):
    return Shared.registry()["definitions"][key]["implementation"]


def at_least(key, minimum):
    return LEVELS.index(level(key)) >= LEVELS.index(minimum)


class SimulationCoverageTests(unittest.TestCase):
    def setUp(self):
        self.coverage = audit.simulation_coverage()

    def test_ability_classes_come_from_ability_factory(self):
        self.assertEqual(self.coverage.ability_classes,
                         {"CSurvivorsPowerUpDefinition_ArcaneBolt", "CSurvivorsPowerUpDefinition_MagicMissile",
                          "CSurvivorsPowerUpDefinition_EchoStrike", "CSurvivorsPowerUpDefinition_InstantAttack"})

    def test_instant_attack_supports_only_strongest_target(self):
        # Mechanics §26 Р15: Annihilation shares the class but not the targeting (Task 23).
        self.assertEqual(self.coverage.ability_targeting,
                         {"CSurvivorsPowerUpDefinition_InstantAttack": {"STRONGEST_TARGET_IN_RANGE"}})

    def test_supported_hero_attributes_has_thirty_four_entries(self):
        # 20 before Task 18 plus 14 attributes of passive items (Mechanics §25).
        self.assertEqual(len(self.coverage.hero_attributes), 34)
        self.assertIn("MaxHP", self.coverage.hero_attributes)
        self.assertIn("Luck", self.coverage.hero_attributes)
        self.assertIn("Lives", self.coverage.hero_attributes)
        self.assertNotIn("DurationEffect", self.coverage.hero_attributes)

    def test_used_attributes_exclude_attr_members(self):
        self.assertIn("ProjectileBounces", self.coverage.used_attributes)
        for member in ("Id", "Name", "Capacity", "Prefix"):
            self.assertNotIn(member, self.coverage.used_attributes)

    def test_all_seven_spawn_behaviours_are_handled(self):
        self.assertEqual(self.coverage.spawn_behaviors, {
            "RANDOM_DIRECTION", "FIXED_DIRECTION", "FIXED_DIRECTION_PERPENDICULAR_WALL", "OVAL_AROUND_PLAYER",
            "SPAWNER_ORIGIN_RADIUS", "STATIC_IN_MAP", "STATIC_IN_MAP_IGNORE_PLAYER_RADIUS"})

    def test_movements_are_the_four_parsed_behaviours(self):
        self.assertEqual(self.coverage.movements, {
            "ENEMY_MOVEMENT_BEHAVIOR_TRACKING", "ENEMY_MOVEMENT_BEHAVIOR_LINEAR",
            "ENEMY_MOVEMENT_BEHAVIOR_LINEAR_SIN_WAVE", "ENEMY_MOVEMENT_BEHAVIOR_STATIONARY"})

    def test_missing_construct_is_a_coverage_error(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(audit.CoverageError):
                audit.simulation_coverage(tmp)


class CsConditionTests(unittest.TestCase):
    def setUp(self):
        self.fields = audit.simulation_coverage().field_maps

    def check(self, expr, record, key="x", cls="EnemyDef", subject="def"):
        return audit.CsCondition(expr, cls, subject, self.fields).evaluate(key, record)

    def test_operators(self):
        record = {"_class": "CSurvivorsEnemyDefinition_Golem", "m_flAbsorbRadius": 10}
        self.assertTrue(self.check('def.AbsorbRadius > 0 && def.Class.EndsWith("_Golem")', record))
        self.assertFalse(self.check('!(def.AbsorbRadius > 0) || def.ResurrectionTimes > 0', record))
        self.assertTrue(self.check('def.Class != "CSurvivorsEnemyDefinition" && (def.SplitOnDeathNumUnits > 0 || def.AbsorbRadius > 5)', record))

    def test_computed_property_is_expanded(self):
        self.assertTrue(self.check("def.IsBoss", {"_class": "CSurvivorsEnemyDefinition_ImperiaBoss"}))
        self.assertFalse(self.check("def.IsBoss", {"_class": "CSurvivorsEnemyDefinition"}))

    def test_missing_field_uses_loader_default(self):
        self.assertFalse(self.check("def.AbsorbRadius > 0", {"_class": "CSurvivorsEnemyDefinition"}))

    def test_key_and_string_comparison(self):
        self.assertTrue(self.check('pickup.Key == "magnet"', {}, key="magnet", cls="PickupDef", subject="pickup"))

    def test_unknown_property_is_a_coverage_error(self):
        with self.assertRaises(audit.CoverageError):
            audit.CsCondition("def.NoSuchProperty > 0", "EnemyDef", "def", self.fields)


class ImplementationTests(unittest.TestCase):
    def test_demo_magic_missile_is_implemented(self):
        self.assertTrue(at_least("powerups/magic_missile", "implemented"))

    def test_echo_strike_is_verified_and_annihilation_is_none(self):
        self.assertEqual(level("powerups/echo_strike"), "verified")
        self.assertEqual(level("powerups/annihilation"), "none")

    def test_demo_absorber_is_implemented(self):
        self.assertTrue(at_least("enemies/absorber", "implemented"))

    def test_powerups(self):
        self.assertTrue(at_least("powerups/kez_innate", "implemented"))
        self.assertTrue(at_least("powerups/yasha", "implemented"))
        self.assertTrue(at_least("powerups/blade_mail", "implemented"))
        self.assertTrue(at_least("powerups/crystalys", "implemented"))
        self.assertTrue(at_least("powerups/ynk_trident", "implemented"))
        self.assertTrue(at_least("powerups/yasha_and_kaya", "implemented"))
        self.assertTrue(at_least("powerups/skywrath_mage_innate", "implemented"))
        self.assertEqual(level("powerups/gold"), "none")

    def test_enemies(self):
        self.assertTrue(at_least("enemies/boss_tower", "implemented"))
        self.assertEqual(level("enemies/snotty"), "none")

    def test_pickups(self):
        self.assertEqual(level("pickups/shard"), "none")
        self.assertEqual(level("pickups/scepter"), "none")
        self.assertTrue(at_least("pickups/treasure", "implemented"))
        self.assertTrue(at_least("pickups/nothing", "implemented"))

    def test_heroes_difficulty_and_mode(self):
        self.assertEqual(level("heroes/dragonus"), "verified")
        self.assertEqual(level("heroes/kez"), "verified")
        self.assertEqual(level("heroes/shen"), "partial")
        self.assertEqual(level("difficulty/meteor_mayhem"), "partial")
        self.assertEqual(level("game_modes/crownfall_act4"), "partial")

    def test_meta_progression_attribute_is_at_most_partial(self):
        registry = Shared.registry()["definitions"]
        for entry in registry.values():
            if entry["role"] == "meta-progression":
                self.assertIn(entry["implementation"], ("none", "partial"), entry["key"])

    def test_every_entry_has_a_level_and_required(self):
        for key, entry in Shared.registry()["definitions"].items():
            self.assertIn(entry["implementation"], LEVELS, key)
            self.assertIs(entry["required"], True, key)


class VerifiedTests(unittest.TestCase):
    def test_magic_missile_lists_its_tests(self):
        entry = Shared.registry()["definitions"]["powerups/magic_missile"]
        self.assertEqual(entry["implementation"], "verified")
        self.assertTrue(any(t.startswith("MagicMissileTests.cs:") for t in entry["tests"]), entry["tests"])

    def test_partial_is_not_raised_to_verified(self):
        entry = Shared.registry()["definitions"]["heroes/shen"]
        self.assertTrue(entry["tests"])
        self.assertEqual(entry["implementation"], "partial")

    def test_attribute_is_verified_by_attr_reference(self):
        refs = audit.test_references()
        self.assertIn("KezInnateTests.cs:", " ".join(refs["Attr.Damage_Magical"]))
        self.assertEqual(level("attributes/k_eSurvivorsAttribute_Damage_Magical"), "verified")


class PrototypeTests(unittest.TestCase):
    def test_prototype_values_are_frozen(self):
        registry = Shared.registry()["definitions"]
        partial = {k for k, e in registry.items() if e["prototype"] == "partial"}
        self.assertEqual(partial, set(audit.PROTOTYPE_PARTIAL))
        self.assertEqual(len(partial), 70)
        self.assertEqual({e["prototype"] for e in registry.values()}, {"none", "partial"})


class RegistryTests(unittest.TestCase):
    def test_schema_status_values_and_summary(self):
        registry = Shared.registry()
        self.assertEqual(registry["schema"], 2)
        self.assertEqual(registry["statusValues"]["implementation"], list(LEVELS))
        self.assertIn("implementation:verified", registry["summary"]["powerups"])

    def test_build_is_repeatable(self):
        types, defs = audit.load_all()
        args = (types, defs, audit.localization(), None, audit.simulation_coverage(), audit.test_references())
        first = json.dumps(audit.build_registry(*args), ensure_ascii=False, sort_keys=True)
        second = json.dumps(audit.build_registry(*args), ensure_ascii=False, sort_keys=True)
        self.assertEqual(first, second)


class RoleTests(unittest.TestCase):
    def test_role_errors_finds_required_entry_without_role(self):
        registry = {"powerups/x": {"required": True, "role": "unknown"},
                    "powerups/y": {"required": True, "role": None},
                    "powerups/z": {"required": False, "role": "unknown"},
                    "powerups/w": {"required": True, "role": "recipe"}}
        self.assertEqual(audit.role_errors(registry), ["powerups/x", "powerups/y"])

    def test_main_fails_without_writing_files_when_role_is_missing(self):
        original = audit.load

        def load(category):
            generic, defs = original(category)
            if category == "powerups":
                defs = dict(defs, roleless_powerup={"_class": "CSurvivorsPowerUpDefinition", "m_unPowerUpID": 9999})
            return generic, defs

        with tempfile.TemporaryDirectory() as tmp, mock.patch.object(audit, "load", load):
            out = os.path.join(tmp, "out")
            stderr = io.StringIO()
            with contextlib.redirect_stderr(stderr), contextlib.redirect_stdout(io.StringIO()):
                code = audit.main(["--out", out, "--dota", os.path.join(tmp, "no-dota")])
            self.assertEqual(code, 1)
            self.assertIn("powerups/roleless_powerup", stderr.getvalue())
            self.assertFalse(os.path.exists(os.path.join(out, "ContentRegistry.json")))

    def test_main_returns_two_on_coverage_error(self):
        error = audit.CoverageError("Run.Drop not found in the simulation sources")
        with tempfile.TemporaryDirectory() as tmp, \
                mock.patch.object(audit, "simulation_coverage", side_effect=error):
            out = os.path.join(tmp, "out")
            stderr = io.StringIO()
            with contextlib.redirect_stderr(stderr), contextlib.redirect_stdout(io.StringIO()):
                code = audit.main(["--out", out, "--dota", os.path.join(tmp, "no-dota")])
            self.assertEqual(code, 2)
            self.assertIn("Run.Drop", stderr.getvalue())
            self.assertFalse(os.path.exists(out))


if __name__ == "__main__":
    unittest.main()
