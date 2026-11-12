using ThornThrone.App;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// Texts of the «Улучшения» screen (Mechanics §40 У2–У9): the client's DOTA_Survivors_* strings of
    /// dota_russian.txt with the values of <see cref="MetaUpgradeRow"/>. Pure functions: built when the screen opens
    /// and after a purchase, not per frame.
    /// </summary>
    public static class UpgradesText
    {
        /// <summary>DOTA_Survivors_UpgradesTitle.</summary>
        public const string Title = "Улучшения";
        /// <summary>DOTA_Survivors_UpgradesDescription.</summary>
        public const string Description = "Собирайте золото, повышайте свои характеристики, убивайте ещё больше врагов.";
        /// <summary>DOTA_Survivors_Upgrade: the buy button.</summary>
        public const string Buy = "Купить";
        /// <summary>DOTA_Survivors_UpgradesAvailable.</summary>
        public const string Available = "Доступны улучшения!";
        /// <summary>DOTA_Survivors_UpgradesDifficultyButton: the results button to the difficulty screen (У11).</summary>
        public const string DifficultyButton = "Улучшения и сложность";
        /// <summary>У9: a purchase that could not be written to the profile (text of the project, Р).</summary>
        public const string SaveError = "Не удалось сохранить покупку";
        /// <summary>«Назад» of the screen.</summary>
        public const string Back = "Назад";

        /// <summary>DOTA_Survivors_TotalGold with the profile currency, thousands with a space.</summary>
        public static string TotalGold(long currency) => "Всего золота " + ResultsText.Integer(currency);

        /// <summary>
        /// У5: «current -> <b>next</b>» (DOTA_Survivors_AttributeUpgradeDetails), 0 before the first level;
        /// У6: «<b>+value</b>» of the last level (DOTA_Survivors_AttributeMaxUpgradeDetails). m_bPercentage ×100 with «%».
        /// </summary>
        public static string Value(MetaUpgradeRow row)
        {
            bool percentage = row.Def.Percentage;
            if (row.IsMax) return "<b>+" + ChoiceText.Value(row.Current, percentage) + "</b>";
            return ChoiceText.Value(row.Current, percentage) + " -> <b>" + ChoiceText.Value(row.Next.Value, percentage) + "</b>";
        }

        /// <summary>DOTA_Survivors_Cost with the price of the next level, or «МАКС.» at the last level (У6).</summary>
        public static string Cost(MetaUpgradeRow row) =>
            row.IsMax ? ChoiceText.MaxLevel : "Стоимость " + ResultsText.Integer(row.Cost);

        /// <summary>У7: DOTA_Survivors_GoldNeeded «Требуется <b>N</b> золота», N — the currency the next level lacks.</summary>
        public static string GoldNeeded(long shortage) => "Требуется <b>" + ResultsText.Integer(shortage) + "</b> золота";
    }
}
