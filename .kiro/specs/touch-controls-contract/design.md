# Дизайн: контракт сенсорного управления (Task 11)

## 1. Архитектура

```
Input System (EnhancedTouch, Mouse, Keyboard) ─┐
виртуальный Touchscreen (-thornInputScript) ──┤
                                              ▼
GameInput (адаптер Unity: фазы, overUi, поворот, клавиатура, «Назад»)
        │  Process(id, phase, position, overUi), Reset(), OpenCards()
        ▼
TouchController (чистый класс, без Screen/Input/EventSystem/Time)
        │  Move, DashPressed, JoystickActive/Origin/Position, CardAccepts(id)
        ▼
MobileGame → run.Update(dt, ScreenToPlane(Move), DashPressed)
GameUi     → рисует джойстик, раскладывает кнопки по TouchLayout, CardButton спрашивает CardAccepts
```

Слой Input по ТЗ §8: `TouchController`, `TouchSettings`, `TouchMetrics`, `BackNavigation` — файл `Scripts/Mobile/TouchController.cs` (тесно связанные типы в одном файле, как `Abilities.cs`). `GameInput` остаётся адаптером Unity. Отдельный asmdef для Input не заводится: это не требуется задачей, а `ThornThrone.Mobile.Tests` уже видит `ThornThrone.Mobile`.

Используются `UnityEngine.Vector2` и `Rect` — это значения без состояния движка, EditMode-тесты создают их напрямую.

## 2. Типы

```csharp
public enum TouchPhaseKind { Began, Moved, Stationary, Ended, Canceled }
public enum TouchAction { None, Joystick, Dash, Ui }
public enum JoystickMode { Floating, Fixed }

/// Player options; Task 13 stores them in the profile and edits them in settings.
public sealed class TouchSettings
{
    public JoystickMode Joystick = JoystickMode.Floating;
    public bool LeftHanded;
    public float DeadZone = 0.1f;
}

/// Screen layout in pixels, filled by GameUi from the safe area and the dash button.
public struct TouchLayout
{
    public Rect SafeArea;
    public Rect DashButton;      // already mirrored for the left-handed mode
    public float JoystickRadius; // px
    public Vector2 FixedCenter;  // px, already mirrored
}

public sealed class TouchController
{
    public TouchSettings Settings { get; }
    public TouchLayout Layout;
    public Vector2 Move { get; }
    public bool DashPressed { get; }
    public bool JoystickActive { get; }
    public Vector2 JoystickOrigin { get; }
    public Vector2 JoystickPosition { get; }

    public void BeginFrame();                                   // DashPressed = false
    public void Process(int id, TouchPhaseKind phase, Vector2 position, bool overUi);
    public void Reset();                                        // §5
    public void OpenCards();                                    // §6
    public bool CardAccepts(int id);
    public TouchAction ActionOf(int id);                         // for tests and debugging
}

public static class TouchMetrics
{
    public const float FallbackDpi = 160, MinTargetDp = 48;
    public static float DpToPixels(float dp, float dpi);       // dpi <= 0 → 160
    public static float MinTargetCanvasUnits(float dpi, float canvasScale);
}

public enum BackScreen { Menu, Playing, Paused, ConfirmQuit, Choosing, Results }
public enum BackAction { None, Pause, Resume, CloseConfirm, ToMenu, QuitApp }
public static class BackNavigation { public static BackAction For(BackScreen screen); }
```

Хранилище касаний — массив фиксированной длины (16 записей `id, action, stale`), без `Dictionary` и выделений в кадре. Касания сверх 16 игнорируются (у Android-экранов обычно до 10).

## 3. Правила

### 3.1. Начало касания (`Began`)
Порядок проверок (первая подходящая решает):
1. Касание помечено после `Reset` или уже известно — ничего (повторный `Began` с тем же id невозможен у Input System, защита от скрипта).
2. Внутри `Layout.DashButton` → `Dash`, `DashPressed = true`. Кнопка рывка проверяется раньше `overUi`: её `Image` — элемент uGUI и попадает в raycast, но это игровой элемент управления, а не UI (**Р1**).
3. `overUi` → `Ui`.
4. В зоне движения и джойстик свободен → `Joystick`; центр = точка касания (плавающий) или `Layout.FixedCenter` (фиксированный).
5. Иначе → `None`.

Зона движения = левая половина `SafeArea` (правая при `LeftHanded`) (**Р2**). Кнопка рывка лежит в другой половине, пересечения нет.

### 3.2. `Moved` / `Stationary`
Только касание `Joystick` обновляет `JoystickPosition` и `Move`. Остальные действия ничего не делают, где бы ни находился палец (требование 2.5).

