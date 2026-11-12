using System;
using System.Collections.Generic;

namespace ThornThrone.Simulation
{
    /// <summary>
    /// Which abilities have an Aghanim's Scepter or Shard effect in this build of the simulation (Mechanics §34). Only
    /// these are offered on the Aghanim screen; the effects live in the files of their abilities. Task 27 part 1 has the
    /// nine effects that change numbers or target rules; the other eight come with Task 27б (design §8).
    /// </summary>
    public static class AghanimEffects
    {
        private static bool All(PowerUpDef def) => true;

        private static readonly Dictionary<string, Func<PowerUpDef, bool>> scepter = new Dictionary<string, Func<PowerUpDef, bool>>
        {
            { "CSurvivorsPowerUpDefinition_ArcaneBolt", All },
            { "CSurvivorsPowerUpDefinition_MagicMissile", All },
            { "CSurvivorsPowerUpDefinition_KnifeThrow", All },
            { "CSurvivorsPowerUpDefinition_InstantAttack", IsAnnihilation },
            { "CSurvivorsPowerUpDefinition_Track", All },
            { "CSurvivorsPowerUpDefinition_Swashbuckle", All },
            { "CSurvivorsPowerUpDefinition_EchoStrike", All },
            { "CSurvivorsPowerUpDefinition_Stampede", All },
            { "CSurvivorsPowerUpDefinition_LagunaBlade", All },
        };

        private static readonly Dictionary<string, Func<PowerUpDef, bool>> shard = new Dictionary<string, Func<PowerUpDef, bool>>
        {
            { "CSurvivorsPowerUpDefinition_MagicMissile", All },
        };

        /// <summary>
        /// The ability can take the scepter (or the shard): an active ability with m_bIsScepterUpgradeable
        /// (m_bIsShardUpgradeable), a handler and an implemented effect.
        /// </summary>
        public static bool Has(PowerUpDef def, bool isShard)
        {
            if (def == null || !def.IsActive || !(isShard ? def.ShardUpgradeable : def.ScepterUpgradeable) || !AbilityFactory.Has(def)) return false;
            return (isShard ? shard : scepter).TryGetValue(def.Class, out var check) && check(def);
        }

        private static readonly AttributeValue[] none = new AttributeValue[0];
        private static readonly AttributeValue[] annihilationLife = { new AttributeValue(Attr.Lives, InstantAttack.ScepterLives) };

        /// <summary>
        /// Hero attributes of a scepter that has no m_scepterUpgradeDefinition: Annihilation «Скри'Ок дарует вам
        /// дополнительную жизнь» (Mechanics §34 Р8). Added by <see cref="Build"/> once while the flag is set.
        /// </summary>
        public static AttributeValue[] ScepterGlobal(PowerUpDef def) => IsAnnihilation(def) ? annihilationLife : none;

        private static bool IsAnnihilation(PowerUpDef def) =>
            def.Class == "CSurvivorsPowerUpDefinition_InstantAttack" && def.Targeting == InstantAttack.RandomTarget;
    }
}
