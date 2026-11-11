# Дизайн: общее ядро боя (Task 17)

## 1. Что найдено в источниках
Порядок поиска из `product.md`: `Research/Json` → `Mechanics.md` → локализация → panorama → строки клиента.

| Что | Источник | Метка |
|---|---|---|
| Урон способностей задан `Damage_Physical` (9 активных) или `Damage_Magical` (8 активных и `kez_innate`); обоих сразу нет ни у кого | `survivors_powerups.json` | **Д** |
| `ProjectileAttack = 1` у magic_missile, arcane_bolt, annihilation, chain_frost, frostbite, laguna_blade, knife_throw, track (не у всех из них летит снаряд) | `survivors_powerups.json` | **Д** |
| `Armor` называется «Сопротивление урону» / «Damage Resistance», процентный (`m_bPercentage`); Blade Mail +0.1 за ранг, постоянная прокачка 0.05…0.25 | `survivors_attributes.json`, локализация | **Д** |
| `DamageReflection` «Отражение урона», процентный; Blade Mail +1 за ранг, прокачка 0.5…2.5 | то же | **Д** |
| Криты: `CriticalStrike_Physical_*` «шанс/урон физического крита», `CriticalStrike_Projectile_*` «шанс/урон крита снарядами». Crystalys: первая запись шанс 0.3, множитель 1.6, далее +0.35; Daedalus 0.3 / 2.5; Khanda 0.3 / 2.5 | данные, локализация | **Д** |
| `BonusPhysicalDamage` «+X к физ. урону» (Demon Edge 5, Daedalus 20); `Projectile_BonusMagicDamage` «+X к доп. маг. урону снарядов» (Phylactery 5, Khanda 20) | данные, локализация | **Д** |
| `DamageMultiplier` «+X% к урону» (только постоянная прокачка 0.05…0.25), `DamageMultiplier_Magical/_Physical` | данные | **Д** |
| Видео 09.11.2026: урон Arcane Bolt 8–9 при прокачке «урон +25%» = 7 × 1.25 | `Mechanics.md` §13 | **Н** (частично: одно значение) |
| Врождённая Shen: «магический урон снижает физическое сопротивление»; англ. описание атрибута — «Increased Physical Damage after dealing Magic Damage», 0.3 на 5 с | локализация, данные | **Д** |
| Врождённая Kez: метка делает «следующую физическую атаку критической»; `VulnerabilityDamagePercent` 2, `VulnerabilityDuration` 5.5, `VulnerabilityStunOnHitDuration` 1 | `Descriptions.md`, данные | **Д** |
| Track: `IncomingDamageAmplification` 0.2 «Бонус к урону» на 10 с | данные | **Д** |
| Замедление: `SlowStrength` «Замедление передвижения» (Snotty 0.5 на 1.5 с). Заморозка: `FreezeStrength` «Сила приморозки» (Frostbite 1 на 1.5 с, Chain Frost 0.25 на 2 с) | данные, локализация | **Д** |
| Периодический урон: Frostbite `ModifierDamagePerTick` 4 «Маг. урон за срабатывание», `TimeBetweenTicks` 0.5 | данные, локализация | **Д** |
| `m_flStatusResistance` у 12 врагов (1 — ghost, skeleton_archer, tower, absorber, големы large/medium, imperia, boss_tower; 0.8 resurrector; 0.5 ogre_summon, hulk_summon; 0 granite_golem_small) | `survivors_enemies.json` | **Д** |
| У врагов нет полей брони и сопротивления урону ни в одном из 43 определений | `survivors_enemies.json` | **Д** (отсутствие) |
| `CSurvivorsModifier`, `SurvivorsApplyDamageData_t`, `CSurvivorsPowerUp::ApplyDamageAndEffects`, `CSurvivorsPowerUp::ApplyDamage(CSurvivorsUnit*, float, float, const VectorWS&)`, `CSurvivorsPowerUpDamageTickInfo`, `m_vecQueuedDamageTicks`, `CSurvivorsGameLogEntry_OutgoingDamage` «PowerUp=%d Damage=%f», `ESurvivorsDamageNumberType`, `m_sDamageNumbersEffectCriticalStrike`, `m_sFreezeParticle`, `m_sVulnerableParticle`, `m_nReturnDamagePct`, convar `dota_survivors_actual_random_damage_range_percent` | `Research/Decoded/client/strings.json` | **К** |

