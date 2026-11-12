using System;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Aghanim's Scepter and Shard (Mechanics §34, .kiro/specs/aghanim-upgrades): pickups of treasure variants 1 and 2,
    /// one scepter per elite room, the queue of choices and the run-wide part of the effects (Stampede's bonus, Track's
    /// second item). The effects of single abilities live in their files.
    /// </summary>
    public sealed partial class Run
    {
        /// <summary>m_nTreasureVariant of the pickups (Д): 0 treasure (an item), 1 shard, 2 scepter.</summary>
        public const int ItemVariant = 0, ShardVariant = 1, ScepterVariant = 2;

        /// <summary>Collected scepters whose choice is not taken yet.</summary>
        public int PendingScepters { get; private set; }
        /// <summary>Collected shards whose choice is not taken yet.</summary>
        public int PendingShards { get; private set; }
        /// <summary>What opened the current (or the last) choice.</summary>
        public ChoiceSource ChoiceSource { get; private set; }

        private bool PendingAghanim => PendingScepters > 0 || PendingShards > 0;
        private bool HasPendingChoice => PendingAghanim || PendingItems > 0 || PendingLevels > 0;

        /// <summary>Order of the queue (Р3): scepter, shard, item, level.</summary>
        private ChoiceSource NextSource() =>
            PendingScepters > 0 ? ChoiceSource.Scepter
            : PendingShards > 0 ? ChoiceSource.Shard
            : PendingItems > 0 ? ChoiceSource.Item
            : ChoiceSource.Level;

        /// <summary>A taken card (or the fallback reward) spends one pending reward of its source (Т4.2).</summary>
        private void ConsumeChoice()
        {
            switch (ChoiceSource)
            {
                case ChoiceSource.Scepter: PendingScepters--; break;
                case ChoiceSource.Shard: PendingShards--; break;
                case ChoiceSource.Item: PendingItems--; break;
                default: PendingLevels--; break;
            }
        }

        private static bool IsScepter(PickupDef pickup) => pickup.RewardsTreasure && pickup.TreasureVariant == ScepterVariant;

        /// <summary>A collected treasure: an item (two with Track's scepter, Р9), a shard or a scepter.</summary>
        private void OnTreasure(PickupDef pickup)
        {
            switch (pickup.TreasureVariant)
            {
                case ItemVariant: PendingItems += TrackScepter() ? 2 : 1; break;
                case ShardVariant: PendingShards++; break;
                case ScepterVariant: PendingScepters++; break;
            }
        }

        private bool TrackScepter()
        {
            for (int i = 0; i < Build.Owned.Count; i++)
                if (Build.Owned[i].Scepter && Build.Owned[i].Def.Class == Track.ClassName) return true;
            return false;
        }

        /// <summary>
        /// One scepter per elite room (owner's decision В1 А, Р1): an enemy of a room does not drop its own scepter; when
        /// the room has no living enemy after a death (split units counted), the scepter appears where it died.
        /// </summary>
        private void RoomScepter(int room, Vec2 position)
        {
            if (room < 0 || room >= EliteRooms.Count || EliteRooms[room].ScepterDropped) return;
            for (int i = 0; i < Enemies.Length; i++)
                if (Enemies[i].Active && Enemies[i].EliteRoom == room) return;
            EliteRooms[room].ScepterDropped = true;
            var scepter = ScepterPickup();
            if (scepter != null) SpawnPickup(scepter, ClampToMap(position), 0);
        }

        private PickupDef scepterPickup;
        private bool scepterSearched;

        private PickupDef ScepterPickup()
        {
            if (scepterSearched) return scepterPickup;
            scepterSearched = true;
            foreach (var p in Data.Pickups.Values)
                if (IsScepter(p)) { scepterPickup = p; break; }
            if (scepterPickup == null)
                Diagnostics.Report("data:scepter-pickup", "No pickup with m_bRewardsTreasure and m_nTreasureVariant 2; elite rooms give no scepter");
            return scepterPickup;
        }

        /// <summary>Gives the scepter or the shard to an owned ability without a choice (tests, smoke -thornForce=KEY+scepter).</summary>
        public bool DebugGrant(string key, bool shard)
        {
            if (!Data.PowerUps.TryGetValue(key, out var def)) return false;
            if (!(shard ? Build.UpgradeShard(def) : Build.UpgradeScepter(def))) return false;
            OnBuildChanged();
            return true;
        }

        /// <summary>Puts a pickup on the ground (tests and debug tools: the shard has no source in the data, Р2).</summary>
        public bool DebugSpawnPickup(string key, Vec2 position) =>
            Data.Pickups.TryGetValue(key, out var def) && SpawnPickup(def, ClampToMap(position), 0) != null;

        // ------------------------------------------------------------------ Stampede's scepter

        private int stampedeVersion = -1;
        private float stampedePerSpeed;

        /// <summary>
        /// Stampede's scepter: physical damage of every ability × (1 + m_flMovementSpeedPercentDamageMultiplier × the
        /// current walking speed / 100), the charge included (owner's decision В4а, Р7). 0 without it.
        /// </summary>
        public float StampedeDamageBonus
        {
            get
            {
                if (stampedeVersion != Build.Version) RefreshStampede();
                return stampedePerSpeed > 0 ? stampedePerSpeed * HeroSpeed / 100 : 0;
            }
        }

        private void RefreshStampede()
        {
            stampedeVersion = Build.Version;
            stampedePerSpeed = 0;
            for (int i = 0; i < Build.Owned.Count; i++)
            {
                var owned = Build.Owned[i];
                if (!owned.Scepter || owned.Def.Class != Stampede.ClassName) continue;
                var token = owned.Def.Raw?[Stampede.ScepterField];
                if (token != null) stampedePerSpeed = Math.Max(stampedePerSpeed, (float)token);
                else Diagnostics.Report($"ability:{owned.Def.Key}:{Stampede.ScepterField}", $"Ability {owned.Def.Key} has no {Stampede.ScepterField}; 0 is used");
            }
        }

        /// <summary>Factor of a physical hit: abilities only, not reflection or «Other» (Р7).</summary>
        private float StampedeFactor(DamageStatEntry source, in DamageInfo info) =>
            info.Type == DamageType.Physical && source.Def != null && !info.Has(DamageFlags.Reflected) ? 1 + StampedeDamageBonus : 1;
    }
}
