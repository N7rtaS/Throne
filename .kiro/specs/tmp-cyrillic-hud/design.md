# Дизайн: TextMeshPro, кириллический шрифт, HUD без аллокаций (Task 10)

## 1. Что уже есть
- `com.unity.ugui` 2.6.0 в Unity 6 содержит TextMeshPro: `Runtime/TMP/Unity.TextMeshPro.asmdef`, namespace `TMPro`, в `Package Resources/` лежит `TMP Essential Resources.unitypackage`. Строка `tech.md` «TextMeshPro … в манифесте пока нет» устарела: отдельный пакет `com.unity.textmeshpro` в Unity 6 не нужен.
- В проекте нет `Assets/TextMesh Pro/`. Без `TMP Settings.asset` и шейдеров TMP при первом `TextMeshProUGUI` в редакторе открывает окно импорта, а в плеере не находит настроек по умолчанию.
- `TMP_Text.SetText(string, float…)` пишет число во внутренний буфер символов без строк. Формат `{0:00}` даёт дополнение нулями до 2 знаков (`padding` в `TMP_Text.SetText`, стр. ~2876 исходника пакета), `{0}` — целое без дробной части.
- В PT Sans (проверено по системной копии macOS `PTSans.ttc`, 694 кодовые точки) есть `× « » — – … № ° Ё ё`, но **нет `→` (U+2192)**. Стрелка используется в `GameUi.Describe`: `"Уровень {rank} → {rank + 1}"`. Остальные символы локализации (`powerups_ru.json`: кириллица, `×`) в шрифте есть.

## 2. Решения (на утверждение)

| # | Решение | Метка | Обоснование |
|---|---|---|---|
| Р1 | Шрифт — **PT Sans Regular** (`PT_Sans-Web-Regular.ttf`, ~280 КБ) из репозитория Google Fonts: `https://raw.githubusercontent.com/google/fonts/main/ofl/ptsans/PT_Sans-Web-Regular.ttf` и `.../ofl/ptsans/OFL.txt` (ParaType, OFL 1.1, URL проверены, HTTP 200). Путь: `UnityProject/Assets/Game/Art/Fonts/PTSans-Regular.ttf` + `OFL.txt`. Системную копию macOS не берём: это файл Apple, условия его распространения не наши. Noto Sans не берём: файл больше, кириллица та же | Р | ТЗ §7, задача называет PT Sans или Noto Sans; PT Sans сделан для кириллицы, меньше по размеру. Жирное начертание не нужно: `<b>` в TMP рисуется утолщением SDF |
| Р2 | `→` в карточке заменить на `»`: `"Уровень 1 » 2 / 4"`. Альтернатива — второй шрифт-заглушка только для стрелок (больше файлов и атласов ради одного символа) | Р | В PT Sans нет U+2192; квадрат вместо символа недопустим (требование 5) |
| Р3 | Атлас **статический**, 1024×1024 Alpha8 (1 МБ), размер выборки 48 pt, отступ 6, рендер SDFAA, без мульти-атласа. Набор символов ~180. Шейдер `TextMeshPro/Mobile/Distance Field` | Р | Статический атлас не дорисовывает глифы во время игры (нет выделений и задержек), исходный TTF плееру не нужен. Размеры текста в UI 32–96 px, SDF 48 pt даёт чистые края при этом масштабе. Параметры можно поменять после проверки на телефоне (блок G) |
| Р4 | TMP Essential Resources импортировать в batchmode `AssetDatabase.ImportPackage(<пакет>/Package Resources/TMP Essential Resources.unitypackage, false)` из `FontBaker` и закоммитить `Assets/TextMesh Pro/` как есть (GUID задаёт пакет). В `TMP Settings` поменять только шрифт по умолчанию на PT Sans SDF. `LiberationSans SDF` (латиница, ~1–2 МБ в `Resources`) остаётся в сборке как запасной шрифт TMP | Р | Стандартная установка TMP; ручной импорт через окно редактора требует GUI |
| Р5 | Шрифт передаётся явно: поле `MobileGame.uiFont` (заполняет `MobileBuilder.CreateScene`) → конструктор `GameUi(localization, font)`. При `null` — `TMP_Settings.defaultFontAsset` | Р | Тот же путь, что у остальных ассетов сцены (`powerupLocalization`, материалы); тесты создают `GameUi` со шрифтом из `AssetDatabase` |

