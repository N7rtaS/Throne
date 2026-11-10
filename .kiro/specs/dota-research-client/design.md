# Дизайн: исследовательский инструмент Dota, только чтение (Task 6)
Требования: `requirements.md`. Клиент на момент проектирования: `steam.inf` ClientVersion 6972, SourceRevision 11209448 (05.11.2026), SHA-256 `pak01_dir.vpk` совпадает с `SourceVersions.json`.

## 1. Что уже есть
- `Tools/vpk.py` читает VPK только через `open(..., "rb")` и проверяет CRC32. `extract-resources.py` уже скопировал в `NestOfThorns/` локализацию vdata (`survivors_*_<язык>.txt`) и 5 разметок + 5 стилей panorama режима (`panorama/layout/battle_pass/crownfall/*survivors*.vxml_c`, `banners/front_page_nest_of_thorns.vxml_c` и их `.vcss_c`).
- `audit.py` ищет в `libclient.dylib` только имена классов (поле `nativeBinary` реестра).
- Не извлечено: строки меню Panorama. Проверка показала: они лежат в `resource/localization/dota_<язык>.txt` внутри `pak01_dir.vpk` (118 ключей `DOTA_Survivors_*`, например `DOTA_Survivors_DisableDialogue` = «Отключить реплики», `DOTA_Survivors_ViewPatchNotes`, патч-ноты 1.1), а не в отдельной панорамной локализации. Разметки ссылаются на них через `#ключ`.
- Пробы на текущем клиенте: в `libclient.dylib` 713 строк с `survivors`, 79 значений `k_eSurvivors*`; из 320 полей данных в клиенте есть 318 (нет `m_flMinSeparationDistanceFromEnemies`, `m_flSpawnOffset`). Симуляция читает 172 поля литералами. Разбор строк обоих бинарников занимает около 2 с. Source2Viewer декодирует `vxml_c` детерминированно (одинаковый MD5 двух запусков).

## 2. Архитектура
Один файл `Tools/research-client.py` (стандартная библиотека), импортирует `vpk.Vpk` и `audit.powerup_role`, `audit.load`.

```
main(argv)
 ├─ Guard(dota)            запретная зона, open_readonly(), check_output()
 ├─ snapshot()             (только с --check-readonly) до и после
 ├─ client_strings()       → Research/Decoded/client/strings.json
 ├─ panorama_layouts()     NestOfThorns/*.vxml_c|*.vcss_c → Source2Viewer → Research/Decoded/panorama/**
 ├─ panorama_localization()→ Research/Decoded/panorama/localization_{english,russian}.json, menu_texts.md
 ├─ descriptions()         → Research/FullGame/Descriptions.md
 └─ report()               → Research/research-client-report.json
```
Все записи идут через `write_text(path, text)`: проверка `Guard.check_output`, запись во временный файл рядом и `os.replace`; если содержимое не изменилось, файл не трогается (mtime проекта тоже стабилен).

### 2.1 Guard (Т1)
- `forbidden_roots(dota)` (Р2): `realpath(~/Library/Application Support/Steam)`; родитель ближайшего к Dota компонента `steamapps` в `realpath(dota)`; если `steamapps` нет — корень установки (`dota/..` при имени `game` → его родитель, иначе сам `dota`). Вычисленный корень не сужается: если это `/` или домашний каталог, он остаётся в зоне, и срабатывает проверка проекта ниже. Путь запрещён, если `realpath(path)` равен корню или начинается с `корень + os.sep`.
- `check_project(project)`: проект внутри любого корня → код 3 до чтения входов. Это защита от нестандартного `--dota`, при котором зона накрыла бы проект.
- `check_output` вызывается для каждого выходного каталога в начале `main` (до чтения входов, код 3) и повторно при каждой записи.
- `open_readonly(path, mode="rb")`: если `mode != "rb"`, `ValueError` без открытия. Вне `vpk.Vpk` инструмент открывает файлы Dota только так. `vpk.Vpk` уже открывает только `"rb"`; тест подменяет `builtins.open` и проверяет режимы всех открытий путей Dota.
- Source2Viewer запускается только с `-i NestOfThorns/…` и `-o <временный каталог вне Dota>`, затем результат переносится через `write_text`. Перед запуском CRC32 копии сверяется с записью `pak01_dir.vpk` (Т3.3).
- `snapshot(root)`: `os.walk(<корень установки Dota>, followlinks=False)` → `{relpath: (size, st_mtime_ns)}`; около 7 100 файлов. Разница печатается в stderr, код 4. Весь Steam не снимается: Steam сам постоянно пишет `logs`, `appcache`, `config`, и проверка падала бы не по вине инструмента; запись в остальной Steam исключена `check_output`.

### 2.2 Строки бинарников (Т2)
`re.finditer(rb"[\x20-\x7e]{4,}", data)` по каждому бинарнику, порядок по смещению сохраняется для правила окрестности, затем результаты сортируются.

