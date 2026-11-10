# Врождённая Kez не попадает в глобальные атрибуты — проект

## Overview

`Build.Recalculate` обрабатывает все врождённые определения одинаково: `BaseAttributes` и `Minor[0].Global` добавляются в `Build.Global`. У `kez_innate` (502) класс `CSurvivorsPowerUpDefinition_InstantAttack`, и в `m_vecBaseAttributes` лежат параметры её собственной атаки (`Range` 1500, `TargetCount` 1, `Radius` 180, `Cooldown` 6, `MovementSpeed` 20 и уязвимость). Через `Build.Stat = Local + Global` эти числа прибавляются к каждой способности Kez, а скорость героя становится 300 вместо 280.

Исправление разделяет врождённые по классу. Врождённая без собственного класса (`_class == "CSurvivorsPowerUpDefinition"`, Dragonus и Shen) по-прежнему даёт герою `BaseAttributes` и `Minor[0].Global`. Врождённая с собственным классом считается способностью героя: её `BaseAttributes` остаются только в `OwnedPowerUp.Local`, а `Run.CreateAbilities` запрашивает для неё `IAbility` у `AbilityFactory`. Класса `InstantAttack` в фабрике пока нет (Task 19), поэтому срабатывает существующая диагностика с ключом `ability:CSurvivorsPowerUpDefinition_InstantAttack`, `Ability` остаётся `null`, и Kez не получает от врождённой ничего. `PowerUpDef.IsActive` сохраняет прежний результат: от него зависят `CountSlots`, `CanUpgrade`, `GenerateChoices` и `Run.IsImplemented`.

## Glossary

- **Bug_Condition (C)**: во владении героя есть определение с `IsInnate = true` и собственным классом (`Class != "CSurvivorsPowerUpDefinition"`). Сейчас это только `kez_innate`.
- **Property (P)**: атрибуты такого определения есть только в его `Local`; `Build.Global` их не содержит; `CreateAbilities` пытается создать для него `IAbility`, а при неизвестном классе выдаёт диагностику.
- **Preservation**: билд всех героев и определений, для которых C ложно: врождённые без собственного класса, активные, пассивные, рецепты, золото; слоты и выбор улучшений; базовые атрибуты героя; отсутствие выделений.
- **`Build.Recalculate`**: метод в `UnityProject/Assets/Game/Scripts/Simulation/Build/Build.cs`; заново собирает `Global` (база героя плюс вклады владений) и `Local` каждого `OwnedPowerUp`. Вызывается из конструктора `Build` и из `Upgrade`, не на каждом шаге.
- **`Build.Stat(owned, attribute)`**: стат способности, `owned.Local[attribute] + Global[attribute]`.
- **`Run.CreateAbilities`**: метод в `UnityProject/Assets/Game/Scripts/Simulation/Run.cs`; для каждого владения без `Ability` создаёт её через `AbilityFactory.Create`, иначе вызывает `Diagnostics.Report("ability:" + Class, …)`. Вызывается из конструктора `Run` и из `OnBuildChanged`.
- **`AbilityFactory`**: таблица «`_class` → конструктор `IAbility`» в `Abilities/Abilities.cs`; сейчас знает `CSurvivorsPowerUpDefinition_ArcaneBolt` и `CSurvivorsPowerUpDefinition_MagicMissile`.
- **`PowerUpDef.Class`**: типизированное поле определения (`Content/Definitions.cs`), заполняется в `GameData` из `_class`.
- **Врождённая без собственного класса**: `IsInnate && Class == "CSurvivorsPowerUpDefinition"` (`skywrath_mage_innate` 501, `vengeful_spirit_innate` 500).
- **Врождённая с собственным классом**: `IsInnate && Class != "CSurvivorsPowerUpDefinition"` (`kez_innate` 502).
- **Базовые атрибуты героя**: `HeroDef.BaseAttributes` из `survivors_heroes.json`; у Kez `MaxHP` 100, `MovementSpeed` 280, `PickupRadius` 250, `DashSpeed` 1300, `DashDuration` 0.25, `NumDashes` 2, `DashCooldown` 3, `MaxRerolls` 2, `Luck` 0.05.

