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
    }

    /// <summary>
    /// Screen flow of the app without the engine (Mechanics §23): menu → hero → difficulty → run → results → again
    /// or back. Creates the <see cref="Run"/> on the chosen hero and echelon, reads open echelons from the profile
    /// (Ф1) and records a finished run once through the reward journal (Ф3), saving the profile at once (§22 Р5).
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
        /// Ф3: adds the result of the finished run, opens the next echelon after a victory (Ф2), marks
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
                // Ф4: the score formula is unknown (Mechanics §13); gold is stored, not yet paid as currency (block D).
                Score = 0,
                Gold = Run.Gold,
            };
            int unlocked = 0;
            if (Run.Victory && Echelon < EchelonCount && !IsUnlocked(Echelon + 1))
            {
                profile.Unlocks.Add(UnlockId(Echelon + 1));
                unlocked = Echelon + 1;
            }
            profile.Results.Add(result);
            profile.Journal.TryAdd(operation);
            LastOutcome = new RunOutcome { Result = result, Echelon = Echelon, UnlockedEchelon = unlocked };
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
            Run = new Run(data, rules(), Hero, UnlockId(Echelon), Seed);
            Screen = AppScreen.Run;
        }
    }
}
