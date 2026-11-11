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
        /// <summary>Position at the start of the last fixed step (render interpolation, Mechanics §17).</summary>
        public Vec2 PreviousPosition;
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
        /// <summary>Statuses of this slot, created with the pool and cleared on every spawn (Mechanics §24).</summary>
        public readonly EnemyStatus Status;
        /// <summary>Seconds of stun left: no movement and no attacks (Magic Missile). Set through <see cref="Run.Stun"/>.</summary>
        public float StunTimer => Status.Stun;
        public bool Stunned => Status.Stunned;

        public Enemy() : this(4) { }
        public Enemy(int maxDots) { Status = new EnemyStatus(maxDots); }
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
        /// <summary>Position at the start of the last fixed step (render interpolation, Mechanics §17).</summary>
        public Vec2 PreviousPosition;
        public float Speed, Radius, Damage, Knockback, Life;
        /// <summary>Type and delivery of <see cref="Damage"/> against enemies (pure by default; abilities set them at launch).</summary>
        public DamageType DamageType;
        public DamageFlags DamageFlags;
        /// <summary>Statistics entry the hits are counted in.</summary>
        public DamageStatEntry DamageSource;
        /// <summary>Hit on an enemy, snapshotted at launch.</summary>
        public DamageInfo Hit => new DamageInfo(Damage, DamageType, DamageFlags, DamageSource);
        public int RemainingHits;
        public Enemy Target;
        public int TargetSpawnId;
        /// <summary>Further targets after the first impact (Magic Missile).</summary>
        public int Bounces;
        /// <summary>Stun seconds applied on impact.</summary>
        public float Stun;
        /// <summary>
        /// Seconds during which the shot cannot hit: after a bounce (m_flBounceMinimumLifetime) or after launch
        /// (m_flSpawnMinimumLifetime).
        /// </summary>
        public float ArmTime;
        /// <summary>Freeze around the impact (Chain Frost), snapshotted at launch: radius, strength and seconds.</summary>
        public float FreezeRadius, FreezeStrength, FreezeTime;
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
        /// <summary>Position at the start of the last fixed step (render interpolation, Mechanics §17).</summary>
        public Vec2 PreviousPosition;
        /// <summary>Experience for experience pickups.</summary>
        public int Value;
    }

    /// <summary>Stage of a <see cref="GroundObject"/>: a mine arms, waits and counts down; a glob flies and burns (Mechanics §31).</summary>
    public enum GroundPhase { Arming, Armed, Triggered, Falling, Burning }
    /// <summary>
    /// Pooled object left on the ground by an ability (Land Mine's mine, Mortimer Kisses' glob and puddle). It does not
    /// move, so it has no previous position: presentation draws a flying glob from <see cref="Origin"/>,
    /// <see cref="Position"/>, <see cref="Age"/> and <see cref="FlightTime"/>. The source ability updates it every
    /// step (<see cref="IGroundObjectAbility"/>); its numbers are snapshotted when it is created (Mechanics §31 Р1–Р3).
    /// </summary>
    public sealed class GroundObject
    {
        public int Slot, SpawnId;
        public bool Active;
        public OwnedPowerUp Source;
        public GroundPhase Phase;
        /// <summary>Where the mine lies or the glob lands.</summary>
        public Vec2 Position;
        /// <summary>Where the glob was launched from (the hero at launch); equals <see cref="Position"/> for a mine.</summary>
        public Vec2 Origin;
        /// <summary>Radius of the mine or puddle.</summary>
        public float Radius;
        /// <summary>Seconds since creation (advanced in the steps after the creation step).</summary>
        public float Age;
        /// <summary>Seconds until the next event of the phase (countdown, landing, next tick).</summary>
        public float Timer;
        public float FlightTime, Life, ArmTime, Interval;
        public int TicksLeft;
        /// <summary><see cref="Run.Time"/> of the step that created it: not advanced in that step.</summary>
        public float Born;
        /// <summary>Type and delivery of <see cref="Damage"/>, snapshotted at creation.</summary>
        public float Damage;
        public DamageType DamageType;
        public DamageFlags DamageFlags;
        public DamageStatEntry DamageSource;
        /// <summary>Hit on an enemy, snapshotted at creation.</summary>
        public DamageInfo Hit => new DamageInfo(Damage, DamageType, DamageFlags, DamageSource);
    }
    public enum SimEventKind
    {
        EnemySpawned, EnemyDied, EnemyDespawned, EnemyDamaged,
        ProjectileFired, ProjectileEnded,
        PickupSpawned, PickupCollected,
        HeroDamaged, HeroDashed, HeroTeleported, LevelUp, EnemyResurrected, EnemyStunned, EnemyAttacked, EliteRoomOpened, EliteRoomStarted, EliteRoomCompleted, ChoicesReady, BossSpawned, RunFinished,
        HeroDowned, HeroRevived,
        /// <summary>An ability hit an area (Echo Strike): Position is its centre, Direction and Extent its shape (Mechanics §26).</summary>
        AbilityStrike,
        /// <summary>An ability hit a circle (Counter Helix): Position is its centre, Extent.X its radius (Mechanics §28 Р8).</summary>
        AbilityBurst,
        /// <summary>A ground object appeared (Mechanics §31): Slot, SpawnId, Position (mine or landing point), Value the powerup ID.</summary>
        GroundObjectSpawned,
        /// <summary>A ground object ended: Slot, SpawnId, Position, Value the powerup ID.</summary>
        GroundObjectEnded,
    }

    /// <summary>
    /// Notification for presentation (views, effects, sound). Read and cleared every frame.
    /// A slot may be reused by a new entity before the frame ends (up to 8 steps), so an event about a pooled entity
    /// carries its <see cref="SpawnId"/>; compare it with the slot before reading the pool
    /// (.kiro/specs/corpse-enemy-slot-reuse).
    /// </summary>
    public struct SimEvent
    {
        public SimEventKind Kind;
        public int Slot;
        public Vec2 Position;
        public float Value;
        /// <summary>SpawnId of the enemy, projectile, pickup or ground object; 0 for events that are not about a pooled entity.</summary>
        public int SpawnId;
        /// <summary>Definition of the enemy at the time of the event (enemy events only).</summary>
        public EnemyDef EnemyDef;
        /// <summary>Enemy direction at the time of the event (enemy events only).</summary>
        public Vec2 Direction;
        /// <summary>Type of the damage (<see cref="SimEventKind.EnemyDamaged"/> only).</summary>
        public DamageType DamageType;
        /// <summary>The hit was critical (<see cref="SimEventKind.EnemyDamaged"/> only).</summary>
        public bool Critical;

        /// <summary>
        /// Length (X) and width (Y) of the struck rectangle (<see cref="SimEventKind.AbilityStrike"/>);
        /// radius (X) of the struck circle (<see cref="SimEventKind.AbilityBurst"/>).
        /// </summary>
        public Vec2 Extent;
    }
}
