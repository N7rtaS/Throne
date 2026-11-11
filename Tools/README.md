# Инструменты (macOS)

Нужно: Python 3, Node.js, Source2Viewer CLI 20.1 для macOS в `Tools/Source2Viewer/cli-macos/` и установленная Dota 2. Путь к Dota по умолчанию — `~/Library/Application Support/Steam/steamapps/common/dota 2 beta/game/dota`, другой задаётся через `--dota` или `THORN_DOTA_DIR`.

Порядок:

```sh
python3 Tools/extract-resources.py   # ресурсы из Dota → NestOfThorns/, манифест с CRC32
Tools/decode-data.sh                 # VData и звуковые события → Research/Decoded, Research/Json
python3 Tools/extract-resources.py   # повторно: добираются звуковые файлы из декодированных событий
python3 Tools/audit.py               # реестры → Research/FullGame/
Tools/import-unity-data.sh           # JSON → UnityProject/Assets/Game/Data/Source, difficulty_ru.json → Data/Localization
```

`audit.py` пишет `ContentRegistry.json`, `SoundRegistry.json` и `SourceVersions.json`. В `ContentRegistry.json` у каждой записи есть поле `implementation` (`none`, `partial`, `implemented`, `verified`). Уровень считается по исходникам симуляции (`UnityProject/Assets/Game/Scripts/Simulation`): `AbilityFactory`, `Run.SupportedHeroAttributes`, `Run.ReportUnsupportedEnemy`, `Run.Drop`, `Run.SpawnPosition`, `Run.ReportUnsupportedRules`. `verified` означает, что у записи уровня `implemented` ключ встречается в тестах `DotNet/**` (список в поле `tests`). Правила описаны в `.kiro/specs/audit-simulation-coverage/design.md` §3. Поле `prototype` хранит прежнее покрытие прототипа и больше не пересчитывается. Другой каталог вывода задаётся через `--out DIR`.

Коды выхода `audit.py`: 0 — успех; 1 — у обязательной записи нет роли (файлы не пишутся); 2 — в исходниках симуляции не найдена ожидаемая конструкция (в сообщении указано, какая). Тесты аудита:

```sh
python3 -m unittest discover -s Tools/tests
```

Реестр ресурсов (после `convert-models.py` и запекания в редакторе):
```sh
python3 Tools/asset-registry.py      # → Research/FullGame/AssetRegistry.json, таблица моделей в stdout
```
Читает только файлы проекта: `NestOfThorns/` с `manifest.tsv` и картой `survivors_arena.vpk`, `ContentRegistry.json`, `SoundRegistry.json`, `SourceVersions.json`, `Research/extract-report.json`, `Research/Json/survivors_*.json`, `UnityProject/Assets/Game/Art/Converted/**` с `.meta` и `Tools/asset-registry-overrides.json`. Dota и Steam не открываются, внешние программы не запускаются. На каждую строку манифеста и каждый файл карты пишется запись: CRC32, размер, ссылки RERL (`references`), сущности, из которых ресурс достижим (`usedBy`), выходы Unity (путь, GUID, импортёр, инструмент, стадия, сжатие текстур по платформам, справочные треугольники glb), статус (`converted`, `baked`, `recreated`, `none`, `unused`) и `verification`. Раздел `models` — справочный вид героев и врагов в треугольниках, бюджеты не проверяются. `summary.missingRequired` — обязательные сущности без единой записи, `unresolved` — ссылки на ресурсы вне манифеста, `orphanOutputs` — выходы без источника. Статус `recreated`, причину `unused` и ссылку на проверку (`verified`) задаёт `asset-registry-overrides.json`: `{"<источник>": {"status": "recreated", "unity": "Assets/…", "verified": "Research/…"}}`. Правила — `.kiro/specs/asset-registry/design.md`. `--out DIR` — другой каталог вывода, `--root DIR` — другой корень проекта. Повторный запуск на тех же входах не меняет файл.
Коды выхода `asset-registry.py`: 0 — успех; 1 — нарушение входов (нет файла из манифеста, CRC32 не совпадает, у выхода нет `.meta` или GUID, неверное переопределение); файл при этом не пишется.

