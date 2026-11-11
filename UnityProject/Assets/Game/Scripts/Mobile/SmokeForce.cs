using System.Collections.Generic;
using System.Text;
using ThornThrone.Simulation;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// Smoke option -thornForce=KEY[,KEY…] (Mechanics §28 Р11): after the run starts every key is given through
    /// <see cref="Run.ForceUpgrade"/> (a repeated key raises the rank); at the end the THORN_FORCE line reports the rank
    /// and the hits of each key, «refused» when the upgrade was refused. Tools/mac-smoke.sh exits with code 14 when a
    /// key was refused or made no hits.
    /// </summary>
    public sealed class SmokeForce
    {
        public const string Prefix = "-thornForce=";

        private readonly List<string> keys = new List<string>();
        private readonly List<bool> refused = new List<bool>();

        /// <summary>Distinct keys in their first order.</summary>
        public IReadOnlyList<string> Keys => keys;

        /// <summary>Keys of the argument: comma separated, blanks skipped, repeats kept (one upgrade each).</summary>
        public static List<string> Parse(string arg)
        {
            var result = new List<string>();
            if (arg == null) return result;
            string list = arg.StartsWith(Prefix) ? arg.Substring(Prefix.Length) : arg;
            foreach (var part in list.Split(','))
            {
                string key = part.Trim();
                if (key.Length > 0) result.Add(key);
            }
            return result;
        }

        /// <summary>Gives every key to the run; false when any upgrade was refused (unknown key, no slot, max rank).</summary>
        public bool Apply(Run run, IEnumerable<string> upgrades)
        {
            bool ok = true;
            foreach (var key in upgrades)
            {
                bool done = run.ForceUpgrade(key);
                ok &= done;
                int at = keys.IndexOf(key);
                if (at < 0) { keys.Add(key); refused.Add(!done); }
                else refused[at] |= !done;
            }
            return ok;
        }

        /// <summary>
        /// Removes the hero's other attacks, so enemies reach the forced areas (the smoke hero stands still and its
        /// starting attack would kill them on the way). A later level-up choice recreates them, as in a normal run.
        /// </summary>
        public void KeepOnlyForced(Run run)
        {
            foreach (var owned in run.Build.Owned)
                if (!keys.Contains(owned.Def.Key)) owned.Ability = null;
        }

        /// <summary>
        /// Longest smoke run with forced upgrades, game seconds: the usual 30 s run goes on until every forced key
        /// has hit, at most this long. Stampede charges at once without enemies near and next after its 30 s
        /// cooldown; the longest active cooldown in the data is Annihilation's 60 s (Mechanics §30 Р9).
        /// </summary>
        public const float MaxSeconds = 65;

        /// <summary>Every key was given and has hits: the smoke run may end.</summary>
        public bool AllHit(Run run)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                var owned = Owned(run, keys[i]);
                if (refused[i] || owned == null || owned.Stats == null || owned.Stats.Hits == 0) return false;
            }
            return true;
        }

        /// <summary>«THORN_FORCE rot=1:82 counter_helix=1:37»: rank and hits per key, «refused» for a refused upgrade.</summary>
        public string Report(Run run)
        {
            var sb = new StringBuilder("THORN_FORCE");
            for (int i = 0; i < keys.Count; i++)
            {
                sb.Append(' ').Append(keys[i]).Append('=');
                var owned = Owned(run, keys[i]);
                if (refused[i] || owned == null) { sb.Append("refused"); continue; }
                sb.Append(owned.Rank).Append(':').Append(owned.Stats != null ? owned.Stats.Hits : 0);
            }
            return sb.ToString();
        }

        private static OwnedPowerUp Owned(Run run, string key)
        {
            foreach (var owned in run.Build.Owned) if (owned.Def.Key == key) return owned;
            return null;
        }
    }
}