Игровых правил задача не вводит, поэтому `Mechanics.md` не меняется. В `Differences.md` добавляется строка Б7 «Шрифт»: в оригинале шрифты клиента Dota (panorama `titleFont`, `monospaceNumbersFont`; файлы Valve, несвободные), у нас PT Sans (OFL); причина — автономная поставка без ресурсов, которые нельзя распространять; влияние — другой рисунок букв, стрелка `→` заменена на `»`.

## 3. Архитектура изменений

### 3.1 `FontBaker` (`Assets/Game/Editor/FontBaker.cs`, namespace `ThornThrone.Editor`)
- `[MenuItem("Thorn Throne/Fonts/Bake UI font")]` и batchmode-метод `ThornThrone.Editor.FontBaker.Bake`. Успех: `THORN_FONT_BAKE_OK`; ошибка — исключение, Unity выходит с ненулевым кодом.
- Шаги: (1) `EnsureTmpEssentials()` — если нет `Assets/TextMesh Pro/Resources/TMP Settings.asset`, импорт пакета (Р4); (2) набор символов `Characters()` = ASCII + русский алфавит + `«»—–…№°×` + символы из `powerups_ru.json` и строковых литералов `Scripts/Mobile/*.cs`; (3) `TMP_FontAsset.CreateFontAsset(font, 48, 6, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false)`, `TryAddCharacters(set, out missing)`; при непустом `missing` — исключение со списком (требование 5); (4) `atlasPopulationMode = Static`, шейдер материала — мобильный; (5) сохранение. Если ассет уже существует, данные копируются в него (`EditorUtility.CopySerialized`, атлас и материал заменяются как подобъекты), чтобы GUID не менялся; (6) `TMP_Settings.defaultFontAsset` → новый ассет.
- Набор символов считается одинаково при каждом запуске (упорядоченный), повторный запуск даёт тот же атлас.
- Запекание — отдельный шаг конвейера, как `ArenaBaker`/`EnemyBaker` (п. 7 конвейера в `tech.md`/`Tools/README.md`). `MobileBuilder` не запекает, а только ссылается на готовый ассет; если ассета нет, сборка падает с подсказкой запустить `FontBaker.Bake`.

### 3.2 `GameUi`
- Поля `Text` → `TMP_Text` (компонент `TextMeshProUGUI`). `Label(...)`: `font`, `fontSize`, `alignment` (соответствие `TextAnchor` → `TextAlignmentOptions`: `UpperLeft→TopLeft`, `UpperCenter→Top`, `MiddleCenter→Center`), `textWrappingMode = Normal`, `overflowMode = Truncate`, `raycastTarget = false`, `richText = true`.
- Убирается `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")` и `StringBuilder text`.
- Кэш значений HUD — маленький `sealed class HudText` (в `GameUi.cs`, публичный для EditMode-тестов): хранит `TMP_Text`, формат и последнее значение (`int`, начальное `int.MinValue`). `Set(int value)` возвращает `false` и не трогает метку, если значение то же; иначе `label.SetText(format, value)`. Для времени — `Set(int a, int b)` с `"{0:00}:{1:00}"` по целым секундам: минуты и секунды пересчитываются из `Mathf.FloorToInt(run.Time)`, кэш по целым секундам.
- HP Империи: `Mathf.CeilToInt(run.Boss.Health)`, формат `"Империя {0}"`. Показ метки по-прежнему через `SetActive`.
- Карточки, пауза, результат пишутся через `text =` как сейчас: это не каждый кадр, выделения там допустимы (требование 10 — только `UpdateHud`). `Describe` меняет `→` на `»` (Р2). `ShowResults` использует `FormatTime` (строка), он остаётся для экрана результата.
- После очистки HUD при `ShowRun` кэш сбрасывается (`Reset()`), чтобы новый забег с теми же значениями (уровень 1, 0 убийств) получил текст.

