namespace ThornThrone.Simulation
{
    /// <summary>
    /// Pool of ground objects (mines, lava globs and puddles, Rot's clouds), Mechanics §31 Р1–Р3. One pool of
    /// <see cref="Rules.MaxGroundObjects"/> slots per run, shared by every ability; a full pool skips new objects,
    /// reports «pool:ground» once and never removes the old ones. Objects are updated in the step after projectiles,
    /// also while the hero is down, by the ability that made them.
    /// </summary>
    public sealed partial class Run
    {
        public readonly GroundObject[] GroundObjects;
        public int ActiveGroundObjects { get; private set; }
        private readonly int[] freeGroundObjects;
        private int freeGroundObjectCount;
        /// <summary>
        /// A new ground object of <paramref name="source"/> at <paramref name="position"/>, or null when the pool is full (Р2).
        /// <paramref name="variant"/> is set before the spawn event, which carries it (Mechanics §34 Р21).
        /// </summary>
        public GroundObject SpawnGroundObject(OwnedPowerUp source, Vec2 position, int variant = 0)
        {
            if (freeGroundObjectCount == 0)
            {
                if (FirstTime(GroundObjects))
                    Diagnostics.Report("pool:ground", $"Ground object pool limit {Rules.MaxGroundObjects} reached; new objects skipped");
                return null;
            }
            var obj = GroundObjects[freeGroundObjects[--freeGroundObjectCount]];
            obj.Active = true;
            obj.SpawnId = ++spawnSerial;
            obj.Source = source;
            obj.Phase = GroundPhase.Arming;
            obj.Position = obj.Origin = position;
            obj.Radius = obj.Age = obj.Timer = obj.FlightTime = obj.Life = obj.ArmTime = obj.Interval = 0;
            obj.TicksLeft = 0;
            obj.Variant = variant;
            obj.Stun = obj.FreezeStrength = obj.FreezeTime = 0;
            obj.Born = Time;
            obj.Damage = 0;
            obj.DamageType = DamageType.Pure;
            obj.DamageFlags = DamageFlags.None;
            obj.DamageSource = source?.Stats ?? DamageStats.Other;
            ActiveGroundObjects++;
            Emit(SimEventKind.GroundObjectSpawned, obj, position);
            return obj;
        }
        private void RetireGroundObject(GroundObject obj)
        {
            obj.Active = false;
            freeGroundObjects[freeGroundObjectCount++] = obj.Slot;
            ActiveGroundObjects--;
            Emit(SimEventKind.GroundObjectEnded, obj, obj.Position);
            obj.Source = null;
        }
        /// <summary>
        /// Advances every live object except those made in this step; an object whose ability is gone (no ground
        /// handler) ends at once.
        /// </summary>
        private void UpdateGroundObjects(float dt)
        {
            if (ActiveGroundObjects == 0 || Finished) return;
            for (int i = 0; i < GroundObjects.Length; i++)
            {
                var obj = GroundObjects[i];
                if (!obj.Active || obj.Born == Time) continue;
                obj.Age += dt;
                if (!(obj.Source?.Ability is IGroundObjectAbility handler) || !handler.UpdateObject(this, obj, dt))
                    RetireGroundObject(obj);
                if (Finished) return;
            }
        }
    }
}