## Bug Details

### Bug Condition

Ошибка возникает при каждом `Recalculate` у героя, который владеет врождённой с собственным классом. Ветка `if (def.IsInnate)` не смотрит на класс и выполняет `Global.Add(def.BaseAttributes)`. Параллельно `CreateAbilities` пропускает такое определение, потому что `IsActive` для врождённых всегда `false`, и о нереализованном классе никто не сообщает.

**Formal Specification:**
```
FUNCTION isBugCondition(input)
  INPUT: input = (hero, build) — герой из данных и его билд после Recalculate
  OUTPUT: boolean

  RETURN EXISTS owned IN input.build.Owned:
           owned.Def.IsInnate
           AND owned.Def.Class != "CSurvivorsPowerUpDefinition"
END FUNCTION
```

```
FUNCTION expectedBehavior(build, diagnostics)
  FOR EACH owned IN build.Owned WHERE owned.Def.IsInnate AND owned.Def.Class != "CSurvivorsPowerUpDefinition" DO
    ASSERT owned.Local = owned.Def.BaseAttributes
    ASSERT build.Global = referenceGlobal(build) — без вклада этого определения
    IF NOT AbilityFactory.Has(owned.Def.Class) THEN
      ASSERT diagnostics.Has("ability:" + owned.Def.Class)
      ASSERT owned.Ability = null
    ELSE
      ASSERT owned.Ability != null
  END FOR
END FUNCTION
```

`referenceGlobal(build)` — правило из `Build`: база героя + врождённые без собственного класса (`BaseAttributes` + `Minor[0].Global`) + пассивные `Minor[0].Global × Rank` + `Authored[u].Global` активных для `u < Rank − Rules.AuthoredUpgradeOffset`.

### Examples

- Kez, старт забега. Ожидается `Global[Range]` = 0, фактически 1500.
- Kez, старт забега. Ожидается `Global[MovementSpeed]` = 280 (база героя), фактически 300.
- Kez, `Build.Stat(echo_strike, Attr.Range)`. Ожидается `echo_strike.Local[Range]` + 0, фактически на 1500 больше; `Stat(echo_strike, TargetCount)` на 1 больше, `Stat(echo_strike, Radius)` на 180 больше.
- Kez, `run.Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_InstantAttack")`. Ожидается `true`, фактически `false`; при этом `ability:CSurvivorsPowerUpDefinition_EchoStrike` уже выдаётся для стартовой способности.
- Граничный случай: Kez после нескольких `Upgrade` (каждый вызывает `Recalculate` и `OnBuildChanged`). Атрибуты `kez_innate` не появляются в `Global`, диагностика сообщается один раз (ключ дедуплицируется в `Diagnostics.Report`), `Find(kez_innate).Rank` остаётся 1.

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- Dragonus и Shen: врождённая без собственного класса добавляет в `Global` свои `BaseAttributes` (сейчас пустые) и `Minor[0].Global` (3.1). Dragonus стартует с 80 HP и 2 целями у Arcane Bolt; `DragonusStartsWithInnateBonuses` проходит (3.2).
- Shen: `Global` совпадает с прежним, включая `MagicDamageWeakensPhysicalDamageResistance_Percentage` 0.3 и `_Duration` 5; урон, перезарядка и остальные статы Magic Missile прежние; `MagicMissileTests` проходят (3.3).
- Врождённые не занимают слот и не предлагаются: `CountSlots`, `CanUpgrade`, `GenerateChoices`, `Reroll` не меняются, потому что `IsActive` даёт прежний результат для всех 47 определений (3.4).
- Не врождённые определения считаются по прежним правилам: пассивные — `Minor[0].Global × Rank`; активные — `BaseAttributes` в `Local` и authored-улучшения с `Rules.AuthoredUpgradeOffset` (3.5).
- Kez получает из базы героя 2 заряда рывка, перезарядку 3 с, 100 HP и остальные атрибуты, не входящие в `kez_innate` (3.6).
- В шаге симуляции нет новых выделений: `Recalculate` и `CreateAbilities` выполняются только при изменении билда, новое условие — сравнение строк и чтение полей (3.7).
- Все 66 существующих тестов проходят (3.8).

