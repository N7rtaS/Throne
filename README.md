# Терновый трон (ThornThrone)

Автономная одиночная Android-игра по мини-режиму Dota 2 «Nest of Thorns» (survivors-like, альбомная ориентация, без сети и аккаунта). Основное ТЗ — `ТЗ_Мобильная_версия_v2.md`; контракт механик, отличия от оригинала и реестры — `Research/FullGame/` (`Mechanics.md`, `Differences.md`, `*Registry.json`). Подробный справочник инструментов (форматы, коды выхода) — `Tools/README.md`; здесь только обзор.

## Требования и версии
Машина разработки — macOS (Apple Silicon), zsh.

| Компонент | Версия | Откуда проверить |
|---|---|---|
| Unity | 6000.6.7f1 (ревизия b22767f04f04) с модулем Android Build Support | `UnityProject/ProjectSettings/ProjectVersion.txt` |
| URP | 17.6.0 | `UnityProject/Packages/packages-lock.json` |
| Input System | 1.20.0 | lock |
| uGUI | 2.6.0 | lock |
| Newtonsoft JSON | 3.2.2 | lock |
| glTFast | 6.19.0 | lock |
| Test Framework | 1.8.0 | lock |
| Прочие пакеты | ide.rider 3.0.40, ide.visualstudio 2.0.28, collab-proxy 2.13.6, vectorgraphics 1.0.0 | lock |
| Android SDK | build-tools 36.0.0; platforms android-34, android-36, android-37.0; platform-tools 37.0.1; cmdline-tools 16.0 | `PlaybackEngines/AndroidPlayer/SDK/**/source.properties` модуля Unity |
| Android NDK | r27c (27.2.12479018) | `PlaybackEngines/AndroidPlayer/NDK/source.properties` |
| OpenJDK | 17.0.18 (Temurin) | `PlaybackEngines/AndroidPlayer/OpenJDK/release` |
| Gradle | 9.3.1 | `PlaybackEngines/AndroidPlayer/Tools/gradle/lib` |
| .NET SDK | 8 (проверено на 8.0.301) | `dotnet --version` |
| Python | 3 (проверено на 3.13.3) | `python3 --version` |
| Node.js | с поддержкой ES-модулей для `Tools/kv3-to-json.mjs` (проверено на 18.16.0) | `node --version` |
| Source2Viewer CLI | 20.1 (20.1.7467) для macOS в `Tools/Source2Viewer/cli-macos/` | нужен только для конвейера ресурсов |
| Git LFS | любая | `git lfs version` |

Модуль Android Unity ставится вместе со своими SDK, NDK, JDK и Gradle; отдельная установка не нужна. Минимальные версии Python и Node.js в проекте не зафиксированы.

## Первый запуск после checkout
1. `git lfs pull` — модели `*.glb` и карта `*.vpk`.
2. Данные режима (`Research/Json`, `UnityProject/Assets/Game/Data/Source`) и ресурсы (`NestOfThorns/`) уже лежат в репозитории. Dota 2 и Source2Viewer нужны только для повторного извлечения.
3. `dotnet test DotNet/ThornThrone.Simulation.Tests` и `Tools/mac-smoke.sh` — проверка, что всё собирается и играется.

## Конвейер ресурсов
Подробности и коды выхода каждого шага — `Tools/README.md`. Каждый шаг завершается ненулевым кодом при ошибке, повторный запуск безопасен.

| Шаг | Команда | Результат |
|---|---|---|
| 1 | `python3 Tools/extract-resources.py` | `NestOfThorns/` + `manifest.tsv` с CRC32 |
| 2 | `Tools/decode-data.sh` | `Research/Decoded`, `Research/Json` |
| 3 | `python3 Tools/extract-resources.py` (повторно) | звуковые файлы |
| 4 | `python3 Tools/audit.py` | `Research/FullGame/` (реестры) |
| 5 | `Tools/import-unity-data.sh` | `UnityProject/Assets/Game/Data/Source` |
| 6 | `python3 Tools/convert-models.py [--heroes …] [--enemies …]` | `UnityProject/Assets/Game/Art/Converted` |
| 7 | Запекание в редакторе: `ArenaBaker`, `EnemyBaker` | запечённые арена и враги |
| 8 | Запекание шрифта: `ThornThrone.Editor.FontBaker.Bake` (меню `Thorn Throne/Fonts/Bake UI font` или batchmode, см. «Шрифты») | `Assets/Game/Art/Fonts/PTSans-Regular SDF.asset`, `Assets/TextMesh Pro/` |

## Проверки и сборка
Все команды — из корня репозитория.

