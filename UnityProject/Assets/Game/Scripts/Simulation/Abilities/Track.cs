using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Track (CSurvivorsPowerUp_Track, Bounty Hunter; m_eTargeting STRONGEST_TARGET_IN_RANGE). Every cooldown, if an
    /// enemy is within Range, hits the TargetCount strongest different enemies in the same step (the data has no
    /// ProjectileSpeed) with a physical projectile hit, then marks each survivor: all non-pure damage to it
    /// × (1 + IncomingDamageAmplification) for IncomingDamageAmplificationDuration + DurationExtension seconds
    /// (<see cref="Run.Amplify"/>), and its death drops m_luckyLootTable without the luck roll. Mechanics §30 Р18–Р21.
    /// </summary>
    public sealed class Track : AbilityBase
    {
        /// <summary>Radius of the AbilityBurst event on a target (display only, Mechanics §30 Р21).</summary>
        public const float BurstRadius = 80;

        /// <summary>_class of Track: its scepter «Каждая сокровищница даёт два предмета» is applied by the run (Mechanics §34 Р9).</summary>
        public const string ClassName = "CSurvivorsPowerUpDefinition_Track";

        private const int MaxTargets = 64;

        private readonly StrongestTargets targets = new StrongestTargets(MaxTargets);
        private float cooldown;

        public Track(OwnedPowerUp owned) : base(owned) { }

        public int TargetCount(Run run) => Math.Min(MaxTargets, Math.Max(1, (int)Stat(run, Attr.TargetCount)));
        public float Amplification(Run run) => Stat(run, Attr.IncomingDamageAmplification);
        public float MarkDuration(Run run) => EffectDuration(run, Attr.IncomingDamageAmplificationDuration);

        public override void Tick(Run run, float dt)
        {
            cooldown -= dt;
            if (cooldown > dt * 0.5f) return; // half a step of float tolerance, as Stampede (Mechanics §30 Р2)
            int count = targets.Collect(run, run.HeroPosition, Stat(run, Attr.Range), TargetCount(run));
            if (count == 0) return;
            cooldown = Cooldown(run);
            var hit = Hit(run);
            float strength = Amplification(run), duration = MarkDuration(run);
            for (int i = 0; i < count; i++)
            {
                var e = targets.Live(i);
                if (e == null) continue;
                run.EmitBurst(e.Position, BurstRadius, owned.Def.Id);
                int id = e.SpawnId;
                run.DealDamage(e, hit);
                if (run.Finished) return;
                if (e.Active && e.SpawnId == id) run.Amplify(e, strength, duration);
            }
        }
    }
}
