# Task 28. Все рецепты и матрица «предмет × способность» — проект

Требования — `requirements.md`. Метки: **Д** — данные, **Н** — наблюдение, **Р** — проектное решение, **К** — имя в клиенте без алгоритма.

## 0. Решения для владельца
Решение владельца 12.11.2026: все рекомендации В1–В5 приняты («дальше»). Правила записаны в `Mechanics.md` §35 (Р1–Р9), отличия — `Differences.md` Б20.

| № | Вопрос | Рекомендация | Альтернатива |
|---|---|---|---|
| В1 | Момент объединения | **Карточка выбора** «Комбинация предметов»: рецепт объединяется, когда игрок её выбрал (§1.1, panorama **Д**) | Оставить автоматическую сборку прототипа (сейчас: последний ранг компонента сразу собирает рецепт) |
| В2 | Когда предлагать рецепт | Все компоненты на `m_nMaxLevel` (правило прототипа, **Р**) | Как только все компоненты есть, на любом ранге |
| В3 | Где и с каким весом | Только в выборе предмета (сундук), вес нового пассивного предмета 0.7 × 0.6 (**Р**) | Гарантированная первая карточка сундука, пока рецепт готов |
| В4 | `DurationExtension` и врождённая Kez | Продлевать только способности с `DurationEffect` > 0 (**Р**, §3.2): уязвимость Kez перестаёт продлеваться | Оставить как есть (продлевается любой статус); тогда в правиле матрицы Kez × Timeless Relic — «меняет» |
| В5 | Аудит `pile_of_coal` | `implemented`: в данных и режиме не используется, симуляция его не выдаёт, тест `ServicePowerUpsAreNeverOffered` (**Р**, §4) | Поле `required: false` и исключение из критерия «47/47» |

Остальное ниже следует из этих решений; при другом ответе меняются §2 и §3.2.

## 1. Что известно об объединении
### 1.1. Источники
- **Д** `survivors_powerups.json`: 9 рецептов, `m_nMaxLevel` 1, `m_bIsPassive`, у каждого одно малое улучшение с глобальными атрибутами. Состав: S&Y = yasha + sange, K&S = kaya + sange, Y&K = yasha + kaya, Heart = reaver + ring, Daedalus = crystalys + demon_edge, Khanda = crystalys + phylactery, `sny_trident` = S&Y + kaya, `kns_trident` = K&S + yasha, `ynk_trident` = Y&K + sange. Все три трезубца дают одно и то же (Knockback 1, скорость 125, маг. урон 0.75).
- **Д** panorama `bp_popup_crownfall_encounter_survivors.vxml`, снипет `PowerUpChoice` (карточка выбора): метка типа `{s:powerup_type}` и контейнер `AbilityCombinationItemsContainer` со строкой `#DOTA_Survivors_CombinedItemTooltip` «Сочетает:» и картинками компонентов (`.RecipeItemImage`). Контейнер виден у карточки с классом `.ItemCombination` (`…survivors.vcss`). Типы карточки в локализации: `DOTA_Survivors_Ability` «Способность», `DOTA_Survivors_Item` «Предмет», `DOTA_Survivors_CombinedItem` «Комбинация предметов». Вывод: рецепт приходит отдельной карточкой выбора, а не собирается сам.
- **К** `m_vecTradeRecipes`, `m_bDisableTrade` рядом со строками режима (§16): назначение неизвестно.
- Не найдено: условие появления карточки, вес, список выбора. Внешний источник (hawk.live, гайд по трезубцу) пишет, что компоненты нужно довести до максимума. Источник слабый (там «уровень 5», в данных 3), поэтому В2 остаётся **Р**.
- Наблюдения в `Research/Наблюдение` и видео §13 рецептов не показывают.

### 1.2. Почему меняется правило прототипа
Сейчас `Build.CombineRecipes` вызывается из `Upgrade` и собирает рецепт в момент последнего ранга компонента. Карточка из клиента с этим не сходится: игроку нечего выбирать, а при двух готовых рецептах с общим компонентом побеждает порядок словаря данных (S&Y раньше K&S). Решение В1 убирает оба эффекта.