## 3. Правила отбора (решения)
### 3.1 Классы и перечисления (**К**)
- `classes`: из каждой строки берутся совпадения `(?:C|I)(?:DOTA_)?_?Survivors\w*`, без числового префикса манглинга (`28CSurvivorsPowerUp_ArcaneBolt` → `CSurvivorsPowerUp_ArcaneBolt`), и `CDOTA_(?:UI_)?Survivors\w*`.
- `enums`: строки `k_eSurvivors\w+`, `E?Survivors\w*_t`, `ESurvivors\w+`.
### 3.2 Кандидаты полей клиента (**Р**)
Строка `m_\w+`, которой нет в данных, попадает в `clientOnlyFields`, если в списке строк бинарника (в порядке смещения) в пределах 10 позиций от неё есть строка с `survivors` или поле данных. Окно 10 выбрано по пробе: 424 кандидата в `libclient`, из них 186 `m_fl*`; при окне 20 — 710 с заметным шумом из соседних модулей (`m_HeroPick_*`, `m_flFallDamageMult`). Итог реального прогона по обоим бинарникам: 566 кандидатов. Линкер сливает одинаковые строки разных модулей, поэтому принадлежность режиму не доказана: в файле поле помечено `"evidence": "К-кандидат"`.
### 3.3 Читаемые поля
`readBySimulation` — имя встречается литералом `"m_…"` в `Scripts/Simulation/**/*.cs`. Чтение по составному имени не учитывается (сейчас таких нет).
### 3.4 Ключи локализации (**Р**)
В `localization_*.json` попадает ключ, если выполнено одно из условий: начинается с `DOTA_Survivors_`; содержит `Survivors` или `NestOfThorns`; на него ссылаются декодированные разметки режима (`#Ключ` в `text`, `placeholder` и т. п.) или строки `survivors` бинарников (`#DOTA_…`). Разбор KV: `"ключ"\s+"значение"` с экранированием `\"`, BOM и CRLF допускаются; при повторе ключа берётся последнее значение (как у движка). Для разметок, которые ссылаются на общие ключи Crownfall (`DOTA_Crownfall_Controls_MoveDescription`), это даёт тексты «Как играть».
### 3.5 Описания powerups
- Источник имён и описаний — `resource/localization/vdata/survivors_powerups/survivors_powerups_{english,russian}.txt` и `survivors_attributes_*` из `pak01_dir.vpk` Dota (не копия в проекте: инструмент сверяет актуальный клиент).
- Группы по `audit.powerup_role` в порядке: активные, пассивные, рецепты, врождённые, служебные; внутри группы — по `m_unPowerUpID`.
- Атрибуты выводятся значениями из данных без форматирования клиента (масштаб процентов в `{f:0:attribute_value}` не восстановлен), рядом рус. имя `…_LocName`. Плейсхолдеры описаний (5 штук: `counter_chance`, `actual_damage_multiplier`, `speed_damage_multiplier`, `extra_damage`, `damage_per_enemy`) не подставляются.
- Ранги активных способностей (`m_vecAuthoredUpgradeChoices`) в `Descriptions.md` не дублируются: файл про тексты, числа рангов уже в данных и `Mechanics.md`.
### 3.6 Выходы
| Файл | Содержимое |
|---|---|
| `Research/Decoded/client/strings.json` | Т2.2 |
| `Research/Decoded/panorama/layout/…/*.vxml`, `styles/…/*.vcss` | декодированные Source2Viewer копии из `NestOfThorns` |
| `Research/Decoded/panorama/localization_english.json`, `localization_russian.json` | Т3.1 |
| `Research/Decoded/panorama/menu_texts.md` | Т3.4 |
| `Research/FullGame/Descriptions.md` | Т4 |
| `Research/research-client-report.json` | Т5.2 |
`Research/Decoded/client/` — новый каталог (**Р**: строки клиента не относятся к panorama). Все выходы идут в git, кроме полной копии локализаций: в git только отобранные ключи.

## 4. Решения, которые нужно утвердить
| № | Решение | Метка |
|---|---|---|
| Р1 | Source2Viewer декодирует копии из `NestOfThorns`, а не VPK Dota. CRC32 копии сверяется с VPK; при расхождении — код 1 и запуск `extract-resources.py`. Так внешняя программа не получает путей Dota | **Р** |
| Р2 | Запретная зона — весь Steam (решение владельца 10.11.2026): `~/Library/Application Support/Steam` и корень библиотеки Steam с Dota (родитель компонента `steamapps`); без `steamapps` — корень установки Dota. Если проект оказался внутри зоны — код 3 и отказ работать | **Р** (утверждено) |
| Р3 | Строки бинарников — ASCII от 4 символов (как `strings`), без UTF-16; оба `libclient` и `libserver`, `libhost` не нужен | **Р** |
| Р4 | Окно ±10 строк для кандидатов полей клиента (§3.2), пометка «К-кандидат» | **Р** |
| Р5 | Локализация Panorama — отобранные ключи `dota_<язык>.txt` (§3.4), не вся локализация Dota (4 МБ на язык) | **Р** |
| Р6 | Языки: английский и русский | **Р** |
| Р7 | `--check-readonly` сравнивает размер и `st_mtime_ns` всех файлов корня установки Dota (`dota 2 beta`). Если в это время работает Dota или Steam обновляет игру, проверка может упасть не по вине инструмента; запускать при закрытой Dota | **Р** |
| Р8 | Ответы на §12 пишутся вручную по выходам инструмента в новый раздел `Mechanics.md` §16 «Ответы из клиента»; §12 получает ссылки. Строка без алгоритма — **К**, патч-ноты и описания — источник «локализация» с меткой **Д** только для прямо сказанного текста | **Р** |

