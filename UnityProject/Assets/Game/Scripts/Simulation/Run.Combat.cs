using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Combat core (Mechanics §24, .kiro/specs/combat-core): every hit on an enemy goes through <see cref="DealDamage"/>,
    /// every hit on the hero through <see cref="DamageHero"/>; enemy statuses are applied and advanced here.
    /// The formulas are design decisions (Р1–Р16); attribute names and values are data.
    /// </summary>
    public sealed partial class Run
    {
        /// <summary>Damage dealt by source: one entry per acquired ability, plus «Reflection» and «Other».</summary>
        public readonly DamageStats DamageStats = new DamageStats();

        /// <summary>
        /// Applies one hit to an enemy (Р2): critical strikes, vulnerability, weakening and amplification for
        /// non-pure damage, then health, Imperia's threshold, statistics and the damage event.
        /// Returns the health actually removed.
        /// </summary>
        public float DealDamage(Enemy target, in DamageInfo info)
        {
            if (!target.Active || Finished || !target.Targetable) return 0;
            var source = info.Source ?? DamageStats.Other;
            var status = target.Status;
            float amount = info.Amount;
            bool critical = false;
            float vulnerableStun = 0;
            if (info.Type != DamageType.Pure)
            {
                float multiplier = 1;
                if (!info.Has(DamageFlags.Periodic) && !info.Has(DamageFlags.Reflected))
                {
                    var global = Build.Global;
                    if (info.Type == DamageType.Physical)
                        Crit(global[Attr.CriticalStrike_Physical_Chance], global[Attr.CriticalStrike_Physical_DamageMultiplier], ref multiplier, ref critical);
                    if (info.Has(DamageFlags.Projectile))
                        Crit(global[Attr.CriticalStrike_Projectile_Chance], global[Attr.CriticalStrike_Projectile_DamageMultiplier], ref multiplier, ref critical);
                    if (info.Type == DamageType.Physical && status.Vulnerable)
                    {
                        status.ConsumeVulnerable(out float vulnerable, out vulnerableStun);
                        multiplier = Math.Max(multiplier, vulnerable);
                        critical = true;
                    }
                }
                amount *= multiplier;
                if (info.Type == DamageType.Physical && status.WeakenTime > 0) amount *= 1 + status.WeakenStrength;
                if (status.AmplifyTime > 0) amount *= 1 + status.AmplifyStrength;
                amount *= ProjectileAmplification.Factor(status, info); // Frostbite (Mechanics §29 Р24)
            }

            float before = Math.Max(0, target.Health);
            float after = target.Health - amount;
            target.Health = after;
            if (target == Boss) ClampBossHealth(target);
            // Health actually removed: no overkill, nothing below Imperia's threshold.
            float dealt = target.Health == after ? Math.Min(amount, before) : Math.Max(0, before - Math.Max(0, target.Health));
            source.Dealt += dealt;
            source.Hits++;
            if (critical) source.Crits++;
            Emit(SimEventKind.EnemyDamaged, target, target.Position, amount, info.Type, critical);
            if (target.Health > 0)
            {
                MagicWeakening.Apply(this, target, info); // Shen's innate (Mechanics §27)
                if (vulnerableStun > 0) Stun(target, vulnerableStun);
                return dealt;
            }
            if (Kill(target)) source.Kills++;
            return dealt;
        }

        /// <summary>A crit roll only when the chance is above 0, so runs without crits keep their random sequence (Р3).</summary>
        private void Crit(float chance, float multiplier, ref float result, ref bool critical)
        {
            if (chance <= 0 || Rng.NextFloat() >= chance) return;
            result = Math.Max(result, Math.Max(1, multiplier));
            critical = true;
        }

        /// <summary>Pure damage from «Other» (tests and debug tools).</summary>
        public void DamageEnemy(Enemy enemy, float damage) =>
            DealDamage(enemy, new DamageInfo(damage, DamageType.Pure, DamageFlags.None, DamageStats.Other));

        /// <summary>Health reached zero: resurrection, or death with rewards and split units. Returns true on death.</summary>
        private bool Kill(Enemy enemy)
        {
            var def = enemy.Def;
            if (enemy.ResurrectionsLeft > 0)
            {
                // Resurrector: returns with full health and faster (m_flMovementSpeedMultiplierPerDeath).
                enemy.ResurrectionsLeft--;
                enemy.Health = enemy.MaxHealth;
                enemy.Speed *= 1 + def.MovementSpeedMultiplierPerDeath;
                Emit(SimEventKind.EnemyResurrected, enemy, enemy.Position, enemy.ResurrectionsLeft);
                return false;
            }
            var position = enemy.Position;
            int room = enemy.EliteRoom;
            bool boss = enemy == Boss;
            RetireEnemy(enemy, SimEventKind.EnemyDied);
            Kills++;
            if (boss) { Finish(true); return true; }
            DropRewards(def, position);
            if (def.SplitOnDeathNumUnits > 0 && Data.EnemiesById.TryGetValue(def.SplitOnDeathEnemyId, out var split))
            {
                for (int i = 0; i < def.SplitOnDeathNumUnits; i++)
                {
                    var offset = Vec2.FromAngle(Rng.Angle()) * Math.Max(1, def.SplitOnDeathKnockbackDistance);
                    var child = SpawnEnemy(split, ClampToMap(position + offset), -1);
                    if (child != null) child.EliteRoom = room;
                }
            }
            return true;
        }

        /// <summary>
        /// Damage to the hero × (1 − min(Armor, MaxHeroArmor)) (Р5). An attacker that touched or hit in melee takes
        /// pure damage «damage before armor × DamageReflection» back (Р6); projectiles and zones pass no attacker.
        /// A downed hero takes nothing; a lethal hit is not reflected (Mechanics §25 Р8).
        /// </summary>
        private void DamageHero(float amount, Enemy attacker = null)
        {
            if (amount <= 0 || Finished || DebugInvulnerable || Downed) return;
            float armor = Math.Min(Build.Global[Attr.Armor], Rules.MaxHeroArmor);
            float taken = amount * (1 - armor);
            Health = Math.Max(0, Health - taken);
            Emit(SimEventKind.HeroDamaged, -1, HeroPosition, taken);
            if (Health <= 0) { HeroDown(); return; } // a life or defeat (Mechanics §25 Р8)
            float reflection = Build.Global[Attr.DamageReflection];
            if (attacker != null && reflection > 0)
                DealDamage(attacker, new DamageInfo(amount * reflection, DamageType.Pure, DamageFlags.Reflected, DamageStats.Reflection));
        }

        // ------------------------------------------------------------------ statuses

        /// <summary>Control duration × (1 − m_flStatusResistance); 0 or less means immune (Р8).</summary>
        private static float ControlTime(Enemy enemy, float duration) => duration * Math.Max(0, 1 - enemy.Def.StatusResistance);

        /// <summary>
        /// Stuns for duration × (1 − m_flStatusResistance); a longer remaining stun is kept (interpretation).
        /// Imperia and other enemies with resistance 1 are immune; stationary enemies are not stunned.
        /// </summary>
        public void Stun(Enemy enemy, float duration)
        {
            if (!enemy.Active || enemy.Def.Movement == MovementBehavior.Stationary) return;
            float time = ControlTime(enemy, duration);
            if (!enemy.Status.ApplyStun(time)) return;
            Emit(SimEventKind.EnemyStunned, enemy, enemy.Position, time);
        }

        /// <summary>Movement slow (SlowStrength / SlowDuration), scaled by status resistance (Р8, Р9).</summary>
        public void Slow(Enemy enemy, float strength, float duration)
        {
            if (!enemy.Active) return;
            float time = ControlTime(enemy, duration);
            if (time > 0) enemy.Status.ApplySlow(strength, time);
        }

        /// <summary>Freeze (FreezeStrength / FreezeDuration): slows own movement, does not stop attacks (Р8, Р9).</summary>
        public void Freeze(Enemy enemy, float strength, float duration)
        {
            if (!enemy.Active) return;
            float time = ControlTime(enemy, duration);
            if (time > 0) enemy.Status.ApplyFreeze(strength, time);
        }

        /// <summary>Periodic damage: perTick every interval for duration, first tick after one interval (Р11).</summary>
        public void ApplyDot(Enemy enemy, DamageStatEntry source, DamageType type, float perTick, float interval, float duration)
        {
            if (!enemy.Active) return;
            if (!enemy.Status.ApplyDot(source ?? DamageStats.Other, type, perTick, interval, duration) && FirstTime(DamageStats))
                Diagnostics.Report("status:dot-overflow", "An enemy had more periodic effects than Rules.MaxDotsPerEnemy; the shortest one was replaced");
        }

        /// <summary>Extra physical damage taken (Shen's innate, Р12); status resistance does not apply.</summary>
        public void Weaken(Enemy enemy, float strength, float duration)
        {
            if (enemy.Active) enemy.Status.ApplyWeaken(strength, duration);
        }

        /// <summary>Extra physical and magical damage taken (Track, Р12); status resistance does not apply.</summary>
        public void Amplify(Enemy enemy, float strength, float duration)
        {
            if (enemy.Active) enemy.Status.ApplyAmplify(strength, duration);
        }

        /// <summary>The next physical hit is critical ×multiplier and stuns for stun seconds (Kez's innate, Р13).</summary>
        public void MarkVulnerable(Enemy enemy, float multiplier, float stun, float duration)
        {
            if (enemy.Active) enemy.Status.ApplyVulnerable(multiplier, stun, duration);
        }

        /// <summary>
        /// Advances status timers and deals due periodic damage (Р14). Runs inside the step only, so pause, choice
        /// and the end of the run stop it. A tick can kill the enemy and give its slot to a split unit: the slot is
        /// left as soon as its SpawnId changes.
        /// </summary>
        private void UpdateStatuses(float dt)
        {
            for (int i = 0; i < Enemies.Length; i++)
            {
                var enemy = Enemies[i];
                if (!enemy.Active) continue;
                var status = enemy.Status;
                status.Advance(dt);
                if (status.DotCount == 0) continue;
                int spawnId = enemy.SpawnId;
                for (int d = 0; d < status.MaxDots; d++)
                {
                    while (status.TakeDotTick(d, out var hit))
                    {
                        DealDamage(enemy, hit);
                        if (Finished) return;
                        if (!enemy.Active || enemy.SpawnId != spawnId) break;
                    }
                    if (!enemy.Active || enemy.SpawnId != spawnId) break;
                }
            }
        }
    }
}
