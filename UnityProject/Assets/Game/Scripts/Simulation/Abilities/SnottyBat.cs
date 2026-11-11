using System;
namespace ThornThrone.Simulation
{
    /// <summary>
    /// Snotty Bat (CSurvivorsPowerUp_Snotty). One companion per ability with the body of the enemy definition named
    /// by m_sSource (snotty): it appears at the hero in the first step after the ability is taken and lives to the end
    /// of the run. Its orbit point turns around the hero's current position at the distance m_flRotationDist with
    /// m_flRotationSpeedDeg counter-clockwise; the bat flies to it no faster than the body's
    /// m_flMoveSpeed. Every cooldown, if a targetable enemy touches the circle of Radius × (1 + AoEIncrease) around
    /// the bat, it strikes: an AbilityBurst, the physical hit once to each such enemy and a slow of SlowStrength for
    /// SlowDuration + DurationExtension on the survivors. Every step it collects the pickups within its own
    /// PickupRadius. While the hero is down it flies, but neither strikes nor collects. Mechanics §32 Р11–Р17
    /// (design decisions; owner's decisions 12.11.2026 for Р14, Р16).
    /// </summary>
    public sealed class SnottyBat : AbilityBase, ICompanionAbility
    {
        private const double DegToRad = Math.PI / 180;
        private readonly CircleTargets targets;
        private float cooldown;
        private Companion bat;
        private int batId;
        /// <summary>Enemy definition of the bat's body (m_sSource), or null when the data has none.</summary>
        public EnemyDef Body { get; }
        /// <summary>m_flRotationDist: distance of the orbit from the hero.</summary>
        public float OrbitDistance { get; }
        /// <summary>m_flRotationSpeedDeg: turn speed of the orbit point, degrees per second.</summary>
        public float OrbitSpeedDeg { get; }
        /// <summary>m_flMoveSpeed of the body.</summary>
        public float Speed => Body?.MoveSpeed ?? 0;
        public SnottyBat(OwnedPowerUp owned, Run run) : base(owned)
        {
            targets = new CircleTargets(run.Enemies.Length);
            OrbitDistance = DataField(run, "m_flRotationDist");
            OrbitSpeedDeg = DataField(run, "m_flRotationSpeedDeg");
            string source = (string)owned.Def.Raw?["m_sSource"];
            if (source != null && run.Data.Enemies.TryGetValue(source, out var body)) Body = body;
            else run.Diagnostics.Report($"ability:{owned.Def.Key}:m_sSource",
                $"Ability {owned.Def.Key} has no enemy body m_sSource «{source}»; the bat is not created");
        }
        public float Radius(Run run) => Area(run, Attr.Radius);
        public float SlowStrength(Run run) => Stat(run, Attr.SlowStrength);
        /// <summary>SlowDuration + DurationExtension (Mechanics §25 Р5).</summary>
        public float SlowDuration(Run run) => EffectDuration(run, Attr.SlowDuration);
        /// <summary>Own PickupRadius only, without the hero's (owner's decision, Р16).</summary>
        public float PickupRadius(Run run) => owned.Local[Attr.PickupRadius];
        public override void Tick(Run run, float dt)
        {
            if (!Alive() && !Find(run) && Body != null) Spawn(run); // a full pool: try again next step (Р12)
            cooldown -= dt;
            if (cooldown > dt * 0.5f || !Alive()) return; // half a step of float tolerance (Mechanics §30 Р2)
            float radius = Radius(run);
            Vec2 center = bat.Position;
            if (!CircleTargets.Any(run, center, radius)) return; // wait for a target, check every step (Р14)
            cooldown = Cooldown(run);
            Strike(run, center, radius);
        }
        public bool UpdateCompanion(Run run, Companion c, float dt)
        {
            bat = c;
            batId = c.SpawnId;
            c.Angle += OrbitSpeedDeg * DegToRad * dt;
            Vec2 orbit = run.HeroPosition + new Vec2((float)(Math.Cos(c.Angle) * OrbitDistance), (float)(Math.Sin(c.Angle) * OrbitDistance));
            Vec2 move = (orbit - c.Position).ClampLength(Speed * dt);
            c.Position += move;
            if (move.LengthSquared > 1e-8f) c.Direction = move.Normalized;
            if (!run.Downed) Collect(run, c); // Р17
            return true;
        }
        private bool Alive() => bat != null && bat.Active && bat.SpawnId == batId && bat.Source == owned;
        /// <summary>Adopts this ability's bat after the handler was recreated (one bat per ability, Р12).</summary>
        private bool Find(Run run)
        {
            var pool = run.Companions;
            for (int i = 0; i < pool.Length; i++)
            {
                if (!pool[i].Active || pool[i].Source != owned) continue;
                bat = pool[i];
                batId = bat.SpawnId;
                return true;
            }
            return false;
        }
        private void Spawn(Run run)
        {
            var c = run.SpawnCompanion(owned, run.HeroPosition);
            if (c == null) return;
            c.Body = Body;
            c.Angle = Math.Atan2(run.Facing.Y, run.Facing.X);
            c.Distance = OrbitDistance;
            c.Life = float.PositiveInfinity;
            bat = c;
            batId = c.SpawnId;
        }
        private void Strike(Run run, Vec2 center, float radius)
        {
            var hit = Hit(run);
            float strength = SlowStrength(run), duration = SlowDuration(run);
            run.EmitBurst(center, radius, owned.Def.Id);
            int count = targets.Collect(run, center, radius);
            for (int i = 0; i < count; i++)
            {
                var e = targets.Live(i);
                if (e == null || run.Finished) continue;
                int id = e.SpawnId;
                run.DealDamage(e, hit);
                if (run.Finished || !e.Active || e.SpawnId != id) continue;
                run.Slow(e, strength, duration);
            }
        }
        /// <summary>Every active pickup within the bat's own PickupRadius, collected as by the hero (Р16).</summary>
        private void Collect(Run run, Companion c)
        {
            float radius = PickupRadius(run);
            float reach = radius * radius;
            var pickups = run.Pickups;
            for (int i = 0; i < pickups.Length; i++)
            {
                var p = pickups[i];
                if (p.Active && Vec2.DistanceSquared(p.Position, c.Position) <= reach) run.CollectPickup(p);
            }
        }
    }
}
