namespace ThornThrone.Simulation
{
    /// <summary>
    /// Events for presentation. An event about a pooled entity carries its SpawnId (and, for enemies, the definition
    /// and direction) because the slot can be taken by a new entity before presentation reads the events
    /// (.kiro/specs/corpse-enemy-slot-reuse).
    /// </summary>
    public sealed partial class Run
    {
        private void Emit(SimEventKind kind, int slot, Vec2 position, float value)
        {
            if (EventCount == Events.Length) return;
            Events[EventCount++] = new SimEvent { Kind = kind, Slot = slot, Position = position, Value = value };
        }

        /// <summary>Event about an enemy: carries its SpawnId, definition and direction, which stay valid after the slot is reused.</summary>
        private void Emit(SimEventKind kind, Enemy enemy, Vec2 position, float value)
        {
            if (EventCount == Events.Length) return;
            Events[EventCount++] = new SimEvent
            {
                Kind = kind, Slot = enemy.Slot, Position = position, Value = value,
                SpawnId = enemy.SpawnId, EnemyDef = enemy.Def, Direction = enemy.Direction,
            };
        }

        /// <summary>Damage to an enemy: the enemy event plus the damage type and the critical flag (Mechanics §24).</summary>
        private void Emit(SimEventKind kind, Enemy enemy, Vec2 position, float value, DamageType type, bool critical)
        {
            if (EventCount == Events.Length) return;
            Events[EventCount++] = new SimEvent
            {
                Kind = kind, Slot = enemy.Slot, Position = position, Value = value,
                SpawnId = enemy.SpawnId, EnemyDef = enemy.Def, Direction = enemy.Direction,
                DamageType = type, Critical = critical,
            };
        }

        /// <summary>Area hit of an ability (<see cref="SimEventKind.AbilityStrike"/>): not about a pooled entity, SpawnId 0.</summary>
        internal void EmitStrike(Vec2 center, Vec2 direction, Vec2 extent, float value)
        {
            if (EventCount == Events.Length) return;
            Events[EventCount++] = new SimEvent
            {
                Kind = SimEventKind.AbilityStrike, Slot = -1, Position = center, Value = value,
                Direction = direction, Extent = extent,
            };
        }

        private void Emit(SimEventKind kind, Projectile shot, Vec2 position, float value)
        {
            if (EventCount == Events.Length) return;
            Events[EventCount++] = new SimEvent { Kind = kind, Slot = shot.Slot, Position = position, Value = value, SpawnId = shot.SpawnId };
        }

        private void Emit(SimEventKind kind, Pickup pickup, Vec2 position, float value)
        {
            if (EventCount == Events.Length) return;
            Events[EventCount++] = new SimEvent { Kind = kind, Slot = pickup.Slot, Position = position, Value = value, SpawnId = pickup.SpawnId };
        }
    }
}
