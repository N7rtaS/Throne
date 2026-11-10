# Исправление потери рывка между шагами — проект

## Overview

`Run.Update(deltaTime, move, dash)` хранит флаг рывка в локальной переменной `dashRequest` и передаёт его только в первый `Tick` текущего вызова. Если накопителю `accumulator` не хватает до `Rules.Step` (1/30 с), цикл `while` не выполняется ни разу, и нажатие пропадает вместе с локальной переменной. При 60 FPS так теряется примерно каждое второе нажатие кнопки рывка.

Исправление: запрос хранится в поле `pendingDash` экземпляра `Run` до ближайшего `Tick`. Первый `Tick` забирает запрос и сбрасывает поле, даже если рывок не начался. Несколько нажатий до шага объединяются логическим «или». Поле сбрасывается в сеттере `Paused` (при `true`) и в `OpenChoices`, а при `CanSimulate = false` новый запрос не сохраняется. Сброс выполняется внутри симуляции: пока забег на паузе, `MobileGame` не вызывает `run.Update` (вызов есть только в состоянии `State.Playing`), поэтому сброс в начале `Update` после возобновления опоздал бы. Правила рывка в `UpdateHero` не меняются.

## Glossary

- **Bug_Condition (C)**: вызов `Update` с `dash = true` при `CanSimulate = true`, после которого не выполнен ни один `Tick` (накоплено меньше `Rules.Step`).
- **Property (P)**: запрос из такого вызова передаётся в первый `Tick` одного из следующих вызовов `Update`, если до этого не наступили пауза или выбор улучшения.
- **Preservation**: поведение всех вызовов, где запрос не попадает в кадр без шага: шаги без рывка, рывок в кадре с шагом, остановка на паузе и выборе, ограничение накопителя, отсутствие выделений.
- **`Run.Update`**: метод в `UnityProject/Assets/Game/Scripts/Simulation/Run.cs`, переводит реальное время в целые шаги `Tick`; при `!CanSimulate` обнуляет `accumulator` и выходит.
- **`Run.Tick(move, dash)`**: один шаг симуляции; вызывает `UpdateHero(dt, move, dash)`, в конце может вызвать `OpenChoices()`.
- **`UpdateHero`**: применяет рывок, если `dash && DashCharges > 0 && dashTimer <= 0` (Mechanics §3), и выдаёт `SimEventKind.HeroDashed`.
- **`OpenChoices`**: открывает выбор улучшения (`Choosing = true`). Вызывается из `Tick` и из `Choose`, когда остались `PendingItems` или `PendingLevels`.
- **`Paused`**: сейчас автосвойство `{ get; set; }`; задаётся из `MobileGame.SetPaused`.
- **`CanSimulate`**: `!Paused && !Finished && !Choosing`.
- **`pendingDash`**: новое закрытое поле `bool`, ожидающий запрос рывка.
- **Кадр без шага**: вызов `Update`, после которого `Time` не изменился, хотя `CanSimulate = true`.

## Bug Details

### Bug Condition

Ошибка возникает, когда нажатие приходит в кадр без шага. `Update` записывает `dash` в локальную `dashRequest`, цикл `while (accumulator >= Rules.Step && CanSimulate)` не выполняется, и метод возвращается. Следующий вызов получает уже `dash = false` от `GameInput`: `DashPressed` сбрасывается в начале каждого кадра и равен `true` только в кадре касания.

**Formal Specification:**
```
FUNCTION isBugCondition(input)
  INPUT: input = (run, deltaTime, move, dash) — состояние Run перед вызовом и аргументы Update
  OUTPUT: boolean

  RETURN input.dash = true
         AND input.run.CanSimulate
         AND min(input.run.accumulator + input.deltaTime, Rules.Step * 8) < Rules.Step
END FUNCTION
```

Для последовательности кадров ошибка видна, если после такого вызова ближайший вызов с шагом (до установки `Paused = true` и до открытия выбора) выполняет первый `Tick` с `dash = false`.

### Examples

