using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Arcane Bolt (CSurvivorsPowerUp_ArcaneBolt). Every cooldown, if an enemy is in range, fires a volley of
    /// TargetCount bolts at ProjectileAttackInterval, one per closest target in turn. A bolt homes on its
    /// target until it hits something, pierces 1 + ProjectileHitCount enemies and knocks them back.
    /// Damage is snapshotted at launch. With Aghanim's Scepter every moment of the volley fires two bolts, at the
    /// next two closest targets in turn (Mechanics §34 Р11).
    /// </summary>
    public sealed class ArcaneBolt : AbilityBase
    {
        private float cooldown, volleyTimer;
        private int volleyRemaining, volleyIndex;

        public ArcaneBolt(OwnedPowerUp owned) : base(owned) { }

        public int Volley(Run run) => Math.Max(1, (int)Stat(run, Attr.TargetCount));
        /// <summary>Bolts at one moment of the volley: 2 with the scepter («Удваивает число снарядов»), else 1.</summary>
        public int BoltsPerShot => owned.Scepter ? 2 : 1;

        public override void Tick(Run run, float dt)
        {
            cooldown -= dt;
            volleyTimer -= dt;
            float range = Stat(run, Attr.Range);
            if (cooldown <= 0 && volleyRemaining == 0 && run.CountTargets(run.HeroPosition, range) > 0)
            {
                volleyRemaining = Volley(run);
                volleyIndex = 0;
                volleyTimer = 0;
                cooldown = Cooldown(run);
            }
            if (volleyRemaining <= 0 || volleyTimer > 0) return;
            int count = run.ClosestTargets(run.HeroPosition, range);
            int bolts = BoltsPerShot;
            // Fire reads only the targets, never the grid, so the shared TargetBuffer stays valid between the bolts.
            if (count > 0) for (int b = 0; b < bolts; b++) Fire(run, run.TargetBuffer[(volleyIndex * bolts + b) % count]);
            volleyIndex++;
            volleyRemaining--;
            volleyTimer = Math.Max(0.02f, Stat(run, Attr.ProjectileAttackInterval));
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
            shot.Knockback = KnockbackDistance(run);
            shot.RemainingHits = 1 + (int)Stat(run, Attr.ProjectileHitCount);
            shot.Life = Math.Max(3, Stat(run, Attr.Range) / Math.Max(1, speed) + 1);
            shot.Target = target;
            shot.TargetSpawnId = target.SpawnId;
        }
    }
}
