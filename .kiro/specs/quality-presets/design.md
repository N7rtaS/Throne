# Task 14. Пресеты качества — дизайн

## 1. Ассеты и уровни
| Уровень | Индекс | URP-ассет | Рендерер | Масштаб | HDR | MSAA | Тени основного | Доп. тени | Каскады | Дистанция | Мягкие |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Low | 0 | `Assets/Settings/Low_RPAsset.asset` (новый) | `Mobile_Renderer` | `clamp(720/h, .5, 1)` | нет | 1 | нет | нет | 1 | 0 | нет |
| Medium | 1 | `Mobile_RPAsset` (GUID `5e6c…1858` сохраняется) | `Mobile_Renderer` | `clamp(900/h, .5, 1)` | нет | 2 | 1024 | нет | 1 | 50 | нет |
| High | 2 | `PC_RPAsset` (GUID `4b83…5dfd` сохраняется) | `PC_Renderer` (SSAO) | 1 | да | 4 | 2048 | да | 2 | 50 | да |

Поля уровней `QualitySettings` (общие для всех: `vSyncCount 0`, `realtimeReflectionProbes 0`): Low — `pixelLightCount 1`, `shadows 0`, `skinWeights 2`, `lodBias 0.7`, `anisotropicTextures 0`, `softParticles 0`, `particleRaycastBudget 64`; Medium — `2`, `shadows 2`, `2`, `1`, `1`, `0`, `256`; High — `4`, `shadows 2`, `4`, `1.5`, `2`, `1`, `1024`. `m_PerPlatformDefaultQuality`: Android → 1, Standalone → 2 (до загрузки профиля).

Файлы ассетов не правятся руками: `ThornThrone.Editor.QualityPresetBuilder.Configure()` (новый файл в `Editor/`) создаёт `Low_RPAsset` копией `Mobile_RPAsset` при первом запуске (`AssetDatabase.CopyAsset`, дальше GUID стабилен), выставляет поля трёх ассетов через `SerializedObject` (имена `m_*` проверяются, отсутствующее поле — исключение) и переписывает массив `m_QualitySettings` в `ProjectSettings/QualitySettings.asset`. Вызывается из `BuildAndroid`, `BuildMacSmoke` и меню. Повторный запуск даёт те же байты.

Масштаб в ассете хранит значение для экрана 1080 строк (Low 0.67, Medium 0.83, High 1); во время игры его переписывает §2.

## 2. Применение (Unity-слой)
Новый `Scripts/Mobile/QualityPresets.cs`:
- `DeviceInfo { MemoryMb, ProcessorCount, Desktop, OpenGles }`, `DeviceInfo.Current()` из `SystemInfo` (`systemMemorySize`, `processorCount`, `deviceType == Desktop`, `graphicsDeviceType == OpenGLES3`).
- `Resolve(QualityPreset, DeviceInfo)` — Auto → пресет по §3, иначе как есть. Чистая функция.
- `Level(preset)` — индекс 0/1/2; `Names = { "Low", "Medium", "High" }` (их же пишет билдер).
- `RenderScale(preset, screenHeight)`.

`ISettingsTargets.SetQuality` теперь возвращает применённый пресет (не Auto). `UnitySettingsTargets.SetQuality`: `Resolve` → `QualitySettings.SetQualityLevel(level, true)` → `asset.renderScale = RenderScale(...)` у `QualitySettings.renderPipeline` → `vSyncCount = 0` (смена уровня берёт `vSyncCount` уровня, у всех 0, но П4 держим явно). Лог `THORN_QUALITY setting=auto level=Medium renderScale=0.83 hdr=0 msaa=2 mainShadows=1`.

`GameSettings` хранит `AppliedQuality`; `ValueText(Quality)` при Auto — «Авто (Среднее)». Подпись строки — «Качество». `MobileGame.SmokeSettings` дописывает `quality=<имя уровня> renderScale=…` и проверяет, что текущий уровень равен `Level(AppliedQuality)`.

`Run` и `Scripts/Simulation` не меняются: пресет влияет только на рендер.

## 3. Решения
- **Р (К1).** Автовыбор: `Desktop` → High; иначе Low, если память < 5000 МБ (телефоны с 4 ГБ сообщают ~3,5–3,8 ГБ, с 6 ГБ — ~5,5 ГБ), ядер < 6 или API OpenGL ES (Vulkan недоступен или в списке запрета Unity — признак старого/слабого GPU); иначе Medium. High на телефоне только вручную: целевой профиль ТЗ §11 — среднее качество. Пороги не проверены на устройствах (блок G).
- **Р (К2).** Масштаб рендера по числу строк: Low 720, Medium 900, High — родное; не ниже 0.5. На 2400×1080: 0.67 / 0.83 / 1; на 2048×1536: 0.5 / 0.59 / 1. Пересчитывается при смене пресета; экран в игре только альбомный, высота не меняется.
- **Р (К3).** HDR без пост-обработки (её в сцене нет) картинку не меняет, поэтому выключен и на Medium ради полосы памяти; включён только на High.
- **Р (К4).** Тени: единственный свет `Sun` создаётся с `LightShadows.None`, материалы героя теней не принимают (`Differences.md` Б4). Настройки теней в пресетах задаются по ТЗ, но сейчас картинку не меняют. LOD, частиц и декоративных объектов в игре пока нет: `lodBias` и `particleRaycastBudget` заданы заранее. Видимая разница пресетов сейчас — масштаб рендера, MSAA и SSAO на High.
- Имена файлов `Mobile_RPAsset`/`PC_RPAsset` оставлены ради неизменных GUID; соответствие уровням фиксирует таблица §1 и билдер.

## 4. Тесты
- EditMode `QualityPresetsTests`: `Resolve` по таблице устройств (4 ГБ, 6 ГБ/8 ядер, 6 ГБ/4 ядра, GLES, desktop, ручные значения не меняются); `RenderScale` для 1080, 720, 1536, 1440; уровни проекта — имена, ассеты, HDR/тени/MSAA Low и прочие поля §1; `UnitySettingsTargets.SetQuality(Low)` ставит уровень 0 и масштаб, `vSyncCount` 0; Auto в `ValueText`; подпись «Качество».
- Существующие `GameSettingsTests` обновлены (подпись, `FakeTargets.SetQuality` возвращает пресет); `TearDown` восстанавливает уровень и масштаб.
- `dotnet test` — без изменений симуляции, все зелёные.
- Smoke `Tools/mac-smoke.sh` с `-thornSettings=quality=low|medium|high` и `-thornExpectSettings`, скриншоты `Tools/mac-screenshot.sh`.

## 5. Риски
- `SetQualityLevel(…, true)` при смене в паузе даёт короткую задержку кадра — допустимо, игра на паузе.
- В редакторе изменение `renderScale` во время игры меняет ассет в памяти; билдер перезаписывает его при сборке, тесты восстанавливают.
