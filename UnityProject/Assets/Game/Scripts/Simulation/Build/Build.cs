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
        private bool itemChoices;
        private readonly HashSet<int> consumed = new HashSet<int>();
        // Pre-sized above the 47 powerups: list growth inside a step is an allocation.
        private readonly List<PowerUpDef> candidates = new List<PowerUpDef>(64);
        private readonly List<float> weights = new List<float>(64);

        public readonly List<OwnedPowerUp> Owned = new List<OwnedPowerUp>();
        public readonly AttributeSet Global = new AttributeSet();
        public readonly List<PowerUpDef> Choices = new List<PowerUpDef>(8);
        public int RerollsLeft { get; private set; }
        /// <summary>True when the last generated choice list was empty and the fallback reward applies.</summary>
        public bool ChoicesAreFallback { get; private set; }
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
        /// Up to m_nLevelUpChoices different options, weighted: new 0.7, already owned 0.35,
        /// Level-ups offer active skills only; treasure offers passive items only.
        /// </summary>
        public void GenerateChoices(Rng rng, bool items = false)
        {
            itemChoices = items;
            Choices.Clear();
            candidates.Clear();
            weights.Clear();
            foreach (var def in data.PowerUps.Values)
            {
                if (!CanUpgrade(def) || (items ? !def.IsPassive : !def.IsActive)) continue;
                float weight = Find(def) != null ? mode.ExistingItemWeight : mode.NewItemWeight;
                if (def.IsPassive) weight *= mode.PassiveItemWeight;
                if (weight <= 0) continue;
                candidates.Add(def);
                weights.Add(weight);
            }
            while (Choices.Count < mode.LevelUpChoices && candidates.Count > 0)
            {
                float total = 0;
                for (int i = 0; i < weights.Count; i++) total += weights[i];
                float roll = rng.NextFloat() * total;
                int pick = 0;
                while (pick < weights.Count - 1 && roll >= weights[pick]) { roll -= weights[pick]; pick++; }
                Choices.Add(candidates[pick]);
                candidates.RemoveAt(pick);
                weights.RemoveAt(pick);
            }
            ChoicesAreFallback = Choices.Count == 0;
        }

        public bool Reroll(Rng rng)
        {
            if (RerollsLeft <= 0 || ChoicesAreFallback) return false;
            RerollsLeft--;
            GenerateChoices(rng, itemChoices);
            return true;
        }

        /// <summary>Takes a choice from the current list.</summary>
        public bool Choose(PowerUpDef def) => Choices.Contains(def) && Upgrade(def);

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
            Owned.Add(new OwnedPowerUp { Def = def, Rank = 1 });
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
                }
            }
            Version++;
        }

        /// <summary>Ability stat: own value plus the hero attribute of the same type.</summary>
        public float Stat(OwnedPowerUp owned, int attribute) => owned.Local[attribute] + Global[attribute];
    }
}