- Dragonus, 1 заряд, 60 FPS. Кадр 1: `Update(1/60, move, true)`, накоплено 1/60 < 1/30, шага нет. Кадр 2: `Update(1/60, move, false)`, один `Tick` с `dash = false`. Ожидается `HeroDashed` и `DashCharges` 1 → 0. Фактически события нет, заряд на месте.
- Тестовый вариант: `Update(Step * 0.4f, Vec2.Zero, true)`, затем `Update(Step, Vec2.Zero, false)`. Ожидается `HeroDashed` в шаге второго вызова. Фактически рывка нет.
- Kez, 2 заряда. Два вызова `Update(Step * 0.3f, …, true)` подряд, затем `Update(Step, …, false)`. Ожидается ровно один `HeroDashed` и `DashCharges` = 1. Фактически рывков нет.
- Граничный случай: `Update(Step * 0.4f, …, true)`, затем `Paused = true`, `Paused = false`, `Update(Step, …, false)`. Ожидается шаг без рывка: пауза сбрасывает запрос. Неисправленный код даёт тот же результат, но исправление, которое буферизует запрос и не чистит его на паузе, дало бы рывок сразу после возобновления.

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- Вызов `Update` с `dash = true`, в котором выполняется хотя бы один `Tick`, передаёт рывок только в первый `Tick`; остальные шаги этого вызова идут с `dash = false` (3.1).
- Без нажатий рывка все шаги идут с `dash = false`, и при одинаковом seed, данных и вводе результат совпадает с прежним (3.2). Поле `pendingDash` в этом случае всегда `false`, а `Rng` не затрагивается.
- Правила рывка в `UpdateHero` не меняются: расход заряда, `DashDuration`, `DashSpeed`, `DashCooldown`, `NumDashes` из данных героя (Mechanics §3), направление по `Facing`, неуязвимость при `Rules.DashGrantsInvulnerability` (3.3).
- При `!CanSimulate` метод `Update` обнуляет `accumulator` и не выполняет шагов, после возобновления пропущенное время не догоняется (3.4).
- Накопитель ограничен `Rules.Step * 8` (3.5).
- В горячем цикле нет выделений управляемой памяти: добавляется одно поле `bool`, без коллекций и замыканий (3.6).
- Все 54 существующих теста проходят (3.7).

**Scope:**
Исправление влияет только на нажатия в кадрах без шага. Не затрагиваются:
- вызовы `Update` с `dash = false`;
- вызовы `Update` с `dash = true`, в которых выполняется шаг;
- вызовы при `CanSimulate = false` (пауза, выбор улучшения, конец забега): запрос не сохраняется, как и раньше;
- `Choose`, `Reroll`, `Teleport`, `ForceUpgrade` и прочие публичные методы, кроме сеттера `Paused`, у которого добавляется побочный сброс запроса;
- код представления (`MobileGame`, `GameInput`): он по-прежнему передаёт `input.DashPressed` раз в кадр.

## Hypothesized Root Cause

1. **Запрос живёт только в стеке `Update`.** `bool dashRequest = dash;` объявлена внутри метода. Если цикл не выполнился, значение теряется при выходе. Это основная причина, она видна прямо в коде (`Run.cs`, `Update`).

2. **Ввод передаётся как событие кадра, а симуляция идёт с фиксированным шагом.** `GameInput.Update` сбрасывает `DashPressed = false` в начале кадра. Нажатие есть ровно в одном кадре, поэтому повторно оно не придёт. Это часть устройства ввода, а не ошибка в нём. Чинить надо со стороны симуляции, которая владеет фиксированным шагом.

3. **Частота кадров выше 30 Гц.** При `deltaTime` = 1/60 с шаг выполняется примерно в каждом втором кадре. Из-за округления float сумма двух 1/60 может оказаться чуть меньше `1/30f`, и тогда кадров без шага подряд бывает два. Поэтому в тестах время задаётся долями `Rules.Step` (`Step * 0.4f`, затем `Step`), а не через 1/60.

4. **Сброс при паузе нельзя делать в `Update`.** `MobileGame.Update` вызывает `run.Update` только в `State.Playing`, а `SetPaused` выставляет `run.Paused` напрямую. Пока идёт пауза, `run.Update` не вызывается, и первый вызов после неё уже видит `CanSimulate = true`. Поэтому сброс должен происходить в момент `Paused = true`, то есть в сеттере.

## Correctness Properties

Property 1: Bug Condition - Нажатие в кадре без шага выполняется в ближайшем шаге

