# Дизайн: глобальные атрибуты, пассивки, жизни (Task 18)

## 1. Что найдено в источниках
Порядок поиска из `product.md`: `Research/Json` → `Mechanics.md` → локализация → panorama → строки клиента.

| Что | Источник | Метка |
|---|---|---|
| 26 пассивок: 16 обычных (`yasha`, `blade_mail`, `kaya`, `crystalys`, `reaver`, `ring_of_tarrasque`, `tome_of_knowledge`, `octarine_core`, `bloodstone`, `world_chasm_artifact`, `apex`, `echo_stone`, `sange`, `phylactery`, `demon_edge`, `timeless_relic`), 9 рецептов (`sange_and_yasha`, `kaya_and_sange`, `yasha_and_kaya`, `heart_of_tarrasque`, `daedalus`, `khanda`, `sny_trident`, `kns_trident`, `ynk_trident`) и `pile_of_coal` (не выпадает) | `survivors_powerups.json` | **Д** |
| У всех пассивок, кроме Crystalys, бонус задан одним малым улучшением `RARITY_COMMON`; базовых атрибутов нет | то же | **Д** |
| Crystalys: `m_nMaxLevel` 3, три авторские записи: шанс 0.3 + множитель 1.6; множитель +0.35; множитель +0.35. Малое улучшение — множитель +0.35 | то же | **Д** |
| Bloodstone `AoEIncrease` +0.1 «+X к радиусу способностей», процентный; Echo Strike и Swashbuckle прибавляют `AoEIncrease` своими авторскими записями | данные, `survivors_attributes_russian.txt` | **Д** |
| Sange `KnockbackDistanceMultiplier` +0.2 «+X% к дальн. отталкивания», процентный; рецепты 0.8 и 1.0 | то же | **Д** |
| Timeless Relic `DurationExtension` +1 «+X сек. к длительности эффектов», ранг до 2 | то же | **Д** |
| Apex `ActiveAbilitySlots` +1 «+X к ячейкам способностей», ранг 1 | то же | **Д** |
| `Lives` «Эгида», «+X к числу Эгид»; источник в фазе 1 — постоянная прокачка (1 и 2 за 10 000 / 30 000) и Aghanim Annihilation «дополнительная жизнь» (Task 27). У героев `Lives` нет | `survivors_attributes.json`, `Descriptions.md` | **Д** |
| `m_flPlayerReviveTimer` 2.7, `m_sReviveEffect` `particles/events/crownfall/revive.vpcf` | `survivors_game_modes.json` | **Д** |
| `Luck` 0.05 у всех трёх героев, «{f:2}% к удаче»; `m_luckyLootTable`: коллекция с шансом 1 — `gold_coin` (ID 6, 1 золото) 1–3; коллекция с шансом 0.2 — `gold_coin` 1–3 и `gold_bag` (ID 7, 10 золота) 1–2 | данные, локализация | **Д** |
| `k_eSurvivorsAttribute_Lives`, `m_nLives`, `m_nFirstTimeBonusLives`, `m_nInvulnerabilityDuration`, `m_bWaitingForInvulnerability`, `m_sInvulnerableParticle`, `m_luckyLootTable`, `m_strLootList` | `Research/Decoded/client/strings.json` | **К** |

Алгоритмов возрождения, неуязвимости и удачи в источниках нет. Всё ниже без **Д** — проектные решения **Р**.

## 2. Архитектура

