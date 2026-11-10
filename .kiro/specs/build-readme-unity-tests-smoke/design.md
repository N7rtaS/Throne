# Дизайн: README сборки, Unity-тесты, автоматический smoke

Roadmap: Task 8. Требования — `requirements.md`. Метки: **Д** — из файлов проекта/инструментов, **Р** — проектное решение.

## 1. Исходное состояние (проверено 2026-11-10)
- Корневого `README.md` нет; есть `Tools/README.md` (инструменты, конвейер, Android).
- Скрипты `Scripts/Mobile/*.cs` компилируются в `Assembly-CSharp`; `Editor/*.cs` — в `Assembly-CSharp-Editor` (папка `Editor`, asmdef нет). Единственный asmdef — `ThornThrone.Simulation`.
- `com.unity.test-framework` 1.8.0 есть в lock как транзитивная зависимость (depth 1, builtin), в `manifest.json` его нет.
- `MobileGame` при `-thornSmoke` играет Dragonus 30 с, пишет `THORN_MOBILE_SMOKE time= kills= level= hp= enemies= diagnostics=` и вызывает `Application.Quit(kills > 0 ? 0 : 1)` (**Д**, `MobileGame.cs`).
- `Tools/mac-screenshot.sh` запускает плеер через `open -n`, поэтому код выхода плеера теряется.
- `dotnet test` — 77 тестов, все проходят (запуск 2026-11-10). Git: 14 коммитов на `main`.
- Версии (**Д**): Unity 6000.6.7f1 (b22767f04f04, `ProjectVersion.txt`); пакеты lock: glTFast 6.19.0, Newtonsoft 3.2.2, Input System 1.20.0, URP 17.6.0, uGUI 2.6.0, test-framework 1.8.0, ide.rider 3.0.40, ide.visualstudio 2.0.28, collab-proxy 2.13.6, vectorgraphics 1.0.0. Модуль Android Unity (`PlaybackEngines/AndroidPlayer`): SDK build-tools 36.0.0, platforms android-34/36/37.0, platform-tools 37.0.1, cmdline-tools 16.0, NDK r27c (27.2.12479018), OpenJDK Temurin 17.0.18, Gradle 9.3.1. Source2Viewer CLI 20.1.7467. На машине: Python 3.13.3, Node 18.16.0, .NET SDK 8.0.301.

## 2. Корневой README.md
Разделы: назначение (1 абзац, ссылки на ТЗ и `Research/FullGame`), требования и версии (таблица «компонент — версия — откуда проверить»), первый запуск после checkout (`git lfs pull`, данные уже в репозитории — Dota нужна только для повторного извлечения), конвейер ресурсов (7 шагов одной таблицей со ссылкой на `Tools/README.md`), команды проверок и сборки (таблица «команда — выход — признак успеха»), переменные окружения, что не коммитится и Git LFS, ограничения (Dota/Steam только чтение; batchmode не работает при открытом Editor на этом проекте; первая Android-сборка ~5 мин).

**Р1.** `Tools/README.md` остаётся подробным справочником инструментов (коды выхода, форматы). Корневой README — короткий обзор со ссылками, без копирования. Причина: один источник правды для каждой детали.

**Р2.** Версии Python/Node указываются как «проверено на» (3.13.3, 18.16.0) плюс минимальные требования: Python 3, Node.js с ES-модулями (`kv3-to-json.mjs`). Минимальные версии в проекте нигде не зафиксированы, придумывать их не стоит.

## 3. Сборки Unity
### 3.1 `ThornThrone.Mobile` (`Assets/Game/Scripts/Mobile/ThornThrone.Mobile.asmdef`)
```json
{ "name": "ThornThrone.Mobile", "rootNamespace": "ThornThrone.Mobile",
  "references": ["ThornThrone.Simulation", "Unity.InputSystem", "Unity.RenderPipelines.Universal.Runtime",
                 "Unity.RenderPipelines.Core.Runtime", "UnityEngine.UI"],
  "overrideReferences": true, "precompiledReferences": ["Newtonsoft.Json.dll"], "autoReferenced": true }
```
Ссылки по `using` в `Scripts/Mobile` (**Д**). Ссылки по имени, как в `ThornThrone.Simulation.asmdef`. glTFast в Mobile не используется (только в `Editor/` через строковые имена).
`Editor/` остаётся в `Assembly-CSharp-Editor`: эта сборка видит все `autoReferenced` asmdef, поэтому `MobileBuilder`/`EnemyBaker` компилируются без правок. `.meta` скриптов не меняются: ссылки сцены и `EnemyVisual`-ассетов идут по GUID скрипта.

**Р3.** asmdef для `Editor/` не создаётся — в задаче его нет, и переход ничего не даёт сейчас. Следствие: тесты не могут ссылаться на `MobileBuilder` (Assembly-CSharp-Editor нельзя подключить из asmdef).