_For any_ последовательности вызовов `Update`, в которой есть вызов с `dash = true` при `CanSimulate = true` без выполненного `Tick` (isBugCondition возвращает true), и до ближайшего выполненного `Tick` не было `Paused = true` и открытия выбора улучшения, исправленный `Run` SHALL передать запрос в этот `Tick`: при `DashCharges > 0` и `dashTimer <= 0` в нём выдаётся ровно одно событие `HeroDashed` и `DashCharges` уменьшается на 1. Несколько таких нажатий до шага дают не больше одного рывка. Запрос считается израсходованным после этого `Tick`, даже если рывок не начался, и на следующие шаги не переносится.

**Validates: Requirements 2.1, 2.2, 2.3, 2.4**

Property 2: Bug Condition - Пауза и выбор улучшения сбрасывают запрос

_For any_ последовательности, в которой ожидающий запрос существует в момент `Paused = true` или открытия выбора улучшения, а также для любого вызова `Update` с `dash = true` при `CanSimulate = false`, исправленный `Run` SHALL выполнить следующий `Tick` после возобновления с `dash = false`, если в новом вызове `Update` не пришло новое нажатие.

**Validates: Requirements 2.5, 2.6, 2.7**

Property 3: Preservation - Ввод без нажатий в кадрах без шага

_For any_ последовательности вызовов `Update`, где ни один вызов не удовлетворяет isBugCondition, исправленный `Run` SHALL дать тот же результат, что исходный: те же шаги, те же аргументы `dash` в каждом `Tick` (рывок только в первом шаге вызова), то же обнуление накопителя при `!CanSimulate`, то же ограничение `Rules.Step * 8`, те же правила рывка, отсутствие выделений и тот же результат при одинаковом seed.

**Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7**

## Fix Implementation

### Changes Required

**File**: `UnityProject/Assets/Game/Scripts/Simulation/Run.cs`

**Members**: поле `pendingDash`, свойство `Paused`, методы `Update` и `OpenChoices`

**Specific Changes**:
1. **Поле запроса.** Добавить рядом с `accumulator` закрытые поля `private bool paused, pendingDash;` с XML-комментарием: запрос рывка из кадра без шага ждёт ближайшего `Tick`; ссылка на `ТЗ_Мобильная_версия_v2.md §6, §8`.

2. **Сеттер `Paused`.** Заменить автосвойство на свойство с полем, публичный контракт `get; set;` сохраняется:
   ```csharp
   public bool Paused
   {
       get => paused;
       set
       {
           paused = value;
           if (value) pendingDash = false; // pause stops timers and drops held input (ТЗ_Мобильная_версия_v2.md §6, §8)
       }
   }
   ```

3. **`Update`.** Ранний выход при `!CanSimulate` остаётся первым, поэтому нажатие во время паузы, выбора или после конца забега не сохраняется (2.7). Затем запрос объединяется с ожидающим, и первый `Tick` забирает его до своего вызова:
   ```csharp
   public void Update(float deltaTime, Vec2 move, bool dash)
   {
       if (!CanSimulate) { accumulator = 0; return; }
       accumulator = Math.Min(accumulator + deltaTime, Rules.Step * 8);
       // A press on a frame without a fixed step waits for the next step instead of being lost
       // (ТЗ_Мобильная_версия_v2.md §6, §8). Several presses before a step merge into one.
       pendingDash |= dash;
       while (accumulator >= Rules.Step && CanSimulate)
       {
           accumulator -= Rules.Step;
           bool dashNow = pendingDash;
           pendingDash = false;
           Tick(move, dashNow);
       }
   }
   ```
   Поле сбрасывается до вызова `Tick`, поэтому запрос израсходован при любом исходе рывка (2.3), а второй и следующие шаги вызова идут с `dash = false` (3.1).

4. **`OpenChoices`.** Добавить первой строкой `pendingDash = false;` с тем же комментарием-ссылкой. Сейчас выбор открывается только внутри `Tick` (запрос к этому моменту уже забран) или из `Choose` (при `Choosing = true`, когда запрос не сохраняется). Поэтому сброс сейчас ничего не меняет и защищает требование 2.6 от будущих вызовов `OpenChoices` вне `Tick`. Если выбор открывается в том же `Tick`, который забрал запрос, рывок в этом шаге уже применён в `UpdateHero`; это соответствует 2.3.

5. **Чего не трогать.** `Tick`, `UpdateHero`, `Choose`, `MobileGame`, `GameInput`, `Rules` не меняются. Запись в `Differences.md` не нужна: игровое правило рывка прежнее, меняется только доставка ввода до фиксированного шага.

