namespace ThornThrone.Simulation
{
    /// <summary>What a choice card does (Mechanics §33).</summary>
    public enum OfferKind
    {
        /// <summary>A powerup the hero does not own yet.</summary>
        New,
        /// <summary>The next rank of an owned powerup.</summary>
        Rank,
        /// <summary>One m_vecMinorUpgradeChoices variant of an active ability at m_nMaxLevel.</summary>
        Minor,
    }

    /// <summary>
    /// One attribute a card changes: the summed attribute now and after the choice, not the value after formulas
    /// (Mechanics §33 Р11). Global: a hero attribute (Build.Global); otherwise the ability's stat (Build.Stat).
    /// </summary>
    public struct AttributeChange
    {
        public int Attribute;
        public float Current, Next;
        public bool Global;
    }

    /// <summary>
    /// A card of the current choice. <see cref="Build"/> owns a fixed set of offers and refills them on every
    /// generation without allocating (Mechanics §33 Р10).
    /// </summary>
    public sealed class ChoiceOffer
    {
        /// <summary>Changes beyond this are not shown; the largest record of the data has 5 attributes.</summary>
        public const int MaxChanges = 8;

        public PowerUpDef Def;
        public OfferKind Kind;
        /// <summary>Rank now (0 for a new powerup) and after the choice; equal for a minor upgrade.</summary>
        public int Rank, NextRank;
        /// <summary>Index into Def.Minor for <see cref="OfferKind.Minor"/>, else −1.</summary>
        public int MinorIndex;
        public UpgradeRarity Rarity;
        public readonly AttributeChange[] Changes = new AttributeChange[MaxChanges];
        public int ChangeCount;

        internal void Reset(PowerUpDef def, OfferKind kind, int rank, int nextRank, int minorIndex, UpgradeRarity rarity)
        {
            Def = def;
            Kind = kind;
            Rank = rank;
            NextRank = nextRank;
            MinorIndex = minorIndex;
            Rarity = rarity;
            ChangeCount = 0;
        }

        /// <summary>Adds <paramref name="value"/> to the attribute's change; repeats in one record add up. False on overflow.</summary>
        internal bool Add(int attribute, bool global, float current, float value)
        {
            for (int i = 0; i < ChangeCount; i++)
            {
                if (Changes[i].Attribute != attribute || Changes[i].Global != global) continue;
                Changes[i].Next += value;
                return true;
            }
            if (ChangeCount >= MaxChanges) return false;
            Changes[ChangeCount++] = new AttributeChange { Attribute = attribute, Global = global, Current = current, Next = current + value };
            return true;
        }
    }
}