Исследование клиента (только чтение):
```sh
python3 Tools/research-client.py --check-readonly   # строки клиента, Panorama, описания powerups
```
Пишет `Research/Decoded/client/strings.json` (строки `bin/osx64/libclient.dylib` и `libserver.dylib`: всё с `survivors`, классы, перечисления, поля данных с признаками «есть в клиенте» и «читает симуляция», кандидаты полей клиента), `Research/Decoded/panorama/` (разметки и стили режима, декодированные Source2Viewer, отобранные ключи `dota_{english,russian}.txt`, `menu_texts.md`), `Research/FullGame/Descriptions.md` (русские и английские описания 47 powerups) и `Research/research-client-report.json` (входы с SHA-256, версия клиента и Source2Viewer). Повторный запуск на тех же входах не меняет файлы. `--out DIR` задаёт другой корень для выходов.
Гарантия только чтения: весь каталог Steam (`~/Library/Application Support/Steam` и библиотека Steam, в которой лежит Dota) — запретная зона. Файлы там открываются только на чтение (`"rb"`), выход в зону и запуск внешних программ с путями зоны запрещены. Source2Viewer получает только копии из `NestOfThorns/`, CRC32 которых сверен с `pak01_dir.vpk`. Если проект сам оказался внутри зоны (например, из-за нестандартного `--dota`), инструмент не работает. `--check-readonly` сравнивает размер и время изменения всех файлов установки Dota до и после запуска; запускать при закрытой Dota.
Коды выхода `research-client.py`: 0 — успех; 1 — нет входа (Dota, Source2Viewer или копия в `NestOfThorns` не совпадает с VPK — запустите `extract-resources.py`); 2 — нарушение данных (не 47 powerups или ключа `m_sLoc*` нет в локализации); 3 — выход или проект в запретной зоне; 4 — файлы установки Dota изменились во время запуска.
Сборка для Android:

```sh
Tools/build-android.sh               # APK → Builds/Android/ThornThrone.apk, отчёт → Research/android-build-report.json
```

Инструменты Android (Gradle, NDK) не работают с путём, в котором есть кириллица («Терновый Трон»). Поэтому скрипт копирует `UnityProject` в `~/Library/Caches/ThornThroneAndroid`, собирает там и возвращает APK. Первая сборка занимает около 5 минут, следующие быстрее.