Формул в источниках нет. Всё, что ниже не помечено **Д**, — проектные решения **Р**.

## 2. Архитектура

```
Abilities/Abilities.cs     IAbility, IProjectileAbility, IProjectileHitEffect, AbilityFactory (без изменений по составу)
Abilities/AbilityBase.cs   общий базовый класс: Stat, Damage, Cooldown, DamageType, IsProjectileAttack, Hit()
Abilities/ArcaneBolt.cs    перенос из Abilities.cs на AbilityBase
Abilities/MagicMissile.cs  перенос из Abilities.cs на AbilityBase; оглушение через статусы
Combat/DamageInfo.cs       enum DamageType, [Flags] DamageFlags, struct DamageInfo
Combat/DamageStats.cs      DamageStats, DamageStatEntry (статистика по источникам)
Status/EnemyStatus.cs      состояние статусов одного врага (поля + фиксированный массив DoT) и правила наложения
Run.Combat.cs              partial Run: DealDamage, DamageEnemy, DamageHero, Stun/Slow/Freeze/ApplyDot/Weaken/Amplify/MarkVulnerable, тик статусов
```

Изменения в существующих файлах:
- `Run.cs`: убрать `DamageEnemy`, `Stun`, `DamageHero` (переезжают в `Run.Combat.cs`); в `Tick` — одна строка `UpdateStatuses(dt)`; в `UpdateEnemies` — `enemy.Status.Stunned`/`MoveFactor` вместо `StunTimer` (2–3 строки); в цикле снарядов — `DealDamage(best, shot.Hit)` вместо `DamageEnemy(best, shot.Damage)`; касание и ближняя атака передают нападающего в `DamageHero`. `CreateAbilities` регистрирует запись статистики. Итог — не больше 10 изменённых строк, логика не добавляется.
- `World/Entities.cs`: `Enemy.Status` (`EnemyStatus`, создаётся с пулом), `Enemy.StunTimer` и `Stunned` — свойства только для чтения поверх статуса; у `Projectile` величина остаётся в `Damage` (так её задают и старые тесты), рядом `DamageType`, `DamageFlags`, `DamageSource`; свойство `Hit` собирает из них `DamageInfo`. `SpawnProjectile` ставит чистый урон и запись источника, способность при выстреле перезаписывает всё снимком `AbilityBase.Hit` (уточнено при реализации); `SimEvent.DamageType`, `SimEvent.Critical`.
- `Build/Build.cs`: `OwnedPowerUp.Stats` — ссылка на запись статистики (не меняется при ранге и переживает поглощение рецептом, потому что запись хранится в `DamageStats`).
- `Stats/Attributes.cs`: объявить используемые атрибуты (`Damage_Physical`, `DamageMultiplier`, `DamageMultiplier_Physical`, `BonusPhysicalDamage`, `Projectile_BonusMagicDamage`, `CriticalStrike_*`, `Armor`, `DamageReflection`).
- `Rules.cs`: `MaxHeroArmor = 0.9f`, `MaxDotsPerEnemy = 4`.
- `Run.Boss.cs`: урон Империи по герою идёт через `DamageHero` без нападающего (как сейчас).

`SupportedHeroAttributes` не меняется: пассивки с новыми атрибутами предлагаются с Task 18.

Новые .cs файлы получают `.meta` с новыми GUID (Unity компилирует исходники симуляции). Папки `Combat/`, `Status/` — тоже с `.meta`.

## 3. Данные и API

```csharp
public enum DamageType { Physical, Magical, Pure }
[Flags] public enum DamageFlags { None = 0, Projectile = 1, Periodic = 2, Reflected = 4 }
public struct DamageInfo
{
    public float Amount; public DamageType Type; public DamageFlags Flags; public DamageStatEntry Source;
}
```
Источник в `DamageInfo` — запись статистики (`DamageStatEntry`), а не `OwnedPowerUp`: так периодический эффект и отражение учитываются одинаково, а запись переживает поглощение рецептом. В записи есть `PowerUpDef Def` (null для «Отражение» и «Прочее»).

