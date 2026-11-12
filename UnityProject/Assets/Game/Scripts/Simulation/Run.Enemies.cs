using System;
using System.Collections.Generic;

namespace ThornThrone.Simulation
{
    /// <summary>Enemy movement, separation, contact and attacks (Mechanics §2, §14).</summary>
    public sealed partial class Run
    {
        // ------------------------------------------------------------------ enemies

        private void RebuildGrid()
        {
            grid.Clear();
            for (int i = 0; i < Enemies.Length; i++)
                if (Enemies[i].Active) grid.Insert(i, Enemies[i].Position);
        }

        private void UpdateEnemies(float dt)
        {
            float smoothing = Math.Min(1, Mode.SeparationInterpolationSpeed * dt);
            for (int i = 0; i < Enemies.Length; i++)
            {
                var enemy = Enemies[i];
                if (!enemy.Active) continue;
                int spawnId = enemy.SpawnId;
                enemy.Age += dt;
                var def = enemy.Def;
                Vec2 toHero = HeroPosition - enemy.Position;
                Vec2 desired;
                // Stunned: no own movement and attack timers stand still; separation and contact still apply
                // (interpretation, Mechanics §15).
                bool stunned = enemy.Stunned;
                if (stunned) desired = Vec2.Zero;
                else switch (def.Movement)
                {
                    case MovementBehavior.Tracking:
                        // Ground walkers go straight while the line to the target is clear, otherwise follow
                        // the flow field around pits; straight line when the field has no step either.
                        Vec2 path;
                        if (WalksOnGround(enemy))
                        {
                            if ((enemy.Slot + stepIndex) % 3 == 0) enemy.ClearPath = Flow.Clear(enemy.Position, ChaseTarget);
                            if (enemy.ClearPath || !Flow.Direction(enemy.Position, out path))
                                path = (ChaseTarget - enemy.Position).Normalized;
                        }
                        else path = (ChaseTarget - enemy.Position).Normalized;
                        // m_flTurnRate: the course turns towards the path at a limited rate and the enemy walks along
                        // it; without the field the course follows the path at once (Mechanics §36 Р1).
                        if (def.HasTurnRate) enemy.Direction = TurnTowards(enemy.Direction, path, enemy.TurnRate * dt);
                        else if (path.LengthSquared > 1e-8f) enemy.Direction = path;
                        desired = (def.HasTurnRate ? enemy.Direction : path) * enemy.Speed;
                        break;
                    case MovementBehavior.Stationary:
                        // A turret with a turn rate (skeleton_archer) turns its course towards the hero; 0 keeps it.
                        if (def.HasTurnRate && enemy.TurnRate > 0) enemy.Direction = TurnTowards(enemy.Direction, toHero.Normalized, enemy.TurnRate * dt);
                        desired = Vec2.Zero;
                        break;
                    case MovementBehavior.Linear: desired = enemy.Direction * enemy.Speed; break;
                    case MovementBehavior.LinearSinWave:
                    {
                        double angle = def.SinMovementAngle * Math.PI / 180
                            * Math.Sin(enemy.Age * Math.PI * 2 * def.SinMovementPeriodMultiplier);
                        float c = (float)Math.Cos(angle), s = (float)Math.Sin(angle);
                        var d = enemy.Direction;
                        desired = new Vec2(d.X * c - d.Y * s, d.X * s + d.Y * c) * enemy.Speed;
                        break;
                    }
                    default: desired = Vec2.Zero; break;
                }
                desired *= enemy.Status.MoveFactor; // slow and freeze (Mechanics §24)

                if (def.Movement != MovementBehavior.Stationary)
                {
                    // Separation: soft push from neighbours within the layer distance (Mechanics §2). Each neighbour's
                    // part is weighted by 2·m_other / (m_own + m_other): equal masses push as before, a light enemy
                    // hardly moves a heavy one (Mechanics §36 Р3).
                    Vec2 push = Vec2.Zero, plain = Vec2.Zero;
                    using var near = grid.Query(enemy.Position, enemy.SeparationDistance);
                    int used = 0;
                    for (int n = 0; n < near.Count && used < 16; n++)
                    {
                        var other = Enemies[near[n]];
                        if (other == enemy) continue;
                        Vec2 away = enemy.Position - other.Position;
                        float distanceSquared = away.LengthSquared;
                        float limit = enemy.SeparationDistance;
                        if (distanceSquared >= limit * limit || distanceSquared < 1e-4f) continue;
                        float distance = (float)Math.Sqrt(distanceSquared);
                        Vec2 part = away * ((1 - distance / limit) / distance);
                        plain += part;
                        push += part * (2 * other.Mass / (enemy.Mass + other.Mass));
                        used++;
                    }
                    Vec2 target = plain.LengthSquared > 1e-6f ? push * (enemy.Speed * Mode.SeparationVelocityInfluence / plain.Length) : Vec2.Zero;
                    enemy.SeparationVelocity += (target - enemy.SeparationVelocity) * smoothing;
                    enemy.Position = MoveEnemyTo(enemy, enemy.Position + (desired + enemy.SeparationVelocity) * dt);
                }

                // Contact with the hero.
                float reach = enemy.Radius + Rules.HeroRadius;
                if (enemy.Hostile && enemy.TouchDamage > 0 && Vec2.DistanceSquared(enemy.Position, HeroPosition) < reach * reach)
                {
                    if (def.DieOnTouch) { RetireEnemy(enemy, SimEventKind.EnemyDespawned); continue; }
                    if (contactTimer <= 0 && !(Rules.DashGrantsInvulnerability && Dashing))
                    {
                        contactTimer = Rules.ContactDamageInterval;
                        if (DamageHero(enemy.TouchDamage, enemy)) SlowHeroByTouch(); // sticky_situation (Mechanics §36 Р2)
                        if (Finished) return;
                        if (!enemy.Active || enemy.SpawnId != spawnId) continue; // killed by reflection
                    }
                }

                if (def.Attacks.Length > 0 && !stunned) { UpdateEnemyAttack(enemy, dt); if (Finished) return; }
                if (!enemy.Active || enemy.SpawnId != spawnId) continue;
                if (def.AbsorbRadius > 0) Absorb(enemy);

                UpdateDespawn(enemy, toHero, dt); // Run.Spawning.cs
            }
        }

