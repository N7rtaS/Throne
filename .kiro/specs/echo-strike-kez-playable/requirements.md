# Требования: Echo Strike и врождённая Kez, Kez играбелен (Task 19)

## Введение
Kez (`survivors_heroes.json`: стартовая 12 `echo_strike`, врождённая 502 `kez_innate`) сейчас не играбелен: `CSurvivorsPowerUpDefinition_EchoStrike` и `CSurvivorsPowerUpDefinition_InstantAttack` не реализованы, в забеге есть диагностика `ability:CSurvivorsPowerUpDefinition_InstantAttack`, в меню карточка «Кез (в разработке)» недоступна. Задача добавляет обе способности в реестр обработчиков (`AbilityFactory`), бонус скорости врождённой и делает Kez выбираемым в меню (облик — капсула до Task 34).

Источники: `Research/Json/survivors_powerups.json` (**Д**), `Research/FullGame/Descriptions.md` (локализация, **Д** в том, что сказано прямо), `Research/Decoded/client/strings.json` (**К**: `CSurvivorsPowerUp_EchoStrike`, `CSurvivorsPowerUp_InstantAttack`, `m_vCachedFacingDirection`, `m_vecQueuedDamageTicks`, звуки `survivors.echo_strike.cast/tick`, `survivors.kez_innate.cast`, `survivors.kez.innate.proc/crit`), Mechanics §3, §24 Р13, §25 Р3–Р5.

Данные Echo Strike (ранг 1, **Д**): `Damage_Physical` 10, `Cooldown` 2.1, `KnockbackDistance` 12, `InitialTickDelay` 0.1, `DamageTicks` 1, `Length` 720, `Width` 420, `TimeBetweenTicks` 1; `m_eOrigin` `PLAYER_ORIGIN`, `m_bUseFacingDirection` true, `m_flWarmupEffectTime` 0.85; 9 авторских записей (`DamageTicks`, `TimeBetweenTicks`, `Damage_Physical`, `AoEIncrease`, `Width`).

Данные `kez_innate` (**Д**): `MovementSpeed` 20, `TargetCount` 1, `Damage_Magical` 0, `Cooldown` 6, `Range` 1500, `Radius` 180, `VulnerabilityDamagePercent` 2 (атрибут процентный: «200%»), `VulnerabilityDuration` 5.5, `VulnerabilityStunOnHitDuration` 1; `m_eTargeting` `STRONGEST_TARGET_IN_RANGE`.

## Требования

### Т1. Стартовый набор Kez
1.1 КОГДА начинается забег Kez, СИСТЕМА ДОЛЖНА выдать ему `echo_strike` ранга 1 и `kez_innate` из `m_vecStartingPowerUps` и `m_vecInnatePowerUps`, и у обоих SHALL быть обработчики (`EchoStrike`, `InstantAttack`).
1.2 КОГДА начинается забег Kez, СИСТЕМА ДОЛЖНА не выдавать диагностик `ability:*` и `damage-type:*`.
Критерий: тест `KezStartsWithEchoStrikeFromData`.

### Т2. Echo Strike: область и урон
2.1 КОГДА перезарядка Echo Strike истекла и в его прямоугольнике есть поражаемый враг, СИСТЕМА ДОЛЖНА применить способность: запомнить положение героя и направление `Run.Facing` и поставить в очередь `DamageTicks` ударов, первый через `InitialTickDelay`, следующие через `TimeBetweenTicks`; перезарядка начинается заново.
2.2 КОГДА срабатывает удар, СИСТЕМА ДОЛЖНА нанести физический урон `AbilityBase.Damage` каждому поражаемому врагу, круг которого пересекает прямоугольник длиной `Length × (1 + AoEIncrease)` и шириной `Width × (1 + AoEIncrease)`, начинающийся в запомненной точке и вытянутый вдоль запомненного направления.
2.3 КОГДА удар попал во врага, СИСТЕМА ДОЛЖНА оттолкнуть его вдоль направления удара на `AbilityBase.KnockbackDistance`.
2.4 КОГДА срабатывают второй и следующие удары одного применения, СИСТЕМА ДОЛЖНА бить по той же области, даже если герой ушёл или повернулся.
2.5 КОГДА растёт ранг Echo Strike, СИСТЕМА ДОЛЖНА брать урон, число ударов, интервал, ширину и `AoEIncrease` из авторских записей со смещением `Rules.AuthoredUpgradeOffset` (ранг 2: 2 удара через 0.8 с; ранг 9: урон 50, 4 удара через 0.4 с, ширина 520, `AoEIncrease` 0.65).
2.6 КОГДА срабатывает удар, СИСТЕМА ДОЛЖНА выдать событие `AbilityStrike` с центром области, направлением, длиной и шириной (для отображения и будущего звука `survivors.echo_strike.tick`).
2.7 КОГДА идёт пауза, выбор улучшения, падение героя или забег окончен, СИСТЕМА ДОЛЖНА не продвигать перезарядку и очередь ударов (удары, уже стоящие в очереди, не срабатывают на паузе).
2.8 КОГДА шаг идёт после прогрева, СИСТЕМА ДОЛЖНА не выделять управляемую память (очередь ударов — массив фиксированного размера).
Критерии: тесты на урон по данным, попадание внутри и промах вне прямоугольника, ширину, `AoEIncrease`, повтор по той же области, ранги, отталкивание, паузу, `StepsDoNotAllocateAfterWarmUp` для Kez.

