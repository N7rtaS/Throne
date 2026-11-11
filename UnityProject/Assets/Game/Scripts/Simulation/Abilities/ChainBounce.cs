namespace ThornThrone.Simulation
{
    /// <summary>
    /// One step of a chain (m_eBounceTargeting RANDOM_TARGET_AT_RANGE), shared by Magic Missile and Chain Frost
    /// (Mechanics §15, §29 Р3): after an impact a shot with bounces left flies on to a random targetable enemy within
    /// range of the impact that it has not hit yet (<see cref="Projectile.HitSpawnIds"/>), and cannot hit anything
    /// for the bounce delay. The chain ends when there is no such enemy or the list of hit enemies is full.
    /// </summary>
    public static class ChainBounce
    {
        /// <summary>Redirects <paramref name="shot"/> to the next target; returns false when the chain ends.</summary>
        /// <param name="life">Flight time of the new leg.</param>
        /// <param name="armTime">Seconds after the bounce during which the shot cannot hit (m_flBounceMinimumLifetime).</param>
        public static bool Next(Run run, Projectile shot, Vec2 impact, float range, float life, float armTime)
        {
            if (shot.Bounces <= 0 || shot.HitCount >= Projectile.MaxHits) return false;
            var next = run.RandomTarget(impact, range, shot);
            if (next == null) return false;
            shot.Bounces--;
            shot.Position = impact;
            shot.Target = next;
            shot.TargetSpawnId = next.SpawnId;
            shot.Velocity = (next.Position - impact).Normalized * shot.Speed;
            shot.Life = life;
            shot.ArmTime = armTime;
            return true;
        }
    }
}
