using NUnit.Framework;
using ThornThrone.Mobile;
using ThornThrone.Simulation;

namespace ThornThrone.Mobile.Tests
{
    /// <summary>
    /// Recipes in the Unity layer (.kiro/specs/recipes-item-ability-matrix, Т2.3, Т3, Mechanics §35 Р5–Р6): the card text
    /// «Комбинация предметов» / «Сочетает:», the 3 s notice «Собран предмет: …» on game time and the -thornRecipe smoke.
    /// </summary>
    public sealed class RecipeNoticeTests
    {
        private static GameData Data() => ChoiceTextTests.LoadData();

        private static Run Ready(string key)
        {
            var run = new Run(Data(), new Rules(), "dragonus", null, 7) { DebugNoSpawns = true, DebugInvulnerable = true };
            Assert.That(SmokeRecipe.ReadyComponents(run, run.Data.PowerUps[key]), Is.True, key);
            return run;
        }

        private static ChoiceOffer RecipeOffer(Run run, string key)
        {
            var def = run.Data.PowerUps[key];
            for (int round = 0; round < 200; round++)
            {
                run.Build.GenerateChoices(run.Rng, true);
                for (int i = 0; i < run.Build.OfferCount; i++)
                    if (run.Build.Offers[i].Def == def) return run.Build.Offers[i];
            }
            Assert.Fail(key + " is never offered");
            return null;
        }

        [Test]
        public void RecipeCardHasItsTypeAndComponents()
        {
            var run = Ready("yasha_and_kaya");
            var offer = RecipeOffer(run, "yasha_and_kaya");
            Assert.That(offer.Kind, Is.EqualTo(OfferKind.Recipe));
            Assert.That(ChoiceText.Header(offer), Is.EqualTo("Комбинация предметов"));
            string text = ChoiceText.Format(offer, "Яша и Кая", "", run.Data.AttributeDefinitions, ChoiceText.Combines(new[] { "Яша", "Кая" }));
            Assert.That(text, Does.StartWith("<b>Яша и Кая</b>\nКомбинация предметов\nОбычное\n\nСочетает: Яша, Кая\n"));
            Assert.That(text, Does.Contain("Скорость передвижения: 355 -> <b>380</b>"), "the hero's totals (Mechanics §35 Р5)");
        }

        [Test]
        public void CombinesLineFollowsTheClientString()
        {
            Assert.That(ChoiceText.CombinedItem, Is.EqualTo("Комбинация предметов"), "DOTA_Survivors_CombinedItem");
            Assert.That(ChoiceText.Combines(new[] { "Сэндж и Яша", "Кая" }), Is.EqualTo("Сочетает: Сэндж и Яша, Кая"), "DOTA_Survivors_CombinedItemTooltip");
            Assert.That(ChoiceText.Combines(new string[0]), Is.EqualTo(""));
        }

        [Test]
        public void NoticeShowsTheNameForThreeSecondsOfGameTime()
        {
            var run = Ready("yasha_and_kaya");
            var notice = new RecipeNotice();
            Assert.That(notice.Visible(run), Is.False);
            notice.Show(run, "Яша и Кая");
            Assert.That(notice.Message, Is.EqualTo("Собран предмет: Яша и Кая"));
            Assert.That(RecipeNotice.Seconds, Is.EqualTo(3));
            Assert.That(notice.Visible(run), Is.True);
            int steps = (int)System.Math.Round(2.9f / run.Rules.Step);
            for (int i = 0; i < steps; i++) run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(notice.Visible(run), Is.True, "2.9 s");
            run.Paused = true;
            for (int i = 0; i < 300; i++) run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(notice.Visible(run), Is.True, "the pause stops the game time");
            run.Paused = false;
            for (int i = 0; i < 6; i++) run.Update(run.Rules.Step, Vec2.Zero, false);
            Assert.That(notice.Visible(run), Is.False, "3 s passed");
            var other = Ready("yasha_and_kaya");
            notice.Show(run, "Яша и Кая");
            Assert.That(notice.Visible(other), Is.False, "a new run does not show the old notice");
        }

        [Test]
        public void RecipeSmokeCombinesTheTridentThroughTheItemChoice()
        {
            foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5, 6, 7 })
            {
                var run = new Run(Data(), new Rules(), "dragonus", null, seed) { DebugNoSpawns = true, DebugInvulnerable = true };
                var smoke = new SmokeRecipe("sny_trident");
                Assert.That(smoke.Prepare(run), Is.True);
                Assert.That(run.Build.Rank("sange_and_yasha"), Is.EqualTo(1), "the nested recipe is combined first");
                Assert.That(run.Build.Rank("sny_trident"), Is.EqualTo(0));
                bool itemChoice = false;
                for (int i = 0; i < 600 && !smoke.Combined; i++)
                {
                    run.Update(run.Rules.Step, Vec2.Zero, false);
                    smoke.Count(run);
                    run.ClearEvents();
                    if (!run.Choosing) continue;
                    itemChoice |= run.ChoiceSource == ChoiceSource.Item;
                    Assert.That(run.Choose(smoke.Choose(run)), Is.True);
                    smoke.Count(run);
                    run.ClearEvents();
                }
                Assert.That(itemChoice, Is.True, $"seed {seed}: the treasure opened an item choice");
                Assert.That(run.Build.Rank("sny_trident"), Is.EqualTo(1), $"seed {seed}");
                Assert.That(smoke.Report(), Is.EqualTo("THORN_RECIPE key=sny_trident offered=1 combined=1"), $"seed {seed}");
            }
        }

        [Test]
        public void RecipeSmokeRefusesAnUnknownKeyAndReportsNothingCombined()
        {
            var run = new Run(Data(), new Rules(), "dragonus", null, 7);
            var smoke = new SmokeRecipe("yasha");
            Assert.That(smoke.Prepare(run), Is.False, "not a recipe");
            Assert.That(new SmokeRecipe("no_such").Prepare(run), Is.False);
            Assert.That(smoke.Report(), Is.EqualTo("THORN_RECIPE key=yasha offered=0 combined=0"));
            Assert.That(SmokeRecipe.Prefix, Is.EqualTo("-thornRecipe="));
        }
    }
}
