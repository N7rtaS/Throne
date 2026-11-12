using System;
using System.Text;
using ThornThrone.App;
using ThornThrone.Simulation;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// Text of a hero card (Mechanics §40 Г1–Г2): name, «Стартовая способность» (DOTA_Survivors_StartingAbilityLabel)
    /// and the innate ability with their names and descriptions from survivors_powerups_russian.txt, and the dash
    /// of the hero's m_vecBaseAttributes. Pure functions; final card layout and icons are Task 34–35.
    /// </summary>
    public static class HeroText
    {
        /// <summary>DOTA_Survivors_StartingAbilityLabel.</summary>
        public const string StartingLabel = "Стартовая способность";
        /// <summary>Label of the innate ability: the client shows only its picture (text of the project, Г1, Р).</summary>
        public const string InnateLabel = "Врождённая способность";
        private const string LabelColor = "<color=#FFD166>";

        /// <param name="name">Localized powerup name; its key when there is no translation.</param>
        /// <param name="description">Localized description, or null.</param>
        public static string Card(HeroInfo info, string heroTitle, Func<PowerUpDef, string> name, Func<PowerUpDef, string> description)
        {
            var sb = new StringBuilder();
            sb.Append("<size=48><b>").Append(heroTitle).Append("</b></size>\n\n");
            Ability(sb, StartingLabel, info.Starting, name, description);
            sb.Append('\n');
            Ability(sb, InnateLabel, info.Innate, name, description);
            sb.Append('\n').Append(Dash(info.Dashes, info.DashCooldown));
            return sb.ToString();
        }

        private static void Ability(StringBuilder sb, string label, PowerUpDef def, Func<PowerUpDef, string> name, Func<PowerUpDef, string> description)
        {
            sb.Append(LabelColor).Append(label).Append("</color>\n");
            sb.Append("<b>").Append(name(def) ?? def.Key).Append("</b>\n");
            string text = description(def);
            if (!string.IsNullOrEmpty(text)) sb.Append("<size=24>").Append(text).Append("</size>\n");
        }

        /// <summary>Г2: «Рывок: N заряд/заряда/зарядов, перезарядка X с» (text of the project, Р).</summary>
        public static string Dash(int charges, float cooldown) =>
            $"Рывок: {charges} {Charges(charges)}, перезарядка {ChoiceText.Value(cooldown, false)} с";

        /// <summary>Russian plural of «заряд».</summary>
        public static string Charges(int n)
        {
            int last2 = Math.Abs(n) % 100, last = last2 % 10;
            if (last2 >= 11 && last2 <= 14) return "зарядов";
            if (last == 1) return "заряд";
            if (last >= 2 && last <= 4) return "заряда";
            return "зарядов";
        }
    }
}
