# Implementation Plan

- [x] 1. Написать тесты исследования условия ошибки
  - **Property 1: Bug Condition** - Атрибуты врождённой с собственным классом остаются в её Local
  - **CRITICAL**: на неисправленном коде тесты ДОЛЖНЫ упасть; падение подтверждает ошибку
  - **НЕ исправлять ни тесты, ни код, когда тесты падают**
  - **NOTE**: тесты кодируют ожидаемое поведение и после исправления подтвердят его, когда пройдут
  - **GOAL**: получить контрпримеры, показывающие утечку атрибутов `kez_innate` в `Build.Global` и отсутствие диагностики
  - **Scoped PBT Approach**: ошибка детерминирована, поэтому основные тесты ограничены конкретным случаем Kez; свойство по seed дополняет их
  - Создать фикстуру `KezInnateTests` в `DotNet/ThornThrone.Simulation.Tests/KezInnateTests.cs`, namespace `ThornThrone.Simulation.Tests`; использовать `TestData.Load()`, `TestData.NewRun` и реальные данные
  - Тесты должны компилироваться на неисправленном коде: «собственный класс» проверять как `Def.Class != "CSurvivorsPowerUpDefinition"`, новые свойства `PowerUpDef` не использовать
  - Условие ошибки из design: `isBugCondition(build) = EXISTS owned IN build.Owned: owned.Def.IsInnate AND owned.Def.Class != "CSurvivorsPowerUpDefinition"`
  - Ожидаемые значения брать из данных: база героя — `HeroDef.BaseAttributes` (сумма по id), атрибуты врождённой — `kez_innate.BaseAttributes`
  - `KezInnateDoesNotLeakRangeIntoGlobals`: Kez; для каждого атрибута из `kez_innate.BaseAttributes` (`MovementSpeed`, `TargetCount`, `Damage_Magical`, `Cooldown`, `Range`, `Radius`, `VulnerabilityDamagePercent`, `VulnerabilityDuration`, `VulnerabilityStunOnHitDuration`) `Build.Global[a]` равен базе героя (`MovementSpeed` = 280, остальные 0); `Find(kez_innate).Local[a]` равен значению из данных; `Build.Stat(echo_strike, Attr.Range)` равен `echo_strike.Local[Attr.Range]`
  - `KezInnateIsReportedAsUnimplementedAbility`: Kez; `run.Diagnostics.Has("ability:CSurvivorsPowerUpDefinition_InstantAttack")` истинно; `Find(kez_innate).Ability` равно `null`
  - `InnateAbilityNeverReachesGlobals`: для всех героев из данных с врождённой с собственным классом (проверить утверждением, что найден хотя бы Kez) и 8 seed случайные `Build.Upgrade` из кандидатов `CanUpgrade` (`System.Random(seed)` в тесте, не `Rng` симуляции); после каждого шага `Build.Global` по всем id `0..Attr.Capacity−1` равен `ReferenceGlobal(run, includeInnateAbility: false)` с `Within(1e-4)`
  - Хелпер `ReferenceGlobal(run, includeInnateAbility)` в фикстуре: база героя + врождённые без собственного класса (`BaseAttributes` + `Minor[0].Global`) + при `includeInnateAbility` врождённые с собственным классом так же + пассивные `Minor[0].Global × Rank` + `Authored[u].Global` активных для `u < Rank − Rules.AuthoredUpgradeOffset`
  - Запустить `dotnet test DotNet/ThornThrone.Simulation.Tests --filter FullyQualifiedName~KezInnateTests` на неисправленном коде
  - **EXPECTED OUTCOME**: все три теста ПАДАЮТ (это правильно: ошибка существует)
  - Записать найденные контрпримеры, например: «у Kez `Global[Range]` = 1500 вместо 0, `Global[MovementSpeed]` = 300 вместо 280; диагностики `ability:CSurvivorsPowerUpDefinition_InstantAttack` нет»; подтвердить причины: безусловный `Global.Add(def.BaseAttributes)` в ветке `IsInnate` в `Build.Recalculate` и фильтр `!owned.Def.IsActive` в `Run.CreateAbilities`
  - Задача выполнена, когда тесты написаны, запущены и падение задокументировано
  - _Requirements: 1.1, 1.2, 1.3, 2.1, 2.2, 2.3, 2.4_

