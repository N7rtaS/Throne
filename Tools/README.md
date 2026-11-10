# Инструменты (macOS)

Нужно: Python 3, Node.js, Source2Viewer CLI 20.1 для macOS в `Tools/Source2Viewer/cli-macos/` и установленная Dota 2. Путь к Dota по умолчанию — `~/Library/Application Support/Steam/steamapps/common/dota 2 beta/game/dota`, другой задаётся через `--dota` или `THORN_DOTA_DIR`.

Порядок:

```sh
python3 Tools/extract-resources.py   # ресурсы из Dota → NestOfThorns/, манифест с CRC32
Tools/decode-data.sh                 # VData и звуковые события → Research/Decoded, Research/Json
python3 Tools/extract-resources.py   # повторно: добираются звуковые файлы из декодированных событий
python3 Tools/audit.py               # реестры → Research/FullGame/
Tools/import-unity-data.sh           # JSON → UnityProject/Assets/Game/Data/Source
```

`audit.py` пишет `ContentRegistry.json`, `SoundRegistry.json` и `SourceVersions.json`. В `ContentRegistry.json` у каждой записи есть поле `implementation` (`none`, `partial`, `implemented`, `verified`). Уровень считается по исходникам симуляции (`UnityProject/Assets/Game/Scripts/Simulation`): `AbilityFactory`, `Run.SupportedHeroAttributes`, `Run.ReportUnsupportedEnemy`, `Run.Drop`, `Run.SpawnPosition`, `Run.ReportUnsupportedRules`. `verified` означает, что у записи уровня `implemented` ключ встречается в тестах `DotNet/**` (список в поле `tests`). Правила описаны в `.kiro/specs/audit-simulation-coverage/design.md` §3. Поле `prototype` хранит прежнее покрытие прототипа и больше не пересчитывается. Другой каталог вывода задаётся через `--out DIR`.

Коды выхода `audit.py`: 0 — успех; 1 — у обязательной записи нет роли (файлы не пишутся); 2 — в исходниках симуляции не найдена ожидаемая конструкция (в сообщении указано, какая). Тесты аудита:

```sh
python3 -m unittest discover -s Tools/tests
```

Сборка для Android:

```sh
Tools/build-android.sh               # APK → Builds/Android/ThornThrone.apk, отчёт → Research/android-build-report.json
```

Инструменты Android (Gradle, NDK) не работают с путём, в котором есть кириллица («Терновый Трон»). Поэтому скрипт копирует `UnityProject` в `~/Library/Caches/ThornThroneAndroid`, собирает там и возвращает APK. Первая сборка занимает около 5 минут, следующие быстрее.

Повторный запуск безопасен: уже извлечённые файлы с верной контрольной суммой не перезаписываются.

Windows-скрипты `*.ps1` из прототипа больше не используются и перенесены в `Удалить/Tools/`. `kv3-to-json.mjs` остаётся: его вызывает `decode-data.sh`.