### 3.3. `Ended` / `Canceled`
Снимают запись. Для джойстика `Move = 0`. Отмена не создаёт рывок и не выбирает карточку.

### 3.4. Вектор движения
`v = (p − center) / radius`, `r = |v|`. `r ≤ DeadZone` → `0`; иначе `v / r · min(1, (r − DeadZone) / (1 − DeadZone))` (**Р3**: перемасштабирование убирает скачок скорости с 0 до 10% на краю мёртвой зоны). Центр плавающего джойстика не следует за пальцем, если тот ушёл дальше радиуса (**Р4**, как сейчас).

Скорость героя пропорциональна длине `Move` (`Run`: `velocity = move · MovementSpeed`), так что неполное отклонение даёт медленный шаг. В оригинале клавиатура и клик мыши дают полную скорость — отличие Б1.1 (**Р5**, нужно утверждение).

### 3.5. Сброс
`Reset()` помечает все текущие записи `stale`, снимает действия, обнуляет выходы. `stale`-запись живёт до `Ended`/`Canceled` своего id и ничего не делает. Удержанный палец после возврата из паузы не двигает героя, пока его не поднимут (**Р6**).

Где вызывается (`MobileGame`/`GameInput`): `SetPaused`, `OnApplicationPause(true)`, `OnApplicationFocus(false)`, открытие выбора, `StartRun`, `EndRun`, смена `Screen.width/height/orientation`, выключение игрового режима. Вызов идемпотентен.

### 3.6. Карточки
`OpenCards()` записывает все удерживаемые сейчас id (включая `stale` и мышь) в список «до открытия». `CardAccepts(id)` = id не в этом списке. Список очищается при следующем `OpenCards`; закончившиеся касания убираются из него.

`CardButton : Selectable, IPointerDownHandler, IPointerClickHandler` в `GameUi` заменяет `Button` у трёх карточек: в `OnPointerDown` запоминает `CardAccepts(id)`, в `OnPointerClick` вызывает выбор только при `true`. id касания — `ExtendedPointerEventData.touchId` для `UIPointerType.Touch`, для мыши — константа `TouchController.MouseId = -1`. Это тот же `TouchControl.touchId`, что у `EnhancedTouch.Touch.touchId` (**К**: `InputSystemUIInputModule.GetPointerStateIndexFor` сравнивает `eventData.touchId`). `ISubmitHandler` не реализуется: клавиатурное «Enter» карточку не выбирает.

`MobileGame` вызывает `OpenCards()` при каждом `ui.ShowChoices` (в том числе после `Choose`, когда в очереди следующий уровень) и после переброса (**Р7**: переброс показывает новый набор, удержанное касание его тоже не выбирает).

uGUI сам не засчитывает клик касанию, нажатому до появления кнопки, так что `CardAccepts` — вторая защита и единственное место, проверяемое тестом без устройства.

### 3.7. Размеры
`DpToPixels(dp, dpi) = dp · (dpi > 0 ? dpi : 160) / 160`. `MinTargetCanvasUnits = DpToPixels(48, dpi) / canvasScale`. `GameUi.ApplyLayout()` (при изменении `Screen.width/height`, safe area или `scaleFactor`) ставит каждой интерактивной цели `sizeDelta = max(исходный размер, MinTargetCanvasUnits)` по каждой оси. Список целей заполняется в `Button(...)` и `CardButton` при построении UI. Радиус джойстика = `max(0.11 · Screen.height, DpToPixels(48, dpi))` пикселей (**Р8**, 0.11 — текущее значение).

Пример: экран 1280×720, `scaleFactor ≈ 0.667`, 245 dpi → 48 dp = 73.5 px = 110 ед. Canvas; пауза 120 ед. проходит. Экран 2400×1080 при 400 dpi: 48 dp = 120 px, `scaleFactor ≈ 1.118` → 107 ед.

### 3.8. Леворукий режим
`GameUi` ставит кнопку рывка в правый нижний угол safe area или зеркально в левый нижний (якорь `(0, 0)`, отступы те же). `FixedCenter` = `(SafeArea.xMin + 0.18·w, SafeArea.yMin + 0.3·h)` или зеркально (**Р9**). Пауза и HUD не отражаются (**Р10**: ТЗ зеркалит «зоны управления»).

