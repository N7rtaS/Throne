# Дизайн: жизненный цикл приложения (Task 15)

## 1. Архитектура
Новый чистый класс `ThornThrone.Mobile.AppLifecycle` (`Scripts/Mobile/AppLifecycle.cs`, слой Platform по ТЗ §8). Без `MonoBehaviour` и без вызовов Unity API, поэтому EditMode-тест вызывает его обработчики напрямую. `MobileGame` только пересылает события Unity и выполняет действия.

```
Unity ──OnApplicationPause/Focus──▶ MobileGame (если не smoke) ──▶ AppLifecycle.OnPause/OnFocus
                                     переход «активно → приостановлено» ─▶ suspend: MobileGame.Suspend
                                         AudioListener.pause = true; LoseFocus (GameInput.Reset,
                                         SetPaused(true) в Playing); settings.RetryFailedSave()
                                     переход «приостановлено → активно» ─▶ resume: AudioListener.pause = false
Update: run.Update(lifecycle.FrameDelta(Time.unscaledDeltaTime), …)
```

### `AppLifecycle`
```csharp
public sealed class AppLifecycle
{
    public AppLifecycle(Action suspend, Action resume);
    public bool InBackground { get; }   // last OnPause value, false at start
    public bool HasFocus { get; }       // last OnFocus value, true at start
    public bool Suspended { get; }      // InBackground || !HasFocus
    public int Suspensions { get; }     // «active → suspended» transitions (diagnostics, tests)
    public void OnPause(bool paused);
    public void OnFocus(bool focused);
    public float FrameDelta(float unscaledDelta); // 0 on the first frame after any lifecycle event
}
```
- `OnPause`/`OnFocus` меняют признак. Если `Suspended` стало `true` из `false` — один вызов `suspend`; из `true` в `false` — один вызов `resume`. Повторы и второй признак при уже приостановленном приложении ничего не вызывают (требования 1–3).
- `resume` пауза забега не снимает: только звук (требования 4, 6).
- Любое событие ставит флаг «отбросить время следующего кадра». `FrameDelta` возвращает 0 и снимает флаг, иначе возвращает аргумент (требование 5). Без выделений памяти.

### `GameSettings.RetryFailedSave()`
Новый метод: если `SaveFailed`, записывает профиль ещё раз и возвращает `true`; иначе ничего не пишет и возвращает `false` (требование 10). Подтверждённый прогресс (сейчас — только настройки) уже записан в момент подтверждения (ТЗ §9, §20 П9). Несохранённым может быть только профиль после неудачной записи, поэтому при уходе в фон повторяется только она. Безусловная запись на каждое событие писала бы файл при каждом открытии шторки и записывала бы новый профиль, который по §19 пишет только первое сохранение. Когда появятся валюта и результаты (Task 16, блок D), они тоже идут через запись профиля в момент подтверждения, а повтор неудачной записи при уходе в фон остаётся общей точкой.

### Изменения `MobileGame`
- `lifecycle = new AppLifecycle(Suspend, () => AudioListener.pause = false)` в начале `Start` после `input`. `OnApplicationPause(bool p) { if (!smoke) lifecycle?.OnPause(p); }`, аналогично `OnApplicationFocus` (требование 8; `?.` — события до `Start` пропускаются, приложение при запуске активно).
- `Suspend()`: `AudioListener.pause = true`; `LoseFocus()` (не меняется: сброс ввода, пауза в `Playing`, в остальных состояниях экран тот же — требование 7); `settings.RetryFailedSave()`, при попытке — строка ошибки на экране настроек и лог, как в `StepSetting`. В фоне кадров нет, поэтому всё выполняется прямо в обработчике.
- `Update`: `float delta = lifecycle.FrameDelta(Time.unscaledDeltaTime)` в начале каждого кадра (до `run == null` и ветки `Playing`), чтобы флаг снимался и в кадре, где забег на паузе. Иначе нулевое время досталось бы первому кадру после «Продолжить». Затем `run.Update(delta, …)`.

### Предупреждение о потере забега (требование 11)
ТЗ §9: «При уходе/выходе игрок видит это правило». Выход из забега уже предупреждает экран «Завершить забег?» (Task 11). Во время ухода в фон показать ничего нельзя, а вернувшийся игрок попадает на экран паузы. Поэтому на экран паузы добавляется строка `GameUi.RunLossWarning` = «Незаконченный забег не сохраняется: если система закроет игру, он будет потерян.» (метка `Pause/Content/Run loss warning`, между заголовком и «Продолжить», шрифт 34). Строка видна при каждой паузе: правило одно и то же, а отдельный режим «пауза после фона» усложнил бы экран без пользы. Выход из приложения кнопкой «Назад» возможен только из главного меню, где забега нет.

