# Документ требований к исправлению: ложная диагностика `_Pillar` в бою с Империей
## Introduction
В smoke `Tools/mac-smoke.sh --no-build -thornBoss` строка итога содержит `diagnostics=1` (так же в образце `Tools/tests/smoke-logs/boss-ok.log`), в обычном smoke — `diagnostics=0`. В логе плеера единственное сообщение симуляции: `[Simulation] Special behaviour of CSurvivorsEnemyDefinition_Pillar (boss_tower) is not implemented`.

Сообщение пишет `Run.ReportUnsupportedEnemy` (`Scripts/Simulation/Run.cs`) при первом появлении врага, чей класс не входит в список обработанных. Колонны `boss_tower` ставит `Run.StartBossFight` (`Run.Boss.cs`) по спавнеру `tower_boss` в 4 точки `destructible_boss_tower`. Всё поведение колонн, заданное данными (`survivors_enemies.json`, `boss_tower`: `m_bHasSolidBody`, `m_bInvulnerable`, `m_bPlayerFriendly`, `m_bUndespawnable`, `STATIONARY`, `m_flCollisionRadius` 125), реализовано: колонны выталкивают героя (`PushOutOfSolids`), останавливают лучи (`BlockedBySolid`), не являются целями (`Enemy.Targetable`), не исчезают. Правило записано в `Mechanics.md` §12 («4 колонны… твёрдые, неуязвимые, загораживают героя и лучи», метка **Р**). Дорожная карта называет эту диагностику ложной (Task 29: «Убрать ложную диагностику `_Pillar`»). Диагностика — дефект классификатора, а не признак нереализованной механики.

Разрушение колонн (звук `survivors.imperia.pillar.destroy`, `Mechanics.md` §12.6, метка **К**) не восстановлено и остаётся открытым вопросом `Mechanics.md`; колонны неуязвимы по данным, поэтому сообщение «Special behaviour … is not implemented» его не описывает.

## Шаги воспроизведения
1. `Tools/mac-smoke.sh --no-build -thornBoss`.
2. В логе `[Simulation] Special behaviour of CSurvivorsEnemyDefinition_Pillar (boss_tower) is not implemented` и `THORN_MOBILE_SMOKE … diagnostics=1`.
3. В `dotnet test`: после `run.SpawnBossNow()` `run.Diagnostics.Has("enemy-class:CSurvivorsEnemyDefinition_Pillar")` равно `true`.

## Bug Analysis
### Current Behavior (Defect)
1.1 WHEN начинается бой с Империей и появляются колонны `boss_tower` (класс `CSurvivorsEnemyDefinition_Pillar`), THEN the system сообщает диагностику `enemy-class:CSurvivorsEnemyDefinition_Pillar`, хотя поведение колонн реализовано
1.2 WHEN smoke `-thornBoss` завершается, THEN the system пишет `diagnostics=1`

### Expected Behavior (Correct)
2.1 WHEN начинается бой с Империей и появляются колонны `boss_tower`, THEN the system SHALL NOT сообщать диагностику `enemy-class:CSurvivorsEnemyDefinition_Pillar`
2.2 WHEN smoke `-thornBoss` завершается без других нереализованных механик, THEN the system SHALL писать `diagnostics=0`

### Unchanged Behavior (Regression Prevention)
3.1 WHEN появляется враг класса, поведение которого не реализовано (например, `CSurvivorsEnemyDefinition_Snotty`), THEN the system SHALL CONTINUE TO сообщать диагностику `enemy-class:<класс>`
3.2 WHEN начинается бой с Империей, THEN the system SHALL CONTINUE TO ставить 4 колонны `boss_tower`, твёрдые, неуязвимые и не являющиеся целями
3.3 WHEN появляется враг с неизвестным поведением движения, THEN the system SHALL CONTINUE TO сообщать диагностику `movement:<имя>`
