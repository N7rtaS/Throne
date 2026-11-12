using ThornThrone.Simulation;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// Smoke option -thornRecipe=KEY (.kiro/specs/recipes-item-ability-matrix §2.4, Mechanics §35): after the run starts
    /// the components of recipe KEY are brought to m_nMaxLevel through <see cref="Run.ForceUpgrade"/> (a component that
    /// is itself a recipe, as in the tridents, is combined through <see cref="Run.ForceCombine"/>) and a treasure is put
    /// at the hero. The automatic choice takes the recipe card; without it, it rerolls, then takes the first card and
    /// puts another treasure, at most <see cref="MaxTreasures"/>. The THORN_RECIPE line reports whether the card was
    /// offered and the recipe combined by the usual choice; Tools/mac-smoke.sh exits with code 17 when it was not.
    /// </summary>
    public sealed class SmokeRecipe
    {
        public const string Prefix = "-thornRecipe=";
        public const string Treasure = "treasure";
        /// <summary>Treasures the smoke puts at most; a ready recipe needs no slot, so it stays a candidate.</summary>
        public const int MaxTreasures = 12;

        private readonly string key;
        private PowerUpDef recipe;
        private int treasures;

        public bool Offered { get; private set; }
        public bool Combined { get; private set; }

        public SmokeRecipe(string key) { this.key = key; }

        /// <summary>Readies the components and puts the first treasure; false for an unknown key, not a recipe or a refused upgrade.</summary>
        public bool Prepare(Run run)
        {
            if (!run.Data.PowerUps.TryGetValue(key, out var def) || !def.IsRecipe) return false;
            recipe = def;
            if (!ReadyComponents(run, def)) return false;
            return PutTreasure(run);
        }

        /// <summary>Every component of <paramref name="recipe"/> at m_nMaxLevel, a nested recipe combined.</summary>
        public static bool ReadyComponents(Run run, PowerUpDef recipe)
        {
            foreach (int id in recipe.RecipeItems)
            {
                if (!run.Data.PowerUpsById.TryGetValue(id, out var component)) return false;
                if (component.IsRecipe)
                {
                    if (run.Build.Rank(component.Key) > 0) continue;
                    if (!ReadyComponents(run, component) || !run.ForceCombine(component.Key)) return false;
                    continue;
                }
                while (run.Build.Rank(component.Key) < component.MaxLevel)
                    if (!run.ForceUpgrade(component.Key)) return false;
            }
            return true;
        }

        private bool PutTreasure(Run run)
        {
            treasures++;
            return run.DebugSpawnPickup(Treasure, run.HeroPosition);
        }

        /// <summary>Index of the recipe card in the open choice, −1 when it is not on the list.</summary>
        public int Pick(Run run)
        {
            if (recipe == null || run.ChoicesAreFallback) return -1;
            for (int i = 0; i < run.Build.OfferCount; i++)
            {
                if (run.Build.Offers[i].Def != recipe || run.Build.Offers[i].Kind != OfferKind.Recipe) continue;
                Offered = true;
                return i;
            }
            return -1;
        }

        /// <summary>The card the smoke takes (see the class summary).</summary>
        public int Choose(Run run)
        {
            if (recipe == null || Combined || run.ChoiceSource != ChoiceSource.Item) return 0;
            int at = Pick(run);
            while (at < 0 && run.Reroll()) at = Pick(run);
            if (at >= 0) return at;
            if (treasures < MaxTreasures) PutTreasure(run);
            return 0;
        }

        /// <summary>Looks for <see cref="SimEventKind.RecipeCombined"/> of the recipe among the run's current events.</summary>
        public void Count(Run run)
        {
            if (recipe == null) return;
            for (int i = 0; i < run.EventCount; i++)
                if (run.Events[i].Kind == SimEventKind.RecipeCombined && (int)run.Events[i].Value == recipe.Id) Combined = true;
        }

        /// <summary>«THORN_RECIPE key=sny_trident offered=1 combined=1».</summary>
        public string Report() => $"THORN_RECIPE key={key} offered={(Offered ? 1 : 0)} combined={(Combined ? 1 : 0)}";
    }
}
