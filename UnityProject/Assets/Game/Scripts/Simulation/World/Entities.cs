namespace ThornThrone.Simulation
{
    /// <summary>
    /// Pooled enemy. <see cref="Slot"/> is stable for the lifetime of the run (presentation keys views by it);
    /// <see cref="SpawnId"/> changes on every reuse so stale references can be detected.
    /// </summary>
    public sealed class Enemy
    {
        public int Slot, SpawnId;
        public bool Active;
        public EnemyDef Def;
        public Vec2 Position, Direction, SeparationVelocity;
        public float Health, MaxHealth, Speed, TouchDamage, Radius, SeparationDistance, KnockbackScale;
        public float Age, OutOfRangeTime;
        /// <summary>Spawn event that owns this enemy (for its minimum count), or -1.</summary>
        public int EventIndex;
        /// <summary>Elite room that spawned this enemy (inherited by split units), or -1.</summary>
        public int EliteRoom;
        /// <summary>Boss portal (hazard index) that spawned this enemy, or -1.</summary>
        public int Portal;
        /// <summary>Visual scale factor (absorber growth), 1 by default.</summary>
        public float Scale;
        public float AttackCooldown, AttackWindup;
        /// <summary>Temporarily invulnerable (elite turrets: all but one are shielded).</summary>
        public bool Shielded;
        /// <summary>Ground walker sees its chase target in a straight line (refreshed every third step).</summary>
        public bool ClearPath;
        public int ResurrectionsLeft;
        public bool Hostile => !Def.PlayerFriendly;
        public bool Targetable => Active && !Def.PlayerFriendly && !Def.Invulnerable && !Shielded;
    }

    public sealed class Projectile
    {
        public const int MaxHits = 32;
        public int Slot, SpawnId;
        public bool Active;
        public OwnedPowerUp Source;
        /// <summary>Fired by an enemy: hits the hero, ignores enemies.</summary>
        public bool Hostile;
        /// <summary>Hostile shot that turns towards the hero (Imperia's Magic Missile).</summary>
        public bool Homing;
        public Vec2 Position, Velocity;
        public float Speed, Radius, Damage, Knockback, Life;
        public int RemainingHits;
        public Enemy Target;
        public int TargetSpawnId;
        public readonly int[] HitSpawnIds = new int[MaxHits];
        public int HitCount;

        public bool HasHit(int spawnId)
        {
            for (int i = 0; i < HitCount; i++) if (HitSpawnIds[i] == spawnId) return true;
            return false;
        }

        public void RecordHit(int spawnId)
        {
            if (HitCount < MaxHits) HitSpawnIds[HitCount++] = spawnId;
        }
    }

    public sealed class Pickup
    {
        public int Slot, SpawnId;
        public bool Active, Attracted;
        public PickupDef Def;
        public Vec2 Position;
        /// <summary>Experience for experience pickups.</summary>
        public int Value;
    }

    public enum SimEventKind
    {
        EnemySpawned, EnemyDied, EnemyDespawned, EnemyDamaged,
        ProjectileFired, ProjectileEnded,
        PickupSpawned, PickupCollected,
        HeroDamaged, HeroDashed, HeroTeleported, LevelUp, EnemyResurrected, EnemyAttacked, EliteRoomOpened, EliteRoomStarted, EliteRoomCompleted, ChoicesReady, BossSpawned, RunFinished,
    }

    /// <summary>Notification for presentation (views, effects, sound). Read and cleared every frame.</summary>
    public struct SimEvent
    {
        public SimEventKind Kind;
        public int Slot;
        public Vec2 Position;
        public float Value;
    }
}
