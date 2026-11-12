using System;
using System.Collections.Generic;
using System.Text;
using ThornThrone.App;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// Texts of the local records (Mechanics §39 З4, З5): the main menu table that replaces the client's friends
    /// leaderboard («Друзья / Счёт», LeaderboardContainerFriends) and the line of an echelon card. Pure functions, built
    /// when the screen opens.
    /// </summary>
    public static class RecordsText
    {
        public const string Empty = "Пока нет результатов";

        /// <summary>«Рекорды / Счёт», then «N. Герой, эшелон K, победа|поражение» with the score on the right.</summary>
        public static string Menu(IReadOnlyList<RecordRow> rows, Func<string, string> heroTitle)
        {
            var text = new StringBuilder("<b>Рекорды<pos=78%>Счёт</b>");
            if (rows == null || rows.Count == 0) return text.Append('\n').Append(Empty).ToString();
            for (int i = 0; i < rows.Count; i++)
            {
                var result = rows[i].Result;
                text.Append('\n').Append(i + 1).Append(". ").Append(heroTitle(result.Hero))
                    .Append(", эшелон ").Append(rows[i].Echelon)
                    .Append(result.Victory ? ", победа" : ", поражение")
                    .Append("<pos=78%>").Append(ResultsText.Integer(result.Score));
            }
            return text.ToString();
        }

        /// <summary>«Рекорд 330 000, побед 1» when the hero played the echelon, otherwise empty.</summary>
        public static string Card(HeroEchelonRecord record)
        {
            if (record == null || record.Runs == 0) return "";
            return $"Рекорд {ResultsText.Integer(record.BestScore)}, побед {record.Victories}";
        }
    }
}
