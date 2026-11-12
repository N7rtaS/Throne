# Task 31. Валюта и постоянная прокачка — дизайн

## Что известно об оригинале
- **Д** `survivors_attributes.json`: у 14 атрибутов `m_vecMetaProgressionTiers`, у каждого уровня `m_flValue` и `m_cost.m_nMinigameCurrency`. Других цен и валют в данных нет: `m_cost` встречается 64 раза, только в этом файле, и всегда содержит одно поле `m_nMinigameCurrency`.
- **Д** локализация Panorama: «Собирайте золото, повышайте свои характеристики, убивайте ещё больше врагов.» (`DOTA_Survivors_UpgradesDescription`), «Требуется N золота» (`GoldNeeded`), «Всего золота» (`TotalGold`), «Золота получено» (`GoldEarned`), «Купить» (`Upgrade`), «Доступны улучшения!» (`UpgradesAvailable`). Значит, цена `m_nMinigameCurrency` платится золотом, собранным в забегах.
- **Д + Н** (Mechanics §13, скриншоты): значения и цены на экране «Улучшения» совпадают с данными; карточка показывает значение уровня, а не сумму («Макс. здоровье 100 → 150» — уровни 4 и 5).
- **Н** (видео, §13): профиль с прокачкой даёт Dragonus 180 HP на старте и урон Arcane Bolt 8–9.
- **К** (`strings.json`): `MetaProgressionTier_t`, `MetaProgressionTierCost_t`, `DOTASurvivorsMetaUpgradeAttribute`, задание Game Coordinator `EventMetaUpgradeAttribute`, `EventMetaDevResetUpgrades`, подсказка `InvalidMetaProgressionUpgrade`. Покупка уходит на сервер Valve, валюта хранится в аккаунте. Алгоритм проверки на сервере неизвестен.
- Неизвестно: начисляется ли золото брошенного забега, можно ли покупать уровни не по порядку, что показывает `InvalidMetaProgressionUpgrade`.

## Решения
| # | Правило | Метка |
|---|---|---|
| М1 | Валюта — золото, собранное в забегах (`Run.Gold`: монеты, мешки, запасная награда `gold`), курс 1:1. Цены `m_nMinigameCurrency` переносятся без пересчёта. Других жетонов и серверных наград нет, замена — только хранение в локальном профиле | Д (тексты) + Р (курс 1:1, локальное хранение) |
| М2 | Начисление — в операции записи результата `result:<runId>` (Ф3), за победу и за поражение. Брошенный забег («Завершить забег») золота не даёт, как не даёт результата (Ф5) | Р |
| М3 | Уровень L даёт `m_flValue` уровня L (не сумму), цена уровня L — его `m_nMinigameCurrency` | Д + Н |
| М4 | Покупка только следующего уровня: L = текущий + 1. Пропуск уровня — отказ | Р |
| М5 | Покупка — одна операция в памяти: проверка, списание, уровень, журнал `meta:<атрибут>:<L>`, сохранение. Метод принимает ожидаемый уровень L, поэтому повтор того же нажатия возвращает «уже куплено» без списания | Р |
| М6 | Покупка возможна на любом экране, кроме забега (`AppScreen.Run`). Экран для неё сделает Task 33 | Р |
| М7 | Неудачное сохранение после покупки не откатывает покупку: флаг `SaveFailed`, повтор `RetryFailedSave` (как Ф11) | Р |
| М8 | Бонусы прибавляются к `Build.Global` сразу после базовых атрибутов героя, при каждом пересчёте билда. Дальше работают существующие формулы: здоровье, восстановление, `DamageMultiplier` в уроне (§24), перезарядка, `Armor`, `DamageReflection`, `Lives` (§25), `MaxRerolls` → `RerollsLeft`, `TargetCount` для всех способностей | Р (порядок не важен, сумма аддитивна) |
| М9 | Ключ атрибута в `Profile.MetaUpgrades` — короткое имя без префикса (`MaxHP`), как `Attr.Name`. Неизвестный ключ — диагностика, не применяется. Уровень выше максимума — диагностика, применяется максимум, покупка невозможна | Р |
| М10 | «Всего золота» на итоге — валюта профиля после начисления. «Доступны улучшения!» — если хватает хотя бы на один следующий уровень | Д (тексты) + Р (условие) |
| М11 | Сброс прокачки не делается (в оригинале это команда разработчика) | К + Р |

Решения утверждены владельцем 12.11.2026 («дальше», включая Т6).

Схема профиля остаётся 2: поля `currency` и `metaUpgrades` уже есть (Mechanics §19).