```
Run.cs                    SupportedHeroAttributes (+14), IsImplemented (+авторские записи), Tick: 3–4 строки вызова,
                          DropRewards: бросок удачи, общий DropCollections для m_fullLootTable и m_luckyLootTable
Run.Lives.cs (новый)      partial Run: LivesLeft, Downed, ReviveTimer, HeroDown(), UpdateRevive(dt)
Run.Combat.cs             DamageHero: «Health <= 0» → HeroDown(); без урона в падении
Build/Build.cs            Recalculate: авторские записи пассивок; MaxActiveSlots с ActiveAbilitySlots
Abilities/AbilityBase.cs  Area(run, attr), KnockbackDistance(run), EffectDuration(run, attr)
Abilities/ArcaneBolt.cs   отталкивание через KnockbackDistance
Abilities/MagicMissile.cs радиус через Area, оглушение через EffectDuration
Stats/Attributes.cs       Attr.AoEIncrease, ActiveAbilitySlots, KnockbackDistanceMultiplier, DurationExtension
Content/Definitions.cs    ModeDef.LuckyLoot (LootCollectionDef[])
Content/GameData.cs       чтение m_luckyLootTable тем же разбором, что m_fullLootTable
World/Entities.cs         SimEventKind.HeroDowned, HeroRevived (в конец перечисления)
Tools/audit.py            passive(): учитывать глобальные атрибуты авторских записей
```

Unity-слой (`Scripts/Mobile`, сцена, генератор) не меняется. Падение на экране пока выглядит как стоящий герой с 0 HP; отображение Эгид и эффект возрождения — Task 35 и 37.

## 3. Правила (Mechanics §25)

- **Р1. Ранги пассивки с авторскими записями.** Если у пассивки есть `m_vecAuthoredUpgradeChoices`, ранг r даёт глобальные атрибуты записей 0..r−1 (смещение 0), малые улучшения на рангах не применяются. Это только Crystalys: у пассивок нет базовых атрибутов, и при смещении 1, как у активных (§4), ранг 1 ничего бы не давал, а запись 2 не применялась бы никогда. Малые улучшения после максимума — Task 26.
- **Р2. Ранги остальных пассивок** — первое малое улучшение × ранг (правило прототипа, теперь с меткой). Рецепт — малое улучшение один раз, компоненты поглощены (§12.4, без изменений).
- **Р3. AoEIncrease.** Радиус области = `Radius × (1 + AoEIncrease)`, где `AoEIncrease` — своё значение способности плюс героя (как любой `Stat`). Множитель, а не прибавка: атрибут процентный. Не действует на `ProjectileRadius`, `PickupRadius` и отталкивание при уровне: в описании сказано «радиус способностей». Будущие радиусы областей (`FreezeSplashRadius` и др.) идут через тот же `AbilityBase.Area`.
- **Р4. KnockbackDistanceMultiplier.** Отталкивание способности = `KnockbackDistance × (1 + KnockbackDistanceMultiplier)`, затем сопротивление отталкиванию врага, как раньше. Отталкивание при уровне (`m_flLevelUpKnockbackDistance`) не меняется.
- **Р5. DurationExtension.** К длительности статуса, который способность героя накладывает на врага, прибавляется `DurationExtension` секунд, если своя длительность больше 0, до сопротивления статусам (§24 Р8). Статусы: оглушение, замедление, заморозка, периодический урон (больше срабатываний), ослабление, усиление, уязвимость. Собственная длительность способностей (`Duration` у Stampede и Spirits) пока не продлевается; решение — в Task 23 и 25.
- **Р6. ActiveAbilitySlots.** Активных слотов `m_nMaxActiveSlots + floor(ActiveAbilitySlots)`. Apex занимает пассивный слот.
- **Р7. Число жизней.** Осталось `max(0, floor(Lives) − использованные)`. Рост `Lives` посреди забега (Aghanim Annihilation, Task 27) сразу даёт жизнь.
- **Р8. Падение.** Смертельный удар при оставшейся жизни: жизнь списывается, здоровье 0, забег продолжается, герой падает на `m_flPlayerReviveTimer` (2.7 с, **Д**). В падении герой не получает урона, не двигается, не делает рывок (запрошенный рывок сбрасывается), не восстанавливает здоровье, способности не стреляют, предметы не притягиваются и не подбираются. Уже выпущенные снаряды летят. Заряды рывка продолжают восстанавливаться. Враги, спавн, статусы, элитные комнаты, Империя и таймер забега идут дальше. Похоже на Эгиду Dota: смерть, задержка, возрождение на месте.
- **Р9. Возрождение.** После таймера — полное здоровье на том же месте, `contactTimer` = `ContactDamageInterval` (0.65 с до первого касания). Отдельной неуязвимости после возрождения нет (см. вопрос 2 в §6).
- **Р10. Таймер падения** идёт только в шаге: пауза, выбор улучшения и конец забега его останавливают.
- **Р11. Удача.** После наград самого врага (`m_vecPickupChances`, `m_vecLootTable`, `m_fullLootTable`) при `Luck > 0` один бросок `Rng.Chance(min(1, Luck))`; успех — счастливая добыча по алгоритму `m_fullLootTable` (§9). Бросают все враги с наградами, включая вазы и ящики; Империя наград не даёт. Ожидание при 0.05: примерно 0.27 золота на убийство (5 % × (2 + 0.2 × (2 + 15))), сотни золота за забег — это согласуется с ценами постоянной прокачки 250–30 000.
- **Р12. Последовательность `Rng`.** Базовая удача 0.05 добавляет один бросок на каждое убийство, поэтому забеги с прежними seed после первого убийства идут иначе. Правило «шанс 0 — без броска» (§24 Р3) сохраняется: при `Luck ≤ 0` броска нет.

