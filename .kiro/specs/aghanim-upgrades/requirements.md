# Требования: Aghanim — скипетр и осколок (Task 27)

## Введение
Сейчас скипетр и осколок в игре не работают. Элитные враги роняют подбор `scepter` (шанс 1), но `Run.Drop` его не создаёт и пишет диагностику `pickup:scepter` (`TreasureVariant` 2 не разобран). Ни одна из 17 способностей не знает об Aghanim. Каждая из Task 17–25 отложила свой эффект сюда (Mechanics §15, §26, §28–§32, «Не сделано»).

Задача:
- получение скипетра из элитных врагов и осколка по клиенту;
- отдельный экран выбора способности для улучшения;
- эффект для каждой способности с `m_bIsScepterUpgradeable` / `m_bIsShardUpgradeable`;
- никакое улучшение не начисляется дважды.

Иконки Aghanim на слотах HUD остаются за Task 35 (финальный HUD), частицы — за Task 37, звуки `survivors.acquire_scepter` / `acquire_shard` — за Task 36.

### Источники
- **Д** `survivors_powerups.json`:
  - у 17 активных способностей `m_bIsScepterUpgradeable = true` и `m_sLocScepterAbilityDesc`;
  - `m_bIsShardUpgradeable = true` только у `magic_missile`, у `chain_frost` поле равно `false`;
  - поля Aghanim: `magic_missile` — `m_flScepterDamageIncreasePerEnemyKilled` 0.01 и `m_flShardDamageMultiplier` 0.25; `counter_helix` — `m_flChanceToCounter` 1 и `m_flDelayBetweenCounters` 0.15; `stampede` — `m_flMovementSpeedPercentDamageMultiplier` 0.1; `frostbite` — `m_flScepterExplodeRadius` 200; `land_mine` — `m_flScepterVacuumRadius` 150, `m_flScepterVacuumDistance` 25, частицы `m_sStasisTrap*` и `m_sRemoteMine*`; `mortimer_kisses` — `m_flSpawnPickupOnKillPercent` 0.05, `m_unSpawnPickupOnKillID` 11 (`cookie`, +10 HP) и `m_flScepterLaunchDistance` 1000;
  - `m_scepterUpgradeDefinition`: у `rot` — `FreezeDuration` +0.5, `FreezeStrength` +0.3, `Duration` +5, `DurationEffect` +1; у `knife_throw` — `ProjectileHitCount` +99.
- **Д** `survivors_pickups.json`: `scepter` (ID 5, `m_bRewardsTreasure`, `m_nTreasureVariant` 2), `shard` (ID 4, `m_bRewardsTreasure`, `m_nTreasureVariant` 1), `treasure` (вариант 0).
- **Д** `survivors_enemies.json`: `scepter` с шансом 1 в `m_vecPickupChances` у `resurrector`, `skeleton_archer`, `absorber` и `granite_golem_small`. Эти враги появляются только в элитных комнатах: 1 воскрешающий, 5 лучников, 1 поглотитель, голем 1 → 3 → 18 малых. Ссылок на `shard` в данных нет.
- **Д** (локализация, `Research/FullGame/Descriptions.md`): тексты Aghanim для 17 способностей (таблица в design §5), `DOTA_Survivors_OptionalBossFightsDescription` «Побеждайте мини-боссов, чтобы получить особые улучшения для способностей». Патч-ноты 1.1: мины — «уменьшает перезарядку на 20%, шанс стазисного капкана (меньше урона, оглушает) или радиомины (больше урона, взрывается мгновенно)»; Counter Helix — скрытая перезарядка 0.05 → 0.15; Magic Missile — бонус за убийство 0.04 → 0.035 (в данных 0.01).
- **К** (`Research/Decoded/client/strings.json`): `m_bScepterUpgraded`, `m_bShardUpgraded`, `m_sLinkedScepterAbility`, `m_sLinkedShardAbility`, `m_sLocShardAbilityDesc`, `m_vecScepterTooltipAttributes`, журнал `Survivors ScepterReceived PowerUp=%d` / `ShardReceived PowerUp=%d`, читы `DOTASurvivorsCheatsGetScepter` / `GetShard`.
- **К** (panorama `bp_popup_crownfall_encounter_survivors.vxml`/`.vcss`): карточка `PowerUpChoice` в общем контейнере выбора `PlayerLevelUpChoiceContainer` с классами `ScepterUpgrade` / `ShardUpgrade` и надписью `DOTA_AbilityTooltip_Aghs_Scepter` / `_Shard`. Слот HUD тоже получает классы `ScepterUpgrade` / `ShardUpgrade`, и у слота могут быть оба сразу.
- Не найдено (Mechanics §12.5, §16): как выбирается способность, сколько скипетров даёт комната, откуда берётся осколок.

