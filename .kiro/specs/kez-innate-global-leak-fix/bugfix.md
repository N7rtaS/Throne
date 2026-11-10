# Документ требований к исправлению

## Introduction

У Kez все атрибуты врождённой способности попадают в атрибуты героя. `Build.Recalculate` для любой врождённой (`m_bIsInnate = true`) добавляет её `BaseAttributes` в `Build.Global`. Для `skywrath_mage_innate` (501, Dragonus) и `vengeful_spirit_innate` (500, Shen) это не важно: у них класс `CSurvivorsPowerUpDefinition` без собственного действия и нет `m_vecBaseAttributes`, а бонусы заданы в `m_vecMinorUpgradeChoices[0].m_vecGlobalUpgradeAttributes`. У `kez_innate` (502) собственный класс `CSurvivorsPowerUpDefinition_InstantAttack`, а в `m_vecBaseAttributes` лежат параметры её атаки: `MovementSpeed` 20, `TargetCount` 1, `Damage_Magical` 0, `Cooldown` 6, `Range` 1500, `Radius` 180, `VulnerabilityDamagePercent` 2, `VulnerabilityDuration` 5.5, `VulnerabilityStunOnHitDuration` 1. Стат способности равен `Local + Global` (`Build.Stat`), поэтому эти числа прибавляются ко всем способностям Kez: +1500 к дальности, +1 цель, +180 к радиусу и т. д. Скорость Kez становится 300 вместо базовых 280.

Исправление: врождённая с собственным классом считается способностью героя. Её атрибуты остаются только в `Local`, а `Run.CreateAbilities` создаёт для неё `IAbility` через `AbilityFactory`. `InstantAttack` будет реализован в Task 19. До этого для `kez_innate` выдаётся диагностика `ability:CSurvivorsPowerUpDefinition_InstantAttack`, и Kez не получает от врождённой никаких бонусов.

Решение владельца (Task 3): все атрибуты `kez_innate`, включая `MovementSpeed` 20, относятся к её атаке, а не к герою. Локализация («Кез передвигается быстрее…») допускает, что +20 к скорости — бонус герою. Поэтому правило записывается в `Mechanics.md` §3 с меткой **Р**. Пока нет Task 19, Kez бегает со скоростью 280. Бонус скорости вернётся через способность.

Обозначения:
- «врождённая без собственного класса» — определение с `m_bIsInnate = true` и `_class == "CSurvivorsPowerUpDefinition"`;
- «врождённая с собственным классом» — определение с `m_bIsInnate = true` и любым другим `_class` (сейчас только `kez_innate`);
- «базовые атрибуты героя» — `m_vecBaseAttributes` героя из `survivors_heroes.json`.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN строится билд героя, у которого врождённая с собственным классом (Kez, `kez_innate`), THEN the system добавляет её `BaseAttributes` в `Build.Global`: у Kez `Global[Range]` = 1500, `Global[TargetCount]` = 1, `Global[Radius]` = 180, `Global[Cooldown]` = 6, `Global[MovementSpeed]` = 300, а также ненулевые `VulnerabilityDamagePercent`, `VulnerabilityDuration`, `VulnerabilityStunOnHitDuration`

1.2 WHEN способность Kez считает стат через `Build.Stat(owned, attribute)`, THEN the system прибавляет к её собственному значению атрибуты `kez_innate` (например, +1500 к дальности и +1 к числу целей)

1.3 WHEN создаются способности забега Kez (`Run.CreateAbilities`), THEN the system не пытается создать `IAbility` для `kez_innate` и не выдаёт диагностику о том, что `CSurvivorsPowerUpDefinition_InstantAttack` не реализован

### Expected Behavior (Correct)

2.1 WHEN строится билд героя, у которого врождённая с собственным классом, THEN the system SHALL помещать её `BaseAttributes` только в `Local` этой врождённой и не добавлять в `Build.Global` ничего из этого определения; у Kez значения `Global` по всем атрибутам `kez_innate` (`MovementSpeed`, `TargetCount`, `Damage_Magical`, `Cooldown`, `Range`, `Radius`, `VulnerabilityDamagePercent`, `VulnerabilityDuration`, `VulnerabilityStunOnHitDuration`) SHALL равняться базовым атрибутам героя (`MovementSpeed` = 280, остальные 0)

2.2 WHEN способность Kez считает стат через `Build.Stat`, THEN the system SHALL складывать её `Local` только с `Global`, в который не входят атрибуты `kez_innate`

2.3 WHEN создаются способности забега и у героя есть врождённая с собственным классом, THEN the system SHALL запрашивать для неё `IAbility` у `AbilityFactory`

2.4 WHEN `AbilityFactory` не знает класс врождённой (сейчас `CSurvivorsPowerUpDefinition_InstantAttack`), THEN the system SHALL выдать диагностику с ключом `ability:CSurvivorsPowerUpDefinition_InstantAttack`, оставить `Ability` пустым и не давать герою бонусов от этой врождённой

### Unchanged Behavior (Regression Prevention)

3.1 WHEN строится билд героя, у которого врождённая без собственного класса (Dragonus — `skywrath_mage_innate`, Shen — `vengeful_spirit_innate`), THEN the system SHALL CONTINUE TO добавлять в `Build.Global` её `BaseAttributes` и `Minor[0].Global`

3.2 WHEN начинается забег Dragonus, THEN the system SHALL CONTINUE TO давать 80 HP (100 − 20 от врождённой) и 2 цели у Arcane Bolt на ранге 1 (1 + 1 от врождённой); тест `DragonusStartsWithInnateBonuses` проходит

3.3 WHEN начинается забег Shen, THEN the system SHALL CONTINUE TO строить тот же `Build.Global`, что до исправления, а Magic Missile SHALL CONTINUE TO иметь прежние урон, перезарядку и остальные статы

3.4 WHEN строится билд любого героя, THEN the system SHALL CONTINUE TO не занимать врождённой активный или пассивный слот и не предлагать её при выборе улучшения, переброске и выдаче предметов (`CountSlots`, `CanUpgrade`, `GenerateChoices`)

3.5 WHEN определение не врождённое (активные способности, пассивные предметы, рецепты, золото), THEN the system SHALL CONTINUE TO считать `Local` и `Global` по прежним правилам: пассивные — `Minor[0].Global` × ранг, активные — `BaseAttributes` в `Local` плюс authored-улучшения с учётом `Rules.AuthoredUpgradeOffset`

3.6 WHEN у Kez строится билд, THEN the system SHALL CONTINUE TO брать из базовых атрибутов героя рывок (2 заряда, перезарядка 3 с), HP 100 и остальные атрибуты, не входящие в `kez_innate`

3.7 WHEN выполняются шаги симуляции, THEN the system SHALL CONTINUE TO не выделять управляемую память в горячем цикле

3.8 WHEN запускаются существующие тесты `dotnet test DotNet/ThornThrone.Simulation.Tests`, THEN the system SHALL CONTINUE TO проходить все 66 тестов