## 2. Объединение через выбор (В1–В3)
### 2.1. Build
- `OfferKind.Recipe` — новый вид карточки.
- `Build.CanCombine(PowerUpDef recipe)`: `recipe.IsRecipe`, `implemented(recipe)`, рецепт не получен и не поглощён, каждый компонент есть в `Owned` и `Rank == MaxLevel`. Свободный слот не проверяется: рецепт освобождает хотя бы один (§33 Р8).
- `OfferWeight(def, items: true)`: для рецепта при `CanCombine` — `NewItemWeight × PassiveItemWeight`; в выборе уровня — 0 (§33 Р1). Вид — `Recipe`.
- `Build.Combine(PowerUpDef recipe)` (используют `Choose` и отладка): компоненты удаляются из `Owned`, их ID — в `consumed`, рецепт `Acquire`, `Recalculate`, `Choices.Clear()`, `LastCombined = recipe`. Ложь без изменений, если `CanCombine` ложно.
- `Upgrade` больше не вызывает `CombineRecipes`; метод удаляется. `CanUpgrade` для рецепта по-прежнему ложь.
- Конкуренция (Т1.5): после `Combine` у второго рецепта нет компонента в `Owned`, `CanCombine` ложно. Отдельного кода не нужно.
- Трезубец (Т1.6): компонент-рецепт имеет `MaxLevel` 1 и ранг 1, правило то же.
- Статистика урона компонентов не трогается (их нет: компоненты пассивные; §24 Р16 остаётся).

### 2.2. Карточка (Т2)
`Describe` для `Recipe`: ранг 0 → 1, `Common`. Изменения считаются через `AddChanges` как суммы атрибутов (§33 Р11): для каждого компонента прибавка `−(его вклад в Global)` (по тем же правилам §25 Р1–Р2, ранг компонента), затем атрибуты рецепта `Minor[0].Global`. `ChoiceOffer.Add` уже сводит повторы атрибута в одно изменение (§33 Р10). Пример Y&K из yasha 3 + kaya 3 у Dragonus: скорость 355 → 380, множитель маг. урона 0.45 → 0.6. Новых полей в `ChoiceOffer` нет: состав берётся из `offer.Def.RecipeItems`.

### 2.3. Run
- `Run.Choose`: если выбранная карточка — `Recipe`, после `Build.Choose` событие `Emit(SimEventKind.RecipeCombined, -1, HeroPosition, recipe.Id)`. Код в `Run.Choices.cs` (метод `EmitBuildEvents()`), в `Run.cs` — одна строка вызова.
- `Run.ForceCombine(string key)` — отладка и тесты: `Build.Combine` + `OnBuildChanged` + событие. `ForceUpgrade` рецепта по-прежнему ложь.
- Выделений в шаге нет: `CanCombine` — циклы по массивам и `Owned`.

### 2.4. Представление (Т3)
- `ChoiceText.Format`: для `OfferKind.Recipe` строка типа «Комбинация предметов» вместо ранга и строка «Сочетает: A, B» (названия компонентов передаёт `GameUi` из `powerups_ru.json`). Для обычных пассивок тип не выводится (как сейчас), чтобы не трогать прочие карточки.
- Уведомление: `GameUi` получает одну строку `TMP_Text` под HUD. `MobileGame` при `RecipeCombined` ставит текст «Собран предмет: <название>» (строка собирается вне шага, один раз на событие) и таймер 3 с, который уменьшается только во время шага забега (на паузе и при выборе стоит). Чистая функция `RecipeNotice` (текст и таймер) — для EditMode-теста.
- Smoke `-thornRecipe=KEY`: после старта компоненты KEY доводятся до максимума через `ForceUpgrade` (вложенный рецепт — через `ForceCombine`), затем `Run.DebugSpawnPickup` кладёт сундук у героя. Автовыбор smoke берёт карточку рецепта, если она есть. Лог `THORN_RECIPE key=<KEY> offered=<0|1> combined=<0|1>`; `Tools/mac-smoke.sh` завершает с кодом 17, если `combined=0`. С `-thornScreenshot` снимается кадр с уведомлением.

## 3. Матрица «предмет × способность» (Т4)
### 3.1. Что это
В клиенте карточка предмета показывает строку «Даёт бонусы способностям» (`DOTA_Survivors_ItemSynergyTooltip`, `ItemAffectedAbilities`), карточка способности — «Получает бонусы от предметов» (`DOTA_Survivors_AbilitySynergyTooltip`), **Д** panorama. Алгоритм связи не найден. Матрица задачи — эта связь: какой предмет меняет какую способность. В задаче она проверяется тестом; показ на карточке — Task 35.

