# Дизайн: порядок экранов (Task 16)

## 1. Что известно об оригинале
| Факт | Метка | Источник |
|---|---|---|
| Порядок: главное меню «Играть» → «Выберите героя» (стрелка назад, «ДАЛЕЕ») → экран «Улучшения» + «Сложность» (стрелка назад, «START») → забег | **Д** (разметка) | `Research/Decoded/panorama/layout/battle_pass/crownfall/bp_popup_crownfall_encounter_survivors.vxml`, строки 585–648: `DOTASurvivorsGameMenuHeroScreen`, `DOTASurvivorsGameMenuDifficultyScreen`, `DOTAEncounterMinigameStart` |
| Экран итога: заголовок исхода, время, уровень, счёт, золото, «Эшелон N», «Открыта новая сложность!», «Доступны улучшения!», кнопки «Выбор героя», «Улучшения и сложность», «Сыграть ещё» | **Д** (разметка, локализация) | тот же файл, строки 700–746; `DOTA_Survivors_PlayAgain` = «Сыграть ещё», `DOTA_Survivors_NewDifficultyUnlocked` |
| Закрытая сложность подписана «НЕДОСТУПНО» | **Д** (локализация) | `DOTA_Survivors_DifficultyLocked`, `Research/Decoded/panorama/localization_russian.json` |
| В первый раз на экране героя сразу «START» без экрана сложности | **К** (только разметка, условие не восстановлено) | `HeroSelectPlayButtonFirstTimeIntro` |
| 8 сложностей, порядок данных = эшелоны 1..8, названия | **Д** | `Research/Json/survivors_difficulty.json`, `NestOfThorns/resource/localization/vdata/survivors_difficulty/survivors_difficulty_russian.txt`, Mechanics §16 |
| Эшелон N включает сложности 1..N, множители складываются | **Н** | Mechanics §8, §13 (Империя 33 000 HP на эшелоне 2) |
| Сначала открыт только эшелон 1; победа над Империей на эшелоне N открывает N+1; закрытый — серый с замком | **Н** (со слов игрока 09.11.2026) | Mechanics §8, ТЗ §16 |
| Данных о правилах открытия в vdata нет (полей `unlock` в `survivors_difficulty.json` и `survivors_game_modes.json` нет) | **Д** (отсутствие) | поиск по `Research/Json` |
| Открытие хранится на сервере (`EventMetaDevUnlockNextDifficulty`, `UpdateDifficultyLevels`, convar `dota_survivors_difficulty_setting`) | **К** | `Research/Decoded/client/strings.json` |
| Формула счёта не известна | — | Mechanics §13 |

