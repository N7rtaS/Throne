using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Frostbite (CSurvivorsPowerUp_Frostbite, m_eTargeting STRONGEST_TARGET_IN_RANGE). Every cooldown, if an enemy
    /// is within Range, frostbites the TargetCount strongest different enemies in the same step (the data has no
    /// ProjectileSpeed): for FreezeDuration + DurationExtension seconds a freeze of FreezeStrength (status
    /// resistance applies), periodic magical damage ModifierDamagePerTick every TimeBetweenTicks, and projectile
    /// hits × (1 + ProjectileDamagePercent). Damage_Magical 0: no direct hit. Mechanics §29 Р1, Р2, Р22–Р26.
    /// </summary>
    public sealed class Frostbite : AbilityBase
    {
        private const int MaxTargets = 64;
        private readonly StrongestTargets targets = new StrongestTargets(MaxTargets);
        private float cooldown;

        public Frostbite(OwnedPowerUp owned) : base(owned) { }

        public int TargetCount(Run run) => Math.Min(MaxTargets, Math.Max(1, (int)Stat(run, Attr.TargetCount)));
        /// <summary>Seconds of every part of the frostbite: FreezeDuration + DurationExtension.</summary>
        public float Duration(Run run) => EffectDuration(run, Attr.FreezeDuration);
        public float FreezeStrength(Run run) => Stat(run, Attr.FreezeStrength);
        public float Interval(Run run) => Stat(run, Attr.TimeBetweenTicks);
        public float ProjectileAmplify(Run run) => Stat(run, Attr.ProjectileDamagePercent);

        /// <summary>ModifierDamagePerTick × (1 + DamageMultiplier + DamageMultiplier_Magical); no flat projectile bonus (Р23).</summary>
        public float PerTick(Run run)
        {
            var global = run.Build.Global;
            return Stat(run, Attr.ModifierDamagePerTick) * (1 + global[Attr.DamageMultiplier] + global[Attr.DamageMultiplier_Magical]);
        }

        public override void Tick(Run run, float dt)
        {
            cooldown -= dt;
            if (cooldown > 0) return;
            int count = targets.Collect(run, run.HeroPosition, Stat(run, Attr.Range), TargetCount(run));
            if (count == 0) return;
            cooldown = Cooldown(run);
            float duration = Duration(run), strength = FreezeStrength(run), perTick = PerTick(run);
            float interval = Interval(run), amplify = ProjectileAmplify(run);
            // No DealDamage here: Damage_Magical is 0, the damage comes from the ticks (Р25).
            for (int i = 0; i < count; i++)
            {
                var e = targets.Live(i);
                if (e == null) continue;
                run.Freeze(e, strength, duration);
                run.ApplyDot(e, owned.Stats, DamageType, perTick, interval, duration);
                ProjectileAmplification.Apply(run, e, amplify, duration);
            }
        }
    }
}