## 4. Детали реализации

### Build
- `Recalculate`, ветка пассивки: `if (def.Authored.Length > 0) for u < min(rank, Authored.Length): Global.Add(Authored[u].Global)` иначе прежнее `Minor[0] × rank`.
- `public int MaxActiveSlots => mode.MaxActiveSlots + (int)Global[Attr.ActiveAbilitySlots];` — в `CanUpgrade` вместо `mode.MaxActiveSlots`.
- `Run.IsImplemented` для пассивки проверяет `Minor[*].Global` и `Authored[*].Global`; пассивка без единого улучшения не реализована, как раньше.

### AbilityBase
```csharp
public float Area(Run run, int attribute) => Stat(run, attribute) * (1 + Stat(run, Attr.AoEIncrease));
public float KnockbackDistance(Run run) => Stat(run, Attr.KnockbackDistance) * (1 + Stat(run, Attr.KnockbackDistanceMultiplier));
public float EffectDuration(Run run, int attribute) { float d = Stat(run, attribute); return d > 0 ? d + run.Build.Global[Attr.DurationExtension] : d; }
```
`MagicMissile.ImpactRadius` → `Area(run, Attr.Radius)`, `StunDuration` → `EffectDuration(run, Attr.StunDuration)`; `ArcaneBolt.Fire`: `shot.Knockback = KnockbackDistance(run)`. Без предметов значения прежние (множитель 1, прибавка 0).

### Жизни (`Run.Lives.cs`)
```csharp
public int LivesLeft => Math.Max(0, (int)Build.Global[Attr.Lives] - livesUsed);
public bool Downed => reviveTimer > 0;
public float ReviveTimer => reviveTimer;
private void HeroDown()      // из DamageHero при Health <= 0
private void UpdateRevive(float dt)
```
- `DamageHero`: в начале `|| Downed` — урона нет; при `Health <= 0` вызывается `HeroDown()`: без жизней `Finish(false)`, иначе `livesUsed++`, `reviveTimer = Mode.PlayerReviveTimer`, `pendingDash = false`, событие `HeroDowned` (значение — оставшиеся жизни). Отражение после смертельного удара не срабатывает (как в §24 Р6).
- `Tick` в `Run.cs`: `if (Downed) { UpdateRevive(dt); move = Vec2.Zero; dash = false; }` перед `UpdateHero`; цикл способностей и `UpdatePickups` под `if (!Downed)`; `Heal` ничего не делает в падении.
- `UpdateRevive`: `reviveTimer -= dt`; при `≤ 0` — `Health = MaxHealth`, `contactTimer = Rules.ContactDamageInterval`, событие `HeroRevived`.
- Если таймер меньше шага, герой возрождается в следующем шаге: при 2.7 с и шаге 1/30 — через 81 шаг.

