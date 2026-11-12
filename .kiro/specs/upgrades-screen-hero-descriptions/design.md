# Task 33. Экран «Улучшения» и описание героев — дизайн

## 0. Что известно об оригинале
- **Д** разметка `bp_popup_crownfall_encounter_survivors.vxml`:
  - `DifficultySelectScreen` состоит из двух половин: слева `MetaProgressionSelection` («Улучшения», `UpgradesDescription`, `MetaUpgradesContainer`, «Всего золота» с `{d:currency_available}` и значком мешка), справа выбор эшелона; внизу «Назад» и «START». Отдельного экрана улучшений нет.
  - Карточка `MetaUpgradeAttribute`: `{s:attribute_name}`, 5 отметок `UpgradePip`, `{s:attribute_value}` (html), «Стоимость», `TokensContainer` + «+» + `{s:currency_cost}` и значок монет, кнопка «Купить», флажок «Фильтр жетонов».
  - Карточка героя `ChooseHeroButton`: модель `DOTALargeModelPreview`, имя `{s:choose_hero_name}`, «Стартовая способность», две картинки — `InnateImage` и `AbilityImage`. Текста описаний на карточке нет.
  - Подсказка `survivors_tooltip_invalid_metaprogression_upgrade.vxml`: «Требуется **{d:gold_amount}** золота».
  - Кнопки (DEV) «Reset Upgrades», «Give Max Points» и т. п. — отладочные, не переносятся.
- **Д** `dota_russian.txt` клиента (прочитан из `pak01_dir.vpk` только на чтение): `DOTA_Survivors_AttributeUpgradeDetails` = `{s:attribute_current_value}{s:attribute_suffix} -> <b>{s:attribute_next_value}{s:attribute_suffix}</b>`; `DOTA_Survivors_AttributeMaxUpgradeDetails` = `<b>+{s:attribute_current_value}{s:attribute_suffix}</b>`; `UpgradesTitle` «Улучшения», `UpgradesDescription`, `TotalGold` «Всего золота», `Cost` «Стоимость», `Upgrade` «Купить», `GoldNeeded`, `UpgradesAvailable` «Доступны улучшения!», `UpgradesDifficultyButton` «Улучшения и сложность», `StartingAbilityLabel` «Стартовая способность».
- **Д** `survivors_heroes.json`: `m_vecStartingPowerUps` (11, 10, 12), `m_vecInnatePowerUps` (501, 500, 502), `m_vecBaseAttributes` (рывки 1/1/2, перезарядка 3/1/3). Описания героя в данных нет; `survivors_heroes_russian.txt` содержит только имена (Драгонус, Шен, Кез).
- **Д** `survivors_powerups_russian.txt` (`powerups_ru.json`): названия и описания всех шести способностей, например «Потомок Скайраса — У Драгонуса меньше здоровья, но все его способности выпускают больше снарядов.»
- **Д + Н** Mechanics §13: на карточке значение уровня, а не сумма («Макс. здоровье 100 → 150»).
- **Д** `survivors_attributes.json`: у каждого атрибута `m_sImage` (panorama: `icon_zoo.psd`, `twin_hearts.png` …), у способностей — `m_sImage`. Картинки лежат в `NestOfThorns` как `vtex_c`, в Unity не сконвертированы (конвейер иконок — Task 35).
- Не найдено: «Fan-favorite» (нет в `Research/Json`, `Research/Decoded`, полных `dota_english.txt`/`dota_russian.txt` клиента, строках `libclient`/`libserver` и истории git; в дорожной карте Task 33 этого слова нет). Вопрос В5.
- Неизвестно: что показывает `{d:gold_amount}` (полную цену или недостачу); как оригинал показывает значение при уровне 0; есть ли подтверждение покупки.