| Команда | Выход | Признак успеха |
|---|---|---|
| `dotnet test DotNet/ThornThrone.Simulation.Tests` | — | все тесты пройдены |
| `python3 -m unittest discover -s Tools/tests` | — | `OK` |
| `Tools/unity-tests.sh` | `Research/unity-tests.xml`, `Research/unity-tests.log` | `Unity EditMode: N passed`, код 0 |
| `Tools/mac-smoke.sh [--no-build] [аргументы плеера]` | `Builds/MacSmoke/ThornThrone.app`, `Research/mac-build.log`, `Research/mac-smoke.log` | строка `THORN_MOBILE_SMOKE …` с `kills > 0`, код 0 |
| `Tools/mac-screenshot.sh out.png [аргументы плеера]` | `out.png` | путь к PNG в выводе |
| `Tools/build-android.sh [-thornDevelopment]` | `Builds/Android/ThornThrone.apk`, `Research/android-build-report.json`, `Research/android-build.log` | `THORN_ANDROID_BUILD_OK` в логе, код 0 |

Коды выхода `mac-smoke.sh` и `unity-tests.sh` — в `Tools/README.md`. Batchmode-методы: `ThornThrone.Editor.MobileBuilder.BuildMacSmoke`, `ThornThrone.Editor.MobileBuilder.BuildAndroid`. Логи и отчёты последнего прогона в `Research/` (`unity-tests.*`, `mac-build.log`, `mac-smoke.log`, `android-build.log`, `android-build-report.json`) не хранятся в git. Сцена `Mobile.unity` генерируется при каждой сборке детерминированно и хранится в git: при неизменном `MobileBuilder` сборка её не меняет. Аргументы плеера smoke: `-thornHero=kez` или `-thornHero=shen` (забег за Кеза или Шена вместо Драгонуса), `-thornBoss`, `-thornElite`, `-thornShowcase`, `-thornCloseup`, `-thornInputScript=ФАЙЛ`, `-thornSettings=ключ=значение,…`, `-thornExpectSettings=…`, `-thornShowSettings` (экран настроек для скриншота), `-thornPauseShot` (экран паузы для скриншота: пауза на 2 с, снимок через 1 с, забег продолжается), `-thornFlow`/`-thornExpectFlow` (порядок экранов, открытие эшелонов), `-thornFlowShot=hero|difficulty|results`, `-thornHudAlloc` (замер выделений HUD, строка `THORN_HUD_ALLOC`). Замер выделений HUD в плеере без Development Build: `Tools/mac-smoke.sh --hud-alloc`. Проверка порядка экранов: `Tools/mac-smoke.sh --no-build --flow`. Проверка сохранения настроек после перезапуска: `Tools/mac-smoke.sh --no-build --settings-restart`. Smoke использует профиль `test`, обычный запуск — `user` (`persistentDataPath/Saves`).

Android-сборка по умолчанию release (без Development Build): только такая годится для приёмки (IL2CPP, ARM64, подписанная). `Tools/build-android.sh -thornDevelopment` собирает development-APK для профайлера на телефоне. Подпись: если заданы все четыре переменные `THORN_KEYSTORE`, `THORN_KEYSTORE_PASS`, `THORN_KEY_ALIAS`, `THORN_KEY_PASS`, APK подписывается этим ключом; без них — отладочным ключом Unity (`CN=Android Debug`), и в логе и выводе скрипта появляется строка `THORN_ANDROID_DEBUG_SIGNED`: такой APK для приёмки не годится. Часть переменных без остальных — ошибка сборки. Путь и пароли в `ProjectSettings.asset` не остаются. Опции сборки и подпись видны в строке `THORN_ANDROID_BUILD_OPTIONS options=… development=0|1 signing=custom|debug` и в `Research/android-build-report.json` (`options`, `development`, `signing`, `scriptingBackend`, `architectures`, `minSdk`, `applicationId`). Ключ хранится вне репозитория, `*.keystore` не коммитится.

`dotnet test DotNet/ThornThrone.Simulation.Tests` собирает и сборку симуляции, и сборку профиля `DotNet/ThornThrone.Progression` (исходники `UnityProject/Assets/Game/Scripts/Progression`).
Сохранения профиля лежат в `Application.persistentDataPath/Saves`: `user.save`, резервная копия `user.save.bak`, повреждённые файлы `*.corruptN` (не удаляются автоматически), тестовый профиль `test.save*`. На macOS это `~/Library/Application Support/<компания>/<продукт>/Saves`, на Android — `/storage/emulated/0/Android/data/com.thornthrone.mobile/files/Saves`. Формат и правила восстановления — `Research/FullGame/Mechanics.md` §19.
Сценарий сенсорного ввода (движение одним пальцем, рывок вторым, переход пальца на кнопку рывка, отмена касания) проигрывается через виртуальный `Touchscreen`:
```sh
Tools/mac-smoke.sh --no-build -thornInputScript=$PWD/Tools/smoke/input-two-fingers.txt
```
Успех — строка `THORN_INPUT_SCRIPT ok=1 …` перед `THORN_MOBILE_SMOKE`; без неё или при `ok=0` — код 8. Формат файла описан в его заголовке.

