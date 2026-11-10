# Дизайн: экран настроек (Task 13)

Решения обозначены П1–П12 (настройки), у каждого метка **Р** (проектное решение): в оригинале настройки — меню клиента Dota, данных режима о них нет.

## 1. Архитектура

```
MobileGame.Start
  ├─ ProfileStorage.Open(user | test в smoke).Load()   → Profile, LoadStatus
  ├─ GameSettings(profile, store.Save, targets)        ← App/Platform, Scripts/Mobile/GameSettings.cs
  │     ApplyAll(): targetFrameRate, vSync 0, TouchSettings, UI scale, AudioListener.volume, IQualityTarget
  └─ GameUi: экран «Настройки» (строки «‹ значение ›»), событие SettingStepped(SettingId, ±1)
                                │
                                ▼
                 GameSettings.Step(id, delta): новое значение → применить → store.Save(profile)
```

- `GameSettings` (новый, `ThornThrone.Mobile`): владеет ссылкой на `Profile`, меняет только `profile.Settings`. Не `MonoBehaviour`. Unity-эффекты идут через узкий интерфейс целей, поэтому EditMode-тест подставляет свои цели и временное хранилище.
- `SettingsOptions` (новый, чистый статический класс в том же файле): списки допустимых значений, шаг, подписи на русском. Без Unity API, тестируется EditMode.
- Профиль живёт в `MobileGame` до Task 16 (там создание забега и профиль переедут в App). Это снимает решение С11 Task 12.

```csharp
public enum SettingId { Quality, TargetFps, MasterVolume, MusicVolume, EffectsVolume, VoiceVolume, UiScale, Joystick, Side, Vibration, DamageNumbers }

public interface ISettingsTargets
{
    void SetFrameRate(int fps);            // Application.targetFrameRate, QualitySettings.vSyncCount = 0
    void SetTouch(JoystickMode mode, bool leftHanded); // TouchController.Settings + Reset
    void SetUiScale(float scale);          // GameUi.SetUiScale
    void SetMasterVolume(float volume);    // AudioListener.volume
    void SetQuality(QualityPreset preset); // до Task 14 — пусто (П3)
}

public sealed class GameSettings
{
    public GameSettings(Profile profile, Func<Profile, SaveResult> save, ISettingsTargets targets);
    public ProfileSettings Values { get; }
    public SaveResult LastSave { get; }    // null до первой записи
    public void ApplyAll();                // при запуске, без записи
    public bool Step(SettingId id, int delta); // false — значение не изменилось (край диапазона), запись не делается
    public bool Set(SettingId id, string value); // smoke: «30», «1.2», «fixed», «left»; недопустимое значение — false и запись в лог
    public string ValueText(SettingId id);
}
```

`UnitySettingsTargets` (в `MobileGame.cs` или отдельном файле) реализует интерфейс над Unity и `GameInput.Controller`, `GameUi`.

## 2. Данные

- `ProfileSettings` получает поле `VoiceVolume` (реплики, ТЗ §7). Схема профиля 1 → 2, шаг миграции `AddVoiceVolume` (`IProfileMigration`, `settings.voiceVolume = 1`). Строгий декодер Task 12 считает отсутствующее поле повреждением, поэтому без миграции нельзя (П1, требует утверждения).
- Перечисление `JoystickMode` есть в двух сборках (`ThornThrone.Progression` и `ThornThrone.Mobile`). Сборки не объединяются; `GameSettings` переводит значения явным `switch` (П11).

## 3. Правила (в `Mechanics.md` §20 «Настройки»)