## 1. Решения
| # | Правило | Метка |
|---|---|---|
| У1 | Экран «Улучшения» — отдельный экран `AppScreen.Upgrades`. Открывается кнопкой «Улучшения» на экране сложности, «Назад» (кнопка и Android) возвращает на экран сложности. В оригинале это левая половина экрана сложности; 14 карточек и 8 эшелонов на одном экране 6" телефона не читаются (В1) | Д (состав) + Р (отдельный экран) |
| У2 | Шапка: «Улучшения», строка `UpgradesDescription`, «Всего золота N» (валюта профиля, целое с пробелом между тысячами, как на итоге) | Д |
| У3 | 14 карточек в порядке `GameData.MetaUpgrades`, сетка 7 × 2 в контейнере 1920×1080, карточка 250 × 365 (кнопка «Купить» 110 высотой, чтобы при 48 dp не наезжать на строку нехватки) | Р |
| У4 | Карточка: название (`m_sLocName`, словарь `ChoiceText`), отметки уровней (N квадратов; купленный — залитый золотой, некупленный — пустая рамка: форма + цвет, ТЗ §7), строка значения, «Стоимость», цена следующего уровня, «Купить» | Д (состав) + Р (вид отметок) |
| У5 | Строка значения: `AttributeUpgradeDetails` — «текущее -> **следующее**», текущее — значение уровня L, следующее — уровня L + 1; при L = 0 текущее 0 (`0 -> 25`). `m_bPercentage`: ×100 и «%». Числа форматируются как на карточках выбора (`ChoiceText.Value`: до двух знаков, точка) | Д + Н (значение уровня) + Р (0 при L = 0) |
| У6 | Последний уровень: `AttributeMaxUpgradeDetails` «**+значение**» и «МАКС.» вместо цены и кнопки | Д |
| У7 | Нехватка: кнопка неактивна (серая), под ней «Требуется **N** золота», N = цена − валюта (недостача, ТЗ §12 «недостаток валюты») (В2) | Д (текст) + Р (N) |
| У8 | Нажатие «Купить» вызывает `AppFlow.BuyUpgrade(ключ, L + 1)` с уровнем L, показанным на карточке в момент последней перерисовки, затем экран перерисовывается. Подтверждения нет: одно нажатие — один уровень | Р |
| У9 | Неудачная запись после покупки — строка «Не удалось сохранить покупку» на экране; покупка остаётся (§38 М7) | Р |
| У10 | На экране сложности под кнопкой «Улучшения» строка «Доступны улучшения!», если `AnyAffordable` | Д (текст) + Р (место) |
| У11 | Кнопка итога «Сложность» → «Улучшения и сложность», ведёт на экран сложности (как раньше) | Д |
| У12 | Фильтра жетонов и значков монет нет (жетонов в данных нет, Б22; иконки — Task 35) | Р |
| Г1 | Карточка героя: имя, «Стартовая способность: <название>» и её описание, «Врождённая: <название>» и её описание, строка рывка. В оригинале на карточке только картинки с подсказкой по наведению; на телефоне наведения нет, поэтому текст на карточке (В3) | Д (состав) + Р (текст вместо картинок) |
| Г2 | Строка рывка: «Рывок: N заряд(а), перезарядка X с» из `NumDashes` и `DashCooldown` героя (без постоянной прокачки) | Д (числа) + Р (строка) |
| Г3 | Способности берутся по `m_vecStartingPowerUps[0]` и `m_vecInnatePowerUps[0]` через `GameData.PowerUpsById`. Пустой массив или неизвестный ID — исключение `HeroInfo.Of` с героем и ID (ошибка данных, не пустая строка). Нет перевода — ключ способности | Р |
| Г4 | Иконок героев и способностей нет до Task 35, модели на карточке нет до Task 34 (В4) | Р |

## 2. Архитектура
### App (`Scripts/App`, сборка без движка, тестируется dotnet)
- `MetaProgression.cs`:
  - `sealed class MetaUpgradeRow { MetaUpgradeDef Def; int Level; int MaxLevel; float Current; float? Next; int Cost; bool Affordable; long Shortage; bool IsMax => Next == null; }` — `Current` = значение уровня L или 0; `Cost` — цена L + 1 (0 на максимуме); `Shortage` = max(0, Cost − Currency).
  - `static List<MetaUpgradeRow> Rows(GameData data, Profile profile)` — в порядке `data.MetaUpgrades`.
- `AppFlow.cs`:
  - `AppScreen.Upgrades` (в конец перечисления).
  - `bool OpenUpgrades()` — Difficulty → Upgrades; `Back()` — Upgrades → Difficulty (эшелон не меняется, Ф6 не перезапускается).
  - `BuyUpgrade` без изменений (на экране Upgrades разрешена, как на любом кроме Run).
  - `List<MetaUpgradeRow> UpgradeRows()` и `bool UpgradesAvailable` — обёртки над `MetaProgression`.
- Новый `HeroInfo.cs`: `sealed class HeroInfo { HeroDef Hero; PowerUpDef Starting; PowerUpDef Innate; int Dashes; float DashCooldown; static HeroInfo Of(GameData, string key); }`; `AppFlow.HeroInfo(key)`.

