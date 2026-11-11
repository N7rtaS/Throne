# Дизайн исправления: ложная диагностика `_Pillar`
## Причина
`Run.ReportUnsupportedEnemy` (`Run.cs`) считает обработанными только базовый класс `CSurvivorsEnemyDefinition`, босса, воскрешающего, поглотителя и големов. Колонны боя с Империей (`boss_tower`, класс `CSurvivorsEnemyDefinition_Pillar`) реализованы в `Run.Boss.cs` (`StartBossFight`, `PushOutOfSolids`, `BlockedBySolid`) и общими флагами данных (`Enemy.Targetable`, `Undespawnable`), но в список не попали. Поэтому при каждом бое с Империей появлялась диагностика `enemy-class:CSurvivorsEnemyDefinition_Pillar`, а smoke `-thornBoss` писал `diagnostics=1`.

## Исправление
Добавить `CSurvivorsEnemyDefinition_Pillar` в список обработанных классов `ReportUnsupportedEnemy`. Механика и правила боя не меняются. Тесты в `BossTests`: нет диагностики колонн; 4 колонны твёрдые, неуязвимые, не цели; нереализованный класс `CSurvivorsEnemyDefinition_Snotty` по-прежнему диагностируется. Образец `Tools/tests/smoke-logs/boss-ok.log` обновлён на `diagnostics=0`. В дорожной карте пункт Task 29 «Убрать ложную диагностику `_Pillar`» отмечен выполненным.

## Риски
- Разрушение колонн (звук `survivors.imperia.pillar.destroy`, `Mechanics.md` §12.6, **К**) не восстановлено. Оно остаётся открытым вопросом `Mechanics.md`, а не диагностикой: по данным колонны неуязвимы.
- Классификатор сверяет имя класса строкой; при новом классе колонн диагностика снова появится, что и нужно.
