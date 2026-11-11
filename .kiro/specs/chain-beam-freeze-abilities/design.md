# Дизайн: Knife Throw, Chain Frost, Laguna Blade, Frostbite (Task 22)
## Архитектура
Все файлы — в `UnityProject/Assets/Game/Scripts/Simulation` (чистый C# 9, без движка). Регистрация — в `AbilityFactory`. `Run.cs` получил две строки: сброс полей заморозки снаряда в `SpawnProjectile` и перенос диагностик загрузки данных в `ReportUnsupportedRules`.

| Файл | Что делает |
|---|---|
| `Abilities/StrongestTargets.cs` (новый) | Общий выбор до N самых сильных целей в дальности (§26 Р10) в буфер способности с `SpawnId`, `Live(i)` как у `CircleTargets`. Вынесен из `InstantAttack.Strongest`; `InstantAttack` переходит на него без изменения поведения. |
| `Abilities/ChainBounce.cs` (новый) | Статический шаг цепи: `bool Next(Run run, Projectile shot, Vec2 impact, float range, float armTime, float life)`. Проверяет отскоки и заполненность пула посещённых, берёт `Run.RandomTarget(impact, range, shot)` (уже исключает `shot.HasHit`), перенаправляет снаряд. Вынесен из `MagicMissile.OnHit`; `MagicMissile` переходит на него без изменения поведения и порядка бросков `Rng`. |
| `Abilities/KnifeThrow.cs` (новый) | Залп по направлению героя; ножи — обычные снаряды без обработчика попадания (урон, отталкивание, пробитие делает общий полёт в `Run.UpdateProjectiles`). |
| `Abilities/ChainFrost.cs` (новый) | `IProjectileAbility`: выстрел в самых сильных, `OnHit` — урон и оглушение цели, заморозка круга (`CircleTargets`), затем `ChainBounce.Next`. |
| `Abilities/LagunaBlade.cs` (новый) | Мгновенный удар по самым сильным, доп. удары по элитным, событие `AbilityStrike`. |
| `Abilities/Frostbite.cs` (новый) | Мгновенное обморожение самых сильных: `Run.Freeze`, `Run.ApplyDot`, `ProjectileAmplification.Apply`. |
| `Status/ProjectileAmplification.cs` (новый) | Наложение и множитель уязвимости к снарядам; хранение — два поля в `EnemyStatus` (`ProjectileAmplifyStrength`, `ProjectileAmplifyTime`, сброс и отсчёт как у `Amplify`). `Run.DealDamage` получает одну строку `amount *= ProjectileAmplification.Factor(status, info);` рядом с ослаблением и усилением. |
| `World/Entities.cs` | `Projectile.FreezeRadius`, `FreezeStrength`, `FreezeTime` — снимок заморозки Chain Frost при выстреле (Р4). |
| `Abilities/AbilityBase.cs` | `DataField(run, поле)` — число определения вне атрибутов; без поля — диагностика `ability:<ключ>:<поле>` и 0. |
| `Content/GameData.cs`, `Rules.cs` | `GameData.Diagnostics` — проблемы данных при загрузке (повтор атрибута, Р12); `Diagnostics.Include` переносит их в каждый забег, Unity пишет их в лог. |
| `Stats/Attributes.cs` | Новые id: `FreezeSplashRadius`, `FreezeDuration`, `FreezeStrength`, `ModifierDamagePerTick`, `ProjectileDamagePercent`, `ProjectileHorizontalOffset`. |
| `Abilities/Abilities.cs` | 4 строки в `factories`; `supports` для `CSurvivorsPowerUpDefinition_ProjectileAttack` (`ChainFrost.Supports`). |

Unity-слой (`Scripts/Mobile`) не меняется: ножи и Chain Frost рисуются общим шаром снаряда `WorldRenderer`, луч Laguna — существующим прямоугольником `AbilityStrike` (0.15 с), `-thornForce` и код 14 в `mac-smoke.sh` уже общие. Заморозка и обморожение на экране не показываются (Task 37). Отличия записываются в `Differences.md` (Б14).

## Данные
Числа — только из `survivors_powerups.json` через `Build.Stat`/`AbilityBase` (ранги со смещением §4). Поля вне атрибутов читаются из `PowerUpDef.Raw` один раз в конструкторе обработчика (как `m_flBounceMinimumLifetime` у Magic Missile): `m_flBounceMinimumLifetime`, `m_flSpawnMinimumLifetime`, `m_eBounceTargeting`, `m_nEliteExtraHit`. Отсутствующее поле не становится нулём молча: если у `chain_frost` нет `m_flBounceMinimumLifetime`/`m_flSpawnMinimumLifetime` или у `laguna_blade` нет `m_nEliteExtraHit`, обработчик пишет диагностику `ability:<ключ>:<поле>` и берёт 0.

## Правила (метки для Mechanics §29)
Источники **Д** и **К** — в requirements.md. Ниже — проектные решения.

### Общее
- **Р1. Самые сильные** — наибольшее текущее здоровье, затем ближе к герою, затем меньший слот, разные цели, до `TargetCount` (то же правило, что §26 Р10, теперь общий `StrongestTargets`). Если целей меньше `TargetCount`, лишние снаряды/лучи/обморожения не выпускаются (Aghanim Laguna «одна цель несколько раз» означает, что без него так нельзя). Описание Chain Frost говорит «в ближайшего врага», но данные — `STRONGEST_TARGET_IN_RANGE`; действуют данные.
- **Р2. Применение по наличию цели.** Chain Frost, Laguna Blade и Frostbite применяются, только если в `Range` есть поражаемый враг; иначе перезарядка остаётся истёкшей и проверка повторяется каждый шаг (как Magic Missile, §26 Р1).
- **Р3. Цепь.** Цель отскока — случайный поражаемый враг в `Range` от точки попадания, которого снаряд ещё не поражал (пул посещённых — `Projectile.HitSpawnIds`, 32 записи). Если такого нет или пул заполнен — цепь кончается. После отскока снаряд `m_flBounceMinimumLifetime` секунд никого не задевает. Это прежнее правило Magic Missile (§15), теперь общий `ChainBounce`. Chain Frost на ранге 9 делает до 14 попаданий, пул не заполняется.
- **Р4. Снимок.** Урон, оглушение и параметры заморозки снаряда фиксируются при выстреле; удар луча и обморожение считаются в шаге применения.
- **Р5. `DurationEffect` 1** (у Chain Frost и Frostbite, нет в `survivors_attributes`, §10) не используется: смысл неизвестен; продление `DurationExtension` действует по §25 Р5 и без него.
- **Р6. Поддерживаемые определения.** Класс `CSurvivorsPowerUpDefinition_ProjectileAttack` общий в клиенте (**К**: `CreateAttackProjectile` с `ESurvivorsAttackTargeting`), в данных он один — `chain_frost`. Обработчик `ChainFrost` поддерживает только `m_eTargeting` `STRONGEST_TARGET_IN_RANGE` с `m_eBounceTargeting` `RANDOM_TARGET_AT_RANGE` (`AbilityFactory.supports`, как §26 Р15).

### Knife Throw
- **Р7. Залп** — как Arcane Bolt: при истёкшей перезарядке и без текущего залпа начинается залп из `TargetCount` ножей, перезарядка стартует с началом залпа, ножи через `max(0.02, ProjectileAttackInterval)`. Новый залп не начинается, пока не брошен последний нож. На ранге 9 перезарядка по данным 0 (патч-нота 1.1 это подтверждает), `AbilityBase.Cooldown` ограничивает её снизу 0.1 с, но залп из 5 ножей длится дольше, поэтому залпы идут подряд с паузой в один шаг.
- **Р8. Без цели.** `PLAYER_FACING`: ножи бросаются по перезарядке, даже если врагов нет.
- **Р9. Направление** — `Run.Facing` в момент броска каждого ножа (последнее направление движения). Наведения нет.
- **Р10. Смещение.** Точка вылета — `HeroPosition ± Facing.Perpendicular × ProjectileHorizontalOffset`; нечётный по счёту нож залпа — со стороны `+Facing.Perpendicular` (поворот направления на 90° против часовой), чётный — с другой. Знак не важен для правил, важно чередование.
- **Р11. Полёт.** Скорость `ProjectileSpeed`, радиус `ProjectileRadius`, время жизни `Range / ProjectileSpeed` (нож пролетает ровно `Range`, а не `Range/скорость + 1` как самонаводящиеся). Попадания, отталкивание по направлению полёта и пробитие `1 + ProjectileHitCount` — общий полёт снаряда (как Arcane Bolt). Стены нож не останавливают (у союзных снарядов этого нет нигде).
- **Р12. Дубликат `ProjectileAttackInterval`** — решение владельца (12.11.2026): повтор одного и того же атрибута в `m_vecBaseAttributes` одного определения считается один раз (0.15), загрузка пишет диагностику `data:duplicate-attribute:knife_throw:ProjectileAttackInterval` (`GameData.Diagnostics`, переносится в диагностику забега). В данных повтор в базовых атрибутах есть только у `knife_throw`; повторы внутри авторских записей (`rot` и `snotty_bat`, запись 8) это правило не трогает.

### Chain Frost
- **Р13. Выстрел.** Самонаводящийся снаряд из положения героя в цель (Р1), скорость `ProjectileSpeed`, радиус `ProjectileRadius`, время ноги `max(3, Range/скорость + 1)` (как Magic Missile), первые `m_flSpawnMinimumLifetime` 0.2 с после выпуска не задевает врагов (`Projectile.ArmTime`), но летит к цели.
- **Р14. Попадание.** Поражённый — первый враг на пути снаряда (общий swept-полёт, как Magic Missile). Урон получает только он («наносит урон, замедляет всех вокруг»); затем, если он жив и тот же `SpawnId`, оглушение `StunDuration` 0.2 (+ `DurationExtension`, сопротивление статусам).
- **Р15. Заморозка круга.** После урона: все поражаемые враги, чей круг касается круга `FreezeSplashRadius × (1 + AoEIncrease)` вокруг положения поражённого врага (`CircleTargets`, §28 Р1), включая его самого, получают `Run.Freeze(FreezeStrength 0.25, FreezeDuration 2 + DurationExtension)`; наложение — §24 Р9. Описание называет это «замедляет»; в данных это `Freeze*`, поэтому используется заморозка (она тоже только замедляет, атаки идут).
- **Р16. Отскок** — Р3, дальность — текущий `Range` способности.
- **Р17. Перезарядка** идёт, пока снаряд летит; стены снаряд не останавливают.

### Laguna Blade
- **Р18. Мгновенный луч.** Удар в шаге применения, без снаряда и полёта. Удар — `DealDamage` с `DamageFlags.Projectile` (`ProjectileAttack` 1), поэтому действуют криты снарядов, `Projectile_BonusMagicDamage` и уязвимость Frostbite.
- **Р19. Ширина луча** — решение владельца (12.11.2026): урон получает только цель (так сказано в описании, ширины в данных нет); ширина нужна только для отображения: `LagunaBlade.BeamWidth` = 60 единиц, это `Extent.Y` события. Тест: враг, стоящий на отрезке герой → цель, урона не получает; событие несёт длину = расстояние до цели и ширину 60.
- **Р20. Элитные.** Цель с `m_bIsElite` получает `1 + m_nEliteExtraHit` = 3 удара подряд в одном шаге (каждый со своим броском крита); удары прекращаются, если цель умерла или слот сменился. Империя не элитная (`m_bIsElite` нет) — 1 удар.
- **Р21. Событие.** Одно `AbilityStrike` на каждую цель: `Position` — середина отрезка герой → цель, `Direction` — к цели, `Extent` = (расстояние, `BeamWidth`), `Value` — ID 17, `SpawnId` 0. Выдаётся до урона, чтобы луч был виден и при убийстве.

### Frostbite
- **Р22. Мгновенное наложение.** У Frostbite нет `ProjectileSpeed`, поэтому снаряд не летит: обморожение ложится на цели в шаге применения. Частица снаряда — только отображение (Task 37).
- **Р23. Обморожение** — три статуса с одной длительностью `D = FreezeDuration + DurationExtension`: заморозка `FreezeStrength` 1 на `D` с сопротивлением статусам; периодический магический урон `ModifierDamagePerTick × (1 + DamageMultiplier + DamageMultiplier_Magical)` каждые `TimeBetweenTicks` на `D` без сопротивления (источник — запись статистики Frostbite, §24 Р11: повтор продлевает, фаза сохраняется); уязвимость к снарядам `ProjectileDamagePercent` на `D` без сопротивления. Плоский бонус `Projectile_BonusMagicDamage` к тикам не добавляется (тик — не удар снарядом).
- **Р24. Уязвимость к снарядам.** Удар с `DamageFlags.Projectile`, не периодический, не чистый, по врагу с действующей уязвимостью × `(1 + сила)`. Наложение — как §24 Р12 (большая сила, большая длительность). Чистый урон, периодический урон, Rot, Echo Strike, Counter Helix не усиливаются. Множитель применяется после критов, вместе с ослаблением и усилением.
- **Р25. Без прямого удара.** `Damage_Magical` 0 — `DealDamage` при наложении не вызывается (как §26 Р13). Ослабление Shen срабатывает от тиков (магический урон, §27 Р1).
- **Р26. Империя** (сопротивление 1) не замораживается, но получает периодический урон и уязвимость.

## Решения владельца (12.11.2026)
1. Knife Throw входит в задачу (так в `roadmap.md`), slug `chain-beam-freeze-abilities`.
2. Р19: урон только цели, ширина 60 — только отображение.
3. Р12: повтор атрибута в базовых атрибутах считается один раз (0.15), диагностика при загрузке.

## Тестовая стратегия
Фикстуры в `DotNet/ThornThrone.Simulation.Tests`: `KnifeThrowTests`, `ChainFrostTests`, `LagunaBladeTests`, `FrostbiteTests` и общая `ChainBeamFreezeTests` (выбор самых сильных, пауза, выделения памяти, бот-забег). Хелперы — `ChainBeamFreezeTests.Arena/Standing/Hits`, `RotTests.Upgrade/Base/Owned`, `EchoStrikeTests.Steps`. Проверки рангов идут на Kez: у Dragonus врождённая `skywrath_mage_innate` даёт глобальный `TargetCount` +1 (§3, §25), который прибавляется к каждой способности; это проверяет отдельный тест `DragonusInnateAddsOneTargetToEachOfThem` (ножей 2, снарядов Chain Frost 2, лучей 2, обморожений 3). Ожидания — из данных (`RotTests.Base`) и таблицы рангов requirements.md.
- Реестр: `Run.IsImplemented` истинно, обработчик нужного типа, нет диагностик `ability:*` и `damage-type:*`; тип урона по данным.
- Ранги 1, 2, 3, 9 — по таблице; ранг 9 — максимальный.
- Knife: нож летит по `Facing` мимо врага сбоку (нет наведения); пробитие 1 / 2 / 3 врага в ряд; отталкивание по полёту на 5; 2 ножа на ранге 2 с интервалом залпа; бросок без врагов; смещение точки вылета ±60.
- Chain Frost: 6 врагов в цепочке → 6 попаданий по 6 разным `SpawnId`, ни одного повтора (цикл по seed 1–5); 2 врага → 2 попадания и конец цепи (нет возврата); урон только цели; заморозка 0.25 / 2 с соседу в 130 + радиус и нет заморозки дальше; оглушение 0.2; нет попадания в первые 0.2 с после выпуска; цель — самый сильный, а не ближайший.
- Laguna: урон в шаг применения; враг на линии луча не задет; событие `AbilityStrike` с длиной и шириной; 3 удара по элитному (`resurrector` или `skeleton_archer`), 1 по обычному; 2 цели — 2 разных врага, при одной цели — один удар.
- Frostbite: DoT 3 тика по 4 за 1.5 с (первый через 0.5 с); заморозка 1 на 1.5 с (враг не двигается); Arcane Bolt ×1.25 по обмороженному и ×1 без обморожения; тик и Rot не усиливаются; Империя без заморозки, с DoT; повтор продлевает, а не удваивает DoT.
- Общие: `StrongestTargetsTests`-случаи внутри `KezInnateTests` не меняются; `MagicMissileTests` не меняются; пауза и выбор улучшения не двигают залп и статусы; `StepsDoNotAllocateAfterWarmUp` со всеми четырьмя; бот-забег Dragonus с четырьмя способностями (120 с, seed 1–5) — каждая бьёт, диагностик нет.
- Инструменты: `Tools/tests/test_audit.py` — 4 новых класса фабрики; `python3 -m unittest discover -s Tools/tests`.
- Unity не меняется: проверка — обычный smoke и `Tools/mac-smoke.sh -thornForce=knife_throw,chain_frost,laguna_blade,frostbite` (делает проверяющий).

- `KezInnateTests.InnateNeverTakesSlotOrIsOffered` сравнивал число активных слотов с `Mode.MaxActiveSlots`; с новыми способностями случайный путь доходит до Apex (+1 слот, §25 Р6) и шестой активной способности. Проверка переведена на `Build.MaxActiveSlots`.

## Риски
- Предложение всех четырёх способностей при уровне меняет выбор улучшений у забегов с прежними seed (как в Task 19 и 21); тесты, зависящие от точного выбора, уже не опираются на него.
- Вынос `ChainBounce` и `StrongestTargets` трогает Magic Missile и врождённую Kez; защищены их существующими тестами без изменения ожиданий.
- Knife на ранге 9 даёт поток снарядов: 5 ножей за ~0.63 с, до 3 попаданий каждый; пул снарядов (`Rules.MaxProjectiles`) не переполняется (нож живёт 1 с, одновременно не больше 10 ножей), это проверяет бот-забег без диагностики `pool:projectiles`.