### 3.9. «Назад» и подтверждение
`BackNavigation.For` — таблица из требования 8.1. `MobileGame` переводит свой `State` в `BackScreen`, добавляется состояние `ConfirmQuit`. Выход из главного меню — `Application.Quit()` (**Р11**: подменю у главного меню пока нет; экраны героя и сложности появятся в Task 16 и получат «Назад → меню»). В выборе улучшения «Назад» ничего не делает (**Р12**: выбор нельзя пропустить, пауза поверх выбора не нужна — таймеры уже стоят).

`GameUi` получает панель `Confirm`: заголовок «Завершить забег?», текст «Прогресс этого забега будет потерян.», кнопки «Завершить» (→ `EndRun`, результат) и «Отмена» (→ пауза). События `QuitRunRequested` → `ShowConfirmQuit`, новые `QuitConfirmed`, `QuitCancelled`.

### 3.10. Порядок кадра и `overUi`
`[DefaultExecutionOrder(100)]` на `MobileGame`: `EventSystem.Update` (порядок 0) успевает обработать касания кадра, и `IsPointerOverGameObject(touchId)` отвечает про текущий кадр (**К**: `InputSystemUIInputModule.IsPointerOverGameObject(int pointerOrTouchId)` ищет состояние по `touchId`; предупреждение выдаётся только при вызове во время обработки событий). Прежний список `GameInput.Blocked` (прямоугольник паузы) удаляется: его заменяет `overUi`.

`GameInput.Update(gameplay)`:
1. `controller.BeginFrame()`; смена экрана → `Reset`; `!gameplay` → `Reset` (один раз при переходе).
2. Каждое `Touch.activeTouches`: фаза `UnityEngine.InputSystem.TouchPhase` → `TouchPhaseKind`, `overUi` только для `Began`; `Process(touch.touchId, …)`.
3. Мышь (редактор, Mac): левая кнопка как касание `MouseId`. Касания и мышь не смешиваются: при активных касаниях мышь не читается (на macOS тачпад не создаёт касаний).
4. Клавиатура: если вектор WASD ненулевой, он заменяет `Move`; Space → `DashPressed`; Escape → `BackPressed`.

### 3.11. Smoke `-thornInputScript`
`InputScript` (`Scripts/Mobile/InputScript.cs`) читает текстовый файл:

```
# время_с  команда  аргументы; координаты в долях safe area (0..1)
0.5  down   1  0.20 0.30
0.6  move   1  0.30 0.30
1.0  down   2  dash          # центр кнопки рывка из TouchLayout
1.1  up     2
1.5  move   1  dash          # палец джойстика на кнопку рывка
2.0  cancel 1
2.5  expect dashes 1
2.5  expect moved true
2.5  expect joystick false
```

Касания идут через `InputSystem.AddDevice<Touchscreen>()` и `InputSystem.QueueStateEvent(touchscreen, new TouchState { touchId, phase, position })` — путь настоящего экрана: `EnhancedTouch` → `GameInput` → `TouchController` → `Run` (**Р13**). Время сценария — `Run.Time` (выбор улучшения в smoke закрывается автоматически, время стоит). `dashes` — число событий `SimEventKind.HeroDashed`, `moved` — сместился ли герой больше чем на 50 единиц от старта, `joystick` — `JoystickActive`. Итог: `THORN_INPUT_SCRIPT ok=1 dashes=1 moved=true`; при несовпадении `ok=0 failed=<строка>` и `Application.Quit(8)`. Без `-thornInputScript` smoke не меняется. Файл сценария: `Tools/smoke/input-two-fingers.txt`.

Это smoke, а не тест мультитача: настоящий экран и жесты проверяются в блоке G.

## 4. Решения

| # | Решение | Метка |
|---|---|---|
| Р1 | Кнопка рывка проверяется раньше `overUi` | Р |
| Р2 | Зона движения — половина safe area со стороны джойстика | Р |
| Р3 | Мёртвая зона 0.1 с перемасштабированием длины `(r − 0.1)/0.9` | Р |
| Р4 | Центр плавающего джойстика не тянется за пальцем | Р |
| Р5 | Скорость пропорциональна отклонению (аналоговый джойстик) | Р, отличие Б1.1 |
| Р6 | После сброса удержанный палец игнорируется до отпускания | Р |
| Р7 | Новый набор после переброса тоже требует нового касания | Р |
| Р8 | Радиус джойстика `max(0.11·высоты, 48 dp)` | Р |
| Р9 | Центр фиксированного джойстика (18%, 30%) safe area | Р |
| Р10 | Пауза и HUD не зеркалятся | Р |
| Р11 | «Назад» в главном меню закрывает приложение | Р |
| Р12 | «Назад» при выборе улучшения ничего не делает | Р |
| Р13 | Smoke ввода через виртуальный `Touchscreen` | Р |
| К1 | `IsPointerOverGameObject(touchId)` и `ExtendedPointerEventData.touchId` дают тот же id, что `EnhancedTouch` | К (исходники Input System 1.20.0) |