### 3.2 `ThornThrone.Mobile.Tests` (`Assets/Game/Tests/EditMode/ThornThrone.Mobile.Tests.asmdef`)
`includePlatforms: ["Editor"]`, `defineConstraints: ["UNITY_INCLUDE_TESTS"]`, `references: ["ThornThrone.Mobile", "ThornThrone.Simulation", "UnityEngine.TestRunner", "UnityEditor.TestRunner"]`, `precompiledReferences: ["nunit.framework.dll", "Newtonsoft.Json.dll"]`, `overrideReferences: true`, `autoReferenced: false`.

**Р4.** Расположение `Assets/Game/Tests/EditMode/` (а не внутри `Scripts/Mobile`), чтобы тестовая сборка не попадала в папку рантайм-asmdef. Добавить строку в таблицу `structure.md`.

`manifest.json`: `"com.unity.test-framework": "1.8.0"` (версия из lock, builtin). Lock изменится только в поле `depth` этой записи.

### 3.3 Стартовые EditMode-тесты (`MobileLayerTests`, один фикстур)
Ожидаемые значения — из данных и кода, не из реализации теста.
| Тест | Проверка | Источник |
|---|---|---|
| `SimulationAssemblyHasNoEngineReferences` | `typeof(Run).Assembly.GetReferencedAssemblies()` без имён `UnityEngine*`/`UnityEditor*` | structure.md «Слои», asmdef `noEngineReferences` |
| `SourceDataLoadsInUnity` | `GameData.Load` из `TextAsset` `Assets/Game/Data/Source/survivors_*.json` + `survivors_arena.gnv.bytes`: героев 3, powerups 47, врагов 43, спавнеров 43, сложностей 8, `Nav != null` | ТЗ §2 (222 определения) |
| `DragonusRunStepsInUnity` | `new Run(data, new Rules(), "dragonus", null, seed)`, 300 вызовов `Update(Rules.Step, …)` без исключений, `Time` ≈ 10 с | `Rules.Step` = 1/30 |
| `LoopedClipFrameWraps` / `OneShotClipFrameClamps` | `EnemyVisual.Frame`: loop — по модулю `frameCount` (в т.ч. при отрицательном времени), иначе — не больше `frameCount-1`, плюс `startFrame`; невалидный клип → 0 | `EnemyVisual.cs` |
| `ToWorldMapsSourceAxes` | `ToWorld((100, 200), 1)` = `(1, 1, 2)` | `WorldRenderer.Scale` = 0.01, Source y → Unity z |

**Р5.** Набор намеренно стартовый: проверяет то, что `dotnet test` не покрывает (Newtonsoft под Mono, `TextAsset`, границу сборок, чистые функции представления). Тесты ввода (`TouchController`) и UI появятся в задачах блока B.

## 4. `Tools/mac-smoke.sh`
```
Tools/mac-smoke.sh [--no-build] [аргументы плеера…]
```
1. Если нет `--no-build`: `"$unity" -batchmode -nographics -quit -projectPath UnityProject -executeMethod ThornThrone.Editor.MobileBuilder.BuildMacSmoke -logFile Research/mac-build.log`. Ненулевой код Unity или нет `THORN_MAC_BUILD_OK` в логе → выход 2 и последние строки `error`/`Exception` лога.
2. Исполняемый файл берётся из `CFBundleExecutable` в `Builds/MacSmoke/ThornThrone.app/Contents/Info.plist` (он назван по `productName` «Терновый трон», уточнено при реализации); нет файла → выход 2.
3. Плеер запускается напрямую (не через `open`), чтобы получить код выхода: `…/MacOS/ThornThrone -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -thornSmoke -logFile "$log" "$@"` в фоне; ожидание с тайм-аутом `THORN_SMOKE_TIMEOUT` (по умолчанию 180 с), по истечении — `kill` и выход 3.
4. Разбор лога `"$log"` (по умолчанию `Research/mac-smoke.log`, переменная `THORN_SMOKE_LOG`):
   - нет строки `THORN_MOBILE_SMOKE` → выход 4;
   - `kills=0` → выход 5;
   - необработанное исключение → выход 6. Признак: строка, начинающаяся с имени типа `…Exception:` (формат `Debug.LogException`/необработанного исключения в логе плеера Unity), или `Crash!!!`. Строки `THORN_*` не учитываются;
   - код выхода плеера ≠ 0 при прочих успешных проверках → выход 7.
5. Успех: печать строки `THORN_MOBILE_SMOKE …` и выход 0.
6. `Tools/mac-smoke.sh --check-log FILE` выполняет только шаг 4 над готовым логом (для тестов скрипта и разбора чужого лога).

**Р6.** Различимые коды выхода (2 сборка, 3 тайм-аут, 4 нет строки, 5 kills=0, 6 исключение, 7 код плеера) вместо одного «1». Причина: проверяющий агент и владелец сразу видят, что сломалось. Документируются в `Tools/README.md`.

**Р7.** Плеер запускается в окне 1280×720 (не `-batchmode`): smoke проверяет и рендер; `-batchmode` у плеера отключает графику. Требует активного сеанса пользователя на Mac (как и нынешний `mac-screenshot.sh`).