## 2. Правила (в `Mechanics.md` §22)
- **Р1.** Приостановка — переход из «активно» в «приостановлено», где приостановлено = фон (`OnApplicationPause(true)`) или потеря фокуса (`OnApplicationFocus(false)`). Выполняется один раз на переход, повторные события ничего не делают. Активным приложение становится только после снятия обоих признаков.
- **Р2.** Приостановка: звук на паузе, сброс ввода, пауза забега в бою, повтор неудачной записи профиля. Возврат снимает только паузу звука. Выбор улучшения, подтверждение выхода и настройки остаются на экране; в меню и на результате меняется только ввод.
- **Р3.** Время первого кадра после любого события жизненного цикла не передаётся симуляции: `Run.Update` получает 0. Дробный остаток шага (`Alpha`) сохраняется по §17 Р1.
- **Р4.** `AudioListener.pause = true`, пока приложение приостановлено; `AudioListener.volume` (общая громкость, §20 П6) не трогается. На игровой паузе звук здесь не меняется: это задача «Звук».
- **Р5.** Подтверждённый прогресс записывается в момент подтверждения; при уходе в фон повторяется только неудачная последняя запись. Незаконченный забег не сохраняется.
- **Р6.** На экране паузы всегда строка о потере незаконченного забега; при выходе — экран «Завершить забег?».
- **Р7.** В smoke (`-thornSmoke`) события жизненного цикла игнорируются: окно macOS теряет фокус при запуске из скрипта.

Источника в оригинале нет: Dota — настольный клиент, сворачивание не ставит игру на паузу. Всё это проектная адаптация под телефон по ТЗ §9. `Differences.md` Б10 «Жизненный цикл».

## 3. Решения и альтернативы
- Флаг «отбросить один кадр» вместо порога (`delta > X → 0`). Порог пропустил бы короткое сворачивание и отбросил бы настоящий долгий кадр (сборка мусора). Привязка к событию точнее.
- Признаки фона и фокуса раздельно. Порядок на Android: уход `Focus(false)` → `Pause(true)`, возврат `Pause(false)` → `Focus(true)`; шторка и системные диалоги дают только `Focus`. Один bool снимался бы на `Pause(false)` при ещё не вернувшемся фокусе.
- `FrameDelta` не зависит от `Suspended`: если Android не пришлёт `Focus(true)`, обнуляется только один кадр, игра не зависает.
- Повтор только неудачной записи вместо безусловной (см. §1).

## 4. Вне объёма
- Экран и сон (`Screen.sleepTimeout`) уже настроены: `NeverSleep` в забеге, системное поведение в меню и на результате.
- Проверка на телефоне — блок G.

## 5. Тестовая стратегия
EditMode `Tests/EditMode/AppLifecycleTests.cs`, вызовы обработчиков напрямую:
- `FocusLossThenBackgroundSuspendsOnce` — `Focus(false)`, `Pause(true)` → одна приостановка; возврат `Pause(false)`, `Focus(true)` → одно `resume`.
- `RepeatedEventsAreIdempotent` — двойные `Pause(true)`, `Focus(false)`, двойной возврат.
- `FocusLossAloneSuspends` — шторка: только `Focus(false)` / `Focus(true)`.
- `ActiveOnlyAfterBothFlagsCleared` — `Pause(false)` при потерянном фокусе: всё ещё `Suspended`, `resume` нет; новый уход после полного возврата считается.
- `FirstFrameAfterEventHasNoTime` — `FrameDelta(5)` после события = 0, следующий = аргумент; без события аргумент не меняется.
- `ReturnFromBackgroundRunsNoSteps` — реальный `Run`: приостановка ставит `run.Paused`, после возврата `run.Paused` всё ещё `true`; игрок снимает паузу, `run.Update(lifecycle.FrameDelta(10), …)` не меняет `run.Time`.
- `SuspendResetsInput` — удержанный палец `TouchController` после приостановки не даёт `Move`.
- `AudioPausedOnlyWhileSuspended` — `AudioListener.pause` через действия как в `MobileGame`, `AudioListener.volume` не меняется.
- `SuspendRetriesOnlyFailedSave` — `GameSettings` с подменной записью: без записей и после удачной `RetryFailedSave` не пишет; после неудачной пишет один раз, после успеха — больше нет.
- `PauseScreenWarnsRunIsLost` — `GameUi.ShowPause(true)`: метка `Run loss warning` видна, текст `GameUi.RunLossWarning` содержит «не сохраняется».
- `FrameDeltaDoesNotAllocate` — `GC.GetAllocatedBytesForCurrentThread` до и после серии вызовов.

`dotnet test` — без новых тестов, симуляция не меняется; полный прогон как регрессия. Smoke `Tools/mac-smoke.sh` как регрессия Unity-слоя.

## 6. Риски
- Реальный порядок событий на телефоне не проверен. Тесты покрывают все порядки, устройство — блок G.
- `AudioListener.pause` статический: тест восстанавливает значение в `TearDown`.