## 5. Ожидаемые находки для «Ответов из клиента» (по пробам)
Это план поиска, итог фиксируется после запуска:
- §12.3 веса и перебросы: `DOTASurvivorsRerollLevelUpChoices`, `DOTA_Survivors_Reroll` («осталось: N»), `k_eSurvivorsAttribute_MaxRerolls`; формулы весов в строках нет — вероятно **К**/не найдено.
- §12.4 рецепт: `DOTA_Survivors_CombinedItem` «Item Combination», «Combines:»; момент объединения — вероятно не найден.
- §12.5 Aghanim: `DOTASurvivorsCheatsGetScepter/GetShard`, `CSurvivorsGameLogEntry_ShardReceived`, `m_bHasShardUpgrade`, `DOTA_Survivors_OptionalBossFightsDescription` («мини-боссы дают особые улучшения способностей»).
- §12.6 атаки Империи: `ESurvivorsBossAbility`, функции `ExecuteRadiateRays`, `ExecuteMagicMissile`, `ExecuteBurningGround`, `ExecuteDemonPortals`; порядок — не найден.
- §12.7 выбор элитных комнат: `m_vecEliteRoomChoices`, `selected_elite`, `MinibossRoom%d`.
- §12.9 удача: `k_eSurvivorsAttribute_Luck`, `m_luckyLootTable` против `m_fullLootTable`.
- §12.10 башни и золото: `DOTA_Survivors_UnlockSeal1/2` «Destroy Tower (NW/NE)», `CSurvivorsSpawnerTower`, патч-нота 1.1 «вазы на арене с Империей гарантированно содержат сыр» (касается `pickups/cheese` из §10).

## 6. Тестовая стратегия
`Tools/tests/test_research_client.py` (unittest, как `test_audit.py`). Фейковая установка во временном каталоге: `Steam/steamapps/common/dota 2 beta/game/dota/pak01_dir.vpk` (одиночный VPK v1, пишет хелпер теста), `bin/osx64/libclient.dylib` и `libserver.dylib` с известными строками, `steam.inf`.
- Guard: выход внутри `game/dota`, внутри `game` и внутри фейкового Steam вне Dota (`Steam/config`) → код 3, ничего не создано; выход внутри известного `~/Library/Application Support/Steam` запрещён; путь через симлинк в Steam → код 3; `open_readonly(path, "wb")` → `ValueError`, файл не создан.
- Корень зоны: путь с `steamapps` → родитель `steamapps`; путь без `steamapps` (`Games/dota 2 beta/game/dota`) → `Games/dota 2 beta`, соседний `Games/other` разрешён.
- Проект в зоне: `--dota` вида `<tmp>/steamapps/common/x/game/dota`, где проект — `<tmp>/project` → код 3 до чтения входов, ничего не записано.
- Только чтение: полный прогон на фейковой установке с `--check-readonly` → код 0, снимок до и после равен; все вызовы `open` с путями Dota имеют режим `rb` (подмена `builtins.open`).
- `--check-readonly` ловит изменение: подмена шага, который трогает файл в фейковой Dota → код 4.
- Строки: известные `CSurvivorsFoo`, `28CSurvivorsPowerUp_Bar`, `k_eSurvivorsAttribute_X`, `m_flNear` рядом с `survivors` и `m_flFar` далеко → правильные разделы; поле данных без литерала в симуляции → `unreadFields`.
- Локализация: разбор `\"`, BOM, CRLF, повтор ключа; отбор ключей по правилам §3.4.
- Описания (на реальных данных `Research/Json` и фейковой локализации из реального `NestOfThorns`): 47 записей, группы 17/16/9/3/2, плейсхолдер `{f:0:counter_chance}` сохранён и перечислен; ключ без перевода → код 2.
- Повторный прогон даёт побайтно те же файлы (сравнение SHA-256 всех выходов).
- Source2Viewer в модульных тестах подменяется функцией; реальный вызов проверяется ручным прогоном (задача 7 в `tasks.md`).
Симуляция и Unity-слой не меняются; `dotnet test` запускается как регрессия.

## 7. Риски
- Обновление Dota меняет строки и ключи: выходы перезапишутся при следующем запуске, отчёт покажет новый SHA-256 и версию клиента.
- Source2Viewer другой версии может иначе форматировать XML: версия пишется в отчёт.
- Работающая Dota меняет файлы в `game/dota` (`rpt`, `shadercache`, `save`): см. Р7.