        /// <summary>
        /// Turns a unit course towards <paramref name="to"/> by at most <paramref name="maxAngle"/> radians
        /// (Mechanics §36 Р1); no allocation, two trigonometric calls only when the turn is limited.
        /// </summary>
        internal static Vec2 TurnTowards(Vec2 from, Vec2 to, float maxAngle)
        {
            if (to.LengthSquared < 1e-8f) return from;
            if (from.LengthSquared < 1e-8f) return to.Normalized;
            double angle = Math.Atan2(from.X * to.Y - from.Y * to.X, Vec2.Dot(from, to));
            if (Math.Abs(angle) <= maxAngle) return to.Normalized;
            double turn = angle > 0 ? maxAngle : -maxAngle;
            float c = (float)Math.Cos(turn), s = (float)Math.Sin(turn);
            return new Vec2(from.X * c - from.Y * s, from.X * s + from.Y * c).Normalized;
        }

        public void Knockback(Enemy enemy, Vec2 direction, float distance)
        {
            if (!enemy.Active || enemy.Def.Movement == MovementBehavior.Stationary) return;
            enemy.Position = MoveEnemyTo(enemy, enemy.Position + direction.Normalized * (distance * enemy.KnockbackScale));
        }


        /// <summary>
        /// m_vecAttacks[0]: when the hero is within Range and the cooldown is over, wind up for AttackPoint seconds,
        /// then fire a straight projectile at the hero's position (Speed &gt; 0, flies MaxDistance) or hit everything
        /// within Radius around the enemy (Speed 0).
        /// </summary>
        private void UpdateEnemyAttack(Enemy enemy, float dt)
        {
            var attack = enemy.Def.Attacks[0];
            enemy.AttackCooldown -= dt;
            if (enemy.AttackWindup >= 0)
            {
                enemy.AttackWindup -= dt;
                if (enemy.AttackWindup >= 0) return;
                enemy.AttackWindup = -1;
                Emit(SimEventKind.EnemyAttacked, enemy, enemy.Position, attack.Speed);
                if (attack.Speed <= 0)
                {
                    float reach = attack.Radius + Rules.HeroRadius;
                    if (Vec2.DistanceSquared(enemy.Position, HeroPosition) < reach * reach && !(Rules.DashGrantsInvulnerability && Dashing))
                        DamageHero(attack.Damage * (1 + Difficulty.EnemyDamageMultiplier), enemy);
                    return;
                }
                var shot = SpawnProjectile(null);
                if (shot == null) return;
                shot.Hostile = true;
                // m_flAttackOffsetForward: the shot leaves from in front of the enemy, towards the hero.
                var aim = (HeroPosition - enemy.Position).Normalized;
                shot.Position = enemy.Position + aim * attack.OffsetForward;
                shot.Speed = attack.Speed;
                shot.Velocity = aim * attack.Speed;
                shot.Radius = attack.Radius;
                shot.Damage = attack.Damage * (1 + Difficulty.EnemyDamageMultiplier);
                shot.RemainingHits = 1;
                shot.Life = Math.Max(attack.MaxDistance, attack.Range) / attack.Speed;
                return;
            }
            if (enemy.AttackCooldown > 0) return;
            if (Vec2.DistanceSquared(enemy.Position, HeroPosition) > attack.Range * attack.Range) return;
            enemy.AttackCooldown = attack.Cooldown;
            enemy.AttackWindup = attack.AttackPoint;
        }

        /// <summary>
        /// Absorber: takes in non-elite enemies within m_flAbsorbRadius (plus both radii), gains
        /// m_flPercentHealthAbsorbed of their health and m_flModelScaleIncreasePerAbsorb of size up to m_flMaxModelScale.
        /// </summary>
        private void Absorb(Enemy absorber)
        {
            var def = absorber.Def;
            float range = def.AbsorbRadius + absorber.Radius;
            using var near = grid.Query(absorber.Position, range + 150);
            for (int i = 0; i < near.Count; i++)
            {
                var other = Enemies[near[i]];
                if (!other.Active || other == absorber || other.Def.IsElite || other.Def.IsDestructible || !other.Hostile || other.Def.IsBoss) continue;
                float reach = range + other.Radius;
                if (Vec2.DistanceSquared(other.Position, absorber.Position) > reach * reach) continue;
                float gain = other.Health * def.PercentHealthAbsorbed;
                absorber.Health += gain;
                absorber.MaxHealth += gain;
                float scale = Math.Min(absorber.Scale + def.ModelScaleIncreasePerAbsorb, def.MaxModelScale > 0 ? def.MaxModelScale : float.MaxValue);
                absorber.Radius *= scale / absorber.Scale;
                absorber.Scale = scale;
                RetireEnemy(other, SimEventKind.EnemyDespawned);
            }
        }
    }
}
