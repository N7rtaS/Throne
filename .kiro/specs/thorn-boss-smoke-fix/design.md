# Smoke `-thornBoss` — проект исправления
## Причина
Общий критерий smoke `kills > 0` (`mac-smoke.sh check_log`, `MobileGame.SmokeCheck`: `Application.Quit(run.Kills > 0 ? 0 : 1)`) применяется и к режиму `-thornBoss`, который с первого коммита снимает способности героя (`owned.Ability = null`) ради скриншота Империи без снарядов героя. Герой без атак не убивает никого и гибнет от Империи примерно на 9 с (`hp=0/80`). Дефект в критерии проверки, а не в игре.
## Рассмотренные варианты
- Оставить способности героя. Тогда на скриншоте снаряды героя и урон по Империи, режим перестаёт быть «чистым» показом босса; kills > 0 проверяет обычный бой, который уже проверяет обычный smoke. Отклонено.
- Свой критерий для режима (выбрано). Проверяется то, ради чего режим есть: Империя появилась и атакует. Назначение режима не меняется, решения владельца не требуется.
## Исправление
- `MobileGame`: флаг `bossSmoke` при `-thornBoss`. В `Update` до `run.ClearEvents()` считаются события `SimEventKind.EnemyAttacked` со `Slot == run.Boss.Slot` (атаки Империи: ракеты, горящая земля, порталы, лучи). В конце забега перед `THORN_MOBILE_SMOKE` пишется `THORN_BOSS ok=… spawned=… attacks=… bossHp=…/… time=…`; код выхода плеера 0 при `ok=1`, иначе 12. Без `-thornBoss` код прежний.
- `mac-smoke.sh`: `check_boss` (код 12) — разбирает поля `spawned` и `attacks`, а не доверяет `ok`. `check_log` получает второй параметр `no-kills`: при нём строка `THORN_MOBILE_SMOKE` и исключения проверяются, `kills` — нет. `run_once` при `-thornBoss` вызывает `check_boss` и `check_log … no-kills`. Новый режим `--check-boss-log FILE`.
- Тесты: `Tools/tests/test_mac_smoke.py` (`MacSmokeBossCheckTests`) на образцах `smoke-logs/boss-*.log`; существующие тесты `--check-log` с `no-kills.log` (код 5) не меняются.
## Риски
- Герой гибнет до 30 с: забег заканчивается раньше, и результат-поражение пишется в тестовый профиль, как и до исправления.
- Если Империя не успеет атаковать до гибели героя, smoke упадёт кодом 12; по логу текущей сборки первая атака идёт в первые секунды.
## Итог
- До исправления: код 5, `THORN_MOBILE_SMOKE time=9.2 kills=0 … hp=0/80`. Новые тесты `MacSmokeBossCheckTests` сначала падали (нет `--check-boss-log`).
- После: `Tools/mac-smoke.sh -thornBoss` — код 0, `THORN_BOSS ok=1 spawned=1 attacks=2 bossHp=27000/27000 time=8.1` (эшелон 1 `story`: HP −10 %). Обычный smoke `kills=29`, `-thornElite` — код 0. Python: 119 тестов (8 новых).