`Run` (в `Run.Combat.cs`):
- `float DealDamage(Enemy target, in DamageInfo info)` — возвращает снятое здоровье.
- `void DamageEnemy(Enemy enemy, float amount)` — чистый урон от «Прочее» (тесты, отладка; сигнатура прежняя).
- `void Stun(Enemy, float)`, `void Slow(Enemy, float strength, float duration)`, `void Freeze(Enemy, float strength, float duration)`, `void ApplyDot(Enemy, DamageStatEntry source, DamageType, float perTick, float interval, float duration)`, `void Weaken(Enemy, float, float)`, `void Amplify(Enemy, float, float)`, `void MarkVulnerable(Enemy, float multiplier, float stun, float duration)`.
- `DamageStats DamageStats` — `Reflection`, `Other`, `Entries`.

`AbilityBase`:
- `Stat(run, attr)`, `Damage(run)`, `Cooldown(run)` — публичные, как сейчас у `ArcaneBolt`/`MagicMissile` (на них ссылаются тесты).
- `DamageType` и `IsProjectileAttack` (`ProjectileAttack > 0` в атрибутах способности) определяются при создании.
- `DamageInfo Hit(Run run)` — снимок урона при выстреле.

## 4. Правила

**Р1. Формула урона способности (снимок при выстреле).** `(Damage_<тип> + бонус) × (1 + DamageMultiplier + DamageMultiplier_<тип>)`. Бонус: `BonusPhysicalDamage` для физического урона; `Projectile_BonusMagicDamage` для магического урона способности с `ProjectileAttack > 0`. Множители складываются (так совпадает видео: 7 × 1.25). Сейчас все бонусы и `DamageMultiplier` равны 0, поэтому числа Arcane Bolt и Magic Missile не меняются.

**Р2. Порядок при попадании** (`DealDamage`):
1. Цель не `Targetable` или забег окончен — урона нет, `Rng` не трогается.
2. Чистый урон: величина без изменений, переход к п. 6.
3. Множитель крита `c = 1`. Физический урон и шанс физического крита > 0 — бросок `Rng.NextFloat() < шанс` → `c = max(c, max(1, множитель))`. Признак `Projectile` и шанс крита снарядами > 0 — отдельный бросок, тот же выбор большего. Признак `Periodic` — без критов.
4. Уязвимость (физический урон, не `Periodic`): `c = max(c, множитель метки)`, удар считается критическим, метка снимается, после урона — оглушение на длительность метки (через обычное `Stun`, с сопротивлением статусам).
5. `× (1 + ослабление)` для физического урона; `× (1 + усиление)` для физического и магического.
6. Здоровье уменьшается; Империя упирается в порог ярости как сейчас (`ClampBossHealth`).
7. Статистика источника: `+снятое здоровье` (разница до и после, без перебора и без отрезанного порогом), `+1` попадание, `+1` крит, при смерти `+1` убийство. Событие `EnemyDamaged` с итоговой величиной (до ограничения здоровьем, как сейчас), типом и признаком крита.
8. Смерть, воскрешение, разделение — без изменений.

**Р3. Множитель крита — полный коэффициент.** Crystalys 1.6 значит урон ×1.6, а не ×2.6; значение ниже 1 крит не ослабляет. Так устроены криты Dota и так читается первая запись Crystalys (шанс 0.3 + множитель 1.6). Бросок — при попадании, по каждой цели отдельно, по текущим атрибутам героя.

**Р4. Сопротивлений у врагов нет.** Ни в одном определении врага нет брони или сопротивления (**Д**, отсутствие), поэтому базовое сопротивление 0 — это не подстановка нуля вместо отсутствующего поля, а отсутствие механики у врагов. «Снижение физического сопротивления» Shen работает как `+сила` к получаемому физическому урону, как сказано в английском описании атрибута.

**Р5. Броня героя.** Весь входящий урон × `(1 − min(Armor, Rules.MaxHeroArmor))`, `MaxHeroArmor = 0.9`. Наибольшее значение по данным: 0.25 (прокачка) + 0.3 (Blade Mail 3 ранга) = 0.55, ограничение срабатывает только на будущих сочетаниях.

