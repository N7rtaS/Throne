using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Magic Missile (CSurvivorsPowerUp_MagicMissile, Shen's starting skill). Every cooldown, if an enemy is in
    /// Range, fires TargetCount missiles at the closest targets (m_eTargeting CLOSEST_TARGET). A missile homes on
    /// its target; the impact damages and stuns every enemy within Radius of the hit enemy for StunDuration.
    /// Then it bounces ProjectileBounces times to a random enemy within Range of the impact that it has not hit
    /// yet (m_eBounceTargeting RANDOM_TARGET_AT_RANGE, <see cref="ChainBounce"/>) and cannot hit for
    /// m_flBounceMinimumLifetime after a bounce.
    /// Damage and stun are snapshotted at launch. Area damage on impact and bounce selection are interpretations
    /// of the data and the description (Mechanics §15). Aghanim's Scepter adds m_flScepterDamageIncreasePerEnemyKilled to
    /// the base damage for every kill of this ability since the scepter; the shard multiplies the damage by
    /// 1 + m_flShardDamageMultiplier (Mechanics §34 Р10).
    /// </summary>
    public sealed class MagicMissile : AbilityBase, IProjectileAbility
    {
        private readonly float bounceDelay;
        // Snapshot of the impact area: damage can kill and recycle slots (split units) while it is applied.
        private readonly Enemy[] area = new Enemy[64];
        private readonly int[] areaIds = new int[64];
        private float cooldown;

        /// <param name="run">Reports missing data fields.</param>
        public MagicMissile(OwnedPowerUp owned, Run run) : base(owned)
        {
            var delay = owned.Def.Raw?["m_flBounceMinimumLifetime"];
            bounceDelay = delay != null ? (float)delay : 0;
            ScepterDamagePerKill = DataField(run, "m_flScepterDamageIncreasePerEnemyKilled");
            ShardDamageMultiplier = DataField(run, "m_flShardDamageMultiplier");
        }

        /// <summary>m_flScepterDamageIncreasePerEnemyKilled (Д 0.01).</summary>
        public float ScepterDamagePerKill { get; }
        /// <summary>m_flShardDamageMultiplier (Д 0.25).</summary>
        public float ShardDamageMultiplier { get; }

        /// <summary>Current scepter bonus: per-kill damage × kills of this ability since the scepter; 0 without it.</summary>
        public float ScepterBonus => owned.Scepter && owned.Stats != null
            ? ScepterDamagePerKill * Math.Max(0, owned.Stats.Kills - owned.KillsAtScepter) : 0;

        protected override float BonusDamage(Run run) => ScepterBonus;
        protected override float DamageScale(Run run) => owned.Shard ? 1 + ShardDamageMultiplier : 1;

        public int Bounces(Run run) => Math.Max(0, (int)Stat(run, Attr.ProjectileBounces));
        public float StunDuration(Run run) => EffectDuration(run, Attr.StunDuration);
        public float ImpactRadius(Run run) => Area(run, Attr.Radius);

        public override void Tick(Run run, float dt)
        {
            cooldown -= dt;
            if (cooldown > 0) return;
            int count = run.ClosestTargets(run.HeroPosition, Stat(run, Attr.Range));
            if (count == 0) return;
            cooldown = Cooldown(run);
            int missiles = Math.Max(1, (int)Stat(run, Attr.TargetCount));
            for (int i = 0; i < missiles; i++) Fire(run, run.TargetBuffer[i % count]);
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
            shot.RemainingHits = 1;
            shot.Bounces = Bounces(run);
            shot.Life = LegLife(run, speed);
            shot.Target = target;
            shot.TargetSpawnId = target.SpawnId;
        }

        private float LegLife(Run run, float speed) => Math.Max(3, Stat(run, Attr.Range) / Math.Max(1, speed) + 1);

        public bool OnHit(Run run, Projectile shot, Enemy hit)
        {
            Vec2 impact = hit.Position;
            int count = Math.Min(area.Length, run.ClosestTargets(impact, ImpactRadius(run)));
            for (int i = 0; i < count; i++) { area[i] = run.TargetBuffer[i]; areaIds[i] = area[i].SpawnId; }
            for (int i = 0; i < count; i++)
            {
                var e = area[i];
                area[i] = null;
                if (!e.Active || e.SpawnId != areaIds[i]) continue;
                run.DealDamage(e, shot.Hit);
                if (run.Finished) return false;
                if (e.Active && e.SpawnId == areaIds[i]) run.Stun(e, shot.Stun);
            }
            return ChainBounce.Next(run, shot, impact, Stat(run, Attr.Range), LegLife(run, shot.Speed), bounceDelay);
        }
    }
}
