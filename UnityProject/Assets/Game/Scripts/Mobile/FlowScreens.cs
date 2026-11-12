using System;
using ThornThrone.App;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// Connects the GameUi buttons of the menu, hero, difficulty and results screens to <see cref="AppFlow"/> and shows
    /// the screen of <see cref="AppFlow.Screen"/> (Mechanics §23). Not a MonoBehaviour, so EditMode tests drive it
    /// without MobileGame; MobileGame follows <see cref="ScreenChanged"/>.
    /// </summary>
    public sealed class FlowScreens
    {
        private readonly AppFlow flow;
        private readonly GameUi ui;

        /// <summary>A new <see cref="AppFlow.Run"/> started («Начать», «Сыграть ещё»).</summary>
        public event Action RunStarted;
        /// <summary>The flow moved to another screen (not raised for a hero or echelon choice on the same screen).</summary>
        public event Action ScreenChanged;

        public FlowScreens(AppFlow flow, GameUi ui)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            ui.PlayRequested += () => Go(flow.Play);
            ui.HeroSelected += key => Refresh(flow.SelectHero(key));
            ui.NextRequested += () => Go(flow.Next);
            ui.BackRequested += () => Back();
            ui.EchelonSelected += n => Refresh(flow.SelectEchelon(n));
            ui.StartRequested += () => Go(flow.Start);
            ui.PlayAgainRequested += () => Go(flow.PlayAgain);
            ui.HeroScreenRequested += () => Go(flow.ToHero);
            ui.DifficultyRequested += () => Go(flow.ToDifficulty);
            ui.MenuRequested += () => Go(flow.ToMenu);
            ui.UpgradesRequested += () => Go(flow.OpenUpgrades);
            ui.UpgradeBought += (key, level) => Buy(key, level);
        }

        /// <summary>
        /// Mechanics §40 У8–У9: buys the level the card asked for and redraws the screen; a failed save of the purchase
        /// shows the error line. Returns the answer of <see cref="AppFlow.BuyUpgrade"/>.
        /// </summary>
        public PurchaseResult Buy(string key, int level)
        {
            var result = flow.BuyUpgrade(key, level);
            if (flow.Screen == AppScreen.Upgrades) ui.ShowUpgrades(flow, result == PurchaseResult.Bought && flow.SaveFailed);
            return result;
        }

        public AppFlow Flow => flow;

        /// <summary>Android Back screen of the flow; the Run screen is refined by MobileGame (pause, cards, confirm).</summary>
        public BackScreen BackScreen
        {
            get
            {
                switch (flow.Screen)
                {
                    case AppScreen.Hero: return BackScreen.Hero;
                    case AppScreen.Difficulty: return BackScreen.Difficulty;
                    case AppScreen.Upgrades: return BackScreen.Upgrades;
                    case AppScreen.Run: return BackScreen.Playing;
                    case AppScreen.Results: return BackScreen.Results;
                    default: return BackScreen.Menu;
                }
            }
        }

        /// <summary>Shows the screen of the current flow state.</summary>
        public void Show()
        {
            switch (flow.Screen)
            {
                case AppScreen.Hero: ui.ShowHeroSelect(flow); break;
                case AppScreen.Difficulty: ui.ShowDifficulty(flow); break;
                case AppScreen.Upgrades: ui.ShowUpgrades(flow); break;
                case AppScreen.Run: ui.ShowRun(); break;
                case AppScreen.Results: ui.ShowResults(flow); break;
                default: ui.ShowMenu(flow); break;
            }
        }

        /// <summary>Ф10: hero → menu, difficulty → hero, upgrades → difficulty (§40 У1). False on other screens.</summary>
        public bool Back() => Go(flow.Back);

        /// <summary>Results → menu (Android Back on the results screen, Р11).</summary>
        public bool ToMenu() => Go(flow.ToMenu);

        /// <summary>The run is over → results, recorded once (Ф3).</summary>
        public bool Finish() => Go(flow.Finish);

        /// <summary>Ф5: «Завершить забег» → menu without a record.</summary>
        public bool Abandon() => Go(flow.Abandon);

        /// <summary>Ф11: repeats a failed result or purchase save and updates the error lines.</summary>
        public bool RetryFailedSave()
        {
            if (!flow.RetryFailedSave()) return false;
            ui.SetResultSaveError(flow.SaveFailed);
            if (flow.Screen == AppScreen.Upgrades) ui.SetUpgradesSaveError(flow.SaveFailed);
            return true;
        }

        private bool Go(Func<bool> command)
        {
            var run = flow.Run;
            if (!command()) return false;
            Show();
            if (flow.Run != null && flow.Run != run) RunStarted?.Invoke();
            ScreenChanged?.Invoke();
            return true;
        }

        private void Refresh(bool changed)
        {
            if (changed) Show();
        }
    }
}