## Шрифты
- Интерфейс использует TextMeshPro (входит в uGUI 2.6.0) и шрифт **PT Sans Regular** © 2010 ParaType Ltd., лицензия SIL Open Font License 1.1. Файлы: `UnityProject/Assets/Game/Art/Fonts/PTSans-Regular.ttf` и текст лицензии `UnityProject/Assets/Game/Art/Fonts/OFL.txt`. Источник: репозиторий Google Fonts, `https://raw.githubusercontent.com/google/fonts/main/ofl/ptsans/PT_Sans-Web-Regular.ttf` и `.../ofl/ptsans/OFL.txt`.
- `FontBaker.Bake` запекает статический SDF-атлас `PTSans-Regular SDF.asset` (1024×1024 Alpha8, 48 pt, отступ 6, шейдер `TextMeshPro/Mobile/Distance Field`). Набор символов: печатный ASCII, русский алфавит с Ё/ё, `«»—–…№°×`, все символы `Data/Localization/powerups_ru.json` и строковых литералов `Scripts/Mobile/*.cs`. Если какого-то символа нет в шрифте, запекание падает со списком символов. Повторное запекание перезаписывает ассет на месте, GUID не меняется. Запускать после изменения текстов UI или локализации:
  ```sh
  "$THORN_UNITY" -batchmode -nographics -quit -projectPath UnityProject -executeMethod ThornThrone.Editor.FontBaker.Bake -logFile -   # успех: THORN_FONT_BAKE_OK
  ```
- При первом запуске `FontBaker` распаковывает TMP Essential Resources из пакета uGUI в `UnityProject/Assets/TextMesh Pro/` (шейдеры, `TMP Settings`, запасной `LiberationSans SDF`, OFL) и делает PT Sans SDF шрифтом по умолчанию в `TMP Settings`. `MobileBuilder.CreateScene` без ассета шрифта падает с подсказкой запустить `FontBaker.Bake`.

## Переменные окружения
| Переменная | Умолчание | Где используется |
|---|---|---|
| `THORN_UNITY` | `/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity` | `build-android.sh`, `mac-smoke.sh`, `unity-tests.sh` |
| `THORN_ANDROID_STAGE` | `~/Library/Caches/ThornThroneAndroid` | `build-android.sh` |
| `THORN_KEYSTORE`, `THORN_KEYSTORE_PASS`, `THORN_KEY_ALIAS`, `THORN_KEY_PASS` | не заданы (отладочная подпись Unity) | `build-android.sh` (`MobileBuilder.BuildAndroid`): путь к keystore вне репозитория, пароль хранилища, имя и пароль ключа |
| `THORN_DOTA_DIR` | `~/Library/Application Support/Steam/steamapps/common/dota 2 beta/game/dota` | инструменты извлечения и исследования |
| `THORN_S2V_CLI` | `Tools/Source2Viewer/cli-macos/Source2Viewer-CLI` | `decode-data.sh` и др. |
| `THORN_SMOKE_TIMEOUT` | `180` (секунд) | `mac-smoke.sh` |
| `THORN_SMOKE_LOG` | `Research/mac-smoke.log` | `mac-smoke.sh` |

## Ограничения
- Каталоги Steam и Dota 2 — только чтение. Инструменты пишут результаты только в `Research/` и `NestOfThorns/`.
- Batchmode-скрипты не работают, пока на `UnityProject` открыт Unity Editor (скрипты сообщают об этом и выходят с кодом 2).
- `mac-smoke.sh` и `mac-screenshot.sh` открывают окно плеера: нужен активный сеанс пользователя на Mac.
- Первая Android-сборка идёт около 5 минут: проект копируется в каталог без кириллицы, Gradle и NDK греют кэши.
- Git LFS — только `*.glb` и `*.vpk`.
- Не коммитятся: `Builds/`, `Удалить/`, `*.apk`, `*.aab`, `*.keystore`, видео, `Tools/Source2Viewer/`, `UnityProject/Library/` и прочие кэши Unity, `bin/`/`obj/`, результаты последних прогонов (`Research/unity-tests.*`, `Research/mac-smoke.log`). Ключ подписи хранится вне репозитория.