## 2. Решения (выносятся на утверждение)
- **Ф1 (Р).** Идентификаторы открытий в `Profile.Unlocks`: `echelon_2` … `echelon_8`. Эшелон 1 открыт всегда и не хранится. Эшелон N открыт, если N = 1 или в профиле есть `echelon_N`; пропуски не достраиваются. Схема профиля не меняется (остаётся 2).
- **Ф2 (Н + Р).** Победа = `Run.Victory` (Империя убита). Победа на эшелоне N < 8 добавляет `echelon_{N+1}`. Победа на эшелоне ниже наибольшего открытого ничего нового не открывает. Победа на 8 ничего не открывает. Поражение ничего не открывает.
- **Ф3 (Р).** Запись результата — одна операция журнала `result:<runId>`; `runId` = `run-` + GUID, создаётся при старте забега. Если журнал уже содержит операцию, профиль не меняется и не пишется. Иначе в памяти: `Results.Add`, открытие по Ф2, `Journal.TryAdd`, затем `SaveStore.Save`. Валюту эта операция пока не начисляет (блок D, задачи 29–33); позже начисление войдёт в ту же операцию.
- **Ф4 (Р).** Поля `RunResult`: `Difficulty` = `echelon_N`, `DurationSeconds` = `Run.Time`, `Gold` = `Run.Gold` (только запись, в валюту не идёт), `Score` = 0 — формула счёта не известна (Mechanics §13). На экране результата счёт и золото не показываются, пока нет счёта и валюты.
- **Ф5 (Р).** «Завершить забег» из паузы ведёт в главное меню без записи результата (совпадает с предупреждением «Прогресс этого забега будет потерян.»). Сейчас этот путь показывает экран результата — поведение меняется. В оригинале «Выйти» вызывает `DOTAEncounterMinigameStart()`, что там происходит, не известно.
- **Ф6 (Р).** Экран сложности: 8 карточек «Эшелон N», название, описание из локализации (`<b>` сохраняется, `<br/>` → перевод строки). Закрытая: серый фон и текст, значок замка (процедурный спрайт, в PT Sans нет 🔒), «НЕДОСТУПНО», `interactable = false`. Первое открытие за запуск выбирает наибольший открытый эшелон, дальше — последний выбранный. Последний выбор между запусками не хранится (иначе нужна схема 3). Блок «Улучшения» на этом экране появится в блоке D.
- **Ф7 (Р).** Экран сложности показывается всегда, в том числе при первом запуске (ветка `HeroSelectPlayButtonFirstTimeIntro` оригинала не воспроизводится: игрок сразу видит, что эшелоны закрыты).
- **Ф8 (Р).** Экран результата: «Победа!»/«Поражение», «Эшелон N · <название>», время, уровень, убито, «Открыта новая сложность!» при открытии; кнопки «Сыграть ещё» (тот же герой и эшелон, новый seed), «Выбор героя», «Сложность», «В меню». Кнопка «В меню» — мобильная добавка (в оригинале окно закрывается крестиком).
- **Ф9 (Р).** Главное меню: заголовок, «Играть», «Настройки», строка о повреждённом профиле (§20 П12). Выбор героя уходит на свой экран: три карточки, Шен и Кез «в разработке» не выбираются (как сейчас), «Назад».
- **Ф10 (Р).** «Назад» Android: экран героя → меню, экран сложности → экран героя (расширение §18 Р11). Остальное без изменений.
- **Ф11 (Р).** Неудачная запись результата: строка «Не удалось сохранить результат» на экране результата, повтор при уходе в фон (`AppLifecycle`, §22 Р5) и любая следующая удачная запись профиля (настройки, следующий результат) сохраняет его, потому что профиль один объект.
- **Ф12 (Р).** Названия эшелонов попадают в Unity файлом `Assets/Game/Data/Localization/difficulty_ru.json`, который создаёт `Tools/import-unity-data.sh` из `survivors_difficulty_russian.txt` (KeyValues → JSON, ключи как в `m_sLocDifficultyName`/`Description`). `FontBaker.Characters` читает и этот файл. Все символы уже есть в атласе (кириллица + ASCII), перепекать шрифт не нужно; проверяется тестом.
- **Ф13 (Р).** `-thornFlow` перезаписывает тестовый профиль новым (`Profile.CreateNew(Test)`) — только `test.save*`, пользовательский файл не трогается (§20 П11). Настройки тестового профиля при этом сбрасываются на значения по умолчанию.

