using System;
using System.Collections.Generic;

namespace ThornThrone.Simulation
{
    /// <summary>One owned powerup and its rank.</summary>
    public sealed class OwnedPowerUp
    {
        public PowerUpDef Def;
        public int Rank;
        /// <summary>Attributes of the powerup itself: base plus applied authored upgrades.</summary>
        public readonly AttributeSet Local = new AttributeSet();
        /// <summary>How many times each m_vecMinorUpgradeChoices variant was taken (client m_vecMinorUpgrades, К; Mechanics §33 Р3).</summary>
        public int[] MinorCounts;
        /// <summary>Aghanim's Scepter and Shard taken (client m_bScepterUpgraded / m_bShardUpgraded, К; Mechanics §34 Р6).</summary>
        public bool Scepter, Shard;
        /// <summary>Kills of <see cref="Stats"/> when the scepter was taken: Magic Missile counts its kills from then (Р10).</summary>
        public int KillsAtScepter;
        public IAbility Ability;
        /// <summary>Damage statistics of this ability; kept in <see cref="Run.DamageStats"/> after a recipe consumes it.</summary>
        public DamageStatEntry Stats;
    }

    /// <summary>
    /// The hero's build: owned powerups, slots, level-up choices, recipes and the resulting attributes.
    /// One rule for every hero and powerup:
    ///   hero attribute  = hero base + innate without its own class (base + first minor global)
    ///                     + passive first-minor global × rank (authored globals 0..rank−1 if the item has them)
    ///                     + authored global upgrades of owned abilities;
    ///   ability stat    = ability local + hero attribute of the same type.
    /// An innate with its own class is the hero's ability: its attributes stay in its Local only.
    /// </summary>
    public sealed class Build
    {
        private readonly GameData data;
        private readonly Rules rules;
        private readonly ModeDef mode;
        private readonly HeroDef hero;
        private readonly Func<PowerUpDef, bool> implemented;
        private ChoiceSource source;
        private readonly HashSet<int> consumed = new HashSet<int>();
        // Pre-sized above the 47 powerups: list growth inside a step is an allocation.
        private readonly List<PowerUpDef> candidates = new List<PowerUpDef>(64);
        private readonly List<float> weights = new List<float>(64);
        private readonly List<OfferKind> kinds = new List<OfferKind>(64);

        public readonly List<OwnedPowerUp> Owned = new List<OwnedPowerUp>();
        public readonly AttributeSet Global = new AttributeSet();
        public readonly List<PowerUpDef> Choices = new List<PowerUpDef>(8);
        /// <summary>Cards of <see cref="Choices"/> in the same order; the first <see cref="OfferCount"/> are valid.</summary>
        public readonly ChoiceOffer[] Offers = new ChoiceOffer[8];
        public int OfferCount => Choices.Count;
        public int RerollsLeft { get; private set; }
        /// <summary>True when the last generated choice list was empty and the fallback reward applies.</summary>
        public bool ChoicesAreFallback { get; private set; }
        /// <summary>Source of the last generated choice list.</summary>
        public ChoiceSource Source => source;
        public int Version { get; private set; }
        /// <summary>m_nMaxActiveSlots plus ActiveAbilitySlots (Apex), Mechanics §25 Р6.</summary>
        public int MaxActiveSlots => mode.MaxActiveSlots + (int)Global[Attr.ActiveAbilitySlots];

        public Build(GameData data, Rules rules, ModeDef mode, HeroDef hero, Func<PowerUpDef, bool> implemented)
        {
            this.data = data;
            this.rules = rules;
            this.mode = mode;
            this.hero = hero;
            this.implemented = implemented;
            for (int i = 0; i < Offers.Length; i++) Offers[i] = new ChoiceOffer();
            foreach (int id in hero.InnatePowerUps) Acquire(data.PowerUpsById[id]);
            foreach (int id in hero.StartingPowerUps) Acquire(data.PowerUpsById[id]);
            Recalculate();
            RerollsLeft = (int)Global[Attr.MaxRerolls];
        }