Проверки Unity-слоя (batchmode; Unity Editor с этим проектом должен быть закрыт, иначе код 2):
```sh
Tools/unity-tests.sh                 # EditMode-тесты → Research/unity-tests.xml (NUnit XML), лог Research/unity-tests.log
Tools/mac-smoke.sh                   # сборка Builds/MacSmoke + прогон -thornSmoke → Research/mac-smoke.log
Tools/mac-smoke.sh --no-build -thornElite   # без сборки, с дополнительными аргументами плеера
Tools/mac-smoke.sh --check-log FILE  # только разбор готового лога
Tools/mac-smoke.sh --no-build --settings-restart  # три запуска: настройки меняются, переживают перезапуск, сбрасываются
Tools/mac-smoke.sh --hud-alloc        # сборка без Development Build (Builds/MacRelease) + замер выделений HUD
```
`unity-tests.sh` запускает тесты сборки `ThornThrone.Mobile.Tests` (`UnityProject/Assets/Game/Tests/EditMode`) и печатает `Unity EditMode: N passed`. Коды выхода: 0 — все прошли; 1 — есть упавший тест (имена печатаются) или Unity завершился с ненулевым кодом; 2 — файл результата не создан (например, ошибки компиляции — первые строки `error CS` печатаются); 3 — не выполнено ни одного теста.
`mac-smoke.sh` собирает `MobileBuilder.BuildMacSmoke` (лог `Research/mac-build.log`), запускает плеер напрямую в окне 1280×720 с `-thornSmoke` и ждёт выхода. Нужен активный сеанс пользователя на Mac. Успех — печать строки `THORN_MOBILE_SMOKE …` и код 0. Коды выхода: 2 — сборка не удалась (нет `THORN_MAC_BUILD_OK`) или плеер не найден; 3 — плеер не завершился за `THORN_SMOKE_TIMEOUT` секунд (по умолчанию 180); 4 — в логе нет строки `THORN_MOBILE_SMOKE`; 5 — `kills=0`; 6 — в логе необработанное исключение (строка, начинающаяся с `…Exception:`, или `Crash!!!`); 7 — проверки лога прошли, но код выхода плеера ненулевой; 8 — передан `-thornInputScript=ФАЙЛ`, а в логе нет строки `THORN_INPUT_SCRIPT ok=1` (сценарий ввода `Tools/smoke/input-two-fingers.txt`). 9 — передан `-thornSettings=…` без строки `THORN_SETTINGS_SET` или `-thornExpectSettings=…` без строки `THORN_SETTINGS ok=1`. `--settings-restart` запускает плеер трижды на тестовом профиле: с `-thornSettings` (логи `mac-smoke-settings-1.log`), с `-thornExpectSettings` (`-2.log`; сверяются профиль и применённые `targetFrameRate`, `TouchController.Settings`, `AudioListener.volume`, масштаб Canvas) и сброс к значениям по умолчанию (`mac-smoke.log`). `diagnostics=N` ошибкой не считается. Лог плеера — `THORN_SMOKE_LOG` (по умолчанию `Research/mac-smoke.log`). 10 — передан `-thornFlow` или `-thornExpectFlow`, а в логе нет строки `THORN_FLOW ok=1`. `--flow` запускает плеер дважды: `-thornFlow` (новый тестовый профиль, проход меню → герой → сложность → забег → победа `Run.DebugWin` → итог → «Сыграть ещё» → «Завершить забег» → меню → сложность, лог `mac-smoke-flow-1.log`) и `-thornExpectFlow` (проверка сохранённых результата и открытого эшелона 2, `mac-smoke.log`); после строки `THORN_FLOW` идёт обычный 30-секундный забег. `-thornFlowShot=hero|difficulty|results` вместе с `-thornScreenshot` снимает экран героя, сложности или итога (`Tools/mac-screenshot.sh out.png -thornFlow -thornFlowShot=difficulty`). Обычный smoke тоже проходит экраны кнопками и играет на эшелоне 1. `Tools/mac-smoke.sh --check-flow-log FILE` — только разбор лога. 11 — `--hud-alloc`: в логе нет строки `THORN_HUD_ALLOC`, `bytes` или `canvas` не 0 либо замер ничего не увидел (`frames`, `changes`, `calibration` равны 0, `method=none`). `--hud-alloc` собирает `MobileBuilder.BuildMacSmoke` с `-thornRelease` (без Development Build, `Builds/MacRelease`) и запускает плеер с `-thornHudAlloc` (лог `mac-smoke-hud-alloc.log`): с 5 до 25 с забега меряются байты в `GameUi.UpdateHud` и в `Canvas.ForceUpdateCanvases` после него, затем калибровка тем же числом окон с `new byte[16]`. В release-плеере Mono байты считаются по куче Mono (блоки 4 КБ, `method=mono_heap`); `-thornHudAlloc` как аргумент обычного прогона проверяет Development-плеер по точному счётчику `GC Allocated In Frame` (`method=profiler_counter`). `Tools/mac-smoke.sh --check-hud-alloc-log FILE` — только разбор лога. Проект: `.kiro/specs/hud-alloc-flaky-fix/design.md` §4. Правила — `Research/FullGame/Mechanics.md` §23. Тесты разбора лога — `Tools/tests/test_mac_smoke.py` на образцах `Tools/tests/smoke-logs/`.
Запекание шрифта интерфейса (batchmode, после изменения текстов UI или `powerups_ru.json`): `-executeMethod ThornThrone.Editor.FontBaker.Bake`, успех — `THORN_FONT_BAKE_OK` в логе, ошибка (нет глифа в PT Sans, нет TMP-ресурсов) — ненулевой код Unity. Подробности — корневой `README.md`, раздел «Шрифты».
Скриншот готовой macOS-сборки: `Tools/mac-screenshot.sh out.png [аргументы плеера]`.
Повторный запуск безопасен: уже извлечённые файлы с верной контрольной суммой не перезаписываются.

Windows-скрипты `*.ps1` из прототипа больше не используются и перенесены в `Удалить/Tools/`. `kv3-to-json.mjs` остаётся: его вызывает `decode-data.sh`.
