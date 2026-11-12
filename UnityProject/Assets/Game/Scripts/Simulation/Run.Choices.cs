namespace ThornThrone.Simulation
{
    /// <summary>
    /// Fallback reward of an empty choice list (Mechanics §33 Р8–Р9, Differences A19): the powerup «gold» gives
    /// m_nGoldAmount of the pickup «gold_bag» (owner's decision В3); no heal. The amount is a design decision (Р).
    /// Also the recipe card's event and debug combine (Mechanics §35).
    /// </summary>
    public sealed partial class Run
    {
        private int fallbackGold = -1;

        /// <summary>Gold of the fallback reward; 0 and the diagnostic «data:fallback-reward» when the data lacks it.</summary>
        public int FallbackGold
        {
            get
            {
                if (fallbackGold >= 0) return fallbackGold;
                bool gold = Data.PowerUps.TryGetValue(Rules.FallbackPowerUp, out var def) && def.IsGold;
                if (gold && Data.Pickups.TryGetValue(Rules.FallbackGoldPickup, out var pickup) && pickup.GoldAmount > 0)
                    return fallbackGold = pickup.GoldAmount;
                Diagnostics.Report("data:fallback-reward",
                    $"Fallback reward needs powerup {Rules.FallbackPowerUp} with m_bIsGold and pickup {Rules.FallbackGoldPickup} with m_nGoldAmount; none given");
                return fallbackGold = 0;
            }
        }

        private void GrantFallbackReward() => Gold += FallbackGold;

        // ------------------------------------------------------------------ recipes (Mechanics §35)

        /// <summary>The recipe of card <paramref name="index"/> when it is a «Комбинация предметов», else null.</summary>
        private PowerUpDef RecipeCard(int index) =>
            index >= 0 && index < Build.OfferCount && Build.Offers[index].Kind == OfferKind.Recipe ? Build.Choices[index] : null;

        /// <summary>Event <see cref="SimEventKind.RecipeCombined"/> for presentation (Р6): Value the recipe's powerup ID.</summary>
        private void EmitCombined(PowerUpDef recipe)
        {
            if (recipe != null) Emit(SimEventKind.RecipeCombined, -1, HeroPosition, recipe.Id);
        }

        /// <summary>Combines a ready recipe without a choice (tests, smoke -thornRecipe); false when it is not ready (Р1).</summary>
        public bool ForceCombine(string key)
        {
            if (!Data.PowerUps.TryGetValue(key, out var def) || !Build.Combine(def)) return false;
            OnBuildChanged();
            EmitCombined(def);
            return true;
        }
    }
}