- [x] 2. Написать тесты сохранения поведения (ДО исправления)
  - **Property 2: Preservation** - Билд без врождённой с собственным классом не меняется
  - **IMPORTANT**: использовать подход «сначала наблюдение»: запустить неисправленный код для Dragonus и Shen и для свойств, не связанных с `kez_innate`, записать фактические значения и закрепить их в тестах
  - Добавить в `KezInnateTests`:
    - `PlainInnatesStillAddToGlobals`: Dragonus и Shen; на старте `Build.Global` по всем id равен базе героя + `BaseAttributes` + `Minor[0].Global` врождённой; у Shen `MagicDamageWeakensPhysicalDamageResistance_Percentage` = 0.3 и `_Duration` = 5 (3.1, 3.3)
    - `PlainInnateBuildsMatchReferenceAfterUpgrades`: Dragonus и Shen, 8 seed, случайные `Upgrade` из кандидатов `CanUpgrade`; после каждого шага `Global` равен `ReferenceGlobal(run, true)`, а `Local` каждого активного владения — `BaseAttributes` + применённые `Authored[u].Local` (3.1, 3.5)
    - `ShenMagicMissileStatsAreUnchanged`: Shen на старте; значения `Build.Stat` Magic Missile по `Damage_Magical`, `Cooldown`, `TargetCount`, `Range`, `ProjectileBounces`, `StunDuration`, `ProjectileSpeed`, снятые на неисправленном коде, закреплены числами с `Within(1e-5)` (3.3)
    - `InnateNeverTakesSlotOrIsOffered`: все три героя, 8 seed; `CanUpgrade(innate)` ложно; после `GenerateChoices` (уровни и предметы) и `Reroll` в `Choices` нет врождённых; ранг врождённой 1; число активных владений без врождённых не больше `Mode.MaxActiveSlots`, пассивных — не больше `Mode.MaxPassiveSlots` (3.4)
    - `KezKeepsHeroBaseAttributes`: Kez; `Global[NumDashes]` = 2, `Global[DashCooldown]` = 3, `Global[MaxHP]` = 100, `Global[PickupRadius]` = 250, `run.MaxHealth` = 100, `DashCharges` = 2 (3.6)
  - Свойства проверяются циклом по seed в NUnit; библиотеку PBT не добавлять. Утверждения в стиле `Assert.That(x, Is.EqualTo(y).Within(eps))`
  - Запустить `dotnet test DotNet/ThornThrone.Simulation.Tests` на неисправленном коде
  - **EXPECTED OUTCOME**: новые тесты сохранения и все 66 существующих тестов ПРОХОДЯТ (это фиксирует исходное поведение); тесты задачи 1 по-прежнему падают
  - Задача выполнена, когда тесты написаны, запущены и проходят на неисправленном коде
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8_

- [x] 3. Исправить утечку атрибутов врождённой Kez в глобальные

  - [x] 3.1 Внести исправление в `Definitions.cs`, `Build.cs` и `Run.cs`
    - `UnityProject/Assets/Game/Scripts/Simulation/Content/Definitions.cs`, `PowerUpDef`: добавить `public const string PlainClass = "CSurvivorsPowerUpDefinition";`, `public bool HasOwnClass => Class != PlainClass;` и `public bool IsInnateAbility => IsInnate && HasOwnClass;` с XML-комментариями `/// <summary>` на английском (врождённая с собственным классом — способность героя, её атрибуты — параметры атаки; Mechanics §3, Р)
    - `IsActive` переписать через `HasOwnClass` без изменения результата: `!IsPassive && !IsInnate && !IsGold && HasOwnClass`; литерал `"CSurvivorsPowerUpDefinition"` остаётся только в `PlainClass`
    - `UnityProject/Assets/Game/Scripts/Simulation/Build/Build.cs`, `Recalculate`: в ветке `if (def.IsInnate)` добавлять `BaseAttributes` и `Minor[0].Global` в `Global` только при `!def.IsInnateAbility`; `owned.Local.Add(def.BaseAttributes)` остаётся для всех; ветки пассивных и активных не менять; уточнить формулу в XML-комментарии класса `Build`
    - `UnityProject/Assets/Game/Scripts/Simulation/Run.cs`, `CreateAbilities`: условие пропуска `if (owned.Ability != null || !(owned.Def.IsActive || owned.Def.IsInnateAbility)) continue;`; создание через `AbilityFactory.Create` и диагностика `Diagnostics.Report("ability:" + owned.Def.Class, …)` прежние
    - Не менять `CountSlots`, `CanUpgrade`, `GenerateChoices`, `Run.IsImplemented`, `AbilityFactory`, `SupportedHeroAttributes`, Unity-слой; `InstantAttack` не реализовывать (Task 19); запись в `Differences.md` не нужна
    - Ограничения: C# 9, netstandard2.1, без `UnityEngine`, без выделений в шаге симуляции, без новых предупреждений (`TreatWarningsAsErrors`)
    - _Bug_Condition: isBugCondition(build) = EXISTS owned IN build.Owned: owned.Def.IsInnate AND owned.Def.Class != "CSurvivorsPowerUpDefinition"_
    - _Expected_Behavior: expectedBehavior(build, diagnostics) из design — атрибуты врождённой с собственным классом только в её `Local`, `Global` равен эталону без её вклада, для неизвестного класса диагностика `ability:<Class>` и `Ability = null` (Property 1 и Property 2)_
    - _Preservation: Preservation Requirements из design (Dragonus и Shen без изменений, слоты и выбор, правила пассивных и активных, база героя Kez, отсутствие выделений)_
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

  - [x] 3.2 Убедиться, что тесты исследования теперь проходят
    - **Property 1: Expected Behavior** - Атрибуты врождённой с собственным классом остаются в её Local
    - **IMPORTANT**: повторно запустить ТЕ ЖЕ тесты из задачи 1 (`KezInnateDoesNotLeakRangeIntoGlobals`, `KezInnateIsReportedAsUnimplementedAbility`, `InnateAbilityNeverReachesGlobals`), новые не писать
    - **EXPECTED OUTCOME**: тесты ПРОХОДЯТ (ошибка исправлена)
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 3.3 Убедиться, что тесты сохранения по-прежнему проходят
    - **Property 2: Preservation** - Билд без врождённой с собственным классом не меняется
    - **IMPORTANT**: повторно запустить ТЕ ЖЕ тесты из задачи 2, новые не писать
    - **EXPECTED OUTCOME**: тесты ПРОХОДЯТ (регрессий нет)
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8_