### 3.2. Правило (Р, выводится из данных)
Предмет меняет способность, если хотя бы один глобальный атрибут его записей (`m_vecMinorUpgradeChoices`, `m_vecAuthoredUpgradeChoices`) проходит условие по базовым атрибутам способности `B`:

| Атрибут предмета | Предметы | Условие на способность |
|---|---|---|
| `DamageMultiplier_Magical` | Kaya, K&S, Y&K, трезубцы | `B.Damage_Magical > 0` или `B.ModifierDamagePerTick > 0` |
| `BonusPhysicalDamage`, `CriticalStrike_Physical_*` | Demon Edge, Crystalys, Daedalus | `B.Damage_Physical > 0` |
| `Projectile_BonusMagicDamage` | Phylactery, Khanda | `B.Damage_Magical > 0` и `B.ProjectileAttack > 0` |
| `CriticalStrike_Projectile_*` | Khanda | `B.ProjectileAttack > 0` и прямой урон > 0 |
| `AoEIncrease` | Bloodstone | есть `Radius`, `Length`, `Width` или `FreezeSplashRadius` |
| `KnockbackDistanceMultiplier` | Sange, S&Y, K&S, трезубцы | `B.KnockbackDistance > 0` |
| `DurationExtension` | Timeless Relic | `B.DurationEffect > 0` (В4) |
| `CooldownReductionMultiplier` | Octarine Core | `B.Cooldown > 0` |
| `TargetCount` | Echo Stone | `TargetCount` есть в `B` |
| `MovementSpeed`, `MaxHP`, `HPRegen`, `Armor`, `DamageReflection`, `ExpMultiplier`, `PickupRadius`, `ActiveAbilitySlots` | прочие | никакую (атрибуты героя; Snotty Bat берёт только свой `PickupRadius`, §32 Р16) |

Основание для В4. `DurationEffect` (значение 1) стоит у 7 способностей и добавляется записью скипетра Rot вместе с `Duration` и `FreezeDuration`; определения атрибута в `survivors_attributes.json` нет, как и у маркера `ProjectileAttack` («Атаки считаются снарядами»). Это похоже на признак «способность получает `DurationExtension`». Land Mine уже следует ему (§34 Р21). Сейчас `AbilityBase.EffectDuration` продлевает любой статус, и врождённая Kez (без `DurationEffect`) получает +2 с к уязвимости. Исправление: `EffectDuration` прибавляет `DurationExtension`, только если `Stat(DurationEffect) > 0`. Врождённая Shen (`MagicWeakening`, глобальные атрибуты героя, не способность) не меняется.

Итог по данным (● — меняет, 166 из 450):

| Предмет | MM | AB | ES | SW | ST | CH | RO | AN | CF | FB | LB | LM | KT | TR | SP | MK | SB | KZ |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `yasha`, `blade_mail`, `reaver`, `ring_of_tarrasque`, `tome_of_knowledge`, `world_chasm_artifact`, `apex`, `heart_of_tarrasque` | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · | · |
| `kaya`, `yasha_and_kaya` | ● | ● | · | · | · | · | ● | · | ● | ● | ● | · | · | · | ● | ● | · | · |
| `crystalys`, `demon_edge`, `daedalus` | · | · | ● | ● | ● | ● | · | ● | · | · | · | ● | ● | ● | · | · | ● | · |
| `octarine_core` | ● | ● | ● | ● | ● | ● | ● | ● | ● | ● | ● | ● | ● | ● | ● | ● | ● | ● |
| `bloodstone` | ● | · | ● | ● | ● | ● | ● | · | ● | · | · | ● | · | · | ● | ● | ● | ● |
| `echo_stone` | ● | ● | · | · | · | · | · | ● | ● | ● | ● | · | ● | ● | ● | ● | · | ● |
| `sange`, `sange_and_yasha` | · | ● | ● | ● | ● | ● | · | · | · | · | · | · | ● | · | · | · | · | · |
| `kaya_and_sange`, `sny_trident`, `kns_trident`, `ynk_trident` | ● | ● | ● | ● | ● | ● | ● | · | ● | ● | ● | · | ● | · | ● | ● | · | · |
| `phylactery` | ● | ● | · | · | · | · | · | · | ● | · | ● | · | · | · | · | · | · | · |
| `khanda` | ● | ● | · | · | · | · | · | ● | ● | · | ● | · | ● | ● | · | · | · | · |
| `timeless_relic` | ● | · | · | · | ● | · | · | · | ● | ● | · | · | · | ● | ● | · | ● | · |

