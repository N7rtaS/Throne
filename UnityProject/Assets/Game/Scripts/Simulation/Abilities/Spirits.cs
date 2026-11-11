using System;
namespace ThornThrone.Simulation
{
    /// <summary>
    /// Spirits (CSurvivorsPowerUp_Spirits, Io). Every cooldown, with or without enemies, summons TargetCount spirits
    /// (<see cref="Companion"/>) evenly on a circle of Range around the hero, the first one in the hero's facing. The
    /// spirits turn around the hero's current position at <see cref="RotationSpeedDeg"/> counter-clockwise; a spirit
    /// deals the magical hit to each targetable enemy whose circle touches its own (<see cref="SpiritRadius"/>) once
    /// in its life. After Duration + DurationExtension seconds it explodes: an AbilityBurst and the same hit once to
    /// every targetable enemy touching Radius × (1 + AoEIncrease) around it. Damage, radii and life are snapshotted at
    /// the summon. Mechanics §32 Р4–Р10 (design decisions; owner's decisions 12.11.2026 for Р5, Р7, Р8).
    /// </summary>
    public sealed class Spirits : AbilityBase, ICompanionAbility
    {
        /// <summary>Turn speed of the spirits, degrees per second (not in the data, Р7).</summary>
        public const float RotationSpeedDeg = 180;
        /// <summary>Radius of a spirit's touch (not in the data, as Magic Missile's projectile, Р8).</summary>
        public const float SpiritRadius = 50;
        private const float Epsilon = 1e-4f;
        private const double TurnRate = RotationSpeedDeg * Math.PI / 180;
        private readonly CircleTargets targets;
        private float cooldown;
        /// <param name="maxEnemies">Size of the enemy pool.</param>
        public Spirits(OwnedPowerUp owned, int maxEnemies) : base(owned) { targets = new CircleTargets(maxEnemies); }
        public int Count(Run run) => Math.Max(1, (int)Stat(run, Attr.TargetCount));
        /// <summary>Own Duration plus DurationExtension (DurationEffect 1, Р5).</summary>
        public float Duration(Run run) => EffectDuration(run, Attr.Duration);
        /// <summary>Radius of the spirits' circle: Range, a distance without AoEIncrease (Р6).</summary>
        public float Distance(Run run) => Stat(run, Attr.Range);
        /// <summary>Radius of the explosion (Р9).</summary>
        public float Radius(Run run) => Area(run, Attr.Radius);
        public override void Tick(Run run, float dt)
        {
            cooldown -= dt;
            if (cooldown > dt * 0.5f) return; // half a step of float tolerance (Mechanics §30 Р2)
            cooldown = Cooldown(run); // a full pool skips spirits, the cooldown still starts (Р2)
            int count = Count(run);
            var hit = Hit(run);
            float life = Duration(run), distance = Distance(run), radius = Radius(run);
            double start = Math.Atan2(run.Facing.Y, run.Facing.X);
            for (int k = 0; k < count; k++)
            {
                double angle = start + 2 * Math.PI * k / count;
                var spirit = run.SpawnCompanion(owned, run.HeroPosition + Offset(angle, distance));
                if (spirit == null) continue;
                spirit.Angle = angle;
                spirit.Distance = distance;
                spirit.Direction = Tangent(angle);
                spirit.Life = life;
                spirit.Radius = radius;
                spirit.Damage = hit.Amount;
                spirit.DamageType = hit.Type;
                spirit.DamageFlags = hit.Flags;
                spirit.DamageSource = hit.Source;
            }
        }
        public bool UpdateCompanion(Run run, Companion spirit, float dt)
        {
            if (spirit.Age >= spirit.Life - Epsilon)
            {
                Explode(run, spirit);
                return false;
            }
            spirit.Angle += TurnRate * dt;
            spirit.Position = run.HeroPosition + Offset(spirit.Angle, spirit.Distance);
            spirit.Direction = Tangent(spirit.Angle);
            Touch(run, spirit);
            return true;
        }
        private static Vec2 Offset(double angle, float distance) =>
            new Vec2((float)(Math.Cos(angle) * distance), (float)(Math.Sin(angle) * distance));
        private static Vec2 Tangent(double angle) => new Vec2((float)-Math.Sin(angle), (float)Math.Cos(angle));
        /// <summary>One hit per spirit and enemy (HitMarks by slot and SpawnId, Р8).</summary>
        private void Touch(Run run, Companion spirit)
        {
            int count = targets.Collect(run, spirit.Position, SpiritRadius);
            var hit = spirit.Hit;
            for (int i = 0; i < count; i++)
            {
                var e = targets.Live(i);
                if (e == null || run.Finished) continue;
                if (spirit.HitMarks[e.Slot] == e.SpawnId) continue;
                spirit.HitMarks[e.Slot] = e.SpawnId;
                run.DealDamage(e, hit);
            }
        }
        private void Explode(Run run, Companion spirit)
        {
            run.EmitBurst(spirit.Position, spirit.Radius, owned.Def.Id);
            int count = targets.Collect(run, spirit.Position, spirit.Radius);
            var hit = spirit.Hit;
            for (int i = 0; i < count; i++)
            {
                var e = targets.Live(i);
                if (e == null || run.Finished) continue;
                run.DealDamage(e, hit);
            }
        }
    }
}