### Mobile (`Scripts/Mobile`)
- Новый `UpgradesText.cs` (чистые функции): `TotalGold(long)`, `Value(MetaUpgradeRow)` (У5/У6), `Cost(MetaUpgradeRow)`, `GoldNeeded(long)`, константы заголовка, описания, «Купить», «Доступны улучшения!», «Не удалось сохранить покупку».
- Новый `HeroText.cs`: `Card(HeroInfo, string heroTitle, Func<PowerUpDef, string> name, Func<PowerUpDef, string> description)` и `Dash(int, float)` с согласованием «заряд/заряда/зарядов».
- `GameUi`: панель «Upgrades» (шапка, 14 карточек `UpgradeCard { Button Buy; TMP_Text Name, Value, Cost, Needed; Image[] Pips; int Level; string Key }`, «Назад», строка ошибки), `ShowUpgrades(AppFlow)`, события `UpgradesRequested`, `UpgradeBought(string key, int level)`; кнопка «Улучшения» и строка «Доступны улучшения!» на экране сложности; карточки героев выше (520 × 640) с текстом `HeroText.Card`, `ShowHeroSelect` заполняет их из `flow.HeroInfo`; подпись кнопки итога. Цели касания регистрируются через `AddTarget` (48 dp).
- `FlowScreens`: `UpgradesRequested → Go(flow.OpenUpgrades)`; `UpgradeBought → flow.BuyUpgrade`, затем `ui.ShowUpgrades(flow)`; `BackScreen` для `AppScreen.Upgrades`.
- `TouchController.cs`: `BackScreen.Upgrades` → `BackAction.ToDifficulty` (новое действие), `MobileGame` выполняет его через `screens.Back()`.
- `MobileGame.FlowSmoke`: шаги `upgrades` (с экрана сложности «Улучшения» → экран Upgrades, 14 карточек, валюта 0, все «Купить» неактивны) и `upgrades_back`; `-thornFlowShot=upgrades|hero`.
- Шрифт: новые строки — кириллица и ASCII, уже в атласе; `FontBaker.Characters` читает литералы `Scripts/Mobile`. EditMode-тест проверяет отсутствие недостающих символов.

`Run`, симуляция и сборка `ThornThrone.Progression` не меняются. Схема профиля остаётся 2.

## 3. Тестовая стратегия
dotnet, `DotNet/ThornThrone.Simulation.Tests/UpgradesScreenTests.cs` (фикстура `UpgradesScreenTests`), реальные данные `TestData.Load()`:
- `RowsFollowDataOrderWithFourteenAttributes`; новый профиль: у MaxHP уровень 0 из 5, Current 0, Next 25, Cost 250; у TargetCount 0 из 2, Next 1, Cost 10000.
- `RowShowsLevelValueNotSum`: MaxHP 4 → Current 100, Next 150, Cost 20000.
- `RowAtMaxLevelHasNoNext`: MaxHP 5 → IsMax, Current 150, Cost 0, Affordable false.
- `ShortageIsCostMinusCurrency`: валюта 100, MaxHP 0 → Shortage 150, Affordable false; валюта 250 → Shortage 0, Affordable true.
- Переходы: `UpgradesOpenOnlyFromDifficulty`, `BackFromUpgradesKeepsEchelon`, `BuyOnUpgradesScreenChargesOnce` (покупка, валюта, одна запись; повтор с тем же уровнем — `AlreadyOwned`), `PurchaseAppliesToNextRun`.
- `HeroInfoComesFromHeroData` (три героя: ключи способностей, зарядов, перезарядка из JSON), `HeroInfoWithUnknownPowerUpThrows` (подменённый JSON).

EditMode `UpgradesTextTests` (тексты улучшений и карточек героев): «0 -> **25**», «5% -> **10%**» (DamageMultiplier), «2.5% -> **5%**» (ExpMultiplier 1→2), «**+150**», «Требуется **150** золота», «Всего золота 30 000», карточки трёх героев с названиями из `powerups_ru.json`, «1 заряд», «2 заряда»; символы в атласе. `ScreenFlowTests`: открыть/закрыть экран, покупка меняет валюту и карточку, неактивная кнопка и строка нехватки, «МАКС.», «Доступны улучшения!» на экране сложности, Android Back, подпись «Улучшения и сложность».

## 4. Риски
- Карточки 250 px шириной с текстом 26–30 px: длинные названия («Уменьшение перезарядок», «Сопротивление урону») переносятся в две строки. Финальная раскладка и проверка на четырёх разрешениях — Task 35.
- Карточки героев выше прежних (640 вместо 340): кнопки «Назад»/«Далее» остаются внизу, проверить скриншотом.
- Профиль `user` владельца без валюты: покупку на устройстве можно проверить только после забегов; в smoke профиль `test` с валютой 0, поэтому smoke проверяет только неактивные кнопки, покупку — EditMode и dotnet.

## 5. Вопросы владельцу (утверждены 12.11.2026: В1 отдельный экран, В2 недостача, В3 текст на карточке, В4 иконки в Task 35, В5 не делать)
- **В1.** Отдельный экран «Улучшения» с кнопкой на экране сложности (рекомендую) или, как в оригинале, левая половина экрана сложности (карточки эшелонов и улучшений станут вдвое меньше)?
- **В2.** «Требуется N золота»: N — недостача (рекомендую, по ТЗ «недостаток валюты») или полная цена?
- **В3.** Карточка героя с текстом описаний стартовой и врождённой способности и строкой рывка (рекомендую) или только названия, как картинки в оригинале?
- **В4.** Иконки героев и способностей — в Task 35 вместе с остальными иконками panorama (рекомендую) или конвертировать их уже здесь (нужен конвейер `vtex_c` → PNG, расширение задачи)?
- **В5.** «Fan-favorite» в описании задачи от диспетчера: в данных, клиенте и дорожной карте такого нет. Что имелось в виду? Предлагаю не делать.