## Требования

### Т1. Получение скипетра
1. КОГДА умирает элитный враг с `scepter` в `m_vecPickupChances`, СИСТЕМА ДОЛЖНА выдать скипетр по правилу выдачи (design §2, вопрос В1).
2. КОГДА герой подбирает скипетр, СИСТЕМА ДОЛЖНА добавить один ожидающий выбор Aghanim и не открывать выбор предмета.
3. СИСТЕМА ДОЛЖНА больше не писать диагностику `pickup:scepter` и `pickup:shard`.

### Т2. Получение осколка
1. КОГДА герой подбирает осколок, СИСТЕМА ДОЛЖНА добавить один ожидающий выбор осколка.
2. СИСТЕМА ДОЛЖНА выдавать осколок по правилу, утверждённому владельцем (design §2, вопрос В2). Если источника нет, осколок доступен через отладку (`-thornForce`), а отличие записано в `Differences.md`.

### Т3. Экран выбора Aghanim
1. КОГДА открывается выбор скипетра, СИСТЕМА ДОЛЖНА предложить до `m_nLevelUpChoices` разных активных способностей героя, у которых есть `m_bIsScepterUpgradeable`, ещё нет скипетра и есть обработчик. Для осколка правило то же, с `m_bIsShardUpgradeable`.
2. Карточка ДОЛЖНА иметь вид `Scepter` или `Shard` и показывать заголовок «Скипетр Аганима» / «Осколок Аганима», название способности и текст `LocScepterAbilityDesc` с подставленными числами из данных.
3. ЕСЛИ подходящих способностей нет, ТО СИСТЕМА ДОЛЖНА выдать запасную награду по правилу §33 Р9 (+10 золота). Скипетр при этом тратится (вопрос В3).
4. Порядок ожидающих выборов: скипетр и осколок, затем предмет, затем уровень. Пауза таймеров та же, что у выбора при уровне.
5. Переброс на экране Aghanim работает по общим правилам (`MaxRerolls`).

### Т4. Без двойного начисления
1. Способность получает скипетр не больше одного раза и осколок не больше одного раза. Повторный вызов `Build.UpgradeScepter` / `UpgradeShard` ДОЛЖЕН вернуть ложь и ничего не менять.
2. Один подбор даёт ровно один выбор. Выбор одной карточки тратит ровно один ожидающий скипетр или осколок.
3. Эффект хранится флагом (`OwnedPowerUp.Scepter`, `Shard`; аналоги `m_bScepterUpgraded`, `m_bShardUpgraded` клиента, **К**). Атрибуты Aghanim пересчитываются в `Build.Recalculate` из флага и не накапливаются: Annihilation даёт ровно +1 жизнь, Knife Throw ровно +99 к `ProjectileHitCount`.
4. Новые ранги и малые улучшения после скипетра флаг не сбрасывают.

### Т5. Эффекты скипетра (по способностям, design §5)
Для каждой из 17 способностей: КОГДА у способности есть скипетр, СИСТЕМА ДОЛЖНА применять эффект из design §5. БЕЗ скипетра поведение ДОЛЖНО остаться прежним (все существующие тесты способностей проходят без изменения ожиданий).

### Т6. Эффект осколка
КОГДА у Magic Missile есть осколок, урон снаряда ДОЛЖЕН умножаться на `1 + m_flShardDamageMultiplier` (design §5, Р).

### Т7. Отображение
1. Карточка выбора при уровне для способности со скипетром ДОЛЖНА показывать строку «Скипетр Аганима» (ТЗ_Полная_игра_Unity.md §6.3: интерфейс показывает полученные эффекты). Значки на слотах HUD — Task 35.
2. Текст карточки собирает чистая функция `ChoiceText`. Её проверяют EditMode-тесты.

### Т8. Отладка и smoke
`-thornForce` ДОЛЖЕН принимать скипетр и осколок (`arcane_bolt+scepter`, `magic_missile+shard`). Smoke с этими ключами проходит, у способностей есть попадания. `-thornElite` открывает выбор скипетра, smoke выбирает первую карточку.

### Т9. Ограничения
Шаг без выделений памяти после прогрева (`StepsDoNotAllocateAfterWarmUp`). Каждый эффект живёт в файле своей способности или в общем помощнике. В `Run.cs` добавляется не больше нескольких строк вызова, механизм — в новом `Run.Aghanim.cs`. Запросы к сетке идут в свои буферы. Новые события несут `SpawnId`. Все решения с меткой **Р** записаны в `Mechanics.md` (§34), отличия — в `Differences.md`.