## Архитектура
### Simulation (`Scripts/Simulation`)
- `Content/Definitions.cs`: `MetaTierDef { float Value; int Cost; }`, `MetaUpgradeDef { string Key; int AttributeId; bool Percentage; MetaTierDef[] Tiers; }`.
- `Content/GameData.cs`: `public readonly List<MetaUpgradeDef> MetaUpgrades` (порядок данных) и `Dictionary<string, MetaUpgradeDef> MetaUpgradesByKey`. Разбор в ветке `"attributes"`. Без `m_flValue`/`m_cost`/`m_nMinigameCurrency` — `InvalidDataException` (или тип, который уже используется загрузчиком для ошибок данных).
- `Run(..., IReadOnlyList<AttributeValue> startAttributes = null)` — новый последний необязательный параметр. Он передаётся в `Build`, `Build.Recalculate` добавляет его после `hero.BaseAttributes`. Старые вызовы не меняются. Горячий цикл не затронут: массив создаётся один раз при старте.

### App (`Scripts/App`)
- Новый `MetaProgression.cs` (static, без движка):
  - `int Level(Profile, MetaUpgradeDef)`;
  - `MetaTierDef NextTier(Profile, MetaUpgradeDef)` (null на максимуме);
  - `PurchaseResult Buy(Profile, GameData, string key, int level)` → `Bought`, `AlreadyOwned`, `NotEnoughCurrency`, `MaxLevel`, `SkipsLevel`, `UnknownAttribute`;
  - `AttributeValue[] StartAttributes(GameData, Profile)` с диагностикой по М9;
  - `bool AnyAffordable(GameData, Profile)`.
- `AppFlow`:
  - `Record()` прибавляет `Run.Gold` к `Profile.Currency` (М2), `RunOutcome.TotalGold` и `RunOutcome.UpgradesAvailable`;
  - `PurchaseResult BuyUpgrade(string key, int level)`: отказ `RunInProgress` на экране забега, иначе `MetaProgression.Buy`, при `Bought` — `LastSave = save(profile)`;
  - `NewRun()` передаёт `MetaProgression.StartAttributes(data, profile)` в `Run`.
- Сборка `ThornThrone.Progression` не меняется (без ссылки на симуляцию).

### Unity-слой (Т6, если утверждено)
- `ResultsText.Header` получает `totalGold`: строка «Итоговый счёт … Золота получено … Всего золота N» (пятая строка не помещается в шапку 240 px). `ResultsText.Notices(unlocked, upgradesAvailable)` даёт строку под таблицами: «Открыта новая сложность!» и/или «Доступны улучшения!», она выводится в существующую метку `Unlocked`. `GameUi` передаёт значения из `flow.LastOutcome`. Символы есть в атласе PT Sans (проверяет EditMode-тест).

## Тестовая стратегия
`DotNet/ThornThrone.Simulation.Tests/MetaProgressionTests.cs` (фикстура `MetaProgressionTests`), реальные данные `TestData.Load()`:
- Загрузка: 14 атрибутов в порядке данных, уровни и цены MaxHP (25/50/75/100/150 за 250/1000/2500/10000/20000), TargetCount и Lives по 2 уровня (10000/30000), сумма всех 64 цен совпадает с суммой по JSON; уровень без `m_cost` — ошибка загрузки (подменённый JSON).
- Применение: MaxHP 4 → 100 − 20 + 100 = 180 у Dragonus; значение уровня, не сумма (MaxHP 5 → +150, а не +400); MaxRerolls 4 → `RerollsLeft` 6; Lives 1 → `LivesLeft` 1; бонус сохраняется после получения улучшения (пересчёт билда); неизвестный ключ и уровень выше максимума — диагностика.
- Сверка с видео Т5: профиль §13 → 180 HP, `Damage` Arcane Bolt 8.75 (`Within(1e-4)`), HPRegen 1.
- Покупка: успешная (валюта, уровень, журнал, одно сохранение); повтор с тем же L — `AlreadyOwned` без списания и без сохранения; нехватка, максимум, пропуск, неизвестный атрибут, во время забега — без изменений; неудачная запись — покупка в памяти, `RetryFailedSave` не списывает.
- Начисление (`AppFlowTests` или новая фикстура): победа и поражение начисляют `Run.Gold` один раз, повторный `Record` и `RetryFailedSave` не начисляют, брошенный забег не начисляет; новый забег через `AppFlow` получает бонусы профиля.
- Т6: EditMode-тест `ResultsText.Header` со строками «Всего золота» и «Доступны улучшения!».

## Риски
- Профиль `user` у владельца пока без валюты, покупок нет до Task 33 (экран). Для проверки на устройстве до Task 33 покупка доступна только из тестов.
- Бонус `TargetCount` +2 и `CooldownReductionMultiplier` влияют на все способности: это меняет баланс забегов с прокачкой, но не забегов без неё (smoke идёт на новом профиле `test`).
- `DamageMultiplier` не входит в `Run.SupportedHeroAttributes` (это список для предложения предметов). Список не меняется, чтобы не изменить пул выбора; применение проверяется тестом урона.