| № | Правило | Метка |
|---|---|---|
| П1 | Громкость реплик — отдельное поле `voiceVolume`, схема профиля 2 с миграцией 1 → 2 (значение 1). Общая громкость остаётся | **Р** |
| П2 | Все настройки — дискретные переключатели «‹ значение ›» с кнопками по краям вместо ползунков: одна запись на нажатие, нет конфликта перетаскивания с касаниями, цели 48 dp. Громкости 0–100% шагом 10%, масштаб UI 80–130% шагом 10%. Числа ограничиваются на краях (кнопка на краю ничего не делает), перечисления и вкл/выкл переключаются по кругу | **Р** |
| П3 | Качество до Task 14: выбор `Авто / Низкое / Среднее / Высокое` виден и сохраняется, `SetQuality` пустой; подпись строки «Качество (в разработке)». Task 14 реализует `SetQuality` (уровни `QualitySettings`, автовыбор по `SystemInfo`). Текущие уровни `Mobile`/`PC` не переключаются: их заменят ассеты Task 14 | **Р** |
| П4 | Частота кадров 30 / 60, `vSyncCount = 0`; по умолчанию 60 (С7) | **Р** |
| П5 | Масштаб UI: `CanvasScaler.referenceResolution = 1920×1080 / scale`. Экраны с фиксированной раскладкой (меню, карточки, пауза, подтверждение, результат, настройки) лежат в контейнере 1920×1080, который уменьшается до `min(1, ширина safe area / 1920, высота safe area / 1080)` в единицах Canvas. HUD растёт вместе со шкалой, у меню рост ограничен вписыванием. Кнопки не выходят за safe area. 48 dp считаются после масштаба (`ApplyLayout` уже использует `canvas.scaleFactor`) | **Р** |
| П6 | Общая громкость — `AudioListener.volume`. Музыка, эффекты, реплики только хранятся; применяет задача «Звук» | **Р** |
| П7 | Вибрация и цифры урона только хранятся и доступны через `GameSettings.Values`. Вибрации и цифр урона в игре нет; разрешение `VIBRATE` не добавляется | **Р** |
| П8 | Смена джойстика или стороны вызывает `TouchController.Reset()`: раскладка меняется под удержанным пальцем, удержание снимается (как поворот, §18 Р6) | **Р** |
| П9 | Каждое изменение сразу применяется и записывается. Ошибка записи значение не откатывает; на экране настроек строка «Не удалось сохранить настройки», в лог — `SaveResult.Message`. Следующее изменение пробует снова | **Р** |
| П10 | «Настройки» открываются из главного меню и из паузы. «Готово» и «Назад» возвращают на исходный экран. В забеге `run.Paused` остаётся `true`. `BackScreen.Settings → BackAction.CloseSettings` | **Р** |
| П11 | Профиль `user` в обычном запуске, `test` при `-thornSmoke` (ТЗ §9: тестовый профиль отдельно), smoke не трогает пользовательский файл | **Р** |
| П12 | `ResetAfterCorruption`: в главном меню строка «Сохранение повреждено. Создан новый профиль, старые файлы сохранены.» до первого старта забега. `RestoredFromBackup` — только запись в лог | **Р** |

## 4. UI

- Экран «Настройки» — панель в контейнере 1920×1080 (П5). Две колонки по 5–6 строк, высота строки 120 единиц:
  - левая «Изображение и управление»: Качество, Частота кадров, Масштаб интерфейса, Джойстик (Плавающий / Фиксированный), Сторона джойстика (Слева / Справа), Вибрация;
  - правая «Звук и бой»: Общая громкость, Музыка, Эффекты, Реплики, Цифры урона.
  - внизу «Готово», над ней строка ошибки записи (П9).
- Строка: подпись, кнопки `«` и `»` по 110×110 (в атласе PT Sans нет `‹ ›`) (цели через `AddTarget`), значение между ними. Текст меняется только при изменении (`ValueText`), не в каждом кадре.
- В меню — кнопка «Настройки», в паузе — «Настройки» между «Продолжить» и «Завершить забег».
- `GameUi`: `ShowSettings(bool)`, `SetSettingText(SettingId, string)`, `SetSettingsError(bool)`, `SetUiScale(float)`, `ShowProfileWarning(bool)`; события `SettingsRequested`, `SettingStepped(SettingId, int)`, `SettingsClosed`.

## 5. Smoke перезапуска

