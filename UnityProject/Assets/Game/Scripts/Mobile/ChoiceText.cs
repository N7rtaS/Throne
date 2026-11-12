using System;
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
        /// <summary>DOTA_AbilityTooltip_Aghs_Scepter in Russian patch notes (Д); header of a scepter card (Mechanics §34 Р5).</summary>
        public const string Scepter = "Скипетр Аганима";
        /// <summary>The Russian localization keeps «Aghanim's Shard» in English; text of the project (Р5).</summary>
        public const string Shard = "Осколок Аганима";
        /// <summary>DOTA_Survivors_CombinedItem: type line of a recipe card (Mechanics §12.4, §35 Р5).</summary>
        public const string CombinedItem = "Комбинация предметов";
        /// <summary>DOTA_Survivors_CombinedItemTooltip, followed by the component names.</summary>
        public const string CombinedItemTooltip = "Сочетает:";

        /// <summary>«Сочетает: A, B» of a recipe card; empty without names.</summary>
        public static string Combines(IReadOnlyList<string> names)
        {
            if (names == null || names.Count == 0) return "";
            var sb = new StringBuilder(CombinedItemTooltip).Append(' ');
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(names[i]);
            }
            return sb.ToString();
        }

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
                case OfferKind.Scepter: return Scepter;
                case OfferKind.Shard: return Shard;
                case OfferKind.Recipe: return CombinedItem;
                default: return $"Уровень {offer.Rank} -> <b>{offer.NextRank}</b> / {offer.Def.MaxLevel}";
            }
        }

        /// <param name="name">Localized powerup name.</param>
        /// <param name="description">
        /// Localized description, shown on a new active ability (Т5.5); on a scepter or shard card the Aghanim text with
        /// its numbers (<see cref="Substitute"/>, <see cref="ShardText"/>).
        /// </param>
        /// <param name="combines">«Сочетает: …» line of a recipe card (<see cref="Combines"/>), shown before the changes.</param>
        public static string Format(ChoiceOffer offer, string name, string description, IReadOnlyDictionary<string, JObject> attributes,
            string combines = null)
        {
            var sb = new StringBuilder();
            sb.Append("<b>").Append(name).Append("</b>\n");
            sb.Append(Header(offer)).Append('\n');
            sb.Append(Rarity(offer.Rarity)).Append('\n');
            // An ability that already has Aghanim shows it on its cards (ТЗ_Полная_игра_Unity.md §6.3, Т7.1).
            if (offer.HasScepter) sb.Append(Scepter).Append('\n');
            if (offer.HasShard) sb.Append(Shard).Append('\n');
            sb.Append('\n');
            bool aghanim = offer.Kind == OfferKind.Scepter || offer.Kind == OfferKind.Shard;
            if (aghanim) sb.Append(description).Append('\n');
            if (!string.IsNullOrEmpty(combines)) sb.Append(combines).Append('\n');
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

        /// <summary>
        /// Replaces the placeholders «{f:N:name}» of a localized text with <paramref name="value"/>(name) shown with N
        /// decimals; an unknown name or form stays as it is (Mechanics §34 Р5).
        /// </summary>
        public static string Substitute(string template, Func<string, float?> value)
        {
            if (string.IsNullOrEmpty(template)) return template ?? "";
            var sb = new StringBuilder(template.Length);
            int at = 0;
            while (at < template.Length)
            {
                int open = template.IndexOf('{', at);
                int close = open < 0 ? -1 : template.IndexOf('}', open);
                if (close < 0) { sb.Append(template, at, template.Length - at); break; }
                sb.Append(template, at, open - at);
                string token = template.Substring(open + 1, close - open - 1);
                var parts = token.Split(':');
                float? v = parts.Length == 3 && parts[0] == "f" && int.TryParse(parts[1], out _) ? value(parts[2]) : null;
                if (v.HasValue) sb.Append(v.Value.ToString("F" + parts[1], CultureInfo.InvariantCulture));
                else sb.Append('{').Append(token).Append('}');
                at = close + 1;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Numbers of the scepter texts (Mechanics §34 Р5): damage_per_enemy and extra_damage (Magic Missile),
        /// counter_chance (Counter Helix, %), speed_damage_multiplier and actual_damage_multiplier (Stampede, % now).
        /// </summary>
        public static float? ScepterValue(Run run, PowerUpDef def, string name)
        {
            switch (name)
            {
                case "damage_per_enemy": return Field(def, "m_flScepterDamageIncreasePerEnemyKilled");
                case "extra_damage":
                    var owned = run.Build.Find(def);
                    return owned?.Ability is MagicMissile missile ? missile.ScepterBonus : 0;
                case "counter_chance": return Field(def, "m_flChanceToCounter") * 100;
                case "speed_damage_multiplier": return Field(def, Stampede.ScepterField) * 100;
                case "actual_damage_multiplier": return Field(def, Stampede.ScepterField) * run.HeroSpeed;
                default: return null;
            }
        }

        private static float? Field(PowerUpDef def, string field) =>
            def.Raw?[field] is JValue v && (v.Type == JTokenType.Float || v.Type == JTokenType.Integer) ? (float?)(float)v : null;

        /// <summary>Shard card: the data has no m_sLocShardAbilityDesc, so «Урон: ×1.25» from m_flShardDamageMultiplier (text of the project, Р5).</summary>
        public static string ShardText(PowerUpDef def)
        {
            float multiplier = Field(def, "m_flShardDamageMultiplier") ?? 0;
            return "Урон: ×<b>" + (1 + multiplier).ToString("0.##", CultureInfo.InvariantCulture) + "</b>";
        }

        /// <summary>Fallback card: the powerup «gold» and its amount (Mechanics §33 Р8–Р9).</summary>
        public static string Fallback(string goldName, int gold) =>
            $"<b>{goldName}</b>\n\n+{gold} золота\n\nВсе улучшения получены";
    }
}
