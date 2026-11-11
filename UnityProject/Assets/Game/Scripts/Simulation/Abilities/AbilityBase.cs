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