        public OwnedPowerUp Find(PowerUpDef def)
        {
            for (int i = 0; i < Owned.Count; i++) if (Owned[i].Def == def) return Owned[i];
            return null;
        }

        public int Rank(string key) => data.PowerUps.TryGetValue(key, out var def) ? Find(def)?.Rank ?? 0 : 0;

        private int CountSlots(bool passive)
        {
            int count = 0;
            for (int i = 0; i < Owned.Count; i++)
            {
                var def = Owned[i].Def;
                if (def.IsInnate || def.IsGold) continue;
                if (passive ? def.IsPassive : def.IsActive) count++;
            }
            return count;
        }

        public bool CanUpgrade(PowerUpDef def)
        {
            if (def.IsInnate || def.IsGold || !def.Rollable || def.IsRecipe || consumed.Contains(def.Id)) return false;
            if (!(def.IsActive || def.IsPassive) || !implemented(def)) return false;
            var owned = Find(def);
            if (owned != null) return owned.Rank < def.MaxLevel;
            return def.IsPassive ? CountSlots(true) < mode.MaxPassiveSlots : CountSlots(false) < MaxActiveSlots;
        }

        /// <summary>
        /// The powerup can be on a card: a new powerup or the next rank (<see cref="CanUpgrade"/>), or a minor upgrade
        /// of an active ability at m_nMaxLevel. A passive item at maximum is not offered (owner's decision В1, Mechanics §33 Р1, Р4).
        /// </summary>
        public bool CanOffer(PowerUpDef def, out OfferKind kind)
        {
            kind = Find(def) == null ? OfferKind.New : OfferKind.Rank;
            if (CanUpgrade(def)) return true;
            kind = OfferKind.Minor;
            return CanUpgradeMinor(def);
        }

        private bool CanUpgradeMinor(PowerUpDef def)
        {
            if (!def.IsActive || !def.Rollable || def.IsRecipe || def.Minor.Length == 0 || !implemented(def)) return false;
            var owned = Find(def);
            return owned != null && owned.Rank >= def.MaxLevel;
        }

        /// <summary>
        /// Weight of the powerup in a choice list, 0 when it is not offered: new m_flNewItemWeight, owned (a rank or a
        /// minor upgrade) m_flExistingItemWeight, passive × m_flPassiveItemWeight. Level-ups offer active skills only,
        /// treasure passive items only (Mechanics §12.3, §25, §33 Р1). Rarity does not change it (Р6).
        /// </summary>
        public float OfferWeight(PowerUpDef def, bool items) => OfferWeight(def, items, out _);

        private float OfferWeight(PowerUpDef def, bool items, out OfferKind kind)
        {
            if (!CanOffer(def, out kind) || (items ? !def.IsPassive : !def.IsActive)) return 0;
            float weight = kind == OfferKind.New ? mode.NewItemWeight : mode.ExistingItemWeight;
            if (def.IsPassive) weight *= mode.PassiveItemWeight;
            return weight > 0 ? weight : 0;
        }

        /// <summary>
        /// Up to m_nLevelUpChoices different options by weight (<see cref="OfferWeight(PowerUpDef, bool)"/>); fewer when
        /// fewer are left. A minor upgrade rolls its variant right after its card is drawn (Mechanics §33 Р2).
        /// </summary>
        public void GenerateChoices(Rng rng, bool items = false) => GenerateChoices(rng, items ? ChoiceSource.Item : ChoiceSource.Level);

