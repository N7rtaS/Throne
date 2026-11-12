using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Knife Throw (CSurvivorsPowerUp_KnifeThrow, m_eTargeting PLAYER_FACING). Every cooldown, with or without
    /// enemies, starts a volley of TargetCount knives thrown ProjectileAttackInterval apart; the next volley waits
    /// for the last knife. A knife flies straight along the hero's facing at the moment it is thrown, from a point
    /// ProjectileHorizontalOffset to the side (the sides alternate), for Range; it pierces 1 + ProjectileHitCount
    /// enemies and pushes them along its flight. Damage is snapshotted at the throw. The volley, side and flight
    /// rules are design decisions (Mechanics §29 Р7–Р11). Aghanim's Scepter: m_scepterUpgradeDefinition
    /// ProjectileHitCount +99, applied by <see cref="Build"/> (Mechanics §34 Р12).
    /// </summary>
    public sealed class KnifeThrow : AbilityBase
    {
        private float cooldown, volleyTimer;
        private int volleyRemaining, volleyIndex;

        public KnifeThrow(OwnedPowerUp owned) : base(owned) { }

        public int Volley(Run run) => Math.Max(1, (int)Stat(run, Attr.TargetCount));
        public float Interval(Run run) => Math.Max(0.02f, Stat(run, Attr.ProjectileAttackInterval));
        /// <summary>Enemies one knife can hit: 1 + ProjectileHitCount.</summary>
        public int Pierce(Run run) => 1 + Math.Max(0, (int)Stat(run, Attr.ProjectileHitCount));
        public float Offset(Run run) => Stat(run, Attr.ProjectileHorizontalOffset);

        public override void Tick(Run run, float dt)
        {
            cooldown -= dt;
            volleyTimer -= dt;
            if (cooldown <= 0 && volleyRemaining == 0)
            {
                volleyRemaining = Volley(run);
                volleyIndex = 0;
                volleyTimer = 0;
                cooldown = Cooldown(run);
            }
            if (volleyRemaining <= 0 || volleyTimer > 0) return;
            Throw(run, volleyIndex);
            volleyIndex++;
            volleyRemaining--;
            volleyTimer = Interval(run);
        }

        private void Throw(Run run, int index)
        {
            float speed = Stat(run, Attr.ProjectileSpeed);
            var shot = run.SpawnProjectile(owned);
            if (shot == null) return;
            Vec2 direction = run.Facing;
            // The 1st, 3rd… knife of a volley leaves from +Perpendicular, the 2nd, 4th… from the other side (Р10).
            float side = index % 2 == 0 ? 1 : -1;
            shot.Position = run.HeroPosition + direction.Perpendicular * (side * Offset(run));
            shot.Speed = speed;
            shot.Velocity = direction * speed;
            shot.Radius = Stat(run, Attr.ProjectileRadius);
            Arm(run, shot);
            shot.Knockback = KnockbackDistance(run);
            shot.RemainingHits = Pierce(run);
            shot.Life = Stat(run, Attr.Range) / Math.Max(1, speed);
            shot.Target = null;
        }
    }
}
