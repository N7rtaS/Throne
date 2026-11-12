using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Frostbite (CSurvivorsPowerUp_Frostbite, m_eTargeting STRONGEST_TARGET_IN_RANGE). Every cooldown, if an enemy
    /// is within Range, frostbites the TargetCount strongest different enemies in the same step (the data has no
    /// ProjectileSpeed): for FreezeDuration + DurationExtension seconds a freeze of FreezeStrength (status
    /// resistance applies), periodic magical damage ModifierDamagePerTick every TimeBetweenTicks, and projectile
    /// hits × (1 + ProjectileDamagePercent). Damage_Magical 0: no direct hit. Mechanics §29 Р1, Р2, Р22–Р26.
    /// With Aghanim's Scepter an enemy that dies with this frostbite on it shatters: every targetable enemy touching
    /// the circle of m_flScepterExplodeRadius × (1 + AoEIncrease) around it gets the frostbite with the ability's
    /// numbers now. The shatter deals no damage, so a chain goes on only through later deaths (Mechanics §34 Р22).
    /// </summary>
    public sealed class Frostbite : AbilityBase, IKillEffect
    {
        private const int MaxTargets = 64;
        private readonly StrongestTargets targets = new StrongestTargets(MaxTargets);
        private readonly CircleTargets shatter;
        private float cooldown;

        /// <summary>m_flScepterExplodeRadius: radius of the shatter before AoEIncrease.</summary>
        public float ScepterExplodeRadius { get; }

        /// <param name="run">Its enemy pool sizes the shatter buffer; reports a missing field.</param>
        public Frostbite(OwnedPowerUp owned, Run run) : base(owned)
        {
            shatter = new CircleTargets(run.Enemies.Length);
            ScepterExplodeRadius = DataField(run, "m_flScepterExplodeRadius");
        }

        /// <summary>m_flScepterExplodeRadius × (1 + AoEIncrease).</summary>
        public float ShatterRadius(Run run) => ScepterExplodeRadius * (1 + Stat(run, Attr.AoEIncrease));

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
                if (e != null) Bite(run, e, strength, duration, perTick, interval, amplify);
            }
        }

        private void Bite(Run run, Enemy e, float strength, float duration, float perTick, float interval, float amplify)
        {
            run.Freeze(e, strength, duration);
            run.ApplyDot(e, owned.Stats, DamageType, perTick, interval, duration);
            ProjectileAmplification.Apply(run, e, amplify, duration);
        }

        /// <summary>The scepter's shatter of an enemy dying with this frostbite (Р22); the dying enemy itself is skipped.</summary>
        public void OnKill(Run run, Enemy enemy, DamageStatEntry source)
        {
            if (!owned.Scepter || !enemy.Status.HasDot(owned.Stats)) return;
            int count = shatter.Collect(run, enemy.Position, ShatterRadius(run));
            if (count == 0) return;
            float duration = Duration(run), strength = FreezeStrength(run), perTick = PerTick(run);
            float interval = Interval(run), amplify = ProjectileAmplify(run);
            for (int i = 0; i < count; i++)
            {
                var e = shatter.Live(i);
                if (e != null && e != enemy) Bite(run, e, strength, duration, perTick, interval, amplify);
            }
        }
    }
}
