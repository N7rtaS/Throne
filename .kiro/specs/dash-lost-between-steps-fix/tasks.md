# Implementation Plan

- [x] 1. Написать тест исследования условия ошибки
  - **Property 1: Bug Condition** - Нажатие в кадре без шага выполняется в ближайшем шаге
  - **CRITICAL**: на неисправленном коде тест ДОЛЖЕН упасть; падение подтверждает ошибку
  - **НЕ исправлять ни тест, ни код, когда тест падает**
  - **NOTE**: тест кодирует ожидаемое поведение и после исправления подтвердит его, когда пройдёт
  - **GOAL**: получить контрпримеры, показывающие потерю нажатия
  - **Scoped PBT Approach**: ошибка детерминирована, поэтому свойство ограничено конкретными падающими случаями; время задаётся долями `run.Rules.Step`, а не 1/60, чтобы не зависеть от округления float
  - Создать фикстуру `DashInputTests` в `DotNet/ThornThrone.Simulation.Tests/DashInputTests.cs`, namespace `ThornThrone.Simulation.Tests`; использовать `TestData.Load()`, `TestData.NewRun`, `TestData.DisableAbilities` и реальные данные
  - Условие ошибки из design: `isBugCondition(input) = input.dash AND input.run.CanSimulate AND min(accumulator + deltaTime, Rules.Step * 8) < Rules.Step`
  - Рывки считать по событиям `SimEventKind.HeroDashed` в `run.Events[0..EventCount)` без `ClearEvents` между проверяемыми кадрами; число зарядов брать из данных героя (`Build.Global[Attr.NumDashes]`)
  - `DashRequestSurvivesFrameWithoutStep`: Dragonus, `DisableAbilities`; `Update(Step * 0.4f, Vec2.Zero, true)` → `Time` не изменился; `Update(Step, Vec2.Zero, false)` → `Time` вырос на один шаг, ровно один `HeroDashed`, `DashCharges` уменьшился на 1, `Dashing = true`
  - `SeveralPressesBeforeStepMergeIntoOneDash`: Kez (`NumDashes` из данных > 1, проверить утверждением); два вызова `Update(Step * 0.3f, Vec2.Zero, true)`, затем `Update(Step, Vec2.Zero, false)` → ровно один `HeroDashed`, `DashCharges` = максимум − 1
  - Запустить `dotnet test DotNet/ThornThrone.Simulation.Tests --filter FullyQualifiedName~DashInputTests` на неисправленном коде
  - **EXPECTED OUTCOME**: оба теста ПАДАЮТ (это правильно: ошибка существует)
  - Записать найденные контрпримеры, например: «после `Update(Step * 0.4f, …, true)` и `Update(Step, …, false)` событий `HeroDashed` 0, `DashCharges` не изменился»; подтвердить причину: запрос живёт в локальной `dashRequest` и теряется, когда цикл `while` не выполнился
  - Задача выполнена, когда тесты написаны, запущены и падение задокументировано
  - _Requirements: 1.1, 1.2, 2.1, 2.2, 2.4_

