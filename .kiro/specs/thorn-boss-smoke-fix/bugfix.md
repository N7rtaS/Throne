# Документ требований к исправлению: smoke `-thornBoss`
## Introduction
`Tools/mac-smoke.sh -thornBoss` завершается кодом 5: `THORN_MOBILE_SMOKE time=9.2 kills=0 level=1 hp=0/80`. Ключ `-thornBoss` в `MobileGame.SmokeOptions` снимает с героя все способности (`owned.Ability = null`), сразу вызывает `Run.SpawnBossNow()` и переносит скриншот на 7 с. Так было с первого коммита (`12a7139`). По истории режим нужен для проверки Империи на экране: скриншот HUD с Империей без снарядов героя (`tmp-cyrillic-hud/design.md`: «Убито 0», «Империя 30000»; `imperia-typed-definition`: «smoke `-thornBoss` работает как раньше»). Без способностей герой никого не убивает, поэтому общий критерий smoke `kills > 0` к этому режиму неприменим, а плеер выходит с кодом 1 (`Application.Quit(run.Kills > 0 ? 0 : 1)`).
Исправление: у `-thornBoss` свой критерий — Империя появилась и атаковала героя, исключений нет. Назначение режима (Империя без снарядов героя на скриншоте) сохраняется. Обычный smoke не меняется.
## Шаги воспроизведения
1. `Tools/mac-smoke.sh -thornBoss` (или `--no-build -thornBoss` после сборки).
2. Код выхода 5, в выводе `mac-smoke: no kills: THORN_MOBILE_SMOKE time=9.2 kills=0 …`; код выхода плеера 1.
## Bug Analysis
### Current Behavior (Defect)
1.1 WHEN плеер запущен с `-thornSmoke -thornBoss`, THEN the system проверяет `kills > 0`, хотя режим снимает способности героя, и `mac-smoke.sh` завершается кодом 5
1.2 WHEN забег `-thornBoss` закончился, THEN the system выходит из плеера с кодом 1, потому что `run.Kills = 0`
1.3 WHEN забег `-thornBoss` закончился, THEN the system не сообщает в логе, появилась ли Империя и атаковала ли она, поэтому smoke не проверяет то, ради чего режим существует
### Expected Behavior (Correct)
2.1 WHEN забег `-thornBoss` закончился (30 с или гибель героя), THEN the system SHALL записать в лог строку `THORN_BOSS ok=<0|1> spawned=<0|1> attacks=<N> bossHp=<hp>/<max> time=<t>`, где `attacks` — число событий `EnemyAttacked` от Империи
2.2 WHEN Империя появилась и `attacks > 0`, THEN the system SHALL записать `ok=1` и выйти из плеера с кодом 0; иначе — `ok=0` и код 12
2.3 WHEN `mac-smoke.sh` получил аргумент плеера `-thornBoss`, THEN the system SHALL требовать строку `THORN_BOSS` с `spawned=1` и `attacks >= 1` (иначе код 12), строку `THORN_MOBILE_SMOKE` (иначе 4) и отсутствие необработанных исключений (иначе 6), но не `kills > 0`
2.4 WHEN вызван `Tools/mac-smoke.sh --check-boss-log FILE`, THEN the system SHALL выполнить только разбор лога по 2.3
### Unchanged Behavior (Regression Prevention)
3.1 WHEN smoke запущен без `-thornBoss`, THEN the system SHALL CONTINUE TO требовать `kills > 0` (код 5) и выходить из плеера с кодом `run.Kills > 0 ? 0 : 1`
3.2 WHEN плеер запущен с `-thornBoss`, THEN the system SHALL CONTINUE TO снимать способности героя, сразу вызывать Империю и делать скриншот `-thornScreenshot` на 7 с
3.3 WHEN лог `-thornBoss` содержит необработанное исключение, THEN the system SHALL CONTINUE TO завершаться кодом 6
3.4 WHEN выполняются `--check-log`, `--check-flow-log`, `--check-hud-alloc-log`, THEN the system SHALL CONTINUE TO давать прежние коды на прежних образцах логов
