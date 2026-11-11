using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;
using ThornThrone.Simulation;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// Text of a choice card (Mechanics §33 Р10–Р12): name, «Новое» / «Уровень r -> r+1» / «МАКС.», rarity and the
    /// changes in the format of DOTA_Survivors_PowerupUpgradeDetails «{attribute_name}: {current} -> {upgraded}».
    /// Attribute names come from the original survivors_attributes_russian.txt, the percent suffix from
    /// m_bPercentage of survivors_attributes.json. Final card layout is Task 35.
    /// </summary>
    public static class ChoiceText
    {
        /// <summary>m_sLocName of the attributes in upgrade records (NestOfThorns/resource/localization/vdata/survivors_attributes).</summary>
        private static readonly Dictionary<string, string> attributeNames = new Dictionary<string, string>
        {
            { "ActiveAbilitySlots", "Доп. ячейки способностей" }, { "AoEIncrease", "Увеличение радиусов" },
            { "Armor", "Сопротивление урону" }, { "BonusPhysicalDamage", "Доп. физический урон" },
            { "CooldownReductionMultiplier", "Уменьшение перезарядок" }, { "Cooldown", "Перезарядка" },
            { "CriticalStrike_Physical_Chance", "Шанс критического урона" },
            { "CriticalStrike_Physical_DamageMultiplier", "Критический физический урон" },
            { "CriticalStrike_Projectile_Chance", "Шанс критического урона снарядами" },
            { "CriticalStrike_Projectile_DamageMultiplier", "Критический урон снарядами" },
            { "DamageMultiplier", "Урон" }, { "DamageMultiplier_Magical", "Множитель маг. урона" },
            { "DamageMultiplier_Physical", "Множитель физ. урона" }, { "DamageReflection", "Отражение урона" },
            { "DamageTicks", "Число срабатываний" }, { "Damage_Magical", "Магический урон" },
            { "Damage_Physical", "Физический урон" }, { "DurationExtension", "Доп. длительность эффектов" },
            { "Duration", "Длительность" }, { "ExpMultiplier", "Множитель опыта" },
            { "FreezeDuration", "Длит. приморозки" }, { "HPRegen", "Восстановление здоровья" },
            { "IncomingDamageAmplification", "Бонус к урону" }, { "KnockbackDistanceMultiplier", "Усиление отталкивания" },
            { "KnockbackDistance", "Дальность отталкивания" }, { "Lives", "Эгида" }, { "Luck", "Удача" },
            { "MagicDamageWeakensPhysicalDamageResistance_Duration", "Длительность снижения физ. сопротивления от маг. урона" },
            { "MagicDamageWeakensPhysicalDamageResistance_Percentage", "Снижение физ. сопротивления от маг. урона" },
            { "MaxHP", "Макс. здоровье" }, { "MaxRerolls", "Замены" }, { "ModifierDamagePerTick", "Маг. урон за срабатывание" },
            { "MovementSpeed", "Скорость передвижения" }, { "PickupRadius", "Радиус подбора" },
            { "ProjectileBounces", "Отскоки снарядов" }, { "ProjectileHitCount", "Число пробитий" },
            { "ProjectileSpeed", "Скорость снарядов" }, { "Projectile_BonusMagicDamage", "Доп. урон от снарядов" },
            { "Radius", "Радиус" }, { "Range", "Дальность" }, { "SlowDuration", "Длительность замедления" },
            { "StampedeMovementSpeed", "Скорость при срабатывании" }, { "StunDuration", "Длительность оглушения" },
            { "TargetCount", "Число снарядов" }, { "TimeBetweenTicks", "Интервал срабатываний" }, { "Width", "Ширина" },
        };

        /// <summary>DOTA_Survivors_NewPowerup.</summary>
        public const string New = "Новое";
        /// <summary>DOTA_Survivors_MaxLevel.</summary>
        public const string MaxLevel = "МАКС.";

        /// <summary>Rarity line; the data has only RARITY_COMMON, its Russian name is a design decision (Р12).</summary>
        public static string Rarity(UpgradeRarity rarity) => "Обычное";

        public static string AttributeName(int attribute)
        {
            string key = Attr.Name(attribute);
            return attributeNames.TryGetValue(key, out var name) ? name : key;
        }

        /// <summary>m_bPercentage of the attribute definition: the value is shown ×100 with «%».</summary>
        public static bool IsPercentage(IReadOnlyDictionary<string, JObject> definitions, int attribute) =>
            definitions != null && definitions.TryGetValue(Attr.Prefix + Attr.Name(attribute), out var o)
            && o["m_bPercentage"] is JValue v && v.Type == JTokenType.Boolean && (bool)v;

        public static string Value(float value, bool percentage) => percentage
            ? (value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%"
            : value.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>«{attribute_name}: {current} -> {upgraded}» (DOTA_Survivors_PowerupUpgradeDetails).</summary>
        public static string Change(AttributeChange change, bool percentage) =>
            $"{AttributeName(change.Attribute)}: {Value(change.Current, percentage)} -> <b>{Value(change.Next, percentage)}</b>";

        /// <summary>Rank line: «Новое», «Уровень r -> r+1 / max» (DOTA_Survivors_LevelUpgradeInfoLong) or «МАКС.».</summary>
        public static string Header(ChoiceOffer offer)
        {
            switch (offer.Kind)
            {
                case OfferKind.New: return New;
                case OfferKind.Minor: return MaxLevel;
                default: return $"Уровень {offer.Rank} -> <b>{offer.NextRank}</b> / {offer.Def.MaxLevel}";
            }
        }

        /// <param name="name">Localized powerup name.</param>
        /// <param name="description">Localized description, shown on a new active ability (Т5.5).</param>
        public static string Format(ChoiceOffer offer, string name, string description, IReadOnlyDictionary<string, JObject> attributes)
        {
            var sb = new StringBuilder();
            sb.Append("<b>").Append(name).Append("</b>\n");
            sb.Append(Header(offer)).Append('\n');
            sb.Append(Rarity(offer.Rarity)).Append("\n\n");
            if (offer.Kind == OfferKind.New && offer.Def.IsActive && offer.ChangeCount == 0)
            {
                sb.Append(description);
                return sb.ToString();
            }
            for (int i = 0; i < offer.ChangeCount; i++)
            {
                var change = offer.Changes[i];
                sb.Append(Change(change, IsPercentage(attributes, change.Attribute))).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Fallback card: the powerup «gold» and its amount (Mechanics §33 Р8–Р9).</summary>
        public static string Fallback(string goldName, int gold) =>
            $"<b>{goldName}</b>\n\n+{gold} золота\n\nВсе улучшения получены";
    }
}