**Scope:**
Исправление затрагивает только определения с `IsInnate && Class != "CSurvivorsPowerUpDefinition"`. Не затрагиваются:
- `skywrath_mage_innate`, `vengeful_spirit_innate` и любые будущие врождённые без собственного класса;
- активные, пассивные, рецепты, `gold`, `pile_of_coal`;
- `Run.IsImplemented`, правила слотов, веса и генерация выбора;
- представление (`MobileGame`, `WorldRenderer`, `GameUi`): оно читает `Owned[i].Ability` и `Build.Global` как раньше.

## Hypothesized Root Cause

1. **Ветка врождённой не различает класс.** В `Recalculate` условие `if (def.IsInnate)` безусловно выполняет `Global.Add(def.BaseAttributes)`. Правило в XML-комментарии `Build` («innate (base + first minor global)») было записано по Dragonus, у которого `m_vecBaseAttributes` нет. Это основная причина, она видна в коде.

2. **В данных один и тот же список играет две роли.** У активных классов `m_vecBaseAttributes` — параметры способности (`Local`). У врождённой без класса бонусы герою лежат в `m_vecMinorUpgradeChoices[0].m_vecGlobalUpgradeAttributes`, а `m_vecBaseAttributes` отсутствует. `kez_innate` устроена как активная способность: есть `m_eTargeting`, частицы, `Cooldown`, `Range`, и нет `m_vecMinorUpgradeChoices`. Значит, её `m_vecBaseAttributes` — параметры атаки, а не бонусы герою.

3. **`CreateAbilities` фильтрует по `IsActive`.** `IsActive` исключает все врождённые, поэтому для `kez_innate` не вызывается `AbilityFactory.Create` и не выдаётся диагностика. Ошибка не была заметна: у Kez нет реализованных способностей вообще (стартовая `echo_strike` тоже не реализована), а скорость 300 против 280 на глаз не отличается.

4. **Строка класса без собственного действия записана один раз в выражении `IsActive`.** Чтобы новое условие не дублировало литерал `"CSurvivorsPowerUpDefinition"`, его нужно вынести в именованную константу рядом с определением.

## Correctness Properties

Property 1: Bug Condition - Атрибуты врождённой с собственным классом остаются в её Local

_For any_ героя из данных, у которого во владении есть врождённая с собственным классом (isBugCondition возвращает true), и для любой последовательности `Upgrade` по нескольким seed исправленный `Build` SHALL держать `BaseAttributes` этой врождённой только в её `Local`: `Build.Global` по каждому атрибуту равен `referenceGlobal` без её вклада (у Kez на старте `MovementSpeed` = 280, `Range`, `TargetCount`, `Radius`, `Cooldown`, `Damage_Magical`, `VulnerabilityDamagePercent`, `VulnerabilityDuration`, `VulnerabilityStunOnHitDuration` = 0), а `Build.Stat(owned, a)` любой другой способности равен `owned.Local[a] + Global[a]` без атрибутов врождённой.

**Validates: Requirements 2.1, 2.2**

Property 2: Bug Condition - Врождённая с собственным классом создаётся как способность

_For any_ героя с врождённой с собственным классом исправленный `Run` SHALL запросить для неё `IAbility` у `AbilityFactory`; если класс неизвестен фабрике, `Diagnostics.Has("ability:" + Class)` равно `true`, `Ability` равно `null`, и герой не получает от неё бонусов. Для Kez ключ — `ability:CSurvivorsPowerUpDefinition_InstantAttack`.

**Validates: Requirements 2.3, 2.4**

Property 3: Preservation - Билд без врождённой с собственным классом не меняется

_For any_ героя и билда, где isBugCondition ложно (Dragonus, Shen), и для любой последовательности выборов улучшений по нескольким seed исправленный `Build` SHALL давать тот же `Global`, те же `Local`, те же слоты и те же списки выбора, что исходный; для всех героев врождённая не занимает слот, не предлагается и остаётся на ранге 1; базовые атрибуты героя, не входящие во врождённую, сохраняются; шаги симуляции не выделяют память.

**Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8**

## Fix Implementation

### Changes Required

