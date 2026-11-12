using System;
using System.Collections.Generic;
using ThornThrone.Progression;
using ThornThrone.Simulation;

namespace ThornThrone.App
{
    /// <summary>App screens in order (Mechanics §23; ТЗ_Мобильная_версия_v2.md §7).</summary>
    public enum AppScreen
    {
        Menu,
        Hero,
        Difficulty,
        Run,
        Results,
    }

    /// <summary>What the results screen shows about the last finished run.</summary>
    public sealed class RunOutcome
    {
        public RunResult Result;
        /// <summary>Echelon 1..8 the run was played on.</summary>
        public int Echelon;
        /// <summary>Echelon opened by this run; 0 — nothing new.</summary>
        public int UnlockedEchelon;
        /// <summary>Fixed result of the run: score, gold, abilities and items (Mechanics §37 Р2).</summary>
        public RunSummary Summary;
        /// <summary>Currency of the profile after this run's gold was paid in (Mechanics §38 М10, «Всего золота»).</summary>
        public long TotalGold;
        /// <summary>The currency pays for at least one next permanent upgrade (М10, «Доступны улучшения!»).</summary>
        public bool UpgradesAvailable;
    }

    /// <summary>
    /// Screen flow of the app without the engine (Mechanics §23): menu → hero → difficulty → run → results → again
    /// or back. Creates the <see cref="Run"/> on the chosen hero and echelon, reads open echelons from the profile
    /// (Ф1) and records a finished run once through the reward journal (Ф3), saving the profile at once (§22 Р5).
    /// The gold of the run is paid into the profile by the same operation; permanent upgrades are bought outside a
    /// run and apply to the start attributes of the next one (Mechanics §38).
    /// A command that is not valid on the current screen changes nothing and returns false.
    /// </summary>
    public sealed class AppFlow
    {
        /// <summary>Journal operation prefix of a recorded run result (Ф3).</summary>
        public const string ResultPrefix = "result:";

        private readonly GameData data;
        private readonly Profile profile;
        private readonly Func<Profile, SaveResult> save;
        private readonly Func<ulong> seeds;
        private readonly Func<Rules> rules;
        private readonly List<string> playable;
        private string runId;
        private bool difficultyOpened;

        /// <param name="playableHeroes">Heroes that can be chosen, in display order; the first is preselected.</param>
        /// <param name="rules">Rules of a new run; null — the default rules.</param>
        public AppFlow(GameData data, Profile profile, Func<Profile, SaveResult> save, Func<ulong> seeds,
            IReadOnlyList<string> playableHeroes, Func<Rules> rules = null)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.save = save ?? throw new ArgumentNullException(nameof(save));
            this.seeds = seeds ?? throw new ArgumentNullException(nameof(seeds));
            this.rules = rules ?? (() => new Rules());
            playable = new List<string>(playableHeroes ?? throw new ArgumentNullException(nameof(playableHeroes)));
            foreach (var key in playable)
                if (!data.Heroes.ContainsKey(key)) throw new ArgumentException("Unknown hero " + key, nameof(playableHeroes));
            Hero = playable.Count > 0 ? playable[0] : null;
        }

        public AppScreen Screen { get; private set; } = AppScreen.Menu;
        /// <summary>The current run on the Run and Results screens, otherwise null.</summary>
        public Run Run { get; private set; }
        public string Hero { get; private set; }
        /// <summary>Chosen echelon 1..<see cref="EchelonCount"/>; 0 until the difficulty screen opens.</summary>
        public int Echelon { get; private set; }
        /// <summary>Seed of the current or last run.</summary>
        public ulong Seed { get; private set; }
        /// <summary>Number of echelons: the difficulties of survivors_difficulty.json in data order (Mechanics §8).</summary>
        public int EchelonCount => data.DifficultyOrder.Count;
        public RunOutcome LastOutcome { get; private set; }
        public SaveResult LastSave { get; private set; }
        public bool SaveFailed => LastSave != null && !LastSave.Succeeded;
        public Profile Profile => profile;

        public static string UnlockId(int echelon) => "echelon_" + echelon;