Ограничения: C# 9, netstandard2.1, без `UnityEngine`, без выделений (одно поле `bool`), `TreatWarningsAsErrors` — новых предупреждений нет.

## Testing Strategy

### Validation Approach

Сначала на неисправленном коде запускаются тест исследования (он должен упасть и подтвердить причину) и тесты сохранения (они должны пройти). Затем вносится исправление, и все тесты, включая 54 существующих, должны пройти. Тесты размещаются в новой фикстуре `DashInputTests` (`DotNet/ThornThrone.Simulation.Tests/DashInputTests.cs`, namespace `ThornThrone.Simulation.Tests`), используют `TestData.NewRun`, `TestData.DisableAbilities` и реальные данные. Значения зарядов берутся из данных героя (`Build.Global[Attr.NumDashes]`), а не задаются числом. Время задаётся долями `run.Rules.Step`, чтобы не зависеть от округления 1/60. Рывки считаются по событиям `SimEventKind.HeroDashed` в `run.Events[0..EventCount)` без вызова `ClearEvents` между проверяемыми кадрами.

### Exploratory Bug Condition Checking

**Goal**: Показать потерю нажатия на неисправленном коде и подтвердить, что причина в локальной `dashRequest`.

**Test Plan**: Вызвать `Update` с долей шага и `dash = true`, убедиться, что `Time` не изменился, затем вызвать `Update(Step, …, false)` и проверить, что шаг выполнен и рывок произошёл.

**Test Cases**:
1. **`DashRequestSurvivesFrameWithoutStep`**: Dragonus, `DisableAbilities`, `Update(Step * 0.4f, Vec2.Zero, true)` → `Time` прежний; `Update(Step, Vec2.Zero, false)` → `Time` вырос на шаг, один `HeroDashed`, `DashCharges` уменьшился на 1, `Dashing = true` (упадёт на неисправленном коде).
2. **`SeveralPressesBeforeStepMergeIntoOneDash`**: Kez (`NumDashes` из данных > 1), два вызова `Update(Step * 0.3f, …, true)`, затем `Update(Step, …, false)` → ровно один `HeroDashed`, `DashCharges` = максимум − 1 (упадёт на неисправленном коде: рывков ноль).
3. **`PendingDashIsConsumedEvenIfDashCannotStart`**: Kez, рывок в кадре с шагом, затем во время `Dashing` нажатие в кадре без шага, затем шаги до конца `DashDuration` и ещё несколько без нажатий → всего один `HeroDashed`; запрос не перенёсся на шаг после окончания рывка (на неисправленном коде проходит; ловит исправление, которое не сбрасывает запрос при неудаче).

**Expected Counterexamples**:
- После кадра без шага с `dash = true` и шага с `dash = false`: `HeroDashed` отсутствует, `DashCharges` не изменился.
- Причина: значение живёт в локальной переменной `dashRequest` и исчезает, когда цикл `while` не выполнился.

### Fix Checking

**Goal**: Для всех входов с условием ошибки исправленный код выполняет рывок в ближайшем шаге.

**Pseudocode:**
```
FOR ALL frames WHERE EXISTS f IN frames: isBugCondition(f)
                AND no Paused = true / OpenChoices between f and the next Tick DO
  run := NewRun(hero, seed)
  feed frames into run.Update_fixed
  ASSERT the first Tick after f receives dash = true
  ASSERT HeroDashed count in that Tick ≤ 1
  ASSERT the request does not reach any later Tick
END FOR
```

Тесты на сброс (Property 2):
- **`DashRequestIsDroppedOnPause`**: `Update(Step * 0.4f, …, true)`, `Paused = true`, `Paused = false`, `Update(Step, …, false)` → шаг выполнен, `HeroDashed` нет, `DashCharges` прежний.
- **`DashRequestIsDroppedWhenUpgradeChoiceOpens`**: `AddExperience(RequiredExperience)`, шаги до `Choosing = true`; во время выбора `Update(Step, …, true)` (не сохраняется, 2.7); `Choose(0)`; `Update(Step, …, false)` → шаг выполнен, `HeroDashed` нет. Дополнительно проверяется, что нажатие в кадре без шага перед шагом, открывающим выбор, применяется в этом шаге (`HeroDashed` есть) и после `Choose(0)` не повторяется.

### Preservation Checking