**File**: `UnityProject/Assets/Game/Scripts/Simulation/Content/Definitions.cs`

**Type**: `PowerUpDef`

**Specific Changes**:
1. **Константа класса.** Добавить `public const string PlainClass = "CSurvivorsPowerUpDefinition";` с `/// <summary>`: `_class` определения без собственного поведения (набор атрибутов).
2. **Собственный класс.** Добавить `public bool HasOwnClass => Class != PlainClass;`. Класс уже доступен в типизированном определении как поле `Class`; отдельное поле в `GameData` не нужно.
3. **`IsActive`.** Заменить литерал на `HasOwnClass`: `!IsPassive && !IsInnate && !IsGold && HasOwnClass`. Результат для всех определений тот же, литерал хранится в одном месте.
4. **Врождённая способность.** Добавить `public bool IsInnateAbility => IsInnate && HasOwnClass;` с `/// <summary>`: врождённая с собственным классом — способность героя; её атрибуты — параметры атаки, не бонусы герою (Mechanics §3, **Р**).

**File**: `UnityProject/Assets/Game/Scripts/Simulation/Build/Build.cs`

**Function**: `Recalculate`

5. **Ветка врождённой.** Добавлять в `Global` только для врождённой без собственного класса:
   ```csharp
   if (def.IsInnate)
   {
       // An innate with its own class is the hero's ability: its attributes stay local (Mechanics §3, Р).
       if (!def.IsInnateAbility)
       {
           Global.Add(def.BaseAttributes);
           if (def.Minor.Length > 0) Global.Add(def.Minor[0].Global);
       }
   }
   ```
   `owned.Local.Add(def.BaseAttributes)` выше остаётся для всех определений. Ветки пассивных и активных не меняются.
6. **XML-комментарий `Build`.** Уточнить формулу: `innate without its own class (base + first minor global)`; врождённая с собственным классом — способность, её атрибуты только в `Local`.

**File**: `UnityProject/Assets/Game/Scripts/Simulation/Run.cs`

**Function**: `CreateAbilities`

7. **Условие создания.** Отдельное условие «активная или врождённая с собственным классом»:
   ```csharp
   if (owned.Ability != null || !(owned.Def.IsActive || owned.Def.IsInnateAbility)) continue;
   ```
   Создание и диагностика остаются прежними: `AbilityFactory.Create(owned)`; при `null` — `Diagnostics.Report("ability:" + owned.Def.Class, …)`. Для Kez получаем `ability:CSurvivorsPowerUpDefinition_InstantAttack`. Склейка строки ключа при каждом `OnBuildChanged` для нереализованной способности уже есть у `echo_strike`; это изменение билда, а не шаг, требование 3.7 не затрагивается.

8. **Чего не трогать.** `IsActive` (по результату), `CountSlots`, `CanUpgrade`, `GenerateChoices`, `Run.IsImplemented`, `AbilityFactory`, `SupportedHeroAttributes`, представление. `InstantAttack` реализуется в Task 19.

**File**: `Research/FullGame/Mechanics.md`, §3

9. **Правило с меткой Р.** После строки о врождённой Драгонуса: врождённая без собственного класса даёт герою `m_vecMinorUpgradeChoices[0].m_vecGlobalUpgradeAttributes` (**Д**). Врождённая с собственным классом (`kez_innate`, `CSurvivorsPowerUpDefinition_InstantAttack`) — способность героя; все её `m_vecBaseAttributes`, включая `MovementSpeed` 20, — параметры её атаки, а не атрибуты героя (**Р**, решение владельца, Task 3). Локализация («Кез передвигается быстрее…») допускает, что +20 к скорости — бонус герою. Пока `InstantAttack` не реализован (Task 19), Kez бегает со скоростью 280.

Запись в `Differences.md` не нужна: это гипотеза о поведении оригинала (**Р**), а не сознательное отличие. Отсутствие атаки до Task 19 — незавершённость, она видна в диагностике.

Ограничения: C# 9, netstandard2.1, без `UnityEngine`, nullable выключен, `TreatWarningsAsErrors` — новых предупреждений нет.

## Testing Strategy

### Validation Approach