- [x] 2. Написать тесты сохранения поведения (ДО исправления)
  - **Property 2: Preservation** - Ввод без нажатий в кадрах без шага
  - **IMPORTANT**: использовать подход «сначала наблюдение»: запустить неисправленный код на входах, где `isBugCondition` ложно, записать фактические результаты и закрепить их в тестах
  - Добавить в `DashInputTests`:
    - `DashInFrameWithStepGoesOnlyToFirstStep`: `Update(Step * 3, Vec2.Zero, true)` → три шага, ровно один `HeroDashed`; для Kez с несколькими зарядами второго рывка нет (3.1)
    - `DashPressedWhileStoppedIsIgnored`: при `Paused = true` вызов `Update(Step, Vec2.Zero, true)` не меняет `Time`; после `Paused = false` и `Update(Step, Vec2.Zero, false)` шаг выполнен без `HeroDashed` (3.4, 2.7)
    - `PendingDashIsConsumedEvenIfDashCannotStart`: Kez, рывок в кадре с шагом; во время `Dashing` нажатие в кадре без шага (`Update(Step * 0.4f, …, true)`); затем шаги без нажатий до конца `DashDuration` и ещё несколько → всего один `HeroDashed` (2.3; на неисправленном коде проходит)
    - `DashFreeFramesAreDeterministic`: цикл по нескольким seed; генератор `System.Random(seed)` в тесте (не `Rng` симуляции) строит последовательность кадров с `deltaTime` в [0; 2·Step], движением от `TestData.Bot`, без нажатий, с `Choose(0)` при `Choosing`; два прогона с одинаковым seed дают совпадающие `Time`, `HeroPosition`, `Health`, `Kills`, `Experience`, `DashCharges` (3.2)
    - Вариант проверки без выделений: после прогрева цикл `Update(Step * 0.5f, move, i % 45 == 0)`; `GC.GetAllocatedBytesForCurrentThread` не растёт (3.6)
  - Свойства проверяются циклом по seed в NUnit; библиотеку PBT не добавлять. Утверждения в стиле `Assert.That(x, Is.EqualTo(y).Within(eps))`
  - Запустить `dotnet test DotNet/ThornThrone.Simulation.Tests` на неисправленном коде
  - **EXPECTED OUTCOME**: новые тесты сохранения и все 54 существующих теста ПРОХОДЯТ (это фиксирует исходное поведение)
  - Задача выполнена, когда тесты написаны, запущены и проходят на неисправленном коде
  - _Requirements: 2.3, 2.7, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