### Т3. Врождённая Kez (`InstantAttack`, «Слабое место»)
3.1 КОГДА перезарядка `kez_innate` (`Cooldown` 6) истекла и в `Range` (1500) от героя есть поражаемый враг, СИСТЕМА ДОЛЖНА выбрать `TargetCount` самых сильных врагов (`STRONGEST_TARGET_IN_RANGE`) и наложить уязвимость на каждого из них и на всех поражаемых врагов в `Radius × (1 + AoEIncrease)` (180) вокруг каждого.
3.2 КОГДА накладывается уязвимость, СИСТЕМА ДОЛЖНА передать в `Run.MarkVulnerable` множитель `VulnerabilityDamagePercent` (2), оглушение `VulnerabilityStunOnHitDuration` (1 с) и длительность `VulnerabilityDuration` (5.5 с), длительности — с `DurationExtension` (§25 Р5).
3.3 КОГДА помеченного врага бьёт физический не периодический удар (Echo Strike), СИСТЕМА ДОЛЖНА сделать удар критическим ×2, снять метку и оглушить врага (правило §24 Р13 без изменений).
3.4 КОГДА урон `InstantAttack` равен 0 (у `kez_innate` `Damage_Magical` 0), СИСТЕМА ДОЛЖНА не вызывать `DealDamage` и не выдавать событий урона.
3.5 КОГДА у героя есть `kez_innate`, СИСТЕМА ДОЛЖНА прибавить её `MovementSpeed` (20) к скорости героя: Kez бегает со скоростью 300; остальные атрибуты врождённой остаются в её `Local` и не попадают в `Build.Global`.
3.6 КОГДА определение класса `InstantAttack` задаёт другой `m_eTargeting` (Annihilation, `RANDOM_TARGET_IN_RANGE`), СИСТЕМА ДОЛЖНА считать его нереализованным: не предлагать при выборе улучшения (`Run.IsImplemented` = false) до Task 23.
Критерии: тесты на выбор самой сильной цели, радиус метки, параметры метки по данным, крит и оглушение от Echo Strike по метке, отсутствие урона, скорость 300, Annihilation не предлагается.

### Т4. Kez играбелен
4.1 КОГДА игрок открывает экран героя, СИСТЕМА ДОЛЖНА показывать карточку «Кез» доступной (`MobileGame.PlayableHeroes` = dragonus, kez); Shen остаётся «в разработке» до Task 20.
4.2 КОГДА начинается забег Kez, СИСТЕМА ДОЛЖНА показывать героя капсулой (модели Kez нет до Task 34), а забег Dragonus — моделью Dragonus.
4.3 КОГДА срабатывает удар Echo Strike, СИСТЕМА ДОЛЖНА кратко показывать область удара плоским прямоугольником; КОГДА враг помечен уязвимостью, СИСТЕМА ДОЛЖНА показывать под ним кольцо (временная замена `kez_katana_echo_strike_survivors.vpcf` и `kez_vulnerable_marker.vpcf` до Task 37).
4.4 КОГДА smoke запущен с `-thornHero=kez`, СИСТЕМА ДОЛЖНА пройти экраны с выбором Kez и завершиться `THORN_MOBILE_SMOKE` с kills > 0 без необработанных исключений.

### Т5. Бот-забег и регрессия
5.1 КОГДА бот играет Kez 120 с на нескольких seed, СИСТЕМА ДОЛЖНА работать без исключений и диагностик `ability:*`, Echo Strike SHALL наносить урон и убивать врагов, а часть ударов SHALL быть критическими от уязвимости.
5.2 КОГДА запускаются существующие тесты, СИСТЕМА ДОЛЖНА проходить их все; тесты Task 3, которые фиксировали «Kez бегает 280» и «`kez_innate` не реализован», заменяются новыми ожиданиями по Т3.5 и Т1.

## Вне объёма
Aghanim Echo Strike (удар назад) — Task 27. Малые улучшения после максимума — Task 26. Звук — Task 36, финальные эффекты — Task 37, модель Kez — Task 34, экран героя с описанием способностей — Task 33. Annihilation — Task 23.