### 3.3 `MobileGame` и `MobileBuilder`
- `public TMP_FontAsset uiFont;` в `MobileGame`; `new GameUi(names, uiFont)`.
- `MobileBuilder.CreateScene`: `game.uiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBaker.FontAssetPath)`; при `null` — `InvalidOperationException`. Сцена `Mobile.unity` меняется (новое поле) и входит в коммит.
- asmdef `ThornThrone.Mobile` и `ThornThrone.Mobile.Tests`: добавить ссылку `Unity.TextMeshPro`.

### 3.4 Документы
- `README.md`: раздел «Шрифты» (PT Sans, ParaType, OFL 1.1, источник, путь, файл лицензии) и шаг запекания `FontBaker.Bake` в конвейере и командах.
- `Tools/README.md`: шаг запекания шрифта рядом с `ArenaBaker`/`EnemyBaker`.
- `tech.md`: заменить «TextMeshPro по ТЗ обязателен, но в манифесте пока нет» на «TextMeshPro входит в uGUI 2.6.0; Essential Resources в `Assets/TextMesh Pro`, шрифт PT Sans SDF запекается `FontBaker`». `product.md` — пункт «Текущее состояние» про число EditMode-тестов.
- `Differences.md`: Б7 (раздел 2).

## 4. Тестовая стратегия (EditMode, `Tests/EditMode/HudTextTests.cs`, фикстура `HudTextTests`)
- `HudUpdateDoesNotAllocate`: `GameUi` со шрифтом из `AssetDatabase`, `Run` Dragonus (seed 7), `GameInput`, `ShowRun`. Прогрев: 60 кадров (шаг + `UpdateHud`), затем `SpawnBossNow` и ещё 30 кадров прогрева. Проверка: 300 кадров; между вызовами (вне замера) `run.Update(Rules.Step, …)` с автоматическим выбором улучшения; сам вызов `ui.UpdateHud(run, input)` + `Canvas.ForceUpdateCanvases()` проверяется `Is.Not.AllocatingGCMemory()` (`UnityEngine.TestTools.Constraints`). Дополнительно утверждается, что за замер текст времени менялся (≥ 9 смен за 10 с), иначе тест не проверял бы ветку `SetText`. Перед тестом без босса — отдельный замер 300 кадров первой минуты.
- `HudTextSkipsUnchangedValue`: `HudText` на метке; `Set(5)` → текст «Убито 5»; вручную `label.text = "x"`; `Set(5)` возвращает `false`, текст остаётся «x»; `Set(6)` → «Убито 6».
- `HudTimeIsZeroPadded`: `Set(1, 5)` → «01:05», `Set(12, 0)` → «12:00».
- `FontCoversUiText`: ассет загружен, `atlasPopulationMode == Static`, `HasCharacters` для ASCII, русского алфавита и всех символов `powerups_ru.json` и литералов `Scripts/Mobile/*.cs` (тот же отбор, что в `FontBaker`, но написанный в тесте независимо).
- `GameUiUsesOnlyTmpText`: после создания `GameUi` в сцене нет `UnityEngine.UI.Text`, у всех `TMP_Text` шрифт — PT Sans SDF.
- Teardown уничтожает созданные объекты (`UI`, `EventSystem`).
- Симуляция не меняется: `dotnet test` как регрессия. Smoke и скриншот — визуальная проверка кириллицы и раскладки.