        /// <summary>The difficulty echelon N adds to 1..N − 1 (data order); its name and description label the card.</summary>
        public DifficultyDef EchelonDifficulty(int echelon)
        {
            if (echelon < 1 || echelon > EchelonCount) throw new ArgumentOutOfRangeException(nameof(echelon), echelon, null);
            return data.Difficulties[data.DifficultyOrder[echelon - 1]];
        }

        public bool IsHeroPlayable(string key) => key != null && playable.Contains(key);

        /// <summary>Ф1: echelon 1 is always open, echelon N when the profile has «echelon_N»; gaps are not filled in.</summary>
        public bool IsUnlocked(int echelon) =>
            echelon >= 1 && echelon <= EchelonCount && (echelon == 1 || profile.Unlocks.Contains(UnlockId(echelon)));

        public int HighestUnlocked
        {
            get
            {
                for (int n = EchelonCount; n > 1; n--)
                    if (IsUnlocked(n)) return n;
                return 1;
            }
        }

        // ------------------------------------------------------------------ menu, hero, difficulty

        public bool Play() => Go(AppScreen.Menu, AppScreen.Hero);

        public bool SelectHero(string key)
        {
            if (Screen != AppScreen.Hero || !IsHeroPlayable(key)) return false;
            Hero = key;
            return true;
        }

        /// <summary>Hero → difficulty.</summary>
        public bool Next()
        {
            if (Screen != AppScreen.Hero || Hero == null) return false;
            OpenDifficulty();
            return true;
        }

        /// <summary>Ф10: hero → menu, difficulty → hero; other screens are handled by the app (BackNavigation).</summary>
        public bool Back()
        {
            if (Screen == AppScreen.Hero) return Go(AppScreen.Hero, AppScreen.Menu);
            return Go(AppScreen.Difficulty, AppScreen.Hero);
        }

        public bool SelectEchelon(int echelon)
        {
            if (Screen != AppScreen.Difficulty || !IsUnlocked(echelon)) return false;
            Echelon = echelon;
            return true;
        }

        public bool Start()
        {
            if (Screen != AppScreen.Difficulty || !IsUnlocked(Echelon)) return false;
            NewRun();
            return true;
        }

        // ------------------------------------------------------------------ run and results

        /// <summary>A finished run → results, recorded once (Ф3). False while the run goes on or after the first call.</summary>
        public bool Finish()
        {
            if (Screen != AppScreen.Run || Run == null || !Run.Finished) return false;
            Screen = AppScreen.Results;
            Record();
            return true;
        }

        /// <summary>
        /// Ф3: adds the result of the finished run, pays its gold into the currency (§38 М2), opens the next echelon
        /// after a victory (Ф2), marks
        /// «result:&lt;runId&gt;» in the journal and saves. False if the run is not finished or already recorded:
        /// the profile is not changed and not saved again (a failed save is repeated by <see cref="RetryFailedSave"/>).
        /// </summary>
        public bool Record()
        {
            if (Run == null || !Run.Finished || runId == null) return false;
            string operation = ResultPrefix + runId;
            if (profile.Journal.Contains(operation)) return false;
            var result = new RunResult
            {
                Id = runId,
                RulesVersion = ProfileSchema.RulesVersion,
                Hero = Hero,
                Difficulty = UnlockId(Echelon),
                Victory = Run.Victory,
                DurationSeconds = Run.Time,
                Level = Run.HeroLevel,
                Kills = Run.Kills,
                // Ф4: the score of the run (Mechanics §37 Р4) and its gold, paid into the currency below (§38 М2).
                Score = Run.Score,
                Gold = Run.Gold,
            };
            int unlocked = 0;
            if (Run.Victory && Echelon < EchelonCount && !IsUnlocked(Echelon + 1))
            {
                profile.Unlocks.Add(UnlockId(Echelon + 1));
                unlocked = Echelon + 1;
            }
            profile.Results.Add(result);
            profile.Currency += Run.Gold;
            profile.Journal.TryAdd(operation);
            LastOutcome = new RunOutcome
            {
                Result = result, Echelon = Echelon, UnlockedEchelon = unlocked, Summary = Run.Summary,
                TotalGold = profile.Currency, UpgradesAvailable = MetaProgression.AnyAffordable(data, profile),
            };
            LastSave = save(profile);
            return true;
        }