        /// <summary>
        /// Choice list of <paramref name="from"/>. Level and Item: by weight (<see cref="OfferWeight(PowerUpDef, bool)"/>).
        /// Scepter and Shard: owned abilities that can take it (<see cref="CanScepter"/>, <see cref="CanShard"/>) with equal
        /// weights (Mechanics §34 Р4).
        /// </summary>
        public void GenerateChoices(Rng rng, ChoiceSource from)
        {
            source = from;
            Choices.Clear();
            candidates.Clear();
            weights.Clear();
            kinds.Clear();
            if (from == ChoiceSource.Scepter || from == ChoiceSource.Shard)
            {
                var kind = from == ChoiceSource.Scepter ? OfferKind.Scepter : OfferKind.Shard;
                for (int i = 0; i < Owned.Count; i++)
                {
                    var def = Owned[i].Def;
                    if (kind == OfferKind.Scepter ? !CanScepter(def) : !CanShard(def)) continue;
                    candidates.Add(def);
                    weights.Add(1);
                    kinds.Add(kind);
                }
            }
            else
            {
                bool items = from == ChoiceSource.Item;
                foreach (var def in data.PowerUps.Values)
                {
                    float weight = OfferWeight(def, items, out var kind);
                    if (weight <= 0) continue;
                    candidates.Add(def);
                    weights.Add(weight);
                    kinds.Add(kind);
                }
            }
            while (Choices.Count < mode.LevelUpChoices && Choices.Count < Offers.Length && candidates.Count > 0)
            {
                float total = 0;
                for (int i = 0; i < weights.Count; i++) total += weights[i];
                float roll = rng.NextFloat() * total;
                int pick = 0;
                while (pick < weights.Count - 1 && roll >= weights[pick]) { roll -= weights[pick]; pick++; }
                var def = candidates[pick];
                var kind = kinds[pick];
                int variant = kind == OfferKind.Minor ? rng.Next(def.Minor.Length) : -1;
                Describe(Offers[Choices.Count], def, kind, variant);
                Choices.Add(def);
                candidates.RemoveAt(pick);
                weights.RemoveAt(pick);
                kinds.RemoveAt(pick);
            }
            ChoicesAreFallback = Choices.Count == 0;
        }

        /// <summary>Fills a card: kind, ranks, rarity and the summed attributes before and after (Mechanics §33 Р7, Р10–Р11).</summary>
        private void Describe(ChoiceOffer offer, PowerUpDef def, OfferKind kind, int variant)
        {
            var owned = Find(def);
            int rank = owned?.Rank ?? 0;
            var rarity = kind == OfferKind.Minor ? def.Minor[variant].Rarity : UpgradeRarity.Common;
            bool aghanim = kind == OfferKind.Scepter || kind == OfferKind.Shard;
            offer.Reset(def, kind, rank, kind == OfferKind.Minor || aghanim ? rank : rank + 1, variant, rarity);
            offer.HasScepter = owned != null && owned.Scepter;
            offer.HasShard = owned != null && owned.Shard;
            if (aghanim)
            {
                // The scepter's own record (m_scepterUpgradeDefinition); the other effects have only a text (Р5).
                if (kind == OfferKind.Scepter && def.Scepter != null)
                {
                    AddChanges(offer, owned, def.Scepter.Local, -1);
                    AddChanges(offer, null, def.Scepter.Global, -1);
                }
                var extra = AghanimEffects.ScepterGlobal(def);
                if (kind == OfferKind.Scepter && extra.Length > 0) AddChanges(offer, null, extra, -1);
            }
            else if (kind == OfferKind.Minor)
            {
                AddChanges(offer, owned, def.Minor[variant].Local, MinorDamageAttribute(def));
                AddChanges(offer, null, def.Minor[variant].Global, -1);
            }
            else if (def.IsPassive)
            {
                // The next rank of §25 Р1–Р2: authored record «rank» when the item has them, else the first minor.
                if (def.Authored.Length > 0) { if (rank < def.Authored.Length) AddChanges(offer, null, def.Authored[rank].Global, -1); }
                else if (def.Minor.Length > 0) AddChanges(offer, null, def.Minor[0].Global, -1);
            }
            else if (owned != null)
            {
                int index = rank - rules.AuthoredUpgradeOffset;
                if (index >= 0 && index < def.Authored.Length)
                {
                    AddChanges(offer, owned, def.Authored[index].Local, -1);
                    AddChanges(offer, null, def.Authored[index].Global, -1);
                }
            }
        }

        /// <summary>Own attributes when <paramref name="owned"/> is set, else hero attributes.</summary>
        private void AddChanges(ChoiceOffer offer, OwnedPowerUp owned, AttributeValue[] values, int damage)
        {
            for (int i = 0; i < values.Length; i++)
            {
                int id = Remap(values[i].Id, damage);
                float current = owned != null ? Stat(owned, id) : Global[id];
                if (!offer.Add(id, owned == null, current, values[i].Value))
                    data.Diagnostics.Report("build:offer-changes", "A choice card changes more attributes than it can show");
            }
        }

