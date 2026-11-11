using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Shared part of active abilities: stats, damage formula, cooldown and the hit snapshot (Mechanics §24, Р1).
    /// The damage type comes from the base attributes: Damage_Physical or Damage_Magical.
    /// </summary>
    public abstract class AbilityBase : IAbility
    {
        protected readonly OwnedPowerUp owned;

        /// <summary>Damage type from the data; <see cref="DamageTypeKnown"/> is false when it has neither or both.</summary>
        public DamageType DamageType { get; }
        public bool DamageTypeKnown { get; }
        /// <summary>ProjectileAttack &gt; 0 in the base attributes: projectile critical strikes and bonuses apply.</summary>
        public bool IsProjectileAttack { get; }

        protected AbilityBase(OwnedPowerUp owned)
        {
            this.owned = owned;
            bool physical = false, magical = false;
            float projectile = 0;
            foreach (var a in owned.Def.BaseAttributes)
            {
                if (a.Id == Attr.Damage_Physical) physical = true;
                else if (a.Id == Attr.Damage_Magical) magical = true;
                else if (a.Id == Attr.ProjectileAttack) projectile += a.Value;
            }
            DamageTypeKnown = physical != magical;
            // Without a known type the hit stays pure and the run reports it (Р1).
            DamageType = !DamageTypeKnown ? DamageType.Pure : physical ? DamageType.Physical : DamageType.Magical;
            IsProjectileAttack = projectile > 0;
        }

        public abstract void Tick(Run run, float dt);

        public float Stat(Run run, int attribute) => run.Build.Stat(owned, attribute);

        /// <summary>Area radius × (1 + AoEIncrease); not for projectile, pickup or level-up radii (Mechanics §25 Р3).</summary>
        public float Area(Run run, int attribute) => Stat(run, attribute) * (1 + Stat(run, Attr.AoEIncrease));

        /// <summary>Knockback × (1 + KnockbackDistanceMultiplier); the level-up knockback is not affected (Mechanics §25 Р4).</summary>
        public float KnockbackDistance(Run run) =>
            Stat(run, Attr.KnockbackDistance) * (1 + Stat(run, Attr.KnockbackDistanceMultiplier));

        /// <summary>
        /// Duration of a status the ability puts on an enemy plus the hero's DurationExtension seconds, when the
        /// own duration is above 0; status resistance applies later (Mechanics §25 Р5).
        /// </summary>
        public float EffectDuration(Run run, int attribute)
        {
            float duration = Stat(run, attribute);
            return duration > 0 ? duration + run.Build.Global[Attr.DurationExtension] : duration;
        }

        /// <summary>
        /// (Damage_&lt;type&gt; + flat bonus) × (1 + DamageMultiplier + DamageMultiplier_&lt;type&gt;).
        /// Flat bonus: BonusPhysicalDamage for physical, Projectile_BonusMagicDamage for magic projectile attacks.
        /// </summary>
        public float Damage(Run run)
        {
            var global = run.Build.Global;
            switch (DamageType)
            {
                case DamageType.Physical:
                    return (Stat(run, Attr.Damage_Physical) + global[Attr.BonusPhysicalDamage])
                        * (1 + global[Attr.DamageMultiplier] + global[Attr.DamageMultiplier_Physical]);
                case DamageType.Magical:
                    float bonus = IsProjectileAttack ? global[Attr.Projectile_BonusMagicDamage] : 0;
                    return (Stat(run, Attr.Damage_Magical) + bonus)
                        * (1 + global[Attr.DamageMultiplier] + global[Attr.DamageMultiplier_Magical]);
                default:
                    return Stat(run, Attr.Damage_Physical) + Stat(run, Attr.Damage_Magical);
            }
        }

        public float Cooldown(Run run) =>
            Math.Max(0.1f, owned.Local[Attr.Cooldown]
                * Math.Max(0.05f, 1 - owned.Local[Attr.CooldownReductionMultiplier])
                * Math.Max(0.05f, 1 - run.Build.Global[Attr.CooldownReductionMultiplier]));

        /// <summary>Snapshot of one hit at launch.</summary>
        public DamageInfo Hit(Run run) =>
            new DamageInfo(Damage(run), DamageType, IsProjectileAttack ? DamageFlags.Projectile : DamageFlags.None, owned.Stats);

        /// <summary>
        /// A number of the definition outside its attributes (m_flSpawnMinimumLifetime, m_nEliteExtraHit…). A missing
        /// field is not silently zero: the run reports «ability:&lt;key&gt;:&lt;field&gt;» and 0 is used.
        /// </summary>
        protected float DataField(Run run, string field)
        {
            var token = owned.Def.Raw?[field];
            if (token != null) return (float)token;
            run.Diagnostics.Report($"ability:{owned.Def.Key}:{field}", $"Ability {owned.Def.Key} has no {field}; 0 is used");
            return 0;
        }

        /// <summary>Loads the hit snapshot into a fired projectile.</summary>
        protected void Arm(Run run, Projectile shot)
        {
            var hit = Hit(run);
            shot.Damage = hit.Amount;
            shot.DamageType = hit.Type;
            shot.DamageFlags = hit.Flags;
            shot.DamageSource = hit.Source;
        }
    }
}
