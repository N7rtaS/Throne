# Трупы читают чужой слот врага — проект исправления

## Причина
`SimEvent` содержит только `Kind`, `Slot`, `Position`, `Value`. `WorldRenderer.Consume` по `EnemyDied` берёт определение, `SpawnId` (вариант облика) и направление из `run.Enemies[ev.Slot]`. Слот освобождается в `RetireEnemy` и кладётся на вершину стека `freeEnemies`, поэтому ближайший `SpawnEnemy` (дети при делении в том же шаге, расписание в следующих шагах кадра) выдаёт именно его. К моменту `Consume` слот описывает другого врага.

## Выбор варианта
| Вариант | Плюсы | Минусы |
|---|---|---|
| А. `Consume` сверяет `SpawnId` и пропускает чужой слот | Минимум изменений | Труп пропадает именно там, где дефект заметнее всего (деление — каждый раз); снова читает пул |
| Б. Событие несёт `SpawnId` и данные трупа | Труп всегда верный, представление не читает пул для смерти | `SimEvent` растёт на 3 поля |

Выбран **Б**. Вариант А превращает неверный труп в отсутствующий у каждого делящегося врага. Правило блока C выполняется: `Consume` вообще не читает слот, а `SpawnId` есть в событии для будущих сверок.

## Исправление
1. `SimEvent` (`World/Entities.cs`): новые поля `SpawnId` (`int`, 0 у событий не о сущности пула), `EnemyDef` (`EnemyDef`, только у событий о враге), `Direction` (`Vec2`, направление врага на момент события).
2. `Run.cs`: перегрузки `Emit(kind, Enemy, position, value)`, `Emit(kind, Projectile, position, value)`, `Emit(kind, Pickup, position, value)` заполняют `Slot`, `SpawnId`, а для врага — `EnemyDef` и `Direction`. Все события о сущностях пула (`Run.cs`, `Run.Boss.cs`) переходят на них. `BossSpawned` при `Boss == null` и события героя, уровня, выбора, конца забега и элитных комнат (слот = номер комнаты) остаются на прежнем `Emit` с `SpawnId = 0`.
3. `RetireEnemy` вызывается до изменения `Position` и `Direction`, поэтому событие смерти несёт место и направление умершего. Порядок вызовов, `Rng` и выдача слотов не меняются (3.3).
4. `WorldRenderer.Consume`: облик — `LookOf(ev.EnemyDef.Key, ev.SpawnId)`, место — `ToWorld(ev.Position)`, поворот — `Facing(run, def, position, direction)` с той же формулой (для `Tracking` — на текущее положение героя, иначе по `Direction`). `LookOf(Enemy)` и `Facing(Run, Enemy)` для живых врагов вызывают те же функции с полями врага.
5. Для EditMode-теста: `WorldRenderer.CorpseCount` и `CorpseAt(i)` (облик, место, поворот) только для чтения; `PrimitiveMesh` в редакторе вне игры удаляет временный объект через `DestroyImmediate` (`Object.Destroy` в EditMode пишет ошибку). Встроенные меши примитивов от удаления объекта не страдают.

Правила игры не меняются, записи в `Mechanics.md` и `Differences.md` не нужны: это доставка данных от симуляции к представлению.

## Риски
- Размер `SimEvent` растёт с 24 до ~48 байт; массив `Events` (8192) выделяется один раз при создании `Run`, в шаге выделений нет.
- `EnemyDef` в событии — ссылка на неизменяемое определение, живёт столько же, сколько `GameData`.
- `Facing` для `Tracking` по-прежнему считается по положению героя в конце кадра, как до исправления.

## Тестовая стратегия
- dotnet `EnemyEventTests` (новая фикстура):
  - `DeathEventDescribesEnemyEvenWhenSlotIsReusedInSameFrame` — шаги воспроизведения из `bugfix.md`: слот совпадает, `EnemyDied` несёт `SpawnId`, `EnemyDef`, `Position`, `Direction` умершего (падает до исправления: полей нет — тест не компилируется; затем проверка значений).
  - `SplitterDeathEventDescribesParentNotChild` — враг с делением из данных (`m_nSplitOnDeathNumUnits > 0`): ребёнок занимает слот родителя, событие смерти описывает родителя.
  - `EntityEventsCarrySpawnIdOfTheirEntity` — несколько seed, кадры по 8 шагов с ботом: каждое `EnemySpawned`/`ProjectileFired`/`PickupSpawned` несёт `SpawnId` сущности в слоте; каждое `EnemyDied` указывает на ранее появившийся `SpawnId` с тем же `EnemyDef`.
- EditMode `WorldRendererTests.ConsumeBuildsCorpseOfDeadEnemyWhenSlotIsReused` — два поддельных `EnemyVisual` с клипом смерти, смерть A и появление B в том же слоте, `Consume` → один труп с обликом A в точке A.
- Регрессия: все старые тесты без изменения ожиданий, `StepsDoNotAllocateAfterWarmUp`, `Tools/mac-smoke.sh`.
