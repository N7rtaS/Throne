using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Chain Frost (CSurvivorsPowerUp_ProjectileAttack, chain_frost). Every cooldown, if an enemy is within Range,
    /// fires TargetCount homing shots at the strongest enemies (m_eTargeting STRONGEST_TARGET_IN_RANGE); a shot
    /// cannot hit for m_flSpawnMinimumLifetime after launch. The impact damages and stuns the struck enemy only and
    /// freezes every enemy touching the FreezeSplashRadius circle around it; then the shot bounces ProjectileBounces
    /// times (m_eBounceTargeting RANDOM_TARGET_AT_RANGE, <see cref="ChainBounce"/>). Damage, stun and freeze are
    /// snapshotted at launch. Interpretations of the data and the description: Mechanics §29 Р1–Р6, Р13–Р17.
    /// With Aghanim's Scepter every successful bounce splits the shot with a chance of 50%: a copy with the same hit
    /// records, bounces left, damage, stun and freeze flies from the impact to another random enemy (Mechanics §34 Р20).
    /// </summary>
    public sealed class ChainFrost : AbilityBase, IProjectileAbility
    {
        /// <summary>The only targeting of the class implemented; it is also the only one in the data (Р6).</summary>
        public const string StrongestTarget = "STRONGEST_TARGET_IN_RANGE";
        public const string RandomBounce = "RANDOM_TARGET_AT_RANGE";
        private const int MaxTargets = 64;
        /// <summary>Chance of a split per bounce with the scepter: «шанс в 50%» of the description (Д, text).</summary>
        public const float ScepterSplitChance = 0.5f;
        private readonly StrongestTargets targets = new StrongestTargets(MaxTargets);
        private readonly CircleTargets area;
        private float cooldown;

        /// <summary>m_flSpawnMinimumLifetime: seconds after launch without hits.</summary>
        public float SpawnDelay { get; }
        /// <summary>m_flBounceMinimumLifetime: seconds after a bounce without hits.</summary>
        public float BounceDelay { get; }

        /// <param name="run">Reports missing data fields; its enemy pool sizes the freeze buffer.</param>
        public ChainFrost(OwnedPowerUp owned, Run run) : base(owned)
        {
            area = new CircleTargets(run.Enemies.Length);
            SpawnDelay = DataField(run, "m_flSpawnMinimumLifetime");
            BounceDelay = DataField(run, "m_flBounceMinimumLifetime");
        }

        /// <summary>Definitions of CSurvivorsPowerUpDefinition_ProjectileAttack this handler implements (Р6).</summary>
        public static bool Supports(PowerUpDef def) => def.Targeting == StrongestTarget && BouncesAtRandom(def);

        private static bool BouncesAtRandom(PowerUpDef def) => (string)def.Raw?["m_eBounceTargeting"] == RandomBounce;

        public int TargetCount(Run run) => Math.Min(MaxTargets, Math.Max(1, (int)Stat(run, Attr.TargetCount)));
        public int Bounces(Run run) => Math.Max(0, (int)Stat(run, Attr.ProjectileBounces));
        public float StunDuration(Run run) => EffectDuration(run, Attr.StunDuration);
        public float FreezeRadius(Run run) => Area(run, Attr.FreezeSplashRadius);
        public float FreezeStrength(Run run) => Stat(run, Attr.FreezeStrength);
        public float FreezeDuration(Run run) => EffectDuration(run, Attr.FreezeDuration);

        public override void Tick(Run run, float dt)
        {
            cooldown -= dt;
            if (cooldown > 0) return;
            int count = targets.Collect(run, run.HeroPosition, Stat(run, Attr.Range), TargetCount(run));
            if (count == 0) return;
            cooldown = Cooldown(run);
            for (int i = 0; i < count; i++)
            {
                var target = targets.Live(i);
                if (target != null) Fire(run, target);
            }
        }

        private void Fire(Run run, Enemy target)
        {
            float speed = Stat(run, Attr.ProjectileSpeed);
            var shot = run.SpawnProjectile(owned);
            if (shot == null) return;
            shot.Position = run.HeroPosition;
            shot.Speed = speed;
            shot.Velocity = (target.Position - run.HeroPosition).Normalized * speed;
            shot.Radius = Stat(run, Attr.ProjectileRadius);
            Arm(run, shot);
            shot.Knockback = 0;
            shot.Stun = StunDuration(run);
            shot.FreezeRadius = FreezeRadius(run);
            shot.FreezeStrength = FreezeStrength(run);
            shot.FreezeTime = FreezeDuration(run);
            shot.RemainingHits = 1;
            shot.Bounces = Bounces(run);
            shot.Life = LegLife(run, speed);
            shot.ArmTime = SpawnDelay;
            shot.Target = target;
            shot.TargetSpawnId = target.SpawnId;
        }

        private float LegLife(Run run, float speed) => Math.Max(3, Stat(run, Attr.Range) / Math.Max(1, speed) + 1);

        public bool OnHit(Run run, Projectile shot, Enemy hit)
        {
            Vec2 impact = hit.Position;
            int id = hit.SpawnId;
            run.DealDamage(hit, shot.Hit);
            if (run.Finished) return false;
            if (hit.Active && hit.SpawnId == id) run.Stun(hit, shot.Stun);
            int count = area.Collect(run, impact, shot.FreezeRadius);
            for (int i = 0; i < count; i++)
            {
                var e = area.Live(i);
                if (e != null) run.Freeze(e, shot.FreezeStrength, shot.FreezeTime);
            }
            float range = Stat(run, Attr.Range), life = LegLife(run, shot.Speed);
            if (!ChainBounce.Next(run, shot, impact, range, life, BounceDelay)) return false;
            if (owned.Scepter && run.Rng.Chance(ScepterSplitChance)) Split(run, shot, impact, range, life);
            return true;
        }

        /// <summary>
        /// The scepter's copy of a shot that has just bounced: same numbers and hit records, its own random target
        /// other than the original's; nothing when there is none or the pool is full (Р20).
        /// </summary>
        private void Split(Run run, Projectile shot, Vec2 impact, float range, float life)
        {
            var next = run.RandomTarget(impact, range, shot, shot.TargetSpawnId);
            if (next == null) return;
            var copy = run.SpawnProjectile(owned);
            if (copy == null) return;
            copy.Position = impact;
            copy.Speed = shot.Speed;
            copy.Velocity = (next.Position - impact).Normalized * shot.Speed;
            copy.Radius = shot.Radius;
            copy.Damage = shot.Damage;
            copy.DamageType = shot.DamageType;
            copy.DamageFlags = shot.DamageFlags;
            copy.DamageSource = shot.DamageSource;
            copy.Knockback = 0;
            copy.Stun = shot.Stun;
            copy.FreezeRadius = shot.FreezeRadius;
            copy.FreezeStrength = shot.FreezeStrength;
            copy.FreezeTime = shot.FreezeTime;
            copy.RemainingHits = 1;
            copy.Bounces = shot.Bounces;
            copy.Life = life;
            copy.ArmTime = BounceDelay;
            copy.Target = next;
            copy.TargetSpawnId = next.SpawnId;
            Array.Copy(shot.HitSpawnIds, copy.HitSpawnIds, shot.HitCount);
            copy.HitCount = shot.HitCount;
        }
    }
}
