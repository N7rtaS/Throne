# Дизайн: типизированные параметры Империи

## Данные
`ImperiaDef` (`Content/Definitions.cs`) — неизменяемый вид полей `CSurvivorsEnemyDefinition_ImperiaBoss`. Ссылка из `EnemyDef.Imperia`; у прочих врагов `null`.

| Группа | Поля `ImperiaDef` | Поле данных (**Д**) | Значение |
|---|---|---|---|
| Ярость | `InitialEnrageTime`, `IncrementalEnrageTime` | `m_flInitialEnrageTime`, `m_flIncrementalEnrageTime` | 30, 30 |
| Пороги | `MandatoryEnrageHealthThresholds` | `m_vecMandatoryEnrageHealthThresholds` | 0.66, 0.33 |
| Ракеты | `NumMagicMissiles`, `MagicMissileProjectileSpeed`, `MagicMissileProjectileSpeedIncreasePerEnrage`, `MagicMissileDamage`, `MagicMissileProjectileRadius` | `m_nNumMagicMissiles`, ... | 3, 700, 40, 15, 50 |
| Метеоры | `BurningGroundImpactDamage`, `ImpactRadius`, `ImpactSpawnDelay`, `FlamesDuration`, `DoTDuration`, `DoTDamage`, `ImpactStunDuration`, `InstancesPerEnrageLevel` | `m_flBurningGround*`, `m_nBurningGroundInstancesPerEnrageLevel` | 20, 280, 2, 5, 3, 4, 0, 3 |
| Лучи | `RadiateRaysCastStartDuration`, `Duration`, `Interval`, `Angle`, `Speed`, `Damage`, `ProjectileRadius` | `m_flRadiateRays*` | 0.5, 8, 0.02, 20, 1300, 15, 50 |
| Порталы | `DemonPortalsNumToCreate`, `MaxDemonPortalCount`, `DemonPortalDeactivateTime`, `DemonPortalDeactivateRadius`, `DemonPortalSpawners` | `m_nDemonPortalsNumToCreate`, ... | 1, 3, 15, 350, `imperia_portal`, `_2`, `_3` |

Имена частиц (`m_s*Particle*`) не нужны симуляции и остаются в `Raw` для представления.

`GameData.ReadImperia` читает поля через обязательные помощники `RF`/`RI`/`RFloats`/`RStrings`: каждое отсутствующее поле добавляется в список, после чтения при непустом списке бросается `InvalidDataException("enemies/imperia: missing ...")`. Это «диагностическая ошибка» из `product.md`: загрузка данных не продолжается с нулём вместо числа.

## Симуляция
- `Run` хранит `bossImperia = bossDef?.Imperia`. Все `F(def, "...", fallback)`, `Floats(...)`, `Raw[...]` в `Run.Boss.cs` заменены полями. Вспомогательные `F` и `Floats` удаляются. Резервные числа в коде исчезают: данные обязательны.
- Индекс спавнера портала был ограничен литералом 2; теперь — `DemonPortalSpawners.Length - 1` (то же значение для данных).
- Константа `Run.BossAttackInterval` переходит в `Rules`. В Unity-слое она не используется.

## Числа, которых нет в данных (`Rules`, **Р**)
Все значения равны прежним литералам `Run.Boss.cs`, поведение не меняется.

| `Rules` | Значение | Смысл |
|---|---|---|
| `BossFirstAttackDelay` | 2 с | Первая обычная атака после начала боя |
| `BossAttackInterval` | 4 с | Ракеты и метеоры чередуются с этим периодом |
| `BossMagicMissileSpread` | 0.35 рад | Угол между ракетами веера |
| `BossMagicMissileLife` | 4 с | Время жизни ракеты |
| `BossRayLife` | 3 с | Время жизни луча |
| `BossRaysTelegraphRadius` | 300 | Радиус телеграфа лучей (только отображение) |
| `BossMeteorScatterMin`, `BossMeteorScatterMax` | 1, 4 радиуса удара | Разброс метеоров кроме первого |
| `BossBurnTickInterval` | 1 с | Период урона горения (`m_flBurningGroundDoTDamage` за тик) |
| `PortalSpawnRadius` | 300 | Радиус появления у портала, если у спавнера нет `m_vecOvalRadius` (общий с `SPAWNER_ORIGIN_RADIUS` в `Run.cs`) |

Защитные минимумы (`Math.Max(0.01, interval)`, `Math.Max(0.1, deactivateTime)`) остаются: они не подставляют значения, а защищают от деления на ноль.

## Риски
- Порядок вызовов `Rng` не меняется, детерминизм и тесты по seed сохраняются.
- Ошибка загрузки при отсутствии поля может «сломать» чужие подмены данных; в репозитории подмен нет.
- `Rules` — класс с полями экземпляра; `Run` уже держит `Rules`, новых зависимостей нет.

## Тестовая стратегия
- `ImperiaDefinitionIsReadFromData`: числа из таблицы выше (из `survivors_enemies.json`), `Within(1e-6)`; у `boss_tower` `Imperia == null`.
- `MissingImperiaFieldFailsLoading`: загрузка данных, где у `imperia` удалено `m_flRadiateRaysSpeed`, бросает `InvalidDataException` с именем поля.
- `BossStepsDoNotAllocate`: бессмертный герой, способности выключены; прогрев 12 с обычных атак, порог 0.66 и 10 с лучей с порталом. Затем 300 измеряемых шагов: 150 шагов ракет и метеоров, удар до порога 0.33 (вне шага), 150 шагов лучей и открытия второго портала. Сумма `GC.GetAllocatedBytesForCurrentThread` по шагам без новых диагностик равна 0; в окне были события обычной атаки и лучей.
- Все прежние `BossTests` без изменений.