**Р8.** `diagnostics=N` в строке smoke не считается ошибкой: диагностики сейчас ожидаемы (неподдержанные атрибуты/способности, product.md «Текущее состояние»). Порог можно ввести позже отдельной задачей.

Сборка идёт в исходном `UnityProject` (как и сейчас для macOS): путь репозитория `/Users/adikan/Documents/Arena` без кириллицы, а macOS-сборка не использует Gradle/NDK.

## 5. `Tools/unity-tests.sh`
```
"$unity" -batchmode -nographics -projectPath UnityProject -runTests -testPlatform EditMode \
  -testResults "$root/Research/unity-tests.xml" -logFile "$root/Research/unity-tests.log"
```
Без `-quit` (с `-runTests` Unity выходит сам). Старый XML удаляется перед запуском. Разбор корневого `<test-run>`: атрибуты `total`, `passed`, `failed`, `result`. Нет файла → 2; `total=0` → 3; `failed>0` или код Unity ≠ 0 → 1 с именами упавших тестов (`test-case[@result="Failed"]/@fullname`). Успех → `Unity EditMode: N passed`, выход 0. Разбор XML — встроенным `python3 -c` (Python уже обязателен для конвейера), без новых зависимостей.

**Р9.** Артефакты запусков (`Research/unity-tests.xml`, `Research/unity-tests.log`, `Research/mac-smoke.log`) добавляются в `.gitignore` и не коммитятся — это результат последнего прогона на конкретной машине, а не исходники. Уже отслеживаемые `Research/mac-build.log`, `android-build.log`, `android-build-report.json` не трогаю (вывод их из git — отдельное решение владельца).

## 6. Исправление документов
Числа берутся из запусков в этой задаче (`dotnet test`, `unity-tests.sh`, `git rev-list --count HEAD`).
- **ТЗ §2:** дата → дата задачи; строка «Симуляция»: «…Dragonus, Arcane Bolt и Magic Missile. N тестов в `DotNet/ThornThrone.Simulation.Tests`»; строка «Мобильный слой»: + asmdef `ThornThrone.Mobile`, M EditMode-тестов; строка «Git»: «История на ветке `main`, коммиты с 10.11.2026; push делает владелец»; новая строка «Сборка и проверки»: корневой `README.md`, `Tools/mac-smoke.sh`, `Tools/unity-tests.sh`.
- **ТЗ §12:** убрать «исходное состояние лежит в индексе… коммит ждёт имени и почты» → «Контрольная точка — первый коммит истории `main`»; README сборки — ссылка на `README.md`.
- **ТЗ §13:** заголовок столбца — новая дата; этап 0: «APK собирается, инструменты и README записаны, история в git. Нет установки на телефон и `Devices.md`»; этап 1: «Готов без телефона: реестры, `AssetRegistry.json`, инструмент клиента; у 27 обязательных сущностей нет ресурсов в данных»; этап 4: «Начат: 2 способности из 17»; этап 5: «Комната с поглотителем реализована в симуляции, по наблюдению не подтверждена; порядок обычных атак Империи не подтверждён».
- **Differences.md:** строка «Тесты: …, 22 проверки» → «Тесты: `dotnet test …` (N на 2026-11-10), Unity EditMode — `Tools/unity-tests.sh`». В датированной записи «Проверка изменения: 41 тест…» история сохраняется, добавляется одно предложение: «Сейчас проверки навигации запускаются на сетке из `NestOfThorns` (N тестов на 2026-11-10)».

## 7. Риски
- Перенос Mobile в asmdef может выявить скрытую зависимость от `Assembly-CSharp` (например, `internal`-доступ из Editor). Проверено: `internal` в Mobile нет. Проверка — `BuildMacSmoke` и `unity-tests.sh` в этой задаче.
- Batchmode-запуск невозможен, пока Unity Editor открыт на проекте (lock `Temp/UnityLockfile`). Скрипты выводят понятную ошибку при наличии lock-файла с живым процессом Unity.
- Если пакет test-framework изменит lock сильнее, чем `depth`, — коммитить lock как есть после проверки, что версии остальных пакетов не изменились.

## 8. Тестовая стратегия
1. Сначала тесты `MobileLayerTests` и asmdef; `unity-tests.sh` до переноса Mobile в asmdef падает (тестовая сборка не находит `ThornThrone.Mobile`) — TDD-красный.
2. Перенос в asmdef → `unity-tests.sh` зелёный.
3. `mac-smoke.sh`: положительная ветка — реальный прогон (код 0). Отрицательные ветки (нет строки, kills=0, исключение) реальным плеером не воспроизвести, поэтому они проверяются режимом `--check-log FILE` на небольших логах-образцах в `Tools/tests/smoke-logs/` тестом `Tools/tests/test_mac_smoke.py` (ожидаемые коды 0/4/5/6).
4. `dotnet test` полностью зелёный; `python3 -m unittest discover -s Tools/tests` зелёный.
