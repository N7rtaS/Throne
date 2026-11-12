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
    /// (design decisions; owner's decisions 12.11.2026 for Р14, Р16). With Aghanim's Scepter there are two bats: the
    /// second appears at the hero with its orbit point opposite the first one's, each bat strikes on its own cooldown
    /// and both collect pickups (Mechanics §34 Р25).
    /// </summary>
    public sealed class SnottyBat : AbilityBase, ICompanionAbility
    {
        private const double DegToRad = Math.PI / 180;
        /// <summary>Bats with the scepter: «Теперь у вас будут 2 летучие мыши» (Д, text).</summary>
        public const int ScepterBats = 2;
        private readonly CircleTargets targets;
        private readonly float[] cooldown = new float[ScepterBats];
        private readonly Companion[] bats = new Companion[ScepterBats];
        private readonly int[] batIds = new int[ScepterBats];
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
        /// <summary>Number of bats: 1, 2 with the scepter (Р25).</summary>
        public int BatCount => owned.Scepter ? ScepterBats : 1;
        public override void Tick(Run run, float dt)
        {
            int count = BatCount;
            for (int k = 0; k < count; k++)
            {
                if (!Alive(k) && !Find(run, k) && Body != null) Spawn(run, k); // a full pool: try again next step (Р12)
                cooldown[k] -= dt;
                if (cooldown[k] > dt * 0.5f || !Alive(k)) continue; // half a step of float tolerance (Mechanics §30 Р2)
                float radius = Radius(run);
                Vec2 center = bats[k].Position;
                if (!CircleTargets.Any(run, center, radius)) continue; // wait for a target, check every step (Р14)
                cooldown[k] = Cooldown(run);
                Strike(run, center, radius);
                if (run.Finished) return;
            }
        }
        public bool UpdateCompanion(Run run, Companion c, float dt)
        {
            Adopt(c);
            c.Angle += OrbitSpeedDeg * DegToRad * dt;
            Vec2 orbit = run.HeroPosition + new Vec2((float)(Math.Cos(c.Angle) * OrbitDistance), (float)(Math.Sin(c.Angle) * OrbitDistance));
            Vec2 move = (orbit - c.Position).ClampLength(Speed * dt);
            c.Position += move;
            if (move.LengthSquared > 1e-8f) c.Direction = move.Normalized;
            if (!run.Downed) Collect(run, c); // Р17
            return true;
        }
        private bool Alive(int k)
        {
            var bat = bats[k];
            return bat != null && bat.Active && bat.SpawnId == batIds[k] && bat.Source == owned;
        }
        /// <summary>Remembers a bat of this ability in a free place (a recreated handler meets its bats here or in <see cref="Find"/>).</summary>
        private void Adopt(Companion c)
        {
            for (int k = 0; k < ScepterBats; k++) if (Alive(k) && bats[k] == c) return;
            for (int k = 0; k < ScepterBats; k++)
            {
                if (Alive(k)) continue;
                bats[k] = c;
                batIds[k] = c.SpawnId;
                return;
            }
        }
        /// <summary>Adopts a bat of this ability not yet known to the handler (one bat per place, Р12).</summary>
        private bool Find(Run run, int k)
        {
            var pool = run.Companions;
            for (int i = 0; i < pool.Length; i++)
            {
                var c = pool[i];
                if (!c.Active || c.Source != owned || Known(c)) continue;
                bats[k] = c;
                batIds[k] = c.SpawnId;
                return true;
            }
            return false;
        }
        private bool Known(Companion c)
        {
            for (int k = 0; k < ScepterBats; k++) if (Alive(k) && bats[k] == c) return true;
            return false;
        }
        /// <summary>A new bat at the hero; with another bat alive its orbit point is opposite that bat's (Р25).</summary>
        private void Spawn(Run run, int k)
        {
            var c = run.SpawnCompanion(owned, run.HeroPosition);
            if (c == null) return;
            int other = k == 0 ? 1 : 0;
            c.Body = Body;
            c.Angle = Alive(other) ? bats[other].Angle + Math.PI : Math.Atan2(run.Facing.Y, run.Facing.X);
            c.Distance = OrbitDistance;
            c.Life = float.PositiveInfinity;
            bats[k] = c;
            batIds[k] = c.SpawnId;
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