Все решения — проектная адаптация: в оригинале управление мышью и клавиатурой, данных о сенсорном вводе нет. В `Mechanics.md` — раздел 18 «Сенсорное управление (проектная адаптация)» с этими правилами; в `Differences.md` — Б1.1 (аналоговая скорость, мёртвая зона, сброс удержаний, «Назад»).

## 5. Изменения файлов
- Новые: `Scripts/Mobile/TouchController.cs`, `Scripts/Mobile/InputScript.cs`, `Tests/EditMode/TouchControllerTests.cs`, `Tools/smoke/input-two-fingers.txt` (+ `.meta` для Unity-файлов).
- `GameInput.cs` — адаптер поверх `TouchController`, сброс при повороте, мышь, `Blocked` удаляется.
- `GameUi.cs` — `TouchLayout` и зеркало, минимальные размеры целей, `CardButton`, панель подтверждения, `UpdateHud(run, input)` берёт джойстик из `TouchController`.
- `MobileGame.cs` — `[DefaultExecutionOrder(100)]`, `ConfirmQuit`, `BackNavigation`, `Reset`/`OpenCards` в переходах, `-thornInputScript`.
- `MobileBuilder` не меняется: сцена создаёт только `MobileGame`, UI строится кодом.
- Документы: `Mechanics.md` §18, `Differences.md` Б1.1, `README.md` (сценарий ввода), `product.md` (число EditMode-тестов), `roadmap.md`.

## 6. Тестовая стратегия
- EditMode `TouchControllerTests` (`ThornThrone.Mobile.Tests`): контроллер с раскладкой 1920×1080, кнопка рывка `(1620, 60, 240, 240)`, радиус 120 px. Ожидаемые значения считаются по правилам §3 этого документа (не по реализации): например, смещение 60 px при радиусе 120 → `r = 0.5` → длина `(0.5 − 0.1)/0.9 = 0.444`.
- `BackActionPerScreen` — таблица 8.1 целиком (`TestCase` на каждый экран).
- `InteractiveTargetsAreAtLeast48Dp` — строит `GameUi` в EditMode (как `HudTextTests`), задаёт `dpi`/`scaleFactor` через параметры `ApplyLayout(dpi, scale)` и проверяет `sizeDelta · scale ≥ DpToPixels(48, dpi)` у всех зарегистрированных целей для 160, 245, 400, 560 dpi.
- Smoke: `Tools/mac-smoke.sh` без аргументов и с `-thornInputScript=$PWD/Tools/smoke/input-two-fingers.txt`.
- Регрессия: `dotnet test` (86), прежние 13 EditMode-тестов, в том числе `HudUpdateDoesNotAllocate` — `UpdateHud` с новым джойстиком не должен выделять память.

## 7. Риски
- Виртуальный `Touchscreen` в плеере macOS может не активировать `EnhancedTouch` так же, как настоящий экран. Тогда сценарий подаёт события прямо в `TouchController` и это фиксируется в `Mechanics.md` как ограничение smoke.
- `DefaultExecutionOrder` не влияет на `EventSystem`, если в будущем появится второй модуль ввода; проверяется тестом только косвенно (smoke сценарий с тапом по паузе не входит в объём).

## 8. Уточнения при реализации
- `CardButton` вынесен в свой файл `Scripts/Mobile/CardButton.cs` (MonoBehaviour), `GameUi` передаёт ему фильтр `CardAccepts`.
- Хранилище касаний без флага `stale`: после `Reset` запись остаётся с действием `None` до `Ended`/`Canceled`, этого достаточно.
- Вне игрового режима `GameInput` вызывает `Reset` каждый кадр (а не один раз при переходе): касание, начатое в паузе или при выборе, сбрасывается в кадре начала и не подхватывается после возврата в забег. Список «до открытия карточек» `Reset` не трогает.
- Потеря фокуса и сворачивание сбрасывают касания в любом состоянии, пауза ставится только из `Playing`. В smoke (`-thornSmoke`) это не делается, как и раньше.
- `GameUi.UpdateLayout(TouchController)` вызывается каждый кадр (и в меню): пересчитывает `TouchLayout`, при смене экрана, `scaleFactor`, `dpi` или леворукого режима — зеркало кнопки рывка и `ApplyLayout`.
- `Tools/mac-smoke.sh` при `-thornInputScript=…` требует строку `THORN_INPUT_SCRIPT ok=1`, иначе код 8.
