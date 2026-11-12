using System;
using System.Collections.Generic;

namespace ThornThrone.Simulation
{
    public enum EliteRoomState { Locked, Open, Channeling, Fighting, Done, Closed }

    /// <summary>One elite room of the run (arena point elite_room).</summary>
    public sealed class EliteRoom
    {
        public int Index;
        public Vec2 Position;
        public float UnlockTime;
        public SpawnerDef Spawner;
        public EliteRoomState State;
        /// <summary>Channel progress 0..1 while the hero stands in the trigger radius.</summary>
        public float Progress;
        public int Alive;
        /// <summary>The room's one scepter has dropped (Mechanics §34 Р1).</summary>
        public bool ScepterDropped;
    }

    public sealed partial class Run
    {
        public readonly List<EliteRoom> EliteRooms = new List<EliteRoom>();

        /// <summary>
        /// Elite rooms (Mechanics §6). Data: unlock times m_vecEliteRoomUnlockTimes (180, 360, 540), choices
        /// m_vecEliteRoomChoices (4 spawners), channel m_flEliteRoomTriggerChannelTime (3 s) within
        /// m_flEliteRoomTriggerRadius (600); places: arena points elite_room (3).
        /// Interpretation until observed: each unlock opens a random remaining point with a random remaining
        /// choice; leaving the circle resets the channel; the fight spawns the spawner's minimum count around the hero
        /// and the room is done when all of its enemies (including split units) are dead.
        /// </summary>
        private void CreateEliteRooms()
        {
            if (!Data.ArenaTargets.TryGetValue("elite_room", out var points) || points.Length == 0) return;
            var pool = new List<Vec2>(points);
            var choices = new List<SpawnerDef>();
            foreach (var name in Mode.EliteRoomChoices)
                if (Data.Spawners.TryGetValue(name, out var spawner)) choices.Add(spawner);
            for (int i = 0; i < Mode.EliteRoomUnlockTimes.Length && pool.Count > 0 && choices.Count > 0; i++)
            {
                int p = Rng.Next(pool.Count), c = Rng.Next(choices.Count);
                EliteRooms.Add(new EliteRoom
                {
                    Index = i, Position = pool[p], UnlockTime = Mode.EliteRoomUnlockTimes[i], Spawner = choices[c],
                });
                pool.RemoveAt(p);
                choices.RemoveAt(c);
            }
        }

        private void UpdateEliteRooms(float dt)
        {
            for (int i = 0; i < EliteRooms.Count; i++)
            {
                var room = EliteRooms[i];
                switch (room.State)
                {
                    case EliteRoomState.Locked:
                        if (Time >= room.UnlockTime && !BossSpawned)
                        {
                            room.State = EliteRoomState.Open;
                            Emit(SimEventKind.EliteRoomOpened, i, room.Position, 0);
                        }
                        break;
                    case EliteRoomState.Open:
                    case EliteRoomState.Channeling:
                    {
                        float r = Mode.EliteRoomTriggerRadius;
                        bool inside = Vec2.DistanceSquared(HeroPosition, room.Position) <= r * r;
                        if (!inside) { room.State = EliteRoomState.Open; room.Progress = 0; break; }
                        room.State = EliteRoomState.Channeling;
                        room.Progress += dt / Math.Max(0.01f, Mode.EliteRoomTriggerChannelTime);
                        if (room.Progress >= 1) StartEliteFight(room);
                        break;
                    }
                    case EliteRoomState.Fighting:
                        room.Alive = 0;
                        Enemy firstShielded = null;
                        bool anyVulnerable = false;
                        for (int e = 0; e < Enemies.Length; e++)
                        {
                            var enemy = Enemies[e];
                            if (!enemy.Active || enemy.EliteRoom != i) continue;
                            room.Alive++;
                            if (!enemy.Shielded) anyVulnerable = true;
                            else if (firstShielded == null || enemy.SpawnId < firstShielded.SpawnId) firstShielded = enemy;
                        }
                        // Turrets: when the vulnerable one dies, the next one (spawn order) loses its shield.
                        if (!anyVulnerable && firstShielded != null) firstShielded.Shielded = false;
                        if (room.Alive == 0)
                        {
                            room.State = EliteRoomState.Done;
                            Emit(SimEventKind.EliteRoomCompleted, i, room.Position, 0);
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// The boss fight starts: a room in its fight loses its enemies without reward, every unfinished room closes and
        /// never opens again; completed rooms stay done (ТЗ_Полная_игра_Unity.md §9, Mechanics §36 Р11).
        /// </summary>
        private void CloseEliteRooms()
        {
            for (int i = 0; i < EliteRooms.Count; i++)
            {
                var room = EliteRooms[i];
                if (room.State == EliteRoomState.Done || room.State == EliteRoomState.Closed) continue;
                if (room.State == EliteRoomState.Fighting)
                    for (int e = 0; e < Enemies.Length; e++)
                        if (Enemies[e].Active && Enemies[e].EliteRoom == i) RetireEnemy(Enemies[e], SimEventKind.EnemyDespawned);
                room.State = EliteRoomState.Closed;
                room.Progress = 0;
                room.Alive = 0;
                Emit(SimEventKind.EliteRoomClosed, i, room.Position, 0);
            }
        }

        private void StartEliteFight(EliteRoom room)
        {
            room.State = EliteRoomState.Fighting;
            room.Progress = 1;
            var s = room.Spawner;
            if (!Data.Enemies.TryGetValue(s.EnemyName, out var def)) return;
            int count = Math.Max(1, s.MinimumCount);
            var radius = s.OvalRadius.X > 0 ? s.OvalRadius : new Vec2(600, 600);
            float phase = Rng.Angle();
            // Observed (user, 09.11.2026): of the elite turrets only one is vulnerable at a time;
            // killing it makes the next one vulnerable.
            bool turrets = s.Class == "CSurvivorsSpawnerEliteTurretDefinition";
            bool first = true;
            for (int n = 0; n < count; n++)
            {
                // Evenly around the hero on the spawner's oval (OVAL_AROUND_PLAYER).
                var a = Vec2.FromAngle(phase + n * (float)(Math.PI * 2) / count);
                var enemy = SpawnEnemy(def, ClampToMap(HeroPosition + new Vec2(a.X * radius.X, a.Y * radius.Y)), -1);
                if (enemy == null) continue;
                enemy.EliteRoom = room.Index;
                enemy.Shielded = turrets && !first;
                first = false;
            }
            Emit(SimEventKind.EliteRoomStarted, room.Index, room.Position, count);
        }
    }
}