MM magic_missile, AB arcane_bolt, ES echo_strike, SW swashbuckle, ST stampede, CH counter_helix, RO rot, AN annihilation, CF chain_frost, FB frostbite, LB laguna_blade, LM land_mine, KT knife_throw, TR track, SP spirits, MK mortimer_kisses, SB snotty_bat, KZ kez_innate.

Тест вычисляет таблицу сам из сырого JSON (`PowerUpDef.Raw`), таблица здесь — для чтения.

### 3.3. Проверка поведением (отпечаток боя)
`ItemAbilityMatrixTests.ItemChangesAbilityExactlyWhenTheMatrixSaysSo(item, ability)`, `TestCaseSource` на 450 пар.
- Герой Kez (на нём есть `kez_innate`), `DebugNoSpawns`, `DebugInvulnerable`, seed 7. У всех способностей, кроме проверяемой, `Ability = null`. Проверяемая активная выдаётся `ForceUpgrade` рангом 1.
- Предмет: пассивка — `ForceUpgrade` до `m_nMaxLevel`; рецепт — компоненты до максимума и `ForceCombine` (трезубец — через свой рецепт).
- Мишени: 60 зомби по спирали с золотым углом на расстояниях 120…1400 от героя, здоровье 1e9 (никто не умирает, наград и опыта нет). Позиции проверяются на проходимость `NavGrid`; сдвиг детерминирован.
- Длительность: `max(30, 2.2 × Cooldown + 3, min(8 × Cooldown, 90))` с базовой способности (Annihilation 135 с, Track 80 с, Stampede 90 с). Минимум 30 с и 8 применений нужны, чтобы шанс крита 0.3 проявился (Track бьёт раз в 10 с). Без выбора уровня (опыта нет).
- Отпечаток: снятое здоровье всех мишеней и его интеграл по времени, число ударов и повреждённых мишеней, сумма координат мишеней в конце, интегралы по шагам: оглушение, замедление, заморозка, ослабление, усиление, уязвимость к снарядам, уязвимость, число периодических эффектов, шаги набега Stampede; события `EnemyStunned`, `ProjectileFired`, `AbilityStrike`, `AbilityBurst`, `GroundObjectSpawned`, `CompanionSpawned` и критические удары — с весом номера шага (ловят сдвиг перезарядки).
- Итог реализации: базовый прогон кэшируется на способность; 453 теста матрицы идут ~1 мин 50 с. Матрица нашла одно расхождение — врождённая Kez × Timeless Relic, исправлено по В4.
- «Не меняет» — отпечатки равны точно (симуляция детерминирована, бросков `Rng` от таких предметов нет). «Меняет» — хотя бы одна величина отличается.
- Время прогона оценивается в 1–2 мин (450 прогонов, большинство короче 30 с). Если выйдет дольше 3 мин — базовый прогон кэшируется на способность (18 вместо 450) и мишеней становится 52 (больше 50 целей Annihilation).

Матрица ловит способность, которая не читает атрибут через `AbilityBase` (`Area`, `KnockbackDistance`, `EffectDuration`, `Damage`, `Cooldown`), и предмет, который действует там, где не должен. Расхождение исправляется в коде способности или выносится владельцу (Т4.5).