- Аргумент `-thornSettings=targetFps=30,uiScale=1.2,joystick=fixed,side=right,masterVolume=0.5,voiceVolume=0.3` (`side` — сторона джойстика; `right` = леворукий режим) — после загрузки профиля значения ставятся через тот же путь, что кнопки (`GameSettings.Set`), и записываются.
- Аргумент `-thornExpectSettings=<тот же формат>` — после загрузки сравниваются значения профиля и фактическое состояние (`Application.targetFrameRate`, `TouchController.Settings`, `AudioListener.volume`, масштаб Canvas). Лог `THORN_SETTINGS ok=1 targetFps=30 …`; при расхождении `ok=0` и код выхода 9.
- `Tools/mac-smoke.sh --settings-restart`: запуск 1 с `-thornSettings`, запуск 2 с `-thornExpectSettings`, затем запуск 3 с `-thornSettings` значений по умолчанию (тестовый профиль возвращается в исходное состояние). Каждый запуск — обычная проверка `THORN_MOBILE_SMOKE`. Новый код 9: нет `THORN_SETTINGS ok=1`.

## 6. Альтернативы
- Ползунки вместо переключателей: точнее для громкости, но запись на каждом кадре перетаскивания или по отпусканию и отдельная логика захвата касания. Переключатели проще и сразу удовлетворяют 48 dp.
- Громкость реплик вместо общей (переименовать поле): без миграции, но теряется общий регулятор и ломается С7. Миграция проверяет механизм Task 12 на деле.
- Временное сопоставление качества с уровнями `Mobile`/`PC`: видимый эффект сейчас, но Task 14 всё переделает, и `PC` на телефоне не проверен.
- Масштаб только для HUD (поэлементно): точнее «без потери доступа», но много ручного кода якорей. Вписывание контейнера даёт тот же результат для меню одним правилом.

## 7. Тестовая стратегия

EditMode `Tests/EditMode/GameSettingsTests.cs` (фейковые цели, `SaveStore` над временным каталогом `DiskFileSystem`, не `persistentDataPath`):
- `ChangingFrameRateSavesAndSetsTargetFrameRate` — реальные цели Unity для частоты: `Application.targetFrameRate == 30`, повторная загрузка хранилища даёт 30; `TearDown` восстанавливает значение.
- `ChangingJoystickAndSideUpdatesTouchController` — `TouchController.Settings.Joystick == Fixed`, `LeftHanded == true`, удержанный палец сброшен.
- `SettingsSurviveReload` — все 11 значений после `Step` и новой загрузки.
- `NumbersClampAtEdgesAndEnumsWrap` — громкость 1 → `Step(+1)` = false без записи; масштаб 0,8 → `Step(-1)` = false; качество `High → Auto`.
- `SaveFailureKeepsAppliedValue` — хранилище с ошибкой записи: значение применено, `LastSave` неуспешен, исключения нет.
- `ApplyAllDoesNotSave` — запуск не пишет файл.
- `UiScaleKeepsButtonsInsideSafeArea` — `GameUi` при 0,8 и 1,3 на 1280×720, 1920×1080, 2400×1080, 2048×1536: каждая цель экрана меню, паузы и настроек внутри safe area (чистая функция вписывания + проверка прямоугольников).
- `BackFromSettingsReturnsToOrigin` — `BackNavigation.For(Settings) == CloseSettings`.
- `CorruptProfileShowsWarning` — `LoadStatus.ResetAfterCorruption` → флаг сообщения.

dotnet (`ProfileTests`/`SaveStoreTests`): `SchemaOneProfileMigratesWithVoiceVolume`, `VoiceVolumeRoundTrips`, `VoiceVolumeOutOfRangeIsCorrupt`; правка существующих ожиданий версии схемы.

Smoke: `Tools/mac-smoke.sh` (регрессия) и `Tools/mac-smoke.sh --settings-restart`, скриншот экрана настроек `-thornShowSettings` в `Research/Скриншоты/task-13-settings.png`.

## 8. Риски
- `Application.targetFrameRate` в EditMode: значение ставится и читается, но кадры редактора от него не зависят; тест проверяет только значение.
- Масштаб 1,3 на 4:3 (2048×1536) при `matchWidthOrHeight = 0.5`: вписывание уменьшит меню; HUD у краёв может перекрываться. Тест проверяет только выход за safe area, наложение — проверка на устройстве (блок G).
- Схема 2: APK с Task 12 (схема 1) откроет новый профиль как повреждённый (С6). Пользовательских установок пока нет.