Сначала на неисправленном коде запускаются тесты исследования (должны упасть и подтвердить причину) и тесты сохранения (должны пройти). Затем вносится исправление, и все тесты, включая 66 существующих, должны пройти. Тесты размещаются в новой фикстуре `KezInnateTests` (`DotNet/ThornThrone.Simulation.Tests/KezInnateTests.cs`, namespace `ThornThrone.Simulation.Tests`), используют `TestData.Load()`, `TestData.NewRun` и реальные данные. Ожидаемые значения берутся из `HeroDef.BaseAttributes` и `PowerUpDef.BaseAttributes`, а не задаются числом, кроме проверки 280 из `Mechanics.md` §3. Тесты должны компилироваться на неисправленном коде, поэтому условие «собственный класс» в тестах проверяется через `Def.Class != "CSurvivorsPowerUpDefinition"`, а не через новые свойства `PowerUpDef`.

Эталон `ReferenceGlobal(run, includeInnateAbility)` в тесте собирает `Global` по правилу из Glossary, проходя `run.Build.Owned`. С `includeInnateAbility = true` он повторяет исходный код, с `false` — исправленный. Сравнение идёт по всем id от 0 до `Attr.Capacity − 1` через `Within(1e-4)`.

### Exploratory Bug Condition Checking

**Goal**: Показать утечку атрибутов `kez_innate` в `Global` и отсутствие диагностики на неисправленном коде, подтвердить, что причина в ветке `IsInnate` и фильтре `IsActive`.

**Test Plan**: Создать забег Kez и сравнить `Global` с базой героя по атрибутам `kez_innate`, затем проверить диагностику.

**Test Cases**:
1. **`KezInnateDoesNotLeakRangeIntoGlobals`**: Kez; для каждого атрибута из `kez_innate.BaseAttributes` `Global[a]` равен сумме `HeroDef.BaseAttributes` по этому id (`MovementSpeed` 280, остальные 0); `Find(kez_innate).Local[a]` равен значению из данных; `Stat(echo_strike, Range)` равен `echo_strike.Local[Range]` (упадёт на неисправленном коде: `Global[Range]` = 1500, `MovementSpeed` = 300).
2. **`KezInnateIsReportedAsUnimplementedAbility`**: Kez; `Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_InstantAttack")` истинно, `Find(kez_innate).Ability` равно `null` (упадёт на неисправленном коде: диагностики нет).
3. **`InnateAbilityNeverReachesGlobals`** (свойство): для всех героев из данных, у которых есть врождённая с собственным классом, и 8 seed со случайными `Upgrade` реализованных определений (`System.Random(seed)` в тесте, кандидаты — `CanUpgrade`) после каждого шага `Global` равен `ReferenceGlobal(run, false)` (упадёт на неисправленном коде для Kez).

**Expected Counterexamples**:
- Kez: `Global[Range]` = 1500 вместо 0, `Global[MovementSpeed]` = 300 вместо 280, `Global[TargetCount]` = 1, `Global[Radius]` = 180, `Global[Cooldown]` = 6.
- `Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_InstantAttack")` = `false`.
- Причины: безусловный `Global.Add(def.BaseAttributes)` в ветке `IsInnate`; фильтр `!owned.Def.IsActive` в `CreateAbilities`.

### Fix Checking

**Goal**: Для всех билдов с условием ошибки исправленный код держит атрибуты врождённой в `Local` и сообщает о нереализованном классе.

**Pseudocode:**
```
FOR ALL (hero, seed) WHERE isBugCondition(NewRun(hero, seed).Build) DO
  run := NewRun(hero, seed)
  REPEAT random Upgrade from CanUpgrade candidates
    ASSERT run.Build.Global = ReferenceGlobal(run, includeInnateAbility = false)
    ASSERT innate.Local = innate.Def.BaseAttributes AND innate.Rank = 1
  ASSERT expectedBehavior(run.Build, run.Diagnostics)
END FOR
```

### Preservation Checking

**Goal**: Для билдов без условия ошибки исправленный код ведёт себя как исходный.