- [x] 4. Записать правило врождённой в `Research/FullGame/Mechanics.md` §3
  - После строки «Врождённая Драгонуса даёт −20 HP и +1 цель…» добавить: врождённая без собственного класса (`_class = CSurvivorsPowerUpDefinition`, 500 и 501) даёт герою `m_vecMinorUpgradeChoices[0].m_vecGlobalUpgradeAttributes` (**Д**)
  - Добавить правило с меткой **Р** (решение владельца, Task 3): врождённая с собственным классом (`kez_innate`, `CSurvivorsPowerUpDefinition_InstantAttack`) — способность героя; все её `m_vecBaseAttributes`, включая `MovementSpeed` 20, — параметры её атаки, а не атрибуты героя
  - Указать, что локализация («Кез передвигается быстрее…») допускает иное: +20 к скорости может быть бонусом герою; правило не объявлять подтверждённым
  - Указать текущее состояние: пока `InstantAttack` не реализован (Task 19), Kez бегает со скоростью 280 и не получает от врождённой бонусов; в забеге есть диагностика `ability:CSurvivorsPowerUpDefinition_InstantAttack`
  - Текст на русском, в стиле соседних пунктов раздела
  - _Requirements: 2.1, 2.4_

- [x] 5. Checkpoint — убедиться, что всё проходит
  - Запустить `dotnet test DotNet/ThornThrone.Simulation.Tests` из корня репозитория с достаточным таймаутом: проходят все 66 существующих тестов и все новые тесты `KezInnateTests`; записать число новых тестов N и итоговое число M
  - Unity-слой не менялся, поэтому macOS smoke по правилам дорожной карты не обязателен
  - Если возникли вопросы, спросить пользователя
  - _Requirements: 3.8_

- [x] 6. Отметить Task 3 в `.kiro/steering/roadmap.md` и закоммитить
  - Отметка ставится ДО коммита, чтобы войти в него. Хеш коммита до коммита неизвестен, а `--amend` не используется, поэтому вместо хеша указать заголовок коммита (по аналогии с Task 1, где стоит «коммит с .kiro»): в начале строки заголовка Task 3 поставить `✅ (ГГГГ-ММ-ДД, коммит «<заголовок коммита>») ` с текущей датой из `date +%F`, остальной текст строки не менять. Формат Task 2 для сравнения: `✅ (2026-11-10, 1c7a102) Task 2: …`
  - Добавить в индекс только файлы задачи по имени: `UnityProject/Assets/Game/Scripts/Simulation/Content/Definitions.cs`, `UnityProject/Assets/Game/Scripts/Simulation/Build/Build.cs`, `UnityProject/Assets/Game/Scripts/Simulation/Run.cs`, `DotNet/ThornThrone.Simulation.Tests/KezInnateTests.cs`, `Research/FullGame/Mechanics.md`, `.kiro/steering/roadmap.md` и файлы спека `.kiro/specs/kez-innate-global-leak-fix/` (`.config.kiro`, `bugfix.md`, `design.md`, `tasks.md`, `tasks.meta.json`, если есть). Не добавлять `Research/mac-build.log`, `UnityProject/Assets/Game/Scenes/Mobile.unity`, `Builds/`, `bin/`, `obj/`, `TestResults/`; не использовать `git add -A` и `git add .`
  - Сообщение коммита на русском по `tech.md`: заголовок — короткое предложение без префиксов, что сделано, например `Не добавлять атрибуты врождённой Kez к атрибутам героя`
  - Тело: врождённая с собственным классом (`kez_innate`, `CSurvivorsPowerUpDefinition_InstantAttack`) теперь способность героя, её атрибуты остаются в `Local` и не прибавляются к другим способностям и скорости; `Run.CreateAbilities` создаёт её через `AbilityFactory`, до реализации `InstantAttack` (Task 19) выдаётся диагностика `ability:CSurvivorsPowerUpDefinition_InstantAttack`; Dragonus и Shen без изменений; ссылка на Mechanics §3 (правило **Р**); число тестов в формате «N новых тестов, проходят M»
  - Выполнить коммит без `--no-verify`; `git push` не выполнять
  - Проверить `git status` и `git show --stat HEAD`: все файлы задачи вошли в коммит, посторонние изменения остались незакоммиченными и не тронуты
  - _Requirements: 3.8_