## 3. Архитектура
Новая сборка `ThornThrone.App` (ТЗ §8 «App»): `Assets/Game/Scripts/App/`, asmdef `noEngineReferences: true`, ссылки `ThornThrone.Simulation`, `ThornThrone.Progression`, Newtonsoft. Для `dotnet` — `DotNet/ThornThrone.App/ThornThrone.App.csproj` (netstandard2.1, C# 9, `TreatWarningsAsErrors`), подключается в `ThornThrone.Simulation.Tests.csproj`. `ThornThrone.Mobile` и `ThornThrone.Mobile.Tests` получают ссылку на `ThornThrone.App`.

```
AppFlow (App)                      FlowScreens (Mobile)              MobileGame (Mobile)
 Screen: Menu|Hero|Difficulty|      GameUi события ↔ AppFlow,          владеет AppFlow, берёт flow.Run,
         Run|Results                показ экранов меню/героя/           внутриигровые состояния
 Play, SelectHero, Next, Back,      сложности/результата                Playing/Paused/ConfirmQuit/
 SelectEchelon, Start, Finish,                                          Choosing/Settings как сейчас
 Abandon, PlayAgain, ToHero,
 ToDifficulty, ToMenu
 Run, Hero, Echelon, LastOutcome,
 IsUnlocked(n), EchelonCount
```

### `AppFlow` (`Scripts/App/AppFlow.cs`)
```csharp
public enum AppScreen { Menu, Hero, Difficulty, Run, Results }
public sealed class RunOutcome { public RunResult Result; public int Echelon; public int UnlockedEchelon; /* 0 — ничего */ public bool Recorded; }
public sealed class AppFlow
{
    public AppFlow(GameData data, Profile profile, Func<Profile, SaveResult> save, Func<ulong> seeds,
                   IReadOnlyCollection<string> playableHeroes, Func<Rules> rules = null);
    public AppScreen Screen { get; }
    public Run Run { get; }               // null вне Run/Results
    public string Hero { get; }
    public int Echelon { get; }           // выбранный эшелон 1..EchelonCount
    public int EchelonCount { get; }      // data.DifficultyOrder.Count
    public bool IsUnlocked(int echelon);
    public int HighestUnlocked { get; }
    public RunOutcome LastOutcome { get; }
    public SaveResult LastSave { get; }   public bool SaveFailed { get; }
    public bool Play(); public bool SelectHero(string key); public bool Next(); public bool Back();
    public bool SelectEchelon(int n); public bool Start();
    public bool Finish();                 // Run.Finished → Results + запись (Ф3); повтор — false, профиль не меняется
    public bool Abandon();                // Run → Menu без записи (Ф5)
    public bool PlayAgain(); public bool ToHero(); public bool ToDifficulty(); public bool ToMenu();
    public bool RetryFailedSave();
    public static string UnlockId(int echelon) => "echelon_" + echelon;
}
```
Запись делает `public bool Record()`: пишет текущий завершённый забег и возвращает `false`, если он уже записан. `Finish` вызывает `Record`. Идемпотентность держится журналом (`result:<runId>`), а не флагом экрана, поэтому повторный `Finish`, повторный `Record` и повтор после неудачной записи профиль не меняют.

### `FlowScreens` (`Scripts/Mobile/FlowScreens.cs`)
Не MonoBehaviour. Подписывается на события `GameUi` (`PlayRequested`, `HeroSelected`, `NextRequested`, `BackRequested`, `EchelonSelected`, `StartRequested`, `PlayAgainRequested`, `HeroScreenRequested`, `DifficultyRequested`, `MenuRequested`), вызывает `AppFlow` и показывает экран по `flow.Screen` (`GameUi.ShowMenu/ShowHeroSelect/ShowDifficulty(flow, names)/ShowResults(outcome, names, saveFailed)`). Событие `RunStarted` сообщает `MobileGame`, что `flow.Run` новый (сброс ввода, герой, HUD, `NeverSleep`). Проверяется EditMode без `MobileGame`.

### `MobileGame`
- `StartRun` и `new Run` удаляются; `run` = `flow.Run`. `State` теряет `Menu`/`Results` как самостоятельные ветки управления: они отражают `flow.Screen` (`Menu`, `Hero`, `Difficulty`, `Results` → `BackScreen`).
- Каждый кадр: если `run.Finished` → `flow.Finish()` → экран результата.
- `QuitConfirmed` → `flow.Abandon()` → меню.
- `Suspend` дополнительно вызывает `flow.RetryFailedSave()`.
- Smoke: обычный путь — `Play → SelectHero("dragonus") → Next → SelectEchelon(1) → Start` через `FlowScreens` (нажатие кнопок). `-thornFlow` / `-thornExpectFlow` — §4.

### `GameUi`
Новые панели: «Выберите героя» (3 карточки + «Далее» + «Назад»), «Сложность» (8 карточек 2×4 с номером, названием, описанием, замком; «Начать», «Назад»), обновлённый результат (Ф8), меню (Ф9). Карточка выбранного эшелона подсвечена рамкой (цвет дополняется формой — ТЗ §7). Все кнопки ≥ 48 dp (механизм `Targets` Task 11).

### `Run`
Добавляется `public void DebugWin() => Finish(true);` рядом с `DebugInvulnerable` (оригинал: отладочная кнопка `Win Game` в разметке). Других изменений нет.

### `BackNavigation`
`BackScreen.Hero → BackAction.ToMenu`, `BackScreen.Difficulty → BackAction.ToHeroSelect` (новое действие).

## 4. Smoke
- `-thornFlow` (Ф13): после загрузки профиль заменяется новым тестовым и записывается. Сценарий нажимает кнопки `GameUi` (`Button.onClick.Invoke` найденных по имени объектов) и проверяет `flow` по шагам из требования 19. Победа — `run.DebugWin()` через 2 с игрового времени. Итог: `THORN_FLOW ok=1 screens=Menu>Hero>Difficulty>Run>Results>Run>Menu>Hero>Difficulty results=1 unlocked=2`, при ошибке `ok=0 failed=<шаг>` и выход с кодом 10. После `ok=1` идёт обычный забег (эшелон 1, 30 с) и `THORN_MOBILE_SMOKE`.
- `-thornExpectFlow`: проверка загруженного профиля без сброса, `THORN_FLOW ok=1 results=1 unlocked=2` или `ok=0`, затем обычный забег.
- `Tools/mac-smoke.sh --flow`: запуск 1 `-thornFlow` (лог `mac-smoke-flow-1.log`), запуск 2 `-thornExpectFlow` (`mac-smoke.log`). Код 10, если передан `-thornFlow`/`-thornExpectFlow`, а строки `THORN_FLOW ok=1` нет. Разбор — `Tools/tests/test_mac_smoke.py` с образцами логов.

## 5. Тестовая стратегия
**dotnet (`DotNet/ThornThrone.Simulation.Tests/AppFlowTests.cs`)**, реальные данные `TestData.Load()`, профиль в `SaveStore` на `FakeFileSystem`:
- `NewProfileHasOnlyEchelonOneOpen` — `EchelonCount` = 8 = `DifficultyOrder.Count`, открыт только 1.
- `ScreensFollowMenuHeroDifficultyRunResults` и `BackReturnsToPreviousScreen`, `InvalidCommandChangesNothing`.
- `StartCreatesRunOnStackedEchelon` — эшелон 2: `run.Difficulty.EnemyHealthMultiplier` = −0.1 + 0.2 из данных, `Key` = `echelon_2`, герой выбранный.
- `LockedEchelonCannotBeSelected`, `UnavailableHeroCannotBeSelected`.
- `ResultIsRecordedOnce` — победа, `Finish` дважды и `Record` повторно: `Results.Count` = 1, журнал 1 запись `result:…`, `Save` вызван один раз.
- `VictoryOnEchelonNUnlocksNPlus1` — цикл N = 1..7 (профиль с открытыми 1..N): после победы открыт N+1, `LastOutcome.UnlockedEchelon` = N+1; на 8 открытий нет.
- `DefeatUnlocksNothing` (герой погибает: `run` без способностей, `Simulate` до смерти, по нескольким seed) — результат записан, `Victory` = false.
- `AbandonedRunIsNotRecorded`.
- `PlayAgainKeepsHeroAndEchelonWithNewSeed`.
- `FailedSaveIsRetried` — первая запись `IoError`, профиль в памяти с результатом, `RetryFailedSave` пишет, повтор после успеха не пишет.
- `RecordedResultSurvivesReload` — после записи `SaveStore.Load` даёт результат и `echelon_2`.
- `DifficultyScreenPreselectsHighestThenLastChosen`.
- `DebugWinFinishesRunWithVictory` (в `SimulationTests`).

**EditMode (`Tests/EditMode/ScreenFlowTests.cs`)**: `GameUi` + `AppFlow` + `FlowScreens` без `MobileGame`; нажатия кнопок по именам → видимая панель; закрытые карточки серые, с замком, `interactable = false`, «НЕДОСТУПНО»; «Назад» для `Hero`/`Difficulty` (`BackNavigation`); результат с «Открыта новая сложность!» и без; все новые кнопки ≥ 48 dp на 1280×720 и 2400×1080; символы `difficulty_ru.json` есть в шрифте.

**Python**: `Tools/tests/test_import_localization.py` (KV → JSON, 16 ключей), `test_mac_smoke.py` (код 10).

## 6. Риски
- `MobileGame` теряет `StartRun`: smoke-ключи `-thornBoss`, `-thornElite`, `-thornShowcase`, `-thornInputScript` должны работать на забеге, созданном `AppFlow`. Проверка — прогон этих smoke.
- Обычный smoke теперь идёт на эшелоне 1 (`story`, HP врагов −10%), а не на `Default`: забег немного легче; критерий kills > 0 не меняется.
- Экран сложности с 8 карточками и описаниями тесен на 1280×720 при масштабе 130 %: проверяется EditMode на 48 dp; наложение текста проверяется скриншотом.
