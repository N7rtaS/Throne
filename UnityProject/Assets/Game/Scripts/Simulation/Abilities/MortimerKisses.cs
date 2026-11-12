using System;
namespace ThornThrone.Simulation
{
    /// <summary>
    /// Mortimer Kisses (CSurvivorsPowerUp_MortimerKisses, Snapfire; m_eOrigin RANDOM_ENEMY_ORIGIN). Every cooldown, if a
    /// targetable enemy is between m_flMinRange and Range from the hero, starts a volley of TargetCount lava globs
    /// thrown ProjectileAttackInterval apart; the next volley waits for the last glob. Each glob picks one random
    /// enemy in that ring at its launch (<see cref="RandomTargets"/>) and flies to the enemy's position at that moment
    /// for distance / ProjectileSpeed seconds without touching anything. Where it lands it becomes a puddle of
    /// Radius × (1 + AoEIncrease) (the same <see cref="GroundObject"/> and SpawnId) that deals DamageTicks magical
    /// ticks, the first after InitialTickDelay, then every TimeBetweenTicks, to every targetable enemy touching it.
    /// All numbers are snapshotted at launch. Mechanics §31 Р11–Р18 (design decisions; owner's decisions 12.11.2026
    /// for Р2, Р12, Р14). With Aghanim's Scepter an enemy killed by this ability drops, with m_flSpawnPickupOnKillPercent,
    /// the pickup m_unSpawnPickupOnKillID (the healing cookie) where it dies (Mechanics §34 Р23).
    /// </summary>
    public sealed class MortimerKisses : AbilityBase, IGroundObjectAbility, IKillEffect
    {
        private const float Epsilon = 1e-4f;
        private readonly RandomTargets picks;
        private readonly CircleTargets targets;
        private float cooldown, volleyTimer;
        private int volleyRemaining;
        /// <summary>m_flMinRange: enemies closer to the hero are not picked (Р12).</summary>
        public float MinRange { get; }
        /// <summary>m_flSpawnPickupOnKillPercent: chance of the cookie per kill with the scepter.</summary>
        public float CookieChance { get; }
        /// <summary>Pickup of m_unSpawnPickupOnKillID, or null when the data has none.</summary>
        public PickupDef Cookie { get; }
        public MortimerKisses(OwnedPowerUp owned, Run run) : base(owned)
        {
            picks = new RandomTargets(run.Enemies.Length);
            targets = new CircleTargets(run.Enemies.Length);
            MinRange = DataField(run, "m_flMinRange");
            CookieChance = DataField(run, "m_flSpawnPickupOnKillPercent");
            int cookie = (int)DataField(run, "m_unSpawnPickupOnKillID");
            if (run.Data.PickupsById.TryGetValue(cookie, out var def)) Cookie = def;
            else run.Diagnostics.Report($"ability:{owned.Def.Key}:pickup:{cookie}",
                $"Ability {owned.Def.Key} names pickup {cookie} (m_unSpawnPickupOnKillID), which the data does not have; no cookie");
        }
        /// <summary>The scepter's cookie: kills of this ability only, one roll per kill (Р23).</summary>
        public void OnKill(Run run, Enemy enemy, DamageStatEntry source)
        {
            if (!owned.Scepter || source != owned.Stats || Cookie == null) return;
            if (run.Rng.Chance(CookieChance)) run.SpawnAbilityPickup(Cookie, enemy.Position);
        }
        public int Volley(Run run) => Math.Max(1, (int)Stat(run, Attr.TargetCount));
        public float Interval(Run run) => Math.Max(run.Rules.Step, Stat(run, Attr.ProjectileAttackInterval));
        public float Range(Run run) => Stat(run, Attr.Range);
        public float Speed(Run run) => Stat(run, Attr.ProjectileSpeed);
        /// <summary>Puddle radius.</summary>
        public float Radius(Run run) => Area(run, Attr.Radius);
        public int Ticks(Run run) => Math.Max(1, (int)Stat(run, Attr.DamageTicks));
        public float TickInterval(Run run) => Math.Max(run.Rules.Step, Stat(run, Attr.TimeBetweenTicks));
        public override void Tick(Run run, float dt)
        {
            cooldown -= dt;
            volleyTimer -= dt;
            if (volleyRemaining == 0)
            {
                if (cooldown > dt * 0.5f) return; // half a step of float tolerance (Mechanics §30 Р2)
                if (!Launch(run)) return; // no enemy in the ring: try again next step (Р11)
                cooldown = Cooldown(run);
                volleyRemaining = Volley(run) - 1;
                volleyTimer = Interval(run);
                return;
            }
            if (volleyTimer > Epsilon) return;
            Launch(run); // no enemy in the ring: this glob is skipped (Р13)
            volleyRemaining--;
            volleyTimer = Interval(run);
        }
        /// <summary>Throws one glob at a random enemy in the ring; false when there is none (a full pool still counts as thrown, Р2).</summary>
        private bool Launch(Run run)
        {
            var hero = run.HeroPosition;
            if (picks.Collect(run, hero, MinRange, Range(run), 1) == 0) return false;
            var target = picks.Live(0);
            if (target == null) return false;
            var glob = run.SpawnGroundObject(owned, target.Position);
            if (glob == null) return true;
            var hit = Hit(run);
            glob.Phase = GroundPhase.Falling;
            glob.Origin = hero;
            glob.FlightTime = Math.Max(run.Rules.Step, (float)Math.Sqrt(Vec2.DistanceSquared(target.Position, hero)) / Math.Max(1, Speed(run)));
            glob.Timer = glob.FlightTime;
            glob.Radius = Radius(run);
            glob.TicksLeft = Ticks(run);
            glob.ArmTime = Stat(run, Attr.InitialTickDelay);
            glob.Interval = TickInterval(run);
            glob.Damage = hit.Amount;
            glob.DamageType = hit.Type;
            glob.DamageFlags = hit.Flags;
            glob.DamageSource = hit.Source;
            return true;
        }
        public bool UpdateObject(Run run, GroundObject glob, float dt)
        {
            glob.Timer -= dt;
            if (glob.Phase == GroundPhase.Falling)
            {
                if (glob.Timer > Epsilon) return true;
                // Landed: the puddle's first tick comes InitialTickDelay later (Р15).
                glob.Phase = GroundPhase.Burning;
                glob.Timer = glob.ArmTime;
            }
            if (glob.Timer > Epsilon) return true;
            Burn(run, glob);
            if (--glob.TicksLeft <= 0) return false;
            glob.Timer = glob.Interval;
            return true;
        }
        private void Burn(Run run, GroundObject puddle)
        {
            int count = targets.Collect(run, puddle.Position, puddle.Radius);
            var hit = puddle.Hit;
            for (int i = 0; i < count; i++)
            {
                var e = targets.Live(i);
                if (e == null || run.Finished) continue;
                run.DealDamage(e, hit);
            }
        }
    }
}