## 5. Риски
- `Canvas.ForceUpdateCanvases()` может выделять память внутри uGUI сам по себе. Тогда замер оставляет только `UpdateHud` (TMP перестраивает меш при отрисовке), а перестройка меша проверяется отдельным прогревом; решение фиксируется в тесте комментарием.
- `Is.Not.AllocatingGCMemory` в EditMode зависит от `Recorder` профайлера; если в batchmode он не работает, замер делается через `GC.GetAllocatedBytesForCurrentThread()` вокруг цикла вызовов.
- Копирование в существующий ассет при повторном запекании (`CopySerialized`) — тонкое место TMP; проверяется сравнением GUID в `.meta` до и после второго запуска.
- `LiberationSans SDF` из Essential Resources добавляет ~1–2 МБ к сборке (бюджет 1,5 ГБ).
## 6. Результаты реализации
- `AssetDatabase.ImportPackage` откладывает импорт и не завершается внутри одного batchmode-метода. `FontBaker` распаковывает `TMP Essential Resources.unitypackage` (tar.gz: `guid/asset`, `asset.meta`, `pathname`) сам и вызывает `AssetDatabase.Refresh`; GUID берутся из `.meta` пакета, как при обычном импорте (Р4 по сути не меняется). В `TMP Settings.asset` кроме шрифта по умолчанию Unity при сохранении переформатировал длинные строки и записал `m_ActiveFontFeatures` в новом формате.
- Повторный `FontBaker.Bake` даёт побайтово тот же `PTSans-Regular SDF.asset`, GUID `df0c92030200d4a68aa588358c44922c` не меняется. Набор — 169 символов, недостающих нет. Ассет 2,2 МБ (YAML), атлас 1 МБ.
- `SetText(string, int, int)` неоднозначен с `SetText(ReadOnlySpan<char>, int start, int length)` (подстрока, не формат), поэтому `HudText` передаёт аргументы как `float`.
- Только в редакторе `TMP_Text.SetText(format, …)` создаёт `char[]` и строку для инспектора (`m_text = InternalTextBackingArrayToString()` под `#if UNITY_EDITOR`), в плеере этого кода нет. Поэтому `HudUpdateDoesNotAllocate` замеряет `UpdateHud` и `Canvas.ForceUpdateCanvases()` отдельно через `Recorder("GC.Alloc")` и требует: перестройка канваса — 0, `UpdateHud` — ровно 2 × (число изменившихся меток) в кадре. Кадр без изменений — 0 (`HudUpdateWithoutChangesDoesNotAllocate`). Нулевые выделения в плеере выводятся из исходника TMP, отдельно в плеере не измерены.
- Найдено тестом: при переходе «Убито 9» → «Убито 10» TMP увеличивает буферы символов и вершин (лишние 2 выделения). `HudText.Warm` один раз размечает метку с самым длинным значением (`9999999`, время `99:59`) при создании UI, буферы TMP только растут.
- В тесте `run.DebugInvulnerable = true`: неуправляемый герой погибает от Империи раньше конца замера, а проверяется HUD.
- По скриншоту smoke: метки «00:00» (56 px в рамке 70) и «II» (56 px в рамке 72) не отображались. Строка PT Sans при 56 px занимает 72 px, а TMP в режиме `Truncate` скрывает строку, которая не помещается. Legacy `Text` при тех же размерах текст показывал. Исправление: высота метки времени 80 (верх и выравнивание прежние), у подписи «II» `Overflow`. Тест `GameUiLabelsAreNotTruncated` проверяет `isTextTruncated` у всех непустых меток меню и HUD. Тексты карточек и результата в нём не проверяются, их видно на экране выбора улучшения.
- Итог: 13 EditMode-тестов (7 новых), 86 тестов `dotnet test`, smoke `THORN_MOBILE_SMOKE time=30.0 kills=29`, скриншот `Research/Скриншоты/task10-tmp-hud.png` (`-thornBoss`: «Ур. 1», «00:07», «Убито 0», «Империя 30000», «II», «Рывок»).