**Pseudocode:**
```
FOR ALL (hero, seed) WHERE NOT isBugCondition(NewRun(hero, seed).Build) DO
  ASSERT Build_original(hero, seed, upgrades) = Build_fixed(hero, seed, upgrades)
END FOR
```

**Testing Approach**: Свойства проверяются циклом по seed в NUnit (генератор `System.Random(seed)` в тесте, не `Rng` симуляции). Библиотеки PBT в проекте нет, новую зависимость не добавляем. Перебор героев, seed и случайных последовательностей улучшений покрывает сочетания пассивных, активных и рецептов, которые ручные примеры пропускают.

**Test Plan**: Тесты сохранения пишутся и запускаются на неисправленном коде, где они должны пройти. Фактические значения (HP, цели, статы Magic Missile, состав `Global`) снимаются с неисправленного кода и закрепляются в тестах, затем тесты повторяются после исправления.

**Test Cases**:
1. **`PlainInnatesStillAddToGlobals`**: Dragonus и Shen; на старте `Global` равен базе героя + `BaseAttributes` + `Minor[0].Global` врождённой; у Shen `MagicDamageWeakensPhysicalDamageResistance_Percentage` 0.3 и `_Duration` 5 (3.1, 3.3).
2. **`PlainInnateBuildsMatchReferenceAfterUpgrades`** (свойство): Dragonus и Shen, 8 seed, случайные `Upgrade`; после каждого шага `Global` равен `ReferenceGlobal(run, true)` (для них совпадает с `false`), а `Local` активных — `BaseAttributes` + применённые `Authored[u].Local` (3.1, 3.5).
3. **`ShenMagicMissileStatsAreUnchanged`**: значения `Stat` Magic Missile (урон, перезарядка, цели, дальность, отскоки, оглушение), снятые на неисправленном коде, совпадают (3.3).
4. **`InnateNeverTakesSlotOrIsOffered`** (свойство): все три героя, 8 seed; `CanUpgrade(innate)` ложно; в `Choices` после `GenerateChoices` (уровни и предметы) и `Reroll` нет врождённых; ранг врождённой 1; число активных владений без врождённых не больше `MaxActiveSlots` (3.4).
5. **`KezKeepsHeroBaseAttributes`**: Kez; `NumDashes` 2, `DashCooldown` 3, `MaxHP` 100, `run.MaxHealth` 100, `DashCharges` 2, `PickupRadius` 250 (3.6).
6. **Существующие тесты**: `DragonusStartsWithInnateBonuses`, `DashComesFromHeroData`, `ChoicesAreDistinctValidAndImplemented`, `PassivesStackAndRecipeConsumesComponents`, `MagicMissileTests`, тесты без выделений в `DashInputTests` проходят без изменений (3.2–3.8).

### Unit Tests

- `KezInnateDoesNotLeakRangeIntoGlobals`, `KezInnateIsReportedAsUnimplementedAbility`.
- `PlainInnatesStillAddToGlobals`, `ShenMagicMissileStatsAreUnchanged`, `KezKeepsHeroBaseAttributes`.

### Property-Based Tests

- `InnateAbilityNeverReachesGlobals` (Property 1): герои с врождённой с собственным классом × 8 seed × случайные улучшения; `Global` = `ReferenceGlobal(run, false)` после каждого шага.
- `PlainInnateBuildsMatchReferenceAfterUpgrades` (Property 3): Dragonus и Shen × 8 seed; `Global` и `Local` совпадают с эталоном исходного правила.
- `InnateNeverTakesSlotOrIsOffered` (Property 3): все герои × 8 seed; врождённая не в слотах и не в выборе.

### Integration Tests

- Полный прогон `dotnet test DotNet/ThornThrone.Simulation.Tests`: 66 существующих и новые тесты проходят.
- Забег Kez через `TestData.Simulate(run, 30, TestData.Bot)`: без исключений, диагностика `ability:CSurvivorsPowerUpDefinition_InstantAttack` сообщена один раз, `Global[MovementSpeed]` = 280 на протяжении забега.
- Unity-слой не меняется, поэтому macOS smoke не обязателен по правилам дорожной карты; при желании его можно запустить, критерий — `THORN_MOBILE_SMOKE` с kills > 0 без необработанных исключений.