        /// <summary>Ф5: «Завершить забег» → menu; an unfinished run is not recorded (ТЗ §9).</summary>
        public bool Abandon()
        {
            if (Screen != AppScreen.Run || Run == null || Run.Finished) return false;
            Run = null;
            runId = null;
            Screen = AppScreen.Menu;
            return true;
        }

        /// <summary>Same hero and echelon, a new seed.</summary>
        public bool PlayAgain()
        {
            if (Screen != AppScreen.Results || !IsUnlocked(Echelon)) return false;
            NewRun();
            return true;
        }

        public bool ToHero()
        {
            if (Screen != AppScreen.Results) return false;
            LeaveResults(AppScreen.Hero);
            return true;
        }

        public bool ToDifficulty()
        {
            if (Screen != AppScreen.Results) return false;
            LeaveResults(AppScreen.Difficulty);
            OpenDifficulty();
            return true;
        }

        public bool ToMenu()
        {
            if (Screen != AppScreen.Results) return false;
            LeaveResults(AppScreen.Menu);
            return true;
        }

        /// <summary>
        /// Mechanics §38 М4–М7: buys level <paramref name="level"/> of a permanent upgrade (<see cref="MetaProgression.Buy"/>)
        /// on any screen but the run and saves the profile once. A repeated press returns AlreadyOwned without a charge
        /// or a save; a failed save keeps the purchase and is repeated by <see cref="RetryFailedSave"/>.
        /// </summary>
        public PurchaseResult BuyUpgrade(string key, int level)
        {
            if (Screen == AppScreen.Run) return PurchaseResult.RunInProgress;
            var result = MetaProgression.Buy(profile, data, key, level);
            if (result == PurchaseResult.Bought) LastSave = save(profile);
            return result;
        }

        /// <summary>Mechanics §39 З3: results of <paramref name="hero"/> on <paramref name="echelon"/> in this profile.</summary>
        public HeroEchelonRecord RecordOf(string hero, int echelon) => Records.Of(data, profile, hero, echelon);

        /// <summary>Mechanics §39 З4: the local records table, best scores first.</summary>
        public List<RecordRow> TopRecords(int count) => Records.Top(data, profile, count);

        /// <summary>
        /// Mechanics §39 З6: the test profile gets every echelon 2..<see cref="EchelonCount"/> and is saved once if
        /// something was added; results, currency, upgrades and the journal stay. Returns the number of echelons added.
        /// </summary>
        /// <exception cref="InvalidOperationException">The profile is not the test one; nothing changes.</exception>
        public int OpenAllEchelons()
        {
            if (profile.Kind != ProfileKind.Test)
                throw new InvalidOperationException("Only the test profile opens every echelon, not " + ProfileSchema.KindName(profile.Kind));
            int added = 0;
            for (int n = 2; n <= EchelonCount; n++)
                if (profile.Unlocks.Add(UnlockId(n))) added++;
            if (added > 0) LastSave = save(profile);
            return added;
        }

        /// <summary>Ф11: saves again after a failed save; false if there is nothing to retry.</summary>
        public bool RetryFailedSave()
        {
            if (!SaveFailed) return false;
            LastSave = save(profile);
            return true;
        }

        // ------------------------------------------------------------------ helpers

        private bool Go(AppScreen from, AppScreen to)
        {
            if (Screen != from) return false;
            Screen = to;
            return true;
        }

        /// <summary>Ф6: the first opening in a launch takes the highest open echelon, later the last chosen one.</summary>
        private void OpenDifficulty()
        {
            Screen = AppScreen.Difficulty;
            if (!difficultyOpened || !IsUnlocked(Echelon)) Echelon = HighestUnlocked;
            difficultyOpened = true;
        }

        private void LeaveResults(AppScreen to)
        {
            Run = null;
            runId = null;
            Screen = to;
        }

        private void NewRun()
        {
            Seed = seeds();
            runId = "run-" + Guid.NewGuid().ToString("N");
            // Permanent upgrades of the profile (§38 М8); their problems are reported by the run.
            var diagnostics = new Diagnostics();
            var start = MetaProgression.StartAttributes(data, profile, diagnostics);
            Run = new Run(data, rules(), Hero, UnlockId(Echelon), Seed, startAttributes: start);
            Run.Diagnostics.Include(diagnostics);
            Screen = AppScreen.Run;
        }
    }
}