- [x] 3. Исправить потерю рывка между шагами

  - [x] 3.1 Внести исправление в `UnityProject/Assets/Game/Scripts/Simulation/Run.cs`
    - Рядом с `accumulator` добавить закрытые поля `private bool paused, pendingDash;` с XML-комментарием `/// <summary>` на английском: запрос рывка из кадра без шага ждёт ближайшего `Tick` (ТЗ_Мобильная_версия_v2.md §6, §8)
    - Заменить автосвойство `Paused` на свойство с полем `paused`; публичный контракт `get; set;` сохраняется; в сеттере при `value = true` сбрасывать `pendingDash = false`
    - В `Update`: ранний выход `if (!CanSimulate) { accumulator = 0; return; }` остаётся первым (нажатие при остановке не сохраняется); после ограничения накопителя `Rules.Step * 8` выполнить `pendingDash |= dash;`; в цикле `while` перед каждым `Tick` забирать `bool dashNow = pendingDash; pendingDash = false;` и вызывать `Tick(move, dashNow)`; локальную `dashRequest` удалить
    - В `OpenChoices` первой строкой добавить `pendingDash = false;` с комментарием-ссылкой на ТЗ §6, §8
    - Не менять `Tick`, `UpdateHero`, `Choose`, `MobileGame`, `GameInput`, `Rules`; запись в `Differences.md` не нужна
    - Ограничения: C# 9, netstandard2.1, без `UnityEngine`, без выделений, без новых предупреждений (`TreatWarningsAsErrors`)
    - _Bug_Condition: isBugCondition(input) = input.dash AND input.run.CanSimulate AND min(accumulator + deltaTime, Rules.Step * 8) < Rules.Step_
    - _Expected_Behavior: запрос передаётся в первый `Tick` следующего вызова `Update` с шагом; не больше одного рывка за шаг; запрос израсходован после `Tick` при любом исходе; пауза и открытие выбора сбрасывают запрос (Property 1 и Property 2 из design)_
    - _Preservation: Preservation Requirements из design (рывок только в первом шаге вызова, детерминизм без нажатий, правила рывка Mechanics §3, остановка без догона, ограничение накопителя, отсутствие выделений)_
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6_

  - [x] 3.2 Убедиться, что тест исследования теперь проходит
    - **Property 1: Expected Behavior** - Нажатие в кадре без шага выполняется в ближайшем шаге
    - **IMPORTANT**: повторно запустить ТЕ ЖЕ тесты из задачи 1 (`DashRequestSurvivesFrameWithoutStep`, `SeveralPressesBeforeStepMergeIntoOneDash`), новые не писать
    - **EXPECTED OUTCOME**: тесты ПРОХОДЯТ (ошибка исправлена)
    - _Requirements: 2.1, 2.2, 2.4_

  - [x] 3.3 Убедиться, что тесты сохранения по-прежнему проходят
    - **Property 2: Preservation** - Ввод без нажатий в кадрах без шага
    - **IMPORTANT**: повторно запустить ТЕ ЖЕ тесты из задачи 2, новые не писать
    - **EXPECTED OUTCOME**: тесты ПРОХОДЯТ (регрессий нет)
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

  - [x] 3.4 Добавить тесты сброса запроса в `DashInputTests`
    - **Property 2: Bug Condition** - Пауза и выбор улучшения сбрасывают запрос
    - `DashRequestIsDroppedOnPause`: `Update(Step * 0.4f, Vec2.Zero, true)`, `Paused = true`, `Paused = false`, `Update(Step, Vec2.Zero, false)` → шаг выполнен, `HeroDashed` нет, `DashCharges` прежний
    - `DashRequestIsDroppedWhenUpgradeChoiceOpens`: `AddExperience(RequiredExperience)`, шаги до `Choosing = true`; во время выбора `Update(Step, …, true)` не сохраняется; `Choose(0)`; `Update(Step, …, false)` → шаг выполнен, `HeroDashed` нет. Дополнительно: нажатие в кадре без шага перед шагом, открывающим выбор, применяется в этом шаге (`HeroDashed` есть) и после `Choose(0)` не повторяется
    - Запустить тесты; **EXPECTED OUTCOME**: проходят
    - _Requirements: 2.3, 2.5, 2.6, 2.7_

  - [x] 3.5 Добавить тесты свойств на случайных последовательностях кадров в `DashInputTests`
    - **Property 1: Expected Behavior** - Нажатие в кадре без шага равно нажатию в ближайшем шаге
    - `PressInFrameWithoutStepEqualsPressInNextStep`: 8 seed, `System.Random(seed)`, 300 кадров, `deltaTime` в [0; 2·Step], нажатие с вероятностью 0,1, изредка `Paused = true/false`. Прогон X получает последовательность как есть и записывает кадры, где вырос `Time`. Прогон Y получает ту же последовательность, где каждое нажатие из кадра без шага перенесено в ближайший кадр с шагом, а при паузе между ними удалено. После каждого кадра совпадают `Time`, `HeroPosition`, `DashCharges`, `Health` и число `HeroDashed`
    - `AtMostOneDashPerStep`: на тех же последовательностях число `HeroDashed` за вызов `Update` ≤ 1 и не больше числа нажатий с предыдущего шага
    - `NoDashAfterPauseOrChoiceWithoutNewPress` (Property 2): после каждого `Paused = true` или открытия выбора первый шаг без нового нажатия не выдаёт `HeroDashed`, если герой не был в рывке
    - Запустить тесты; **EXPECTED OUTCOME**: проходят
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 3.1, 3.2_

- [x] 4. Checkpoint — убедиться, что всё проходит
  - Запустить `dotnet test DotNet/ThornThrone.Simulation.Tests` из корня репозитория: проходят все 54 существующих теста и все новые тесты `DashInputTests`; записать итоговое число
  - Собрать macOS smoke-сборку batchmode-методом `ThornThrone.Editor.MobileBuilder.BuildMacSmoke` (в логе `THORN_MAC_BUILD_OK`), запустить её с `-thornSmoke` с достаточным таймаутом; в логе есть `THORN_MOBILE_SMOKE` с kills > 0 и нет необработанных исключений
  - Ручную проверку на телефоне при 60 FPS не отмечать выполненной без измерения на устройстве
  - Закоммитить изменения `Run.cs` и `DashInputTests.cs` (без `Builds/`, `bin/`, `obj/`, `TestResults/`). Сообщение на английском в повелительном наклонении без префиксов, например: `Buffer dash press until the next simulation step`. В теле: запрос рывка из кадра без шага сохраняется до ближайшего шага, несколько нажатий объединяются в один рывок, пауза и выбор улучшения сбрасывают запрос; ссылка на Mechanics §3 (правила рывка не изменились) и ТЗ §6, §8; число тестов в формате «N new tests, M pass»
  - Если возникли вопросы, спросить пользователя
  - _Requirements: 3.7_