        /// <summary>
        /// Damage attribute of the ability's base (Damage_Physical or Damage_Magical, one of them), else −1. A minor
        /// upgrade's damage of the other type goes to this one (owner's decision В2, variant А; Mechanics §33 Р5, Differences).
        /// </summary>
        private static int MinorDamageAttribute(PowerUpDef def)
        {
            bool physical = false, magical = false;
            foreach (var a in def.BaseAttributes)
            {
                if (a.Id == Attr.Damage_Physical) physical = true;
                else if (a.Id == Attr.Damage_Magical) magical = true;
            }
            if (physical == magical) return -1;
            return physical ? Attr.Damage_Physical : Attr.Damage_Magical;
        }

        private static int Remap(int attribute, int damage) =>
            damage >= 0 && (attribute == Attr.Damage_Physical || attribute == Attr.Damage_Magical) ? damage : attribute;

        public bool Reroll(Rng rng)
        {
            if (RerollsLeft <= 0 || ChoicesAreFallback) return false;
            RerollsLeft--;
            GenerateChoices(rng, source);
            return true;
        }

        /// <summary>Takes a choice from the current list: its rank or its rolled minor variant.</summary>
        public bool Choose(PowerUpDef def)
        {
            int at = Choices.IndexOf(def);
            if (at < 0) return false;
            var offer = Offers[at];
            switch (offer.Kind)
            {
                case OfferKind.Minor: return UpgradeMinor(def, offer.MinorIndex);
                case OfferKind.Scepter: return UpgradeScepter(def);
                case OfferKind.Shard: return UpgradeShard(def);
                default: return Upgrade(def);
            }
        }

        /// <summary>
        /// An owned active ability (not innate) with m_bIsScepterUpgradeable, an implemented effect
        /// (<see cref="AghanimEffects.Has"/>) and no scepter yet (Mechanics §34 Р4, Р6).
        /// </summary>
        public bool CanScepter(PowerUpDef def)
        {
            var owned = Find(def);
            return owned != null && !owned.Scepter && AghanimEffects.Has(def, false);
        }

        /// <summary>The same rule as <see cref="CanScepter"/> with m_bIsShardUpgradeable and the shard flag.</summary>
        public bool CanShard(PowerUpDef def)
        {
            var owned = Find(def);
            return owned != null && !owned.Shard && AghanimEffects.Has(def, true);
        }

        /// <summary>Sets the scepter flag once; false and nothing changed on a repeat (Т4). Also for tests and debug tools.</summary>
        public bool UpgradeScepter(PowerUpDef def)
        {
            if (!CanScepter(def)) return false;
            var owned = Find(def);
            owned.Scepter = true;
            owned.KillsAtScepter = owned.Stats?.Kills ?? 0;
            Recalculate();
            Choices.Clear();
            return true;
        }

        /// <summary>Sets the shard flag once; false and nothing changed on a repeat (Т4). Also for tests and debug tools.</summary>
        public bool UpgradeShard(PowerUpDef def)
        {
            if (!CanShard(def)) return false;
            Find(def).Shard = true;
            Recalculate();
            Choices.Clear();
            return true;
        }

        /// <summary>
        /// Adds minor variant <paramref name="index"/> to an active ability at m_nMaxLevel; the rank stays, repeats add up
        /// without a limit (Mechanics §33 Р3). Also for tests and debug tools.
        /// </summary>
        public bool UpgradeMinor(PowerUpDef def, int index)
        {
            if (!CanUpgradeMinor(def) || index < 0 || index >= def.Minor.Length) return false;
            Find(def).MinorCounts[index]++;
            Recalculate();
            Choices.Clear();
            return true;
        }