**Р6. Отражение.** Касание и ближняя атака (`attack.Speed <= 0`) знают нападающего: он получает чистый урон `урон до брони × DamageReflection` с признаком `Reflected`, источник «Отражение». Снаряды врагов и зоны Империи нападающего не хранят — отражения нет. Если герой урон не получил (рывок, `DebugInvulnerable`, конец забега), отражения нет. Отражённый урон не отражается и не критует.

**Р7. Хранение статусов.** Каждый слот пула врагов создаёт свой `EnemyStatus` один раз; при `SpawnEnemy` вызывается `Reset()`. Поля: `Stun`; `SlowStrength`, `SlowTime`; `FreezeStrength`, `FreezeTime`; `WeakenStrength`, `WeakenTime`; `AmplifyStrength`, `AmplifyTime`; `VulnerableMultiplier`, `VulnerableStun`, `VulnerableTime`; DoT — массивы длины `Rules.MaxDotsPerEnemy` (источник, тип, урон за срабатывание, интервал, таймер, остаток). Отдельный массив структур по номеру слота в `Run` тоже подошёл бы, но объект на враге сохраняет `enemy.Stunned` для представления и тестов и не требует передавать номер слота в правила.

**Р8. Контроль и сопротивление статусам.** Длительность оглушения, замедления и заморозки × `(1 − m_flStatusResistance)`; при 0 и меньше статус не накладывается. Неподвижные враги не оглушаются (как сейчас). Ослабление, усиление, уязвимость и периодический урон от сопротивления не зависят.

**Р9. Наложение замедления и заморозки.** По одной ячейке на вид. Сильнее — замена силы и длительности; равная сила — длительность до большей; слабее — без изменений. Скорость собственного движения × `1 − max(замедление, заморозка)`, не меньше 0. Заморозка не останавливает атаки (атаки останавливает только оглушение). Расталкивание и отталкивание действуют.

**Р10. Оглушение.** Без изменений Mechanics §15: более длинное оставшееся не сокращается; оглушённый не идёт и не атакует, таймеры атаки стоят; событие `EnemyStunned` при каждом продлении.

**Р11. Периодический урон.** До 4 эффектов на врага; ключ — источник. Повтор от того же источника: остаток до большего, новый урон за срабатывание, фаза сохраняется. Новый источник при полных ячейках вытесняет эффект с наименьшим остатком, диагностика `status:dot-overflow` один раз. Первое срабатывание через интервал; урон через `DealDamage` с `Periodic` (без критов, уязвимость не снимает); последнее срабатывание — когда остаток доходит до 0 на границе интервала (Frostbite 1.5 с / 0.5 с = 3 срабатывания).

**Р12. Ослабление и усиление.** По одной ячейке: сила — большая из двух, длительность — большая из двух.

**Р13. Уязвимость.** Одна метка: множитель — больший, длительность — до большей, оглушение — большее. Снимается первым физическим не периодическим уроном.

**Р14. Тик статусов.** `UpdateStatuses(dt)` вызывается в `Tick` сразу после `UpdateEnemies` и до способностей: уменьшает таймеры и выполняет срабатывания DoT. Оглушение уменьшается там же, а не внутри `UpdateEnemies`. Вне `Tick` таймеры не идут, поэтому пауза, выбор и конец забега их останавливают.

**Р15. Разброс урона.** Не применяется. В клиенте есть convar `dota_survivors_actual_random_damage_range_percent` (**К**), но его значение и смысл неизвестны.

**Р16. Статистика.** Записи: «Отражение», «Прочее» и по одной на каждую полученную активную способность (создаётся в `CreateAbilities`, вне шага). Хранятся до конца забега, поэтому переживают поглощение рецептом.

