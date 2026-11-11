# Врождённая Shen, Shen играбелен (Task 20)

Источники: `Research/Json/survivors_powerups.json` (`vengeful_spirit_innate`, ID 500), `survivors_heroes.json` (`shen`), `Research/FullGame/Descriptions.md` («Нанося магический урон, Шен снижает физическое сопротивление жертв»), `Mechanics.md` §24 (Р4, Р8, Р12), §25 Р5, §26.

Данные (**Д**): у Shen врождённая 500 без собственного класса; её `m_vecMinorUpgradeChoices[0]` даёт в `Build.Global` `MagicDamageWeakensPhysicalDamageResistance_Percentage` 0.3 и `MagicDamageWeakensPhysicalDamageResistance_Duration` 5. Стартовая способность — Magic Missile (ID 10, `Damage_Magical`).

## Требования

1. КОГДА враг получает магический урон и остаётся жив, а у героя `MagicDamageWeakensPhysicalDamageResistance_Percentage` > 0 и `_Duration` > 0, СИСТЕМА ДОЛЖНА наложить на врага ослабление силой Percentage на Duration + `DurationExtension` секунд через `Run.Weaken`.
2. КОГДА ослабленный враг получает физический урон, СИСТЕМА ДОЛЖНА умножить урон на `1 + сила` (Mechanics §24 Р4): удар 10 даёт 13.
3. КОГДА враг получает физический или чистый урон, СИСТЕМА НЕ ДОЛЖНА накладывать ослабление.
4. КОГДА ослабленный враг снова получает магический урон, СИСТЕМА ДОЛЖНА обновить ослабление по §24 Р12 (большая сила, большая длительность).
5. КОГДА проходит длительность ослабления, СИСТЕМА ДОЛЖНА снять его; пауза и выбор улучшения таймер останавливают (§24 Р14).
6. КОГДА герой — Dragonus или Kez (атрибутов нет), СИСТЕМА НЕ ДОЛЖНА ослаблять врагов магическим уроном.
7. КОГДА Shen в забеге с ботом стреляет Magic Missile, СИСТЕМА ДОЛЖНА ослаблять поражённых врагов и не выделять память в шаге после прогрева.
8. КОГДА игрок открывает экран выбора героя, СИСТЕМА ДОЛЖНА давать выбрать Shen (без пометки «в разработке»); Shen показан временной капсулой.
9. КОГДА smoke запущен с `-thornHero=shen`, СИСТЕМА ДОЛЖНА провести забег за Shen (строка `THORN_MOBILE_SMOKE hero=shen`, kills > 0).

## Критерии приёмки
- Новые тесты симуляции `ShenInnateTests` проходят, весь `dotnet test` зелёный.
- EditMode: `MobileGame.PlayableHeroes` = dragonus, shen, kez; Shen рисуется капсулой.
- Smoke `Tools/mac-smoke.sh -thornHero=shen` завершается кодом 0.
