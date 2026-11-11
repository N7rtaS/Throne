# Дизайн: Rot и Counter Helix (Task 21)

## 1. Архитектура
Новые файлы симуляции (по правилам блока C — по файлу на способность, `Run.cs` не растёт):
| Файл | Содержимое |
|---|---|
| `Abilities/CircleTargets.cs` | `sealed class CircleTargets` — общий примитив круга: `Collect(run, center, radius)`, `static Any(run, center, radius)`, `Live(i)`, `static Touches(enemy, center, radius)` |
| `Abilities/Rot.cs` | `sealed class Rot : AbilityBase` — постоянная аура с личной перезарядкой врага |
| `Abilities/CounterHelix.cs` | `sealed class CounterHelix : AbilityBase` — круговой удар по перезарядке с отложенным ударом и отталкиванием |

Изменения в существующих файлах:
- `Abilities/Abilities.cs` — две строки реестра `AbilityFactory.factories`.
- `World/Entities.cs` — `SimEventKind.AbilityBurst` (Position — центр, `Extent.X` — радиус, Value — ID способности).
- `Run.Events.cs` — `EmitBurst(center, radius, value)` по образцу `EmitStrike`, SpawnId 0.
- Unity: `WorldRenderer` (круг Rot, диск `AbilityBurst`), `MobileGame` (`-thornForce=`, строка `THORN_FORCE`), `Tools/mac-smoke.sh` (код 14), `Tools/tests/test_audit.py` (множество классов фабрики).

`EchoStrike` не переводится на `CircleTargets` (у него прямоугольник; попутных рефакторингов нет).

### 1.1 CircleTargets
```csharp
public sealed class CircleTargets
{
    private readonly Enemy[] hits; private readonly int[] ids;
    public CircleTargets(int maxEnemies);
    public static bool Touches(Enemy e, Vec2 center, float radius);   // |e−C| ≤ R + e.Radius
    public static bool Any(Run run, Vec2 center, float radius);       // есть ли поражаемый враг
    public int Collect(Run run, Vec2 center, float radius);           // копия в hits/ids
    public Enemy Live(int i);                                          // null, если умер или слот занят; обнуляет ссылку
}
```
Запрос: `using var near = run.Grid.Query(center, radius + run.MaxEnemyRadius);` — буфер из стека сетки (Task 43). Урон наносится после выхода из `using`, по копии, с проверкой `Active && SpawnId == ids[i]` (Task 44, правило блока C).

### 1.2 Rot
Поля: `double clock` (часы способности, идут только в `Tick`), `double[] readyAt` и `int[] readyId` по слоту пула врагов, `CircleTargets targets`.
`Tick(run, dt)`: `clock += dt`; `count = targets.Collect(run, HeroPosition, Area(Radius))`; `hit = Hit(run)` и `cd = Cooldown(run)` один раз за шаг; для каждого живого врага: если `readyId[slot] != SpawnId || clock ≥ readyAt[slot] − ε` — `DealDamage`, затем `readyId = SpawnId`, `readyAt = clock + cd`. После `run.Finished` — выход.
Запись по слоту хранит `SpawnId`, поэтому новый враг в слоте бьётся сразу, а сброс массива при появлении врага не нужен.

### 1.3 CounterHelix
Поля: `float cooldown`, `int remaining`, `float timer`, снимок `DamageInfo hit`, `float radius, knockback, interval`, `CircleTargets targets`.
`Tick`: `cooldown -= dt`; если `remaining > 0`: `timer -= dt`, пока `timer ≤ ε && remaining > 0` — `Strike`, `remaining--`, `timer += interval`. Затем, если `cooldown ≤ 0 && remaining == 0 && CircleTargets.Any(...)` — `cooldown = Cooldown(run)`, снимок, `remaining = Ticks`, `timer = InitialDelay`.
`Strike`: центр — текущее `HeroPosition`, `EmitBurst`, `Collect`, по каждому живому `DealDamage`, затем у выжившего с тем же `SpawnId` — `run.Knockback(e, (e − center).Normalized, knockback)`; при нулевом векторе — `run.Facing`.

## 2. Данные
Все числа читаются из `survivors_powerups.json` через `Build.Stat(owned, attr)`; ранги — общим механизмом `Build.Recalculate` (авторские записи со смещением `Rules.AuthoredUpgradeOffset`, §4). Новых полей в `PowerUpDef` не нужно: `m_flChanceToCounter` и `m_flDelayBetweenCounters` понадобятся только для Aghanim (Task 27), сейчас не читаются.