## 4. Аудит (Т5)
- `vengeful_spirit_innate`: `Attr.MagicDamageWeakensPhysicalDamageResistance_Percentage` и `_Duration` добавляются в `Run.SupportedHeroAttributes`, их читает `Status/MagicWeakening.cs`. Это меняет только аудит: `IsImplemented` смотрит этот набор лишь для пассивок.
- `gold` (`service:gold`): `implemented`, если `Rules.cs` называет ключ в `FallbackPowerUp` (аудит читает строку `public string FallbackPowerUp = "<ключ>";`). Тесты есть → `verified`.
- `pile_of_coal` (`service:not-rollable`, недостижим из режима): `implemented` — правильное поведение «никогда не выдаётся», если `m_bRollable = false` и на ключ нет ссылки из режима (В5). Тесты `ServicePowerUpsAreNeverOffered` и др. → `verified`.
- Рецепты, `world_chasm_artifact`, `skywrath_mage_innate`: тесты с ключом в `[TestCase("…")]` (аудит не видит ключи в массивах полей класса): `RecipeIsOfferedOnlyWithItsComponentsAtMax`, `PassiveAtRankOneAndMaxAddsDataValues` переводится с `TestCaseSource` на `[TestCase]` с теми же 16 ключами, `DragonusInnateAddsTargetCountAndLowersMaxHealth`.
- Правило уровня рецепта в аудите остаётся (компоненты не ниже `implemented` и атрибуты поддержаны).
- `Tools/tests/test_audit*.py`: тесты на `gold` по `FallbackPowerUp` и на `pile_of_coal`.

## 5. Mechanics и Differences
- `Mechanics.md` §12 п. 4 и §16 §12.4: ссылка на новый раздел. Новый §35 «Рецепты и матрица (Task 28)»: источники §1.1, правила Р1 (карточка, В1), Р2 (условие, В2), Р3 (вес и список, В3), Р4 (конкуренция и трезубцы), Р5 (карточка и текст), Р6 (уведомление), Р7 (матрица, §3.2), Р8 (`DurationEffect`, В4). В §25 Р2 и Р5 — ссылки на §35.
- `Differences.md`: Б20 «Уведомление о собранном предмете — строка текста; картинок компонентов на карточке нет (Task 35)». Если владелец выберет альтернативу В1 — запись А «рецепт собирается сам».

## 6. Затронутые тесты
- `PassiveItemTests.EveryPassiveAndRecipeIsImplemented`: строка «recipes form by themselves» заменяется проверкой `CanCombine` = ложь без компонентов.
- `PassiveItemTests.RecipeGivesOnlyItsOwnBonuses`, `HeartOfTarrasqueReplacesReaverAndRing`, `SimulationTests.PassivesStackAndRecipeConsumesComponents`: после компонентов — `ForceCombine` (до этого рецепта нет). Ожидаемые значения прежние.
- `ChoiceRulesTests` (строка 351 «a recipe changes everything»): проверяется после правок, ожидания не меняются.
- Последовательность `Rng` меняется у забегов, где готовый рецепт попадает в сундук: кандидатов становится больше. Тесты на точные seed-последовательности рецептов не трогали.

## 7. Тестовая стратегия
- dotnet `RecipeTests` (новая фикстура): рецепт не предлагается при компоненте ниже максимума; предлагается в сундуке с весом 0.42 и не предлагается при уровне; выбор объединяет и даёт событие `RecipeCombined` с ID; повторный `Combine` — ложь; два готовых рецепта с общим компонентом — две карточки, после выбора одного второй пропадает; трезубец после S&Y и kaya; рецепт при полных слотах и максимальных пассивках вместо запасной награды; карточка показывает итоговые изменения (Y&K: 355 → 380, 0.45 → 0.6); статистика урона сохраняется; без выделений в шаге с готовым рецептом. По тесту `[TestCase]` на каждый из 9 рецептов: `RecipeIsOfferedOnlyWithItsComponentsAtMax`.
- dotnet `ItemAbilityMatrixTests`: 450 пар (§3.3); отдельный тест «врождённая Kez не продлевается Timeless Relic» (В4) и «Track продлевается» как точечные примеры.
- dotnet `DragonusInnateAddsTargetCountAndLowersMaxHealth`.
- EditMode: `ChoiceTextTests` (карточка рецепта: «Комбинация предметов», «Сочетает: Яша, Кая»), `RecipeNoticeTests` (текст, 3 с, стоит на паузе).
- Python: тесты аудита §4; `test_mac_smoke.py` — код 17 и разбор `THORN_RECIPE`.
- Smoke: `Tools/mac-smoke.sh -thornRecipe=yasha_and_kaya`, `-thornRecipe=sny_trident`; полный набор проверяющего как регрессия.

## 8. Риски
- Время теста матрицы — меры в §3.3.
- Точное равенство отпечатков «не меняет» может нарушить скрытая зависимость (например, скорость героя через Stampede). Это и есть цель проверки: такой случай разбирается по Т4.5.
- В4 меняет баланс Kez с Timeless Relic (−2 с уязвимости на максимуме).