        /// <summary>Adds or ranks up a powerup without a choice list (tests, debug tools).</summary>
        public bool Upgrade(PowerUpDef def)
        {
            if (!CanUpgrade(def)) return false;
            var owned = Find(def);
            if (owned == null) Acquire(def);
            else owned.Rank++;
            CombineRecipes();
            Recalculate();
            Choices.Clear();
            return true;
        }

        private void Acquire(PowerUpDef def)
        {
            Owned.Add(new OwnedPowerUp { Def = def, Rank = 1, MinorCounts = new int[def.Minor.Length] });
        }

        /// <summary>
        /// A recipe forms when all of its components are at maximum rank; the components are consumed
        /// and their slots freed (prototype rule, Mechanics §12.4).
        /// </summary>
        private void CombineRecipes()
        {
            foreach (var recipe in data.PowerUps.Values)
            {
                if (!recipe.IsRecipe || Find(recipe) != null) continue;
                bool ready = true;
                foreach (int id in recipe.RecipeItems)
                {
                    var component = data.PowerUpsById.TryGetValue(id, out var c) ? Find(c) : null;
                    if (component == null || component.Rank < component.Def.MaxLevel) { ready = false; break; }
                }
                if (!ready) continue;
                foreach (int id in recipe.RecipeItems)
                {
                    Owned.Remove(Find(data.PowerUpsById[id]));
                    consumed.Add(id);
                }
                Acquire(recipe);
            }
        }

        private void Recalculate()
        {
            Global.Clear();
            Global.Add(hero.BaseAttributes);
            for (int i = 0; i < Owned.Count; i++)
            {
                var owned = Owned[i];
                var def = owned.Def;
                owned.Local.Clear();
                owned.Local.Add(def.BaseAttributes);
                if (def.IsInnate)
                {
                    // An innate with its own class is the hero's ability: its attributes stay local (Mechanics §3, Р).
                    if (!def.IsInnateAbility)
                    {
                        Global.Add(def.BaseAttributes);
                        if (def.Minor.Length > 0) Global.Add(def.Minor[0].Global);
                    }
                    else
                    {
                        // Its MovementSpeed is the hero's bonus speed: «Kez has bonus movement speed» (Mechanics §26 Р14).
                        Global.Add(Attr.MovementSpeed, owned.Local[Attr.MovementSpeed]);
                    }
                }
                else if (def.IsPassive)
                {
                    // Authored records 0..rank−1 when the item has them (Crystalys), else first minor × rank (Mechanics §25 Р1, Р2).
                    if (def.Authored.Length > 0)
                        for (int u = 0; u < owned.Rank && u < def.Authored.Length; u++) Global.Add(def.Authored[u].Global);
                    else if (def.Minor.Length > 0) Global.Add(def.Minor[0].Global, owned.Rank);
                }
                else
                {
                    int applied = owned.Rank - rules.AuthoredUpgradeOffset;
                    for (int u = 0; u < applied && u < def.Authored.Length; u++)
                    {
                        owned.Local.Add(def.Authored[u].Local);
                        Global.Add(def.Authored[u].Global);
                    }
                    // Minor upgrades after the maximum (Mechanics §33 Р3, Р5).
                    int damage = MinorDamageAttribute(def);
                    for (int m = 0; m < owned.MinorCounts.Length; m++)
                    {
                        int times = owned.MinorCounts[m];
                        if (times == 0) continue;
                        var local = def.Minor[m].Local;
                        for (int k = 0; k < local.Length; k++) owned.Local.Add(Remap(local[k].Id, damage), local[k].Value * times);
                        Global.Add(def.Minor[m].Global, times);
                    }
                    // Aghanim's Scepter: its record and extra attributes once while the flag is set (Mechanics §34 Р6).
                    if (owned.Scepter)
                    {
                        if (def.Scepter != null)
                        {
                            owned.Local.Add(def.Scepter.Local);
                            Global.Add(def.Scepter.Global);
                        }
                        Global.Add(AghanimEffects.ScepterGlobal(def));
                    }
                }
            }
            Version++;
        }

        /// <summary>Ability stat: own value plus the hero attribute of the same type.</summary>
        public float Stat(OwnedPowerUp owned, int attribute) => owned.Local[attribute] + Global[attribute];
    }
}
