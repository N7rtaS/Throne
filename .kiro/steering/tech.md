# Технологии и команды

## Стек
- Unity 6000.6.7f1, URP 17.6.0 (пайплайны `Assets/Settings/Mobile_RPAsset` и `PC_RPAsset`, назначены по уровням качества).
- Пакеты: Input System 1.20.0 (единственный путь ввода, старый Input Manager выключен), uGUI 2.6.0, Newtonsoft JSON 3.2.2, glTFast 6.19.0. Версии зафиксированы `Packages/packages-lock.json`. TextMeshPro входит в uGUI 2.6.0 (сборка `Unity.TextMeshPro`); Essential Resources лежат в `Assets/TextMesh Pro`, шрифт PT Sans SDF запекается `ThornThrone.Editor.FontBaker.Bake` (`THORN_FONT_BAKE_OK`).
- Android: IL2CPP, ARM64, min API 26, target API Auto, Vulkan + OpenGL ES 3, только landscape, `com.thornthrone.mobile`. Настройки задаются кодом в `MobileBuilder.ConfigureAndroid()` — менять там, а не только в Player Settings.
- Симуляция: чистый C# (netstandard2.1, C# 9), без ссылок на UnityEngine.
- Тесты: .NET 8, NUnit 3.14.
- Машина разработки: macOS (M2 Pro), zsh. Windows и PowerShell не используются.

## Ограничения кода симуляции
- Только C# 9 и API .NET Standard 2.1: проект компилируется и Unity, и `dotnet`. Не использовать возможности C# 10+ (file-scoped namespace, `record struct`, global using и т.п.).
- `TreatWarningsAsErrors=true` — предупреждения ломают сборку тестов.
- Nullable отключён.
- Никаких `UnityEngine`, MonoBehaviour, Android API в `Scripts/Simulation` (asmdef с `noEngineReferences: true`).
- Фиксированный шаг 1/30 с (`Rules.Step`), seed-детерминизм в одной конфигурации, все случайности через `Rng` симуляции.
- Горячий цикл: без выделений управляемой памяти после прогрева, без LINQ, без разбора JSON. Пулы и `SpatialGrid` вместо перебора всей орды.
- Пауза и выбор улучшения останавливают все игровые таймеры.

## Команды
```sh
# Тесты симуляции (из корня репозитория; решения .sln нет)
dotnet test DotNet/ThornThrone.Simulation.Tests

# Android APK → Builds/Android/ThornThrone.apk, отчёт → Research/android-build-report.json
Tools/build-android.sh

# Скриншот macOS smoke-сборки
Tools/mac-screenshot.sh out.png

# Unity EditMode-тесты → Research/unity-tests.xml
Tools/unity-tests.sh

# macOS smoke: сборка, прогон -thornSmoke, разбор лога (код 0 — успех; --no-build, аргументы плеера)
Tools/mac-smoke.sh
```
- `build-android.sh` копирует `UnityProject` в `~/Library/Caches/ThornThroneAndroid` (Gradle/NDK не работают с кириллицей в пути). Переменные: `THORN_UNITY`, `THORN_ANDROID_STAGE`.
- Batchmode-методы: `ThornThrone.Editor.MobileBuilder.BuildAndroid`, `ThornThrone.Editor.MobileBuilder.BuildMacSmoke`. Успех в логе: `THORN_ANDROID_BUILD_OK`, `THORN_MAC_BUILD_OK`.
- Сцена `Mobile.unity` генерируется `MobileBuilder.CreateScene` при каждой сборке — не править её вручную, менять генератор.
- Агент сам запускает для проверки задач: `dotnet test`, `Tools/unity-tests.sh`, `Tools/mac-smoke.sh` (сборка `MobileBuilder.BuildMacSmoke` и прогон с `-thornSmoke`), `Tools/build-android.sh`, `Tools/mac-screenshot.sh`. Долгие команды запускать с достаточным таймаутом.
- Smoke успешен, если в логе есть `THORN_MOBILE_SMOKE` с kills > 0 и нет необработанных исключений.
- Открывать Unity Editor с интерфейсом, менять настройки Unity Hub и ставить модули — только с согласия пользователя.

## Конвейер ресурсов (`Tools/README.md`)
Требуется Python 3, Node.js, Source2Viewer CLI 20.1 (`Tools/Source2Viewer/cli-macos/`, `THORN_S2V_CLI`), установленная Dota 2 (`THORN_DOTA_DIR`).
1. `python3 Tools/extract-resources.py` → `NestOfThorns/` + `manifest.tsv` с CRC32
2. `Tools/decode-data.sh` → `Research/Decoded`, `Research/Json`
3. `python3 Tools/extract-resources.py` повторно (звуки)
4. `python3 Tools/audit.py` → `Research/FullGame/`
5. `Tools/import-unity-data.sh` → `UnityProject/Assets/Game/Data/Source`
6. `python3 Tools/convert-models.py [--heroes ...] [--enemies ...]` → `Assets/Game/Art/Converted`
7. Запекание в редакторе: `ArenaBaker`, `EnemyBaker`, `FontBaker` (шрифт UI)

Каждый шаг завершается ненулевым кодом при ошибке; повторный запуск безопасен; GUID существующих ассетов не должны меняться.

## Git
- Git LFS только для `*.glb` и `*.vpk`. Не добавлять в LFS другие типы без необходимости.
- Не коммитить: `Builds/`, `Удалить/`, `*.apk`, `*.aab`, `*.keystore`, видео, `Tools/Source2Viewer/`, `Library/` и прочие кэши Unity, `bin/`/`obj/`.
- Ключ подписи хранится вне репозитория.
- Сообщения коммитов — на русском (решение владельца 10.11.2026; более ранние коммиты на английском не переписывать). Заголовок — короткое предложение без префиксов, что сделано: `Добавить способность Magic Missile с оглушением и отскоками`. В теле — описание поведения, ссылка на раздел Mechanics/ТЗ и число тестов («6 новых тестов, проходят 54»). Имена классов, методов и файлов оставлять как в коде.
