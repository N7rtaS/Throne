# Задачи: README сборки, Unity-тесты, автоматический smoke

Roadmap: Task 8. Требования — `requirements.md`, решения — `design.md`.

- [x] 1. Тесты скрипта smoke (Т3)
  - [x] 1.1 Логи-образцы `Tools/tests/smoke-logs/{ok,no-kills,exception,missing}.log`
  - [x] 1.2 `Tools/tests/test_mac_smoke.py`: `--check-log` даёт коды 0/5/6/4; запустить — красный
- [x] 2. `Tools/mac-smoke.sh` (Т3, design §4): сборка, запуск плеера с тайм-аутом, `check_log`, `--no-build`, `--check-log`, проброс аргументов, проверка lock-файла Unity; тесты 1.2 зелёные
- [x] 3. Unity-тесты (Т2, Т4)
  - [x] 3.1 `com.unity.test-framework` 1.8.0 в `manifest.json`
  - [x] 3.2 `Assets/Game/Tests/EditMode/ThornThrone.Mobile.Tests.asmdef` и `MobileLayerTests.cs` (design §3.3)
  - [x] 3.3 `Tools/unity-tests.sh` (design §5); запуск — красный (нет сборки `ThornThrone.Mobile`)
  - [x] 3.4 `Assets/Game/Scripts/Mobile/ThornThrone.Mobile.asmdef` (design §3.1); `unity-tests.sh` — зелёный
- [x] 4. Проверка Unity-слоя: `Tools/mac-smoke.sh` код 0, `kills > 0`; скриншот `Research/Скриншоты/` через `Tools/mac-screenshot.sh`
- [x] 5. Документы (Т1, Т5)
  - [x] 5.1 Корневой `README.md` (design §2)
  - [x] 5.2 `Tools/README.md`: разделы про `mac-smoke.sh` и `unity-tests.sh` с кодами выхода
  - [x] 5.3 `.gitignore`: `Research/unity-tests.xml`, `Research/unity-tests.log`, `Research/mac-smoke.log` (Р9)
  - [x] 5.4 `structure.md`: строки `Tests/EditMode/` и asmdef `ThornThrone.Mobile`; `tech.md`: команды `mac-smoke.sh`, `unity-tests.sh`
  - [x] 5.5 ТЗ §2, §12, §13 и `Differences.md` по фактическим числам (design §6)
- [x] 6. Финал: `dotnet test DotNet/ThornThrone.Simulation.Tests`, `python3 -m unittest discover -s Tools/tests`, `Tools/unity-tests.sh` — зелёные; отметка в `roadmap.md`; один коммит (`Roadmap: Task 8`)
