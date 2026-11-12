using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// All definitions of the mode, parsed once from the nine survivors_*.json files
    /// (Research/Json, copied to Assets/Game/Data/Source). Source data is never modified.
    /// </summary>
    public sealed class GameData
    {
        public static readonly string[] Categories =
            { "attributes", "difficulty", "enemies", "game_modes", "heroes", "levels", "pickups", "powerups", "spawners" };

        public readonly Dictionary<string, ModeDef> Modes = new Dictionary<string, ModeDef>();
        public readonly Dictionary<string, LevelDef> Levels = new Dictionary<string, LevelDef>();
        public readonly Dictionary<string, DifficultyDef> Difficulties = new Dictionary<string, DifficultyDef>();
        public readonly Dictionary<string, HeroDef> Heroes = new Dictionary<string, HeroDef>();
        public readonly Dictionary<string, PowerUpDef> PowerUps = new Dictionary<string, PowerUpDef>();
        public readonly Dictionary<int, PowerUpDef> PowerUpsById = new Dictionary<int, PowerUpDef>();
        public readonly Dictionary<string, EnemyDef> Enemies = new Dictionary<string, EnemyDef>();
        public readonly Dictionary<int, EnemyDef> EnemiesById = new Dictionary<int, EnemyDef>();
        public readonly Dictionary<string, SpawnerDef> Spawners = new Dictionary<string, SpawnerDef>();
        public readonly Dictionary<string, PickupDef> Pickups = new Dictionary<string, PickupDef>();
        public readonly Dictionary<int, PickupDef> PickupsById = new Dictionary<int, PickupDef>();
        public readonly Dictionary<string, JObject> AttributeDefinitions = new Dictionary<string, JObject>();
        /// <summary>Problems of the data found while loading; every run reports them too.</summary>
        public readonly Diagnostics Diagnostics = new Diagnostics();

        /// <summary>
        /// Named points of the arena entity lump (survivors_arena_entities.json, Tools/arena-entities.py), Source units:
        /// player_spawn, destructible_spawn_vase, elite_room, boss_spawn, ... Empty when the file is absent.
        /// </summary>
        public readonly Dictionary<string, Vec2[]> ArenaTargets = new Dictionary<string, Vec2[]>();

        /// <summary>Arena grid navigation (survivors_arena.gnv), or null when not available.</summary>
        public NavGrid Nav;

        /// <param name="readCategory">Returns the JSON text of survivors_{category}.json.</param>
        /// <param name="readArenaEntities">Returns survivors_arena_entities.json, or null when it is not available.</param>
        /// <param name="readNavGrid">Returns the bytes of survivors_arena.gnv, or null when it is not available.</param>
        public static GameData Load(Func<string, string> readCategory, Func<string> readArenaEntities = null, Func<byte[]> readNavGrid = null)
        {
            var data = new GameData();
            foreach (var category in Categories)
            {
                var root = JObject.Parse(readCategory(category));
                foreach (var property in root.Properties())
                {
                    if (!(property.Value is JObject value) || property.Name.StartsWith("_", StringComparison.Ordinal)) continue;
                    data.Add(category, property.Name, value);
                }
            }
            string arena = readArenaEntities?.Invoke();
            if (arena != null && JObject.Parse(arena)["targets"] is JObject targets)
                foreach (var property in targets.Properties())
                    data.ArenaTargets[property.Name] = property.Value.Select(p => new Vec2((float)p[0], (float)p[1])).ToArray();
            var gnv = readNavGrid?.Invoke();
            if (gnv != null) data.Nav = new NavGrid(gnv);
            return data;
        }

        public bool TryArenaPoint(string name, out Vec2 point)
        {
            point = Vec2.Zero;
            if (!ArenaTargets.TryGetValue(name, out var points) || points.Length == 0) return false;
            point = points[0];
            return true;
        }

        private void Add(string category, string key, JObject v)
        {
            switch (category)
            {
                case "attributes": AttributeDefinitions[key] = v; Attr.Id(key); break;
                case "difficulty":
                    if (!Difficulties.ContainsKey(key)) DifficultyOrder.Add(key);
                    Difficulties[key] = ReadDifficulty(key, v);
                    break;
                case "enemies": var e = ReadEnemy(key, v); Enemies[key] = e; EnemiesById[e.Id] = e; break;
                case "game_modes": Modes[key] = ReadMode(key, v); break;
                case "heroes": Heroes[key] = ReadHero(key, v); break;
                case "levels": Levels[key] = ReadLevel(key, v); break;
                case "pickups": var p = ReadPickup(key, v); Pickups[key] = p; PickupsById[p.Id] = p; break;
                case "powerups": var u = ReadPowerUp(key, v); u.BaseAttributes = Distinct(key, u.BaseAttributes); PowerUps[key] = u; PowerUpsById[u.Id] = u; break;
                case "spawners": Spawners[key] = ReadSpawner(key, v); break;
            }
        }

        /// <summary>Difficulty keys in data order; the client lists them in this order as echelons 1..8.</summary>
        public readonly List<string> DifficultyOrder = new List<string>();
        /// <summary>
        /// A difficulty key, or "echelon_N": difficulties 1..N stacked. Video 09.11.2026: Imperia has 33 000 HP
        /// on echelon 2, i.e. 30 000 * (1 - 0.1 + 0.2): multipliers of story and harder_punches add up.
        /// </summary>
        public DifficultyDef Difficulty(string key)
        {
            if (key != null && Difficulties.TryGetValue(key, out var d)) return d;
            const string prefix = "echelon_";
            if (key != null && key.StartsWith(prefix) && int.TryParse(key.Substring(prefix.Length), out int n) && n >= 1)
                return Echelon(n);
            return DifficultyDef.None(key ?? "Default");
        }
        public DifficultyDef Echelon(int n)
        {
            var result = DifficultyDef.None("echelon_" + n);
            var events = new List<LevelEventDef>();
            for (int i = 0; i < n && i < DifficultyOrder.Count; i++)
            {
                var d = Difficulties[DifficultyOrder[i]];
                result.EnemyHealthMultiplier += d.EnemyHealthMultiplier;
                result.EnemyDamageMultiplier += d.EnemyDamageMultiplier;
                result.EnemyMovementSpeedMultiplier += d.EnemyMovementSpeedMultiplier;
                result.EnemyTurnRateMultiplier += d.EnemyTurnRateMultiplier;
                result.EnemySpawnCountMultiplier += d.EnemySpawnCountMultiplier;
                result.XPDropChanceMultiplier += d.XPDropChanceMultiplier;
                result.AdditionalFirstFloorTime += d.AdditionalFirstFloorTime;
                result.EnemyTouchMovementSlowDuration = Math.Max(result.EnemyTouchMovementSlowDuration, d.EnemyTouchMovementSlowDuration);
                result.EnableMeteorModifier |= d.EnableMeteorModifier;
                events.AddRange(d.AdditionalEnemyEvents);
                if (d.OverrideImperiaEnrageHealthThresholds.Length > 0)
                    result.OverrideImperiaEnrageHealthThresholds = d.OverrideImperiaEnrageHealthThresholds;
            }
            result.AdditionalEnemyEvents = events.ToArray();
            return result;
        }

        // ---- field helpers ----
        private static float F(JObject o, string name, float fallback = 0) => o.TryGetValue(name, out var t) ? (float)t : fallback;
        private static float? NF(JObject o, string name) => o.TryGetValue(name, out var t) ? (float?)(float)t : null;
        private static int I(JObject o, string name, int fallback = 0) => o.TryGetValue(name, out var t) ? (int)t : fallback;
        private static int? NI(JObject o, string name) => o.TryGetValue(name, out var t) ? (int?)(int)t : null;
        private static bool B(JObject o, string name, bool fallback = false) => o.TryGetValue(name, out var t) ? (bool)t : fallback;
        private static string S(JObject o, string name) => o.TryGetValue(name, out var t) ? (string)t : null;
        private static int[] Ints(JObject o, string name) => o.TryGetValue(name, out var t) ? t.Select(x => (int)x).ToArray() : new int[0];
        private static float[] Floats(JObject o, string name) => o.TryGetValue(name, out var t) ? t.Select(x => (float)x).ToArray() : new float[0];
        private static Vec2 V2(JObject o, string name)
        {
            var values = Floats(o, name);
            return values.Length >= 2 ? new Vec2(values[0], values[1]) : Vec2.Zero;
        }

        public static AttributeValue[] Attributes(JToken token)
        {
            if (!(token is JArray items)) return new AttributeValue[0];
            return items.Select(i => new AttributeValue(Attr.Id((string)i["m_eType"]), (float)i["m_flValue"])).ToArray();
        }

        /// <summary>
        /// An attribute listed twice in m_vecBaseAttributes of one powerup (knife_throw: ProjectileAttackInterval 0.15
        /// twice) counts once; the repeat is reported (owner's decision, Mechanics §29 Р12).
        /// </summary>
        private AttributeValue[] Distinct(string key, AttributeValue[] items)
        {
            var kept = new List<AttributeValue>(items.Length);
            foreach (var item in items)
            {
                bool repeated = false;
                foreach (var k in kept) if (k.Id == item.Id) { repeated = true; break; }
                if (!repeated) { kept.Add(item); continue; }
                string name = Attr.Name(item.Id);
                Diagnostics.Report($"data:duplicate-attribute:{key}:{name}",
                    $"Powerup {key} lists {name} more than once in m_vecBaseAttributes; counted once");
            }
            return kept.Count == items.Length ? items : kept.ToArray();
        }

        private UpgradeChoice[] Choices(string key, JObject o, string name)
        {
            if (!(o[name] is JArray items)) return new UpgradeChoice[0];
            return items.Select(i => new UpgradeChoice
            {
                Rarity = Rarity(key, i),
                Local = Attributes(i["m_vecUpgradeAttributes"]),
                Global = Attributes(i["m_vecGlobalUpgradeAttributes"]),
            }).ToArray();
        }

        /// <summary>
        /// RARITY_COMMON, or no field (authored records have none), is common; anything else is reported and
        /// counts as common, because the other client values are not recovered (Mechanics §33 Р6–Р7).
        /// </summary>
        private UpgradeRarity Rarity(string key, JToken choice)
        {
            string value = (string)choice["m_unRarity"];
            if (value == null || value == "RARITY_COMMON") return UpgradeRarity.Common;
            Diagnostics.Report($"data:unknown-rarity:{key}:{value}",
                $"Powerup {key} has an unknown upgrade rarity {value}; counted as common");
            return UpgradeRarity.Common;
        }

        private static LevelEventDef[] Events(JObject o, string name)
        {
            if (!(o[name] is JArray items)) return new LevelEventDef[0];
            return items.Cast<JObject>().Select(e => new LevelEventDef
            {
                SpawnerName = S(e, "m_sSpawnerName"),
                Start = F(e, "m_flStartTime"),
                End = NF(e, "m_flEndTime"),
                MinimumCountOverride = NI(e, "m_nMinimumEnemyCountOverride"),
                IntervalOverride = NF(e, "m_flSpawnIntervalOverride"),
                MaxSpawnCountPerIntervalOverride = NI(e, "m_nMaxSpawnCountPerIntervalOverride"),
            }).ToArray();
        }

        /// <summary>m_vecLootEntryCollections of a loot table (enemy m_fullLootTable, mode m_luckyLootTable).</summary>
        private static LootCollectionDef[] LootCollections(JToken table) =>
            table?["m_vecLootEntryCollections"] is JArray collections
                ? collections.Cast<JObject>().Select(c => new LootCollectionDef
                {
                    Chance = F(c, "m_flChance", 1),
                    Entries = (c["m_vecLootEntries"] as JArray ?? new JArray()).Cast<JObject>().Select(e => new LootEntryDef
                    {
                        PickupId = I(e, "m_unPickupID"), MinAmount = I(e, "m_nMinAmount"), MaxAmount = I(e, "m_nMaxAmount"),
                    }).ToArray(),
                }).ToArray()
                : new LootCollectionDef[0];

        private static ModeDef ReadMode(string key, JObject v)
        {
            var mode = new ModeDef
            {
                Key = key, Raw = v,
                LevelName = S(v, "m_sLevelName"), DifficultyName = S(v, "m_sDifficultyName"),
                FirstFloorTimeLimit = F(v, "m_flFirstFloorTimeLimit"),
                MaxActiveSlots = I(v, "m_nMaxActiveSlots"), MaxPassiveSlots = I(v, "m_nMaxPassiveSlots"),
                LevelUpChoices = I(v, "m_nLevelUpChoices", 3), MaxXPPickupsInWorld = I(v, "m_nMaxXPPickupsInWorld"),
                ExistingItemWeight = F(v, "m_flExistingItemGenerationWeight"),
                NewItemWeight = F(v, "m_flNewItemGenerationWeight"),
                PassiveItemWeight = F(v, "m_flPassiveItemGenerationWeight"),
                EnemyRadius = F(v, "m_flEnemyRadius"), EnemyRadiusVariance = F(v, "m_flEnemyRadiusVariance"),
                EnemyDespawnTime = F(v, "m_flEnemyDespawnTime"), EnemyDespawnBuffer = F(v, "m_flEnemyDespawnBuffer"),
                SeparationVelocityInfluence = F(v, "m_flSeperationVelocityInfluence"),
                SeparationInterpolationSpeed = F(v, "m_flSeperationVelocityInterpolationSpeed"),
                PlayerReviveTimer = F(v, "m_flPlayerReviveTimer"),
                PlayerPositionHistoryBufferDuration = F(v, "m_flPlayerPositionHistoryBufferDuration"),
                RequiredExperienceBase = F(v, "m_flRequiredExperienceBase"),
                RequiredExperienceExponent = F(v, "m_flRequiredExperienceExponent"),
                LevelUpDelay = F(v, "m_flLevelUpDelay"),
                LevelUpKnockbackRadius = F(v, "m_flLevelUpKnockbackRadius"),
                LevelUpKnockbackDistance = F(v, "m_flLevelUpKnockbackDistance"),
                InitialItemSpawns = I(v, "m_nInitialItemSpawns"), InitialMagnetSpawns = I(v, "m_nInitialMagnetSpawns"),
                EliteRoomUnlockTimes = Floats(v, "m_vecEliteRoomUnlockTimes"),
                EliteRoomChoices = v["m_vecEliteRoomChoices"]?.Select(x => (string)x).ToArray() ?? new string[0],
                EliteRoomTriggerChannelTime = F(v, "m_flEliteRoomTriggerChannelTime"),
                EliteRoomTriggerRadius = F(v, "m_flEliteRoomTriggerRadius"),
                LuckyLoot = LootCollections(v["m_luckyLootTable"]),
            };
            if (v["m_vecSeperationLayerData"] is JArray layers)
                foreach (JObject layer in layers)
                    mode.SeparationDistance[S(layer, "m_eSeparationLayer")] = F(layer, "m_flSeperationDistance");
            return mode;
        }

        private static LevelDef ReadLevel(string key, JObject v) => new LevelDef
        {
            Key = key, Raw = v, MapBounds = V2(v, "m_vMapBounds"),
            Events = Events(v, "m_vecEvents"), BossEvents = Events(v, "m_vecBossEvents"),
        };

        private static DifficultyDef ReadDifficulty(string key, JObject v) => new DifficultyDef
        {
            Key = key, Raw = v,
            EnemyHealthMultiplier = F(v, "m_flEnemyHealthMultiplier"),
            EnemyDamageMultiplier = F(v, "m_flEnemyDamageMultiplier"),
            EnemyMovementSpeedMultiplier = F(v, "m_flEnemyMovementSpeedMultiplier"),
            EnemyTurnRateMultiplier = F(v, "m_flEnemyTurnRateMultiplier"),
            EnemySpawnCountMultiplier = F(v, "m_flEnemySpawnCountMuliplier"), // sic: original spelling
            XPDropChanceMultiplier = F(v, "m_flXPDropChanceMultiplier"),
            AdditionalFirstFloorTime = F(v, "m_flAdditionalFirstFloorTime"),
            EnemyTouchMovementSlowDuration = F(v, "m_flEnemyTouchMovementSlowDuration"),
            EnableMeteorModifier = B(v, "m_bEnableMeteorModifier"),
            AdditionalEnemyEvents = Events(v, "m_vecAdditionalEnemyEvents"),
            OverrideImperiaEnrageHealthThresholds = Floats(v, "m_vecOverrideImperiaEnrageHealthThresholds"),
        };

        private static HeroDef ReadHero(string key, JObject v) => new HeroDef
        {
            Key = key, Raw = v, Id = I(v, "m_unHeroID"), DotaHeroId = I(v, "m_nDOTAHeroID"),
            BaseAttributes = Attributes(v["m_vecBaseAttributes"]),
            StartingPowerUps = Ints(v, "m_vecStartingPowerUps"), InnatePowerUps = Ints(v, "m_vecInnatePowerUps"),
            EconItems = Ints(v, "m_vecEconItems"),
        };

        private PowerUpDef ReadPowerUp(string key, JObject v) => new PowerUpDef
        {
            Key = key, Raw = v, Class = S(v, "_class"), Source = S(v, "m_sSource"), Targeting = S(v, "m_eTargeting"),
            Id = I(v, "m_unPowerUpID"), MaxLevel = I(v, "m_nMaxLevel", 1),
            IsPassive = B(v, "m_bIsPassive"), IsInnate = B(v, "m_bIsInnate"), IsGold = B(v, "m_bIsGold"),
            Rollable = B(v, "m_bRollable", true),
            BaseAttributes = Attributes(v["m_vecBaseAttributes"]),
            Authored = Choices(key, v, "m_vecAuthoredUpgradeChoices"), Minor = Choices(key, v, "m_vecMinorUpgradeChoices"),
            RecipeItems = Ints(v, "m_vecRecipeItems"),
            ScepterUpgradeable = B(v, "m_bIsScepterUpgradeable"), ShardUpgradeable = B(v, "m_bIsShardUpgradeable"),
            Scepter = v["m_scepterUpgradeDefinition"] is JObject scepter
                ? new UpgradeChoice
                {
                    Rarity = UpgradeRarity.Common,
                    Local = Attributes(scepter["m_vecUpgradeAttributes"]),
                    Global = Attributes(scepter["m_vecGlobalUpgradeAttributes"]),
                }
                : null,
        };

        private static PickupDef ReadPickup(string key, JObject v) => new PickupDef
        {
            Key = key, Raw = v, Id = I(v, "m_unPickupID"), HealAmount = I(v, "m_nHealAmount"),
            GoldAmount = I(v, "m_nGoldAmount"), TreasureVariant = I(v, "m_nTreasureVariant"),
            RewardsTreasure = B(v, "m_bRewardsTreasure"),
            CanSpawnWithVelocity = B(v, "m_bCanSpawnWithVelocity"), MaxSpawnVelocity = F(v, "m_flMaxSpawnVelocity"),
        };

        private static SpawnBehavior ParseSpawn(string name)
        {
            switch (name)
            {
                case "RANDOM_DIRECTION": return SpawnBehavior.RandomDirection;
                case "FIXED_DIRECTION": return SpawnBehavior.FixedDirection;
                case "FIXED_DIRECTION_PERPENDICULAR_WALL": return SpawnBehavior.FixedDirectionPerpendicularWall;
                case "OVAL_AROUND_PLAYER": return SpawnBehavior.OvalAroundPlayer;
                case "SPAWNER_ORIGIN_RADIUS": return SpawnBehavior.SpawnerOriginRadius;
                case "STATIC_IN_MAP": return SpawnBehavior.StaticInMap;
                case "STATIC_IN_MAP_IGNORE_PLAYER_RADIUS": return SpawnBehavior.StaticInMapIgnorePlayerRadius;
                default: return SpawnBehavior.Unknown;
            }
        }

        private static MovementBehavior ParseMovement(string name)
        {
            switch (name)
            {
                case "ENEMY_MOVEMENT_BEHAVIOR_TRACKING": return MovementBehavior.Tracking;
                case "ENEMY_MOVEMENT_BEHAVIOR_LINEAR": return MovementBehavior.Linear;
                case "ENEMY_MOVEMENT_BEHAVIOR_LINEAR_SIN_WAVE": return MovementBehavior.LinearSinWave;
                case "ENEMY_MOVEMENT_BEHAVIOR_STATIONARY": return MovementBehavior.Stationary;
                default: return MovementBehavior.Unknown;
            }
        }

        private static SpawnerDef ReadSpawner(string key, JObject v)
        {
            string behavior = S(v, "m_eSpawnBehavior");
            return new SpawnerDef
            {
                Key = key, Raw = v, Class = S(v, "_class"), EnemyName = S(v, "m_sEnemyName"),
                MinimumCount = I(v, "m_nMinimumEnemyCount"), SpawnInterval = F(v, "m_flSpawnInterval"),
                MaxSpawnCountPerInterval = NI(v, "m_nMaxSpawnCountPerInterval"),
                BehaviorName = behavior, Behavior = ParseSpawn(behavior),
                IgnoreDifficultySpawnMultiplier = B(v, "m_bIgnoreDifficultySpawnMultiplier"),
                Persistent = B(v, "m_bIsPersistant"), // sic
                OvalRadius = V2(v, "m_flSpawnOvalRadius"),
                FixedDirectionDistanceVariance = F(v, "m_flFixedDirectionSpawnDistanceVariance"),
                PerpendicularWallSpacing = F(v, "m_flPerpendicularWallSpacing"),
                SpawnInfoTargetName = S(v, "m_sSpawnInfoTargetName"),
                SpawnChance = F(v, "m_flSpawnChance", 1),
                MinimumDistanceBetween = F(v, "m_flMinimumDistanceBetween"),
                OverflowCount = I(v, "m_nOverflowEnemySpawnCount"),
                ResetSpawnIntervalOnKill = B(v, "m_bResetSpawnIntervalOnKill"),
                Destructibles = S(v, "_class") == "CSurvivorsSpawnerDestructiblesDefinition",
                DestroyDistance = F(v, "m_flDestroyDistance"),
                SpawnPositionsLayer = S(v, "m_eSpawnPositionsLayer"),
            };
        }

        private static EnemyDef ReadEnemy(string key, JObject v)
        {
            string movement = S(v, "m_eMovementBehavior");
            PickupChanceDef[] Chances(string field) => v[field] is JArray items
                ? items.Cast<JObject>().Select(c => new PickupChanceDef
                {
                    PickupName = S(c, "m_sPickupName"), Chance = F(c, "m_flChance"),
                    ExperienceReward = I(c, "m_nExperienceReward"),
                }).ToArray()
                : new PickupChanceDef[0];
            var chances = Chances("m_vecPickupChances");
            var fullLoot = LootCollections(v["m_fullLootTable"]);
            var attacks = v["m_vecAttacks"] is JArray attackItems
                ? attackItems.Cast<JObject>().Select(a => new EnemyAttackDef
                {
                    Damage = F(a, "m_flDamage"), Cooldown = F(a, "m_flAttackCooldown", 1), Speed = F(a, "m_flSpeed"),
                    Range = F(a, "m_flRange"), MaxDistance = F(a, "m_flMaxDistance"), AttackPoint = F(a, "m_flAttackPoint"),
                    Radius = F(a, "m_flRadius", 50), LifeTime = F(a, "m_flLifeTime"),
                    OffsetForward = F(a, "m_flAttackOffsetForward"),
                }).ToArray()
                : new EnemyAttackDef[0];
            return new EnemyDef
            {
                Key = key, Raw = v, Class = S(v, "_class"), Id = I(v, "m_unEnemyID"),
                MaxHealth = F(v, "m_flMaxHealth"), MaxHealthPerPlayerLevel = F(v, "m_flMaxHealthPerPlayerLevel"),
                MoveSpeed = F(v, "m_flMoveSpeed"), TouchDamage = F(v, "m_flTouchDamage"),
                HasCollisionRadius = v.ContainsKey("m_flCollisionRadius"), CollisionRadius = F(v, "m_flCollisionRadius"),
                ModelScale = F(v, "m_flModelScale", 1),
                MovementName = movement, Movement = ParseMovement(movement),
                Flying = S(v, "m_eMovementCapability") == "ENEMY_MOVEMENT_CAPABILITY_FLYING",
                SeparationLayer = S(v, "m_eSeparationLayer") ?? "SMALL",
                KnockbackResistance = F(v, "m_flKnockbackResistance"), StatusResistance = F(v, "m_flStatusResistance"),
                TurnRate = F(v, "m_flTurnRate"), HasTurnRate = v.ContainsKey("m_flTurnRate"),
                Mass = NF(v, "m_flMass"), DespawnBuffer = NF(v, "m_flOverrideDespawnRadiusBuffer"),
                MinSeparationDistance = F(v, "m_flMinSeparationDistanceFromEnemies"),
                SinMovementAngle = F(v, "m_flSinMovementAngle"), SinMovementPeriodMultiplier = F(v, "m_flSinMovementPeriodMultiplier", 1),
                IsElite = B(v, "m_bIsElite"), IsDestructible = B(v, "m_bIsDestructible"), Invulnerable = B(v, "m_bInvulnerable"),
                Undespawnable = B(v, "m_bUndespawnable"), PlayerFriendly = B(v, "m_bPlayerFriendly"), DieOnTouch = B(v, "m_bDieOnTouch"),
                HasSolidBody = B(v, "m_bHasSolidBody"),
                PickupChances = chances, LootTable = Chances("m_vecLootTable"), FullLoot = fullLoot, Attacks = attacks,
                ResurrectionTimes = I(v, "m_nNumResurrectionTimes"),
                MovementSpeedMultiplierPerDeath = F(v, "m_flMovementSpeedMultiplierPerDeath"),
                AbsorbRadius = F(v, "m_flAbsorbRadius"), PercentHealthAbsorbed = F(v, "m_flPercentHealthAbsorbed"),
                ModelScaleIncreasePerAbsorb = F(v, "m_flModelScaleIncreasePerAbsorb"), MaxModelScale = F(v, "m_flMaxModelScale"),
                SplitOnDeathNumUnits = I(v, "m_nSplitOnDeathNumUnits"), SplitOnDeathEnemyId = I(v, "m_unSplitOnDeathEnemyID"),
                SplitOnDeathKnockbackDistance = F(v, "m_flSplitOnDeathKnockbackDistance"),
                Imperia = (S(v, "_class") ?? "").EndsWith("ImperiaBoss", StringComparison.Ordinal) ? ReadImperia(key, v) : null,
            };
        }

        /// <summary>
        /// Imperia's fight fields (Mechanics §7). All are required: a missing one fails loading instead of
        /// becoming zero or a number from code.
        /// </summary>
        private static ImperiaDef ReadImperia(string key, JObject v)
        {
            var missing = new List<string>();
            JToken Need(string name)
            {
                if (v.TryGetValue(name, out var t) && t.Type != JTokenType.Null) return t;
                missing.Add(name);
                return null;
            }
            float RF(string name) => Need(name) is JToken t ? (float)t : 0;
            int RI(string name) => Need(name) is JToken t ? (int)t : 0;
            float[] RFloats(string name) => Need(name) is JArray t ? t.Select(x => (float)x).ToArray() : new float[0];
            string[] RStrings(string name) => Need(name) is JArray t ? t.Select(x => (string)x).ToArray() : new string[0];
            var d = new ImperiaDef
            {
                InitialEnrageTime = RF("m_flInitialEnrageTime"),
                IncrementalEnrageTime = RF("m_flIncrementalEnrageTime"),
                MandatoryEnrageHealthThresholds = RFloats("m_vecMandatoryEnrageHealthThresholds"),
                NumMagicMissiles = RI("m_nNumMagicMissiles"),
                MagicMissileProjectileSpeed = RF("m_flMagicMissileProjectileSpeed"),
                MagicMissileProjectileSpeedIncreasePerEnrage = RF("m_flMagicMissileProjectileSpeedIncreasePerEnrage"),
                MagicMissileDamage = RF("m_flMagicMissileDamage"),
                MagicMissileProjectileRadius = RF("m_flMagicMissileProjectileRadius"),
                BurningGroundImpactDamage = RF("m_flBurningGroundImpactDamage"),
                BurningGroundImpactRadius = RF("m_flBurningGroundImpactRadius"),
                BurningGroundImpactSpawnDelay = RF("m_flBurningGroundImpactSpawnDelay"),
                BurningGroundFlamesDuration = RF("m_flBurningGroundFlamesDuration"),
                BurningGroundDoTDuration = RF("m_flBurningGroundDoTDuration"),
                BurningGroundDoTDamage = RF("m_flBurningGroundDoTDamage"),
                BurningGroundImpactStunDuration = RF("m_flBurningGroundImpactStunDuration"),
                BurningGroundInstancesPerEnrageLevel = RI("m_nBurningGroundInstancesPerEnrageLevel"),
                RadiateRaysCastStartDuration = RF("m_flRadiateRaysCastStartDuration"),
                RadiateRaysDuration = RF("m_flRadiateRaysDuration"),
                RadiateRaysInterval = RF("m_flRadiateRaysInterval"),
                RadiateRaysAngle = RF("m_flRadiateRaysAngle"),
                RadiateRaysSpeed = RF("m_flRadiateRaysSpeed"),
                RadiateRaysDamage = RF("m_flRadiateRaysDamage"),
                RadiateRaysProjectileRadius = RF("m_flRadiateRaysProjectileRadius"),
                DemonPortalsNumToCreate = RI("m_nDemonPortalsNumToCreate"),
                MaxDemonPortalCount = RI("m_nMaxDemonPortalCount"),
                DemonPortalDeactivateTime = RF("m_flDemonPortalDeactivateTime"),
                DemonPortalDeactivateRadius = RF("m_flDemonPortalDeactivateRadius"),
                DemonPortalSpawners = RStrings("m_vecDemonPortalSpawners"),
            };
            if (missing.Count > 0)
                throw new System.IO.InvalidDataException($"enemies/{key}: missing Imperia fields {string.Join(", ", missing)}");
            return d;
        }
    }
}
