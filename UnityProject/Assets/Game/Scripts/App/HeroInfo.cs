using System;
using System.IO;
using ThornThrone.Simulation;

namespace ThornThrone.App
{
    /// <summary>
    /// What the hero card shows (Mechanics §40 Г1–Г3): the first of m_vecStartingPowerUps and of m_vecInnatePowerUps
    /// of survivors_heroes.json and the dash of m_vecBaseAttributes (NumDashes, DashCooldown) without upgrades.
    /// </summary>
    public sealed class HeroInfo
    {
        public HeroDef Hero;
        public PowerUpDef Starting;
        public PowerUpDef Innate;
        public int Dashes;
        /// <summary>Seconds.</summary>
        public float DashCooldown;

        /// <exception cref="ArgumentException">No such hero.</exception>
        /// <exception cref="InvalidDataException">The hero has no starting or innate ability, or its ID is not in survivors_powerups.json (Г3).</exception>
        public static HeroInfo Of(GameData data, string key)
        {
            if (key == null || !data.Heroes.TryGetValue(key, out var hero)) throw new ArgumentException("Unknown hero " + key, nameof(key));
            var info = new HeroInfo
            {
                Hero = hero,
                Starting = First(data, hero, hero.StartingPowerUps, "m_vecStartingPowerUps"),
                Innate = First(data, hero, hero.InnatePowerUps, "m_vecInnatePowerUps"),
            };
            foreach (var a in hero.BaseAttributes)
            {
                if (a.Id == Attr.NumDashes) info.Dashes = (int)a.Value;
                else if (a.Id == Attr.DashCooldown) info.DashCooldown = a.Value;
            }
            return info;
        }

        private static PowerUpDef First(GameData data, HeroDef hero, int[] ids, string field)
        {
            if (ids == null || ids.Length == 0) throw new InvalidDataException($"Hero {hero.Key} has no {field}");
            if (!data.PowerUpsById.TryGetValue(ids[0], out var def))
                throw new InvalidDataException($"Hero {hero.Key} lists powerup {ids[0]} in {field}, which is not in survivors_powerups.json");
            return def;
        }
    }
}
