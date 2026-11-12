using System;
namespace ThornThrone.Simulation
{
    /// <summary>
    /// Pool of companions (Spirits' spirits, the Snotty Bat), Mechanics §32 Р1–Р3. One pool of
    /// <see cref="Rules.MaxCompanions"/> slots per run, shared by every ability; a full pool skips new companions,
    /// reports «pool:companions» once and never removes the old ones. Companions are updated in the step after the
    /// ground objects, also while the hero is down, by the ability that made them.
    /// </summary>
    public sealed partial class Run
    {
        public readonly Companion[] Companions;
        public int ActiveCompanions { get; private set; }
        private readonly int[] freeCompanions;
        private int freeCompanionCount;
        /// <summary>A new companion of <paramref name="source"/> at <paramref name="position"/>, or null when the pool is full (Р2).</summary>
        public Companion SpawnCompanion(OwnedPowerUp source, Vec2 position)
        {
            if (freeCompanionCount == 0)
            {
                if (FirstTime(Companions))
                    Diagnostics.Report("pool:companions", $"Companion pool limit {Rules.MaxCompanions} reached; new companions skipped");
                return null;
            }
            var c = Companions[freeCompanions[--freeCompanionCount]];
            c.Active = true;
            c.SpawnId = ++spawnSerial;
            c.Source = source;
            c.Body = null;
            c.Position = c.PreviousPosition = position;
            c.Direction = Facing;
            c.Angle = 0;
            c.Distance = c.Age = c.Life = c.Timer = c.Radius = 0;
            c.Born = Time;
            c.Damage = 0;
            c.DamageType = DamageType.Pure;
            c.DamageFlags = DamageFlags.None;
            c.DamageSource = source?.Stats ?? DamageStats.Other;
            Array.Clear(c.HitMarks, 0, c.HitMarks.Length);
            ActiveCompanions++;
            Emit(SimEventKind.CompanionSpawned, c, position);
            return c;
        }
        private void RetireCompanion(Companion c)
        {
            c.Active = false;
            freeCompanions[freeCompanionCount++] = c.Slot;
            ActiveCompanions--;
            Emit(SimEventKind.CompanionEnded, c, c.Position);
            c.Source = null;
            c.Body = null;
        }
        /// <summary>
        /// Advances every live companion except those made in this step; a companion whose ability is gone (no
        /// companion handler) ends at once (Р3).
        /// </summary>
        private void UpdateCompanions(float dt)
        {
            if (ActiveCompanions == 0 || Finished) return;
            for (int i = 0; i < Companions.Length; i++)
            {
                var c = Companions[i];
                if (!c.Active || c.Born == Time) continue;
                c.Age += dt;
                if (!(c.Source?.Ability is ICompanionAbility handler) || !handler.UpdateCompanion(this, c, dt))
                    RetireCompanion(c);
                if (Finished) return;
            }
        }
        /// <summary>Render snapshot before the step (Mechanics §17).</summary>
        private void SnapshotCompanions()
        {
            for (int i = 0; i < Companions.Length; i++) if (Companions[i].Active) Companions[i].PreviousPosition = Companions[i].Position;
        }
        /// <summary>Companions created during the step (possibly in a reused slot) have no previous position.</summary>
        private void ResetNewCompanions(int serial)
        {
            for (int i = 0; i < Companions.Length; i++)
            {
                var c = Companions[i];
                if (c.Active && c.SpawnId > serial) c.PreviousPosition = c.Position;
            }
        }
        /// <summary>Collects a pickup for the hero wherever it lies (Snotty Bat, §32 Р16): the hero's own collection.</summary>
        internal void CollectPickup(Pickup p)
        {
            if (p.Active && !fallen) Collect(p); // nothing is collected in the rest of a fatal step (Mechanics §37 Р3)
        }
    }
}
