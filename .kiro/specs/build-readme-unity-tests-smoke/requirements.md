# Требования: README сборки, Unity-тесты, автоматический smoke

Roadmap: Task 8 (блок A, последняя). Основание: ТЗ_Мобильная_версия_v2.md §12 (README сборки, воспроизводимость), §14 (проверки), §2/§13 (состояние проекта).

## Глоссарий
- **Smoke** — запуск macOS-сборки мобильной сцены с `-thornSmoke`: 30 с игры Dragonus, в лог пишется `THORN_MOBILE_SMOKE time=… kills=… …`, процесс выходит с кодом `kills > 0 ? 0 : 1` (`MobileGame`).
- **EditMode-тесты** — тесты Unity Test Framework, выполняемые в редакторе без входа в Play Mode.

## Требование 1. Корневой README сборки
История: как владелец, я хочу один документ, по которому новый checkout собирается и проверяется без расспросов.

Критерии приёмки:
1. СИСТЕМА ДОЛЖНА содержать `README.md` в корне репозитория на русском.
2. README ДОЛЖЕН перечислять версии: Unity 6000.6.7f1 (ревизия b22767f04f04) с модулем Android; пакеты прямых зависимостей из `UnityProject/Packages/packages-lock.json`; Python 3, Node.js, .NET SDK 8, Source2Viewer CLI 20.1; Android SDK (build-tools, platforms), NDK r27c, OpenJDK 17, Gradle из модуля Android Unity. Для каждой версии — откуда она взята (lock, `ProjectVersion.txt`, `source.properties` модуля).
3. README ДОЛЖЕН описывать конвейер ресурсов шагами 1–7 из `Tools/README.md` и ссылаться на `Tools/README.md` за подробностями, не дублируя его.
4. README ДОЛЖЕН перечислять команды: тесты симуляции, тесты инструментов, Unity EditMode-тесты, macOS smoke, скриншот, Android APK; для каждой — выходные файлы и признак успеха.
5. README ДОЛЖЕН перечислять переменные окружения: `THORN_UNITY`, `THORN_ANDROID_STAGE`, `THORN_DOTA_DIR`, `THORN_S2V_CLI` и новые переменные скриптов этой задачи, с умолчаниями.
6. README ДОЛЖЕН указывать ограничения: Dota/Steam только чтение; что не коммитится; Git LFS только `*.glb`/`*.vpk`; batchmode-скрипты не работают при открытом на этом проекте Unity Editor.

## Требование 2. Сборки ThornThrone.Mobile и ThornThrone.Mobile.Tests
Критерии приёмки:
1. СИСТЕМА ДОЛЖНА содержать asmdef `ThornThrone.Mobile` в `Assets/Game/Scripts/Mobile` со ссылками только на нужные сборки: `ThornThrone.Simulation`, `Unity.InputSystem`, `Unity.RenderPipelines.Universal.Runtime`, `Unity.RenderPipelines.Core.Runtime`, `UnityEngine.UI` и `Newtonsoft.Json.dll`.
2. КОГДА скрипты Mobile переносятся в asmdef, СИСТЕМА ДОЛЖНА сохранить их GUID (`.meta` не меняются), а `Editor/` (`MobileBuilder`, `ArenaBaker`, `EnemyBaker`) ДОЛЖЕН компилироваться без изменений.
3. СИСТЕМА ДОЛЖНА содержать asmdef `ThornThrone.Mobile.Tests` (только Editor, `UNITY_INCLUDE_TESTS`, ссылки `ThornThrone.Mobile`, `ThornThrone.Simulation`, NUnit из `com.unity.test-framework`).
4. `com.unity.test-framework` ДОЛЖЕН быть явной зависимостью в `Packages/manifest.json` (сейчас 1.8.0 приходит транзитивно).
5. СИСТЕМА ДОЛЖНА содержать стартовый набор EditMode-тестов, проверяющих код Unity-слоя, который нельзя проверить `dotnet test`:
   - сборка `ThornThrone.Simulation` не ссылается на `UnityEngine*` (граница слоёв, structure.md «Слои»);
   - данные из `Assets/Game/Data/Source` загружаются в Unity (Newtonsoft под Mono) и `Run` для Dragonus создаётся без диагностик загрузки;
   - `EnemyVisual.Frame` для зацикленного и незацикленного клипа, включая отрицательное время;
   - `WorldRenderer.ToWorld`: Source x → Unity x, Source y → Unity z, масштаб 0.01.

## Требование 3. `Tools/mac-smoke.sh`
Критерии приёмки:
1. КОГДА запускается `Tools/mac-smoke.sh`, СИСТЕМА ДОЛЖНА собрать macOS-сборку через `MobileBuilder.BuildMacSmoke` в batchmode, затем запустить плеер с `-thornSmoke` и дождаться его завершения.
2. ЕСЛИ сборка завершилась ошибкой или в логе сборки нет `THORN_MAC_BUILD_OK`, СИСТЕМА ДОЛЖНА завершиться с ненулевым кодом, не запуская плеер.
3. ЕСЛИ в логе плеера нет строки `THORN_MOBILE_SMOKE`, или `kills` в ней равен 0, или в логе есть необработанное исключение, или плеер не завершился за отведённое время, или его код выхода ненулевой, СИСТЕМА ДОЛЖНА завершиться с ненулевым кодом и вывести причину.
4. КОГДА все проверки прошли, СИСТЕМА ДОЛЖНА вывести строку `THORN_MOBILE_SMOKE …` и завершиться с кодом 0.
5. Скрипт ДОЛЖЕН поддерживать пропуск сборки (`--no-build`) и передачу дополнительных аргументов плееру (`-thornBoss`, `-thornElite`).
6. Скрипт НЕ ДОЛЖЕН открывать Unity Editor с интерфейсом и ничего не писать вне репозитория, кроме временных файлов.

## Требование 4. `Tools/unity-tests.sh`
Критерии приёмки:
1. КОГДА запускается `Tools/unity-tests.sh`, СИСТЕМА ДОЛЖНА выполнить EditMode-тесты в batchmode и записать результат NUnit XML в `Research/unity-tests.xml`.
2. ЕСЛИ тест упал, тестов 0 или файл результата не создан, СИСТЕМА ДОЛЖНА завершиться с ненулевым кодом и вывести счётчики.
3. КОГДА все тесты прошли, СИСТЕМА ДОЛЖНА вывести число пройденных тестов и завершиться с кодом 0.

## Требование 5. Устаревшие числа в документах
Критерии приёмки:
1. ТЗ §2 ДОЛЖЕН отражать текущее состояние: дата, способности симуляции (Arcane Bolt, Magic Missile), число тестов симуляции по фактическому запуску, Git (коммиты есть, ветка `main`), README сборки и Unity-тесты.
2. ТЗ §12 и §13 НЕ ДОЛЖНЫ утверждать, что коммитов нет; состояние этапов 0, 1, 4, 5 ДОЛЖНО соответствовать roadmap (задачи 1–8 блока A) и `ContentRegistry.json` (комната поглотителя реализована в симуляции, по наблюдению не подтверждена).
3. `Differences.md` НЕ ДОЛЖЕН содержать устаревшее общее число тестов («22 проверки») и утверждение о незапущенных проверках навигации как текущее состояние; датированные записи сохраняют историю.
4. Числа ДОЛЖНЫ браться из фактического запуска команд, а не из памяти.