## 5. Риски
- **Порядок шага.** Сейчас `UpdateEnemies` сначала видит оглушение, потом уменьшает его. Если уменьшать раньше `UpdateEnemies`, враг с остатком меньше шага пошёл бы на шаг раньше. Поэтому `UpdateStatuses` стоит после `UpdateEnemies`: число шагов, которые враг стоит, не меняется. Проверяется старыми тестами Magic Missile без изменения ожиданий.
- **`Rng`.** Новые броски только при шансе > 0, поэтому seed-тесты и smoke дают те же забеги.
- **Unity.** Исходники симуляции компилирует Unity: новые файлы требуют `.meta`; `Enemy.StunTimer` становится свойством (в `WorldRenderer` используется только `Stunned`). Проверка — smoke проверяющего агента.
- **Аудит.** `Tools/audit.py` читает `AbilityFactory` из `Abilities/Abilities.cs` и `Attr.X` из всех файлов: фабрика остаётся на месте, новые `Attr` повышают счёт «используемых» атрибутов. После задачи — `python3 -m unittest discover -s Tools/tests`.

## 6. Тестовая стратегия
Реальные данные (`TestData.Load()`), значения из `Research/Json`; атрибуты героя в тестах задаются через `run.Build.Global.Add(...)` после последнего изменения билда. Случайность — циклом по seed.

`DamageTests.cs`:
- `AbilityDamageTypeComesFromData` — Arcane Bolt и Magic Missile магические, `IsProjectileAttack`.
- `DamageMultiplierMatchesVideoProfile` — `DamageMultiplier` 0.25 → Arcane Bolt 8.75 (Mechanics §13).
- `FlatBonusesFollowDamageType` — `Projectile_BonusMagicDamage` прибавляется к Arcane Bolt, `BonusPhysicalDamage` — нет.
- `PhysicalCritUsesCrystalysMultiplier` — шанс 1, множитель из первой записи Crystalys (1.6).
- `MagicalHitDoesNotRollPhysicalCrit`, `ProjectileCritAppliesToMagicProjectile` (Khanda 2.5), `HigherCritMultiplierWins`.
- `CritRateMatchesChanceAcrossSeeds` — шанс 0.3, 2000 ударов × 5 seed, доля 0.3 ± 0.03; `CritSequenceIsReproducibleForSeed`.
- `NoCritChanceDoesNotTouchRng` — состояние `Rng` до и после совпадает.
- `PureDamageIgnoresModifiers`.
- `WeakenAmplifiesOnlyPhysicalDamage` (Shen 0.3), `AmplificationAppliesToPhysicalAndMagical` (Track 0.2).
- `VulnerabilityDoublesNextPhysicalHitAndStuns` (Kez 2 / 1 с), второй удар обычный.
- `HeroArmorReducesIncomingDamage` (Blade Mail 0.1), `HeroArmorIsCapped`.
- `ReflectionHitsContactAttackerWithPureDamage` (Blade Mail 1), `NoReflectionWhileDashing`, `NoReflectionFromEnemyProjectiles`.
- `DamageStatsCountDealtHealthBySource` — Arcane Bolt в своей записи, перебор не считается, убийства; `BossThresholdClampIsNotCounted`.
- `DamageEventCarriesTypeAndCrit`.
- `DealDamageDoesNotAllocate`.

`StatusTests.cs`:
- `SlowReducesOwnSpeedBySlowStrength` (Snotty 0.5): смещение за шаг вдвое меньше.
- `FreezeStrengthOneStopsMovementButNotAttacks` (Frostbite 1).
- `StrongerSlowReplacesWeakerIsIgnored`, `EqualSlowExtendsDuration`, `SlowAndFreezeTakeStrongest` (Chain Frost 0.25 + Snotty 0.5).
- `ControlDurationScalesWithStatusResistance` — ogre_summon 0.5, imperia 1 (иммунитет) для замедления и заморозки.
- `DotTicksEveryIntervalForDuration` (Frostbite 4 / 0.5 / 1.5 → 3 срабатывания, 12 магического урона, в записи источника).
- `DotFromSameSourceRefreshes`, `DotOverflowReplacesShortestAndReports`.
- `PeriodicDamageDoesNotCritOrConsumeVulnerability`.
- `StatusesStandStillWhilePausedOrChoosing`.
- `ReusedSlotStartsWithoutStatuses`.
- `StatusTicksDoNotAllocate` — 1000 врагов со всеми статусами, 300 шагов.

Старые тесты Magic Missile (`Stun`, `StunTimer`, иммунитет Империи) и Arcane Bolt проходят без изменения ожиданий.