### Удача
- `ModeDef.LuckyLoot` из `m_luckyLootTable.m_vecLootEntryCollections` — общий разбор с `m_fullLootTable` (вынести из разбора врага в функцию).
- `DropRewards`: цикл по `def.FullLoot` выносится в `DropCollections(LootCollectionDef[], Vec2)`; после него `float luck = Build.Global[Attr.Luck]; if (luck > 0 && Rng.Chance(Math.Min(1, luck))) DropCollections(Mode.LuckyLoot, position);`.
- Монеты и мешки уже обрабатывает `Drop` (`GoldAmount > 0`), подбор прибавляет `Run.Gold`. Запись золота в профиль — Task 31.

### Объём `Run.cs`
Около 10 строк: 14 атрибутов в наборе, условие авторских записей в `IsImplemented`, 3–4 строки в `Tick`, 2 строки удачи и вынесенный цикл коллекций.

## 5. Тестовая стратегия
Ожидаемые значения — из `survivors_powerups.json`, `survivors_game_modes.json` (читаются тестом из JSON через `GameData` и `Raw`, а не из `Build`) и правил выше.

Новые фикстуры:
- **`PassiveItemTests`**
  - `EveryPassiveAndRecipeIsImplemented` — 16 обычных и 9 рецептов проходят `Run.IsImplemented`; `pile_of_coal` не выпадает.
  - `PassiveAtRankOneAndMaxAddsDataValues` (`TestCaseSource` по 16 пассивкам) — прибавка к `Build.Global` на ранге 1 и максимальном равна сумме из данных (малое × ранг или авторские 0..r−1).
  - `RecipeGivesOnlyItsOwnBonuses` (`TestCaseSource` по 9 рецептам) — компоненты до максимума через `ForceUpgrade`, рецепт получен, компоненты поглощены, `Global` = база героя + врождённая + бонус рецепта. Момент сборки и выбор между конкурирующими рецептами не меняются (Task 28).
  - `CrystalysRanksFollowAuthoredUpgrades` — 0.3 / 1.6, затем 1.95 и 2.3.
  - `BladeMailFromItemReducesAndReflectsDamage` — касание зомби: урон × 0.9 на ранге 1, × 0.7 на ранге 3; отражение 1 и 3.
  - `BloodstoneWidensMagicMissileImpact` — 176 / 208; враг на расстоянии 190 от цели оглушается только с Bloodstone ранга 3.
  - `SangeLengthensArcaneBoltKnockback` — × 1.2 / × 1.6, смещение врага больше.
  - `TimelessRelicExtendsMagicMissileStun` — 2 с / 3 с; `StunTimer` врага после попадания.
  - `PhylacteryAddsToArcaneBoltDamage` — 7 + 5 и 7 + 15; `DemonEdgeAddsToPhysicalDamageFormula` — 5 и 15 в `Global[BonusPhysicalDamage]` (физических способностей пока нет).
  - `EchoStoneAddsArcaneBoltVolley` — залп +1 и +2.
  - `ApexAllowsSixthActiveAbility` — на `Build` с `implemented = _ => true`: при 5 активных шестая недоступна; с Apex доступна, седьмая — нет.
  - Перенос прототипа: `ReaverRaisesMaxHealthAndKeepsDeficit`, `HeartOfTarrasqueReplacesReaverAndRing` (+200 HP, +3 HP/с, без компонентов, дефицит сохранён), `OctarineCoreAndTomeWorkTogether` (перезарядка Arcane Bolt × 0.85 и опыт × 1.3 на ранге 3), `HealthDeficitIsKeptWhenMaxHealthGrows`.