**Goal**: Для входов без условия ошибки исправленный код ведёт себя как исходный.

**Pseudocode:**
```
FOR ALL frames WHERE NOT EXISTS f IN frames: isBugCondition(f) DO
  ASSERT Run_original(frames) = Run_fixed(frames)
END FOR
```

**Testing Approach**: Проверка свойств на случайных последовательностях кадров (цикл по seed, генератор `System.Random(seed)` в тесте, не `Rng` симуляции) покрывает больше сочетаний `deltaTime`, нажатий, паузы и выбора, чем ручные примеры. Библиотеки PBT в проекте нет; новую зависимость не добавляем, свойства проверяются циклом по seed в NUnit по принятому стилю тестов.

**Test Plan**: Тесты сохранения пишутся и запускаются на неисправленном коде, где они должны пройти, затем повторно после исправления.

**Test Cases**:
1. **`DashInFrameWithStepGoesOnlyToFirstStep`**: `Update(Step * 3, …, true)` → три шага, один `HeroDashed`; для Kez с двумя зарядами второго рывка нет (3.1).
2. **`DashPressedWhileStoppedIsIgnored`**: при `Paused = true` вызов `Update(Step, …, true)` не меняет `Time`; после `Paused = false` шаг без рывка (3.4, 2.7).
3. **`DashFreeFramesAreDeterministic`**: для нескольких seed две одинаковые случайные последовательности кадров (`deltaTime` от 0 до `2 * Step`, движение от `TestData.Bot`, без нажатий, с выбором `Choose(0)`) дают совпадающие `Time`, `HeroPosition`, `Health`, `Kills`, `Experience`, `DashCharges` (3.2).
4. **Существующие тесты**: `DashComesFromHeroData`, проверка навигации с рывком раз в 90 шагов, тест без выделений в горячем цикле, тест «нет симуляции во время выбора и нет догона» проходят без изменений (3.3–3.7).

### Unit Tests

- `DashRequestSurvivesFrameWithoutStep`, `SeveralPressesBeforeStepMergeIntoOneDash`, `PendingDashIsConsumedEvenIfDashCannotStart`.
- `DashRequestIsDroppedOnPause`, `DashRequestIsDroppedWhenUpgradeChoiceOpens`.
- `DashInFrameWithStepGoesOnlyToFirstStep`, `DashPressedWhileStoppedIsIgnored`.

### Property-Based Tests

- **`PressInFrameWithoutStepEqualsPressInNextStep`** (Property 1, 3): для 8 seed случайная последовательность из 300 кадров (`deltaTime` в [0; 2·Step], нажатия с вероятностью 0,1, изредка `Paused = true/false`). Прогон X получает последовательность как есть и записывает, в каких кадрах вырос `Time`. Прогон Y получает ту же последовательность, где каждое нажатие из кадра без шага перенесено в ближайший кадр с шагом, а при паузе между ними удалено. Входы Y не удовлетворяют isBugCondition, значит Y ведёт себя как исходный код. Утверждается совпадение `Time`, `HeroPosition`, `DashCharges`, `Health` и числа `HeroDashed` у X и Y после каждого кадра.
- **`AtMostOneDashPerStep`** (Property 1): для тех же последовательностей число `HeroDashed` за вызов `Update` не превышает 1 и не превышает число нажатий с момента предыдущего шага.
- **`NoDashAfterPauseOrChoiceWithoutNewPress`** (Property 2): для случайных последовательностей после каждого `Paused = true` или открытия выбора первый шаг без нового нажатия не выдаёт `HeroDashed`, если герой не был в рывке.

### Integration Tests

- Полный прогон `dotnet test DotNet/ThornThrone.Simulation.Tests`: 54 существующих и новые тесты проходят.
- Вариант теста без выделений с нажатием рывка в кадрах без шага (`Update(Step * 0.5f, …, i % 45 == 0)`): после прогрева `GC.GetAllocatedBytesForCurrentThread` не растёт (3.6).
- macOS smoke-сборка (`MobileBuilder.BuildMacSmoke`, запуск с `-thornSmoke`): в логе `THORN_MOBILE_SMOKE` с kills > 0, без необработанных исключений. Ручная проверка на телефоне при 60 FPS (каждое нажатие кнопки рывка даёт рывок, пауза и выбор не оставляют отложенного рывка) выполняется, когда устройство будет доступно, и не отмечается выполненной без измерения.
