using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ThornThrone.Simulation;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// Texts of the results screen (Mechanics §37 Р6): the header and the tables of abilities and items laid out like
    /// the client's PostGameScreen (bp_popup_crownfall_encounter_survivors.vxml). Labels are the client's
    /// localization_russian.json strings. Pure functions: built once when the screen opens.
    /// </summary>
    public static class ResultsText
    {
        /// <summary>Rows of a table under its header; one more row would not fit the 1920×1080 container.</summary>
        public const int MaxRows = 11;
        /// <summary>Thousands with a space, decimals with a comma; fixed, so the device locale does not change it.</summary>
        private static readonly NumberFormatInfo Numbers = new NumberFormatInfo
        {
            NumberGroupSeparator = " ",
            NumberDecimalSeparator = ",",
            NegativeSign = "-",
        };
        private const string Consumed = "<color=#9A9A9A>";

        /// <summary>Whole seconds as mm:ss (minutes are not capped at 99).</summary>
        public static string Time(float seconds)
        {
            int s = Math.Max(0, (int)Math.Floor(seconds));
            return $"{s / 60:00}:{s % 60:00}";
        }

        /// <summary>Whole number with a space between thousands; fractions are dropped.</summary>
        public static string Integer(double value) => Math.Floor(value).ToString("#,0", Numbers);

        /// <summary>Damage per second with one decimal and a comma ({f:1:ability_dps}).</summary>
        public static string Dps(double value) => value.ToString("#,0.0", Numbers);

        /// <summary>
        /// Outcome, echelon, time, level, kills, final score, gold earned (Т5.1) and the profile's gold after it was
        /// paid in, «Всего золота» (DOTA_Survivors_TotalGold, Mechanics §38 М10).
        /// </summary>
        public static string Header(RunSummary summary, int echelon, string echelonName, long totalGold)
        {
            return $"<size=56><b>{(summary.Victory ? "Победа!" : "Поражение")}</b></size>" +
                   $"\nЭшелон {echelon} — {echelonName}" +
                   $"\nВремени прожито {Time(summary.Seconds)}   Уровень игрока {summary.Level}   Убито {Integer(summary.Kills)}" +
                   $"\nИтоговый счёт {Integer(summary.Score)}   Золота получено {Integer(summary.Gold)}   Всего золота {Integer(totalGold)}";
        }

        /// <summary>
        /// The line under the tables: «Открыта новая сложность!» (Ф8) and «Доступны улучшения!»
        /// (DOTA_Survivors_UpgradesAvailable, Mechanics §38 М10); empty when neither applies.
        /// </summary>
        public static string Notices(bool unlocked, bool upgradesAvailable)
        {
            if (unlocked && upgradesAvailable) return "Открыта новая сложность!   Доступны улучшения!";
            if (unlocked) return "Открыта новая сложность!";
            return upgradesAvailable ? "Доступны улучшения!" : "";
        }

        /// <summary>«Способность / Уровень / Всего урона / Время владения / УРОН/С» (AbilityStats); consumed rows grey.</summary>
        public static string Abilities(IReadOnlyList<PowerUpResult> rows, Func<PowerUpDef, string> name)
        {
            var text = new StringBuilder("<b>Способность<pos=32%>Уровень<pos=45%>Всего урона<pos=62%>Время владения<pos=84%>УРОН/С</b>");
            return Rows(text, rows, row =>
                $"{name(row.Def)}<pos=32%>{row.Rank}<pos=45%>{Integer(row.Damage)}<pos=62%>{Time(row.Held)}<pos=84%>{Dps(row.Dps)}");
        }

        /// <summary>«Предмет / Уровень / Время владения» (ItemStats); consumed rows grey.</summary>
        public static string Items(IReadOnlyList<PowerUpResult> rows, Func<PowerUpDef, string> name)
        {
            var text = new StringBuilder("<b>Предмет<pos=45%>Уровень<pos=63%>Время владения</b>");
            return Rows(text, rows, row => $"{name(row.Def)}<pos=45%>{row.Rank}<pos=63%>{Time(row.Held)}");
        }

        private static string Rows(StringBuilder text, IReadOnlyList<PowerUpResult> rows, Func<PowerUpResult, string> line)
        {
            if (rows == null || rows.Count == 0) return text.Append("\n—").ToString();
            int shown = rows.Count <= MaxRows ? rows.Count : MaxRows - 1;
            for (int i = 0; i < shown; i++)
            {
                text.Append('\n');
                if (rows[i].Consumed) text.Append(Consumed);
                text.Append(line(rows[i]));
                if (rows[i].Consumed) text.Append("</color>");
            }
            if (shown < rows.Count) text.Append($"\nи ещё {rows.Count - shown}");
            return text.ToString();
        }
    }
}
