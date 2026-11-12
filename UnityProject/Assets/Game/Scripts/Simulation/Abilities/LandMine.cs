namespace ThornThrone.Simulation
{
    /// <summary>
    /// Land Mine (CSurvivorsPowerUp_LandMine, Techies). Every cooldown, with or without enemies, plants a mine at the
    /// hero's position (a <see cref="GroundObject"/>) with its damage, radius Radius × (1 + AoEIncrease), ArmingTime,
    /// TriggerTime and LifeTime snapshotted. For ArmingTime seconds the mine ignores enemies; then the first
    /// targetable enemy whose circle touches the mine's circle starts the TriggerTime countdown, which is never
    /// cancelled. At its end every targetable enemy touching the circle takes the physical hit once, with an
    /// AbilityBurst first, and the mine is gone. A mine that has not started counting down vanishes after LifeTime
    /// without damage. Mechanics §31 Р4–Р10 (design decisions; owner's decisions 12.11.2026 for Р2, Р7, Р9).
    /// With Aghanim's Scepter the cooldown is × 0.8 and each plant rolls a third each: a mine, a stasis trap (damage
    /// × 0.5, stun 1 s; before the explosion the enemies touching m_flScepterVacuumRadius beyond the radius are pulled
    /// m_flScepterVacuumDistance towards the centre) or a radio mine (damage × 2, no countdown). Mechanics §34 Р21,
    /// owner's decision В4г.
    /// </summary>
    public sealed class LandMine : AbilityBase, IGroundObjectAbility
    {
        /// <summary><see cref="GroundObject.Variant"/> of the three kinds (Р21).</summary>
        public const int PlainMine = 0, StasisTrap = 1, RadioMine = 2;
        /// <summary>Cooldown factor with the scepter: «−20% перезарядки» of the patch notes (Д, text).</summary>
        public const float ScepterCooldownFactor = 0.8f;
        /// <summary>Damage factors of the trap and the radio mine, the trap's stun seconds (Р, owner's decision В4г).</summary>
        public const float TrapDamageFactor = 0.5f, RadioDamageFactor = 2, TrapStun = 1;
        private const float Epsilon = 1e-4f;
        private readonly CircleTargets targets;
        private float cooldown;

        /// <summary>m_flScepterVacuumRadius: reach of the trap's pull beyond the mine's radius.</summary>
        public float VacuumRadius { get; }
        /// <summary>m_flScepterVacuumDistance: distance of the pull.</summary>
        public float VacuumDistance { get; }

        /// <param name="run">Its enemy pool sizes the buffer; reports missing fields.</param>
        public LandMine(OwnedPowerUp owned, Run run) : base(owned)
        {
            targets = new CircleTargets(run.Enemies.Length);
            VacuumRadius = DataField(run, "m_flScepterVacuumRadius");
            VacuumDistance = DataField(run, "m_flScepterVacuumDistance");
        }
        /// <summary>Radius of the trigger and of the explosion (one radius in the data, Р7).</summary>
        public float Radius(Run run) => Area(run, Attr.Radius);
        public float LifeTime(Run run) => Stat(run, Attr.LifeTime);
        public float ArmingTime(Run run) => Stat(run, Attr.ArmingTime);
        public float TriggerTime(Run run) => Stat(run, Attr.TriggerTime);
        /// <summary>Seconds between plants: the cooldown, × 0.8 with the scepter (Р21).</summary>
        public float PlantCooldown(Run run) => Cooldown(run) * (owned.Scepter ? ScepterCooldownFactor : 1);
        public override void Tick(Run run, float dt)
        {
            cooldown -= dt;
            if (cooldown > dt * 0.5f) return; // half a step of float tolerance (Mechanics §30 Р2)
            cooldown = PlantCooldown(run); // a full pool skips the mine, the cooldown still starts (Р2)
            // Without the scepter no roll: the random sequence of such runs is unchanged.
            int variant = owned.Scepter ? run.Rng.Next(3) : PlainMine;
            var mine = run.SpawnGroundObject(owned, run.HeroPosition, variant);
            if (mine == null) return;
            var hit = Hit(run);
            float factor = variant == StasisTrap ? TrapDamageFactor : variant == RadioMine ? RadioDamageFactor : 1;
            mine.Phase = GroundPhase.Arming;
            mine.Radius = Radius(run);
            mine.ArmTime = ArmingTime(run);
            mine.Interval = variant == RadioMine ? 0 : TriggerTime(run);
            mine.Life = LifeTime(run);
            mine.Damage = hit.Amount * factor;
            mine.DamageType = hit.Type;
            mine.DamageFlags = hit.Flags;
            mine.DamageSource = hit.Source;
            mine.Stun = variant == StasisTrap ? TrapStun : 0;
        }
        public bool UpdateObject(Run run, GroundObject mine, float dt)
        {
            if (mine.Phase == GroundPhase.Triggered)
            {
                mine.Timer -= dt;
                if (mine.Timer > Epsilon) return true;
                Explode(run, mine);
                return false;
            }
            // Arming or armed: LifeTime ends the mine without damage (Р9).
            if (mine.Age >= mine.Life - Epsilon) return false;
            if (mine.Phase == GroundPhase.Arming)
            {
                if (mine.Age < mine.ArmTime - Epsilon) return true;
                mine.Phase = GroundPhase.Armed;
            }
            if (!CircleTargets.Any(run, mine.Position, mine.Radius)) return true;
            if (mine.Interval <= Epsilon)
            {
                Explode(run, mine); // the radio mine: no countdown (Р21)
                return false;
            }
            mine.Phase = GroundPhase.Triggered;
            mine.Timer = mine.Interval;
            return true;
        }
        private void Explode(Run run, GroundObject mine)
        {
            if (mine.Variant == StasisTrap) Pull(run, mine);
            run.EmitBurst(mine.Position, mine.Radius, owned.Def.Id);
            int count = targets.Collect(run, mine.Position, mine.Radius);
            var hit = mine.Hit;
            for (int i = 0; i < count; i++)
            {
                var e = targets.Live(i);
                if (e == null || run.Finished) continue;
                int id = e.SpawnId;
                run.DealDamage(e, hit);
                if (mine.Stun > 0 && !run.Finished && e.Active && e.SpawnId == id) run.Stun(e, mine.Stun);
            }
        }
        /// <summary>The trap's vacuum: enemies touching radius + m_flScepterVacuumRadius move towards the centre (Р21).</summary>
        private void Pull(Run run, GroundObject mine)
        {
            int count = targets.Collect(run, mine.Position, mine.Radius + VacuumRadius);
            for (int i = 0; i < count; i++)
            {
                var e = targets.Live(i);
                if (e == null) continue;
                Vec2 to = mine.Position - e.Position;
                if (to.LengthSquared > 1e-8f) run.Knockback(e, to, System.Math.Min(VacuumDistance, (float)System.Math.Sqrt(to.LengthSquared)));
            }
        }
    }
}
