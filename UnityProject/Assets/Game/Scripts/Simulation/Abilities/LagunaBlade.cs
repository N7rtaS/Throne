using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Laguna Blade (CSurvivorsPowerUp_LagunaBlade, m_eTargeting STRONGEST_TARGET_IN_RANGE). Every cooldown, if an
    /// enemy is within Range, strikes the TargetCount strongest different enemies in the same step: magical
    /// projectile-attack damage to the target only, 1 + m_nEliteExtraHit hits on an elite. One AbilityStrike event
    /// per target shows the beam from the hero to it. The beam width has no data and only serves the display
    /// (owner's decision). Mechanics §29 Р1, Р2, Р18–Р21.
    /// </summary>
    public sealed class LagunaBlade : AbilityBase
    {
        /// <summary>Width of the beam in the AbilityStrike event, Source units; display only (Р19).</summary>
        public const float BeamWidth = 60;
        private const int MaxTargets = 64;
        private readonly StrongestTargets targets = new StrongestTargets(MaxTargets);
        private float cooldown;

        /// <summary>m_nEliteExtraHit: extra hits on an elite target.</summary>
        public int EliteExtraHits { get; }

        /// <param name="run">Reports missing data fields.</param>
        public LagunaBlade(OwnedPowerUp owned, Run run) : base(owned)
        {
            EliteExtraHits = Math.Max(0, (int)DataField(run, "m_nEliteExtraHit"));
        }

        public int TargetCount(Run run) => Math.Min(MaxTargets, Math.Max(1, (int)Stat(run, Attr.TargetCount)));

        public override void Tick(Run run, float dt)
        {
            cooldown -= dt;
            if (cooldown > 0) return;
            Vec2 hero = run.HeroPosition;
            int count = targets.Collect(run, hero, Stat(run, Attr.Range), TargetCount(run));
            if (count == 0) return;
            cooldown = Cooldown(run);
            var hit = Hit(run);
            for (int i = 0; i < count; i++)
            {
                var e = targets.Live(i);
                if (e == null) continue;
                Vec2 toTarget = e.Position - hero;
                float length = toTarget.Length;
                Vec2 direction = length > 1e-4f ? toTarget * (1 / length) : run.Facing;
                // Before the damage, so the beam is shown on a kill too (Р21).
                run.EmitStrike(hero + toTarget * 0.5f, direction, new Vec2(length, BeamWidth), owned.Def.Id);
                int spawnId = e.SpawnId;
                int hits = e.Def.IsElite ? 1 + EliteExtraHits : 1;
                for (int h = 0; h < hits && e.Active && e.SpawnId == spawnId; h++)
                {
                    run.DealDamage(e, hit);
                    if (run.Finished) return;
                }
            }
        }
    }
}
