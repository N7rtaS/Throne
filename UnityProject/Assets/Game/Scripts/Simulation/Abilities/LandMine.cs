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
    /// </summary>
    public sealed class LandMine : AbilityBase, IGroundObjectAbility
    {
        private const float Epsilon = 1e-4f;
        private readonly CircleTargets targets;
        private float cooldown;
        /// <param name="maxEnemies">Size of the enemy pool.</param>
        public LandMine(OwnedPowerUp owned, int maxEnemies) : base(owned) { targets = new CircleTargets(maxEnemies); }
        /// <summary>Radius of the trigger and of the explosion (one radius in the data, Р7).</summary>
        public float Radius(Run run) => Area(run, Attr.Radius);
        public float LifeTime(Run run) => Stat(run, Attr.LifeTime);
        public float ArmingTime(Run run) => Stat(run, Attr.ArmingTime);
        public float TriggerTime(Run run) => Stat(run, Attr.TriggerTime);
        public override void Tick(Run run, float dt)
        {
            cooldown -= dt;
            if (cooldown > dt * 0.5f) return; // half a step of float tolerance (Mechanics §30 Р2)
            cooldown = Cooldown(run); // a full pool skips the mine, the cooldown still starts (Р2)
            var mine = run.SpawnGroundObject(owned, run.HeroPosition);
            if (mine == null) return;
            var hit = Hit(run);
            mine.Phase = GroundPhase.Arming;
            mine.Radius = Radius(run);
            mine.ArmTime = ArmingTime(run);
            mine.Interval = TriggerTime(run);
            mine.Life = LifeTime(run);
            mine.Damage = hit.Amount;
            mine.DamageType = hit.Type;
            mine.DamageFlags = hit.Flags;
            mine.DamageSource = hit.Source;
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
            mine.Phase = GroundPhase.Triggered;
            mine.Timer = mine.Interval;
            return true;
        }
        private void Explode(Run run, GroundObject mine)
        {
            run.EmitBurst(mine.Position, mine.Radius, owned.Def.Id);
            int count = targets.Collect(run, mine.Position, mine.Radius);
            var hit = mine.Hit;
            for (int i = 0; i < count; i++)
            {
                var e = targets.Live(i);
                if (e == null || run.Finished) continue;
                run.DealDamage(e, hit);
            }
        }
    }
}