- **`LivesTests`**: `LethalHitWithoutLivesEndsRun`, `LethalHitWithLifeDownsHero`, `DownedHeroTakesNoDamageAndDoesNotMove`, `DownedHeroAbilitiesDoNotFire`, `HeroRevivesAfterReviveTimerWithFullHealth` (`Mode.PlayerReviveTimer` = 2.7 из данных; ещё в падении на 2.6 с), `ReviveTimerStopsOnPauseAndChoice`, `LastLifeThenDefeat`, `BossKilledWhileDownedIsVictory`, `ReviveStepsDoNotAllocate`.
- **`LuckTests`**: `FullLuckDropsLuckyLootOnEveryKill`, `ZeroLuckDropsNoLuckyLoot`, `BaseLuckDropsAboutFivePercent` (цикл по seed), `LuckyLootIsReadFromMode`.
- Жизни и удача задаются в тестах через `run.Build.Global.Add(Attr.Lives/Luck, …)`, как атрибуты в `DamageTests`: источников в данных фазы 1 до Task 27 и 31 нет.
- Существующие: `KezInnateTests.ReferenceGlobal` — добавить ветку авторских записей пассивок (эталон повторяет Р1). Полный прогон `dotnet test`, `python3 -m unittest discover -s Tools/tests` (`test_audit`: `blade_mail` → не ниже `implemented`, ожидание меняется по требованию 2).

## 6. Риски и вопросы владельцу
1. **Удача (Р11).** Толкование «шанс счастливой добычи = Luck на каждое убийство» не подтверждено. Другая версия: удача увеличивает шансы обычных наград врага. Выбрана первая: таблица режима существует отдельно от наград врагов, и с ней получается разумное количество золота.
2. **Возрождение (Р8, Р9).** Выбрано «упал на 2.7 с, неуязвим и неподвижен, затем встал с полным здоровьем», без неуязвимости после. Другая версия: сразу встаёт с полным здоровьем и неуязвим 2.7 с. Если нужна защита после возрождения, можно добавить `Rules.ReviveInvulnerability` (например, 1 с).
3. **DurationExtension (Р5)** продлевает только статусы на врагах, но не `Duration` способностей.
4. **Последовательность `Rng` (Р12)** меняется во всех забегах. Существующие тесты в основном проверяют свойства по нескольким seed. Если какой-то тест держится за точный seed, это будет записано здесь, а тест не будет подогнан молча.
5. Crystalys, Demon Edge и Daedalus пока ничего не меняют в бою: физических способностей нет до Task 19+. Они всё равно попадают в выбор, потому что атрибуты реализованы, как предлагает оригинал независимо от билда.

### Итог реализации
- Смена последовательности `Rng` (Р12) не задела ни одного существующего теста: все 194 прежних теста `dotnet test` прошли без изменений ожиданий, включая `StepsDoNotAllocateAfterWarmUp` и `BossStepsDoNotAllocate`.
- Удача бросается только у врагов, у которых есть хотя бы одна своя награда (`m_vecPickupChances`, `m_vecLootTable` или `m_fullLootTable`). Без наград в данных 10 определений: `tower`, `granite_golem_large`, `granite_golem_medium`, `imperia`, `hulk_summon`, `skeleton_summon`, `ogre_summon`, `boss_tower`, `death`, `snotty`.
- При падении прерывается и текущий рывок (`dashTimer = 0`), иначе герой продолжил бы движение по инерции рывка. Рост максимального здоровья в падении здоровье не поднимает: возрождение даёт полное.
- `Run.cs` вырос на 19 строк: 4 строки атрибутов, вспомогательный `GlobalsSupported` (8 строк, проверка малых и авторских записей), 1 строка в `Tick`, 4 строки удачи и заголовок вынесенного `DropCollections`. Логика жизней — в `Run.Lives.cs`.
- `Tools/tests/test_audit.py`: число поддержанных атрибутов 20 → 34, `blade_mail`, `crystalys` и `ynk_trident` — не ниже `implemented`.
- Тесты: 55 новых (`PassiveItemTests` 39, `LivesTests` 11, `LuckTests` 5), всего 249 в `dotnet test`; 119 тестов инструментов.