## 3. Правила (будут в Mechanics §28)
- **Д.** Числа и описания из введения `requirements.md`. Rot — магический урон, Counter Helix — физический (`AbilityBase`, §24 Р1).
- **К.** Иерархия классов клиента `CSurvivorsPowerUp_AreaAttack` → `_Circle` → `_CircleConstant`, отдельный `CSurvivorsPowerUp_CounterHelix`, перечисление `ESurvivorsAreaAttackOrigin`. Алгоритмы не восстановлены.
- **Р1. Область.** Круг радиуса `Radius × (1 + AoEIncrease)` вокруг текущего положения героя (`PLAYER_ORIGIN`); враг поражён, если его круг пересекает область (как прямоугольник Echo Strike, §26 Р2).
- **Р2. Rot: личная перезарядка.** Описание прямо говорит о перезарядке на каждого врага (**Д**); как именно она считается — **Р**: враг в круге бьётся сразу, если у него нет действующей перезарядки, после удара перезарядка `Cooldown` (с сокращением) для этого врага. Выход из круга перезарядку не сбрасывает. Перезарядка снимается в момент удара, рост ранга действует со следующего удара.
- **Р3. Rot: часы.** Личные перезарядки идут по часам способности (только шаги, где способность тикает), поэтому пауза, выбор, падение героя и конец забега их останавливают (§26 Р7). Часы — `double`, допуск 1e-4 с: при 1 с удар ровно каждые 30 шагов.
- **Р4. Rot без отталкивания и без события области**: в данных нет `KnockbackDistance`; урон показывается обычным `EnemyDamaged`.
- **Р5. Counter Helix: применение.** Как Echo Strike §26 Р1: при истёкшей перезарядке и наличии цели в круге. Без Aghanim способность не реагирует на урон по герою: «срабатывает при получении урона» сказано только в тексте Aghanim (**Д**), а `m_flChanceToCounter` 1 и `m_flDelayBetweenCounters` 0.15 — поля этого режима (Task 27).
- **Р6. Counter Helix: удар.** Через `InitialTickDelay` (0.05 с; при шаге 1/30 — второй шаг после применения), `DamageTicks` ударов через `max(шаг, TimeBetweenTicks)` (у данных `TimeBetweenTicks` нет, `DamageTicks` 1). Центр — положение героя в момент удара (вращение вокруг героя, за 0.05 с разница не больше одного шага движения). Урон, радиус и отталкивание — снимок при применении (§26 Р4). Новое применение не начинается, пока не сработали удары предыдущего.
- **Р7. Counter Helix: отталкивание** от центра к врагу на `KnockbackDistance × (1 + KnockbackDistanceMultiplier)` (§25 Р4), после урона, только у выжившего с тем же `SpawnId`; враг точно в центре отталкивается по `Run.Facing`.
- **Р8. Событие `AbilityBurst`** — одно на удар Counter Helix (центр, радиус в `Extent.X`, ID способности в `Value`, SpawnId 0); Rot события не выдаёт.
- **Р9. Отображение** (проектная адаптация, Differences Б13): Rot — зелёный полупрозрачный круг под героем, пока способность есть; удар Counter Helix — плоский диск на 0.15 с. Финальные частицы `pudge_rot.vpcf`, `axe_counter_helix.vpcf` — Task 37, звук — Task 36.
- **Р10. Предложение.** Обе способности становятся реализованными и предлагаются при уровне всем героям (как Echo Strike): выбор улучшений у забегов с прежними seed меняется. Ни один тест не зависит от точной последовательности выбора, кроме тех, что уже фиксируют его по ключу.
- **Р11. `-thornForce=KEY[,KEY…]`** (только smoke): после старта забега `Run.ForceUpgrade` для каждого ключа, затем остальные атаки героя снимаются (`SmokeForce.KeepOnlyForced`: при стоящем герое стартовая атака убивала врагов до круга Rot, первый прогон дал Rot 0 попаданий); строка `THORN_FORCE key=rank:hits …` в конце (попадания из `DamageStats`); неизвестный ключ, отказ `ForceUpgrade` или 0 попаданий — код 14 в `mac-smoke.sh`. Повтор ключа повышает ранг (`-thornForce=rot,rot` — ранг 2).

## 4. Тестовая стратегия
`DotNet/ThornThrone.Simulation.Tests/RotTests.cs` и `CounterHelixTests.cs` (фикстуры по способности), общие хелперы `EchoStrikeTests.Standing`/`Steps` переиспользуются (они `internal`).
- Арена: Dragonus, `DebugInvulnerable`, `DebugNoSpawns`, остальные способности отключены (`owned.Ability = null` после каждого `ForceUpgrade`), неподвижные зомби с большим здоровьем.
- Ожидания — из данных (`Base(...)` по JSON и таблица рангов из записей), не из реализации.
- Rot: удар при входе в том же шаге; на стоящего врага 1 удар за 30 шагов (ранг 1) и за 18 шагов (ранг 9, 0.6 с); два врага, вошедшие в разное время, бьются по своим часам; выход и возврат до конца перезарядки — нет удара; край круга (касание `Enemy.Radius`) и промах; `AoEIncrease` (Bloodstone); ранги 2, 3, 9 — урон и радиус; пауза и выбор не продвигают часы; без отталкивания.
- Counter Helix: нет применения без цели; удар на втором шаге после появления цели; урон 25 и отталкивание 30 вдоль направления от героя; между применениями 3.5 с (105 шагов); ранги 2, 3, 9; событие `AbilityBurst` с радиусом; урон по герою не вызывает удар; пауза.
- Общий: `CircleTargets` пропускает врага, чей слот занят новым во время удара (смерть от первого удара — разделение `Split`, если есть враг с разделением; иначе прямой тест через убийство и `SpawnEnemy` в тот же слот).
- Реестр: `Run.IsImplemented` для обоих, нет диагностик после `ForceUpgrade`; `ChoicesAreDistinctValidAndImplemented` остаётся зелёным.
- Бот: Dragonus + `ForceUpgrade(rot)` и `ForceUpgrade(counter_helix)`, 120 с, seed 1–5: нет диагностик, у обеих записей `DamageStats.Hits > 0`.
- Память: `StepsDoNotAllocateAfterWarmUp` — вариант с обеими способностями.
- Unity EditMode: круг Rot виден при наличии Rot и скрыт без него; `Consume` по `AbilityBurst` добавляет диск; разбор `-thornForce=`.
- Smoke: `Tools/mac-smoke.sh -thornForce=rot,counter_helix` (код 0, `THORN_FORCE` с попаданиями обеих), обычный smoke.

## 5. Риски
- Изменение пула выбора меняет забеги с прежними seed (бот-тесты с порогами по убийствам могут сдвинуться) — проверить полный `dotnet test`, пороги не подгонять, при падении разобраться в причине.
- Rot бьёт всех в круге каждый шаг по готовности: стоимость — один запрос к сетке за шаг, без выделений.
