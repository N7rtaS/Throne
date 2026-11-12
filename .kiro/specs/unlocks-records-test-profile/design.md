# Дизайн: открытия, рекорды, тестовый профиль (Task 32)

## 1. Что есть
- Открытия эшелонов (`AppFlow.IsUnlocked`, `Profile.Unlocks`, Ф1–Ф2) и одноразовая запись результата (Ф3) сделаны в Task 16. Цепочка проверена только по одному шагу на свежем профиле (`VictoryOnEchelonNUnlocksNPlus1`).
- `RunResult` хранит героя, `echelon_N`, исход, время, уровень, убийства, счёт, золото, версию правил. Рекордов и сводки «герой × эшелон» нет.
- Профиль `test` выбирается только флагом `-thornSmoke`; тестового профиля с открытыми эшелонами нет.

## 2. Решения
Оригинал: открытия и таблица друзей хранятся на сервере Valve (**К**, `EventMetaDevUnlockNextDifficulty`, разметка `LeaderboardContainerFriends`: аватар, имя, `{d:score}`, заголовки «Друзья» / «Счёт» — **Д**). Локального аналога нет, поэтому всё ниже — **Р**, отличие — `Differences.md` Б23.

- **З1 (Р). Сводка не хранится.** Результаты «герой × эшелон» и рекорды вычисляются из `Profile.Results` при показе экрана. Схема профиля остаётся 2, миграции нет, нечего рассинхронизировать.
- **З2 (Р). Что учитывается.** Только результаты с `rulesVersion = original-6972`, героем из `GameData.Heroes` и сложностью `echelon_N`, 1 ≤ N ≤ числа эшелонов. Остальное пропускается молча, профиль не меняется (ТЗ §9: несопоставимые рекорды не мигрируются; это не данные режима, поэтому не диагностика).
- **З3 (Р). Поля пары.** Забегов, побед, лучший счёт (включая поражения: счёт сохраняется при поражении, §37 Р4), лучшее время победы = наименьшее `DurationSeconds` среди побед; без побед — `HasVictory = false`.
- **З4 (Р). Таблица рекордов.** 5 лучших учитываемых результатов по счёту, при равенстве — раньше записанный выше. Без имён игроков: строка «N. Герой, эшелон K, победа|поражение» (в атласе PT Sans нет «·») и счёт справа. Заголовки «Рекорды» / «Счёт» (в оригинале «Друзья» / «Счёт»). Пусто — «Пока нет результатов». Место — справа от кнопки «Играть» в главном меню.
- **З5 (Р). Карточка эшелона.** У открытой карточки, если выбранный герой играл эшелон: строка внизу «Рекорд 330 000, побед 1». Иначе пусто. У закрытой — «НЕДОСТУПНО» на том же месте.
- **З6 (Р). Тестовый профиль.** `AppFlow.OpenAllEchelons()` добавляет `echelon_2`…`echelon_N` в профиль `test` и пишет его один раз, если что-то добавлено; возвращает число добавленных. Журнал не трогается: это не награда, а тестовая настройка. Для профиля `user` — `InvalidOperationException` до любых изменений.
- **З7 (Р). Аргумент `-thornProfile=test|user`.** Без `-thornSmoke`: `test` — тестовый профиль со всеми эшелонами, `user` или без аргумента — пользовательский. С `-thornSmoke`: `test` — то же, `user` — выход с кодом 18 (smoke не трогает файл игрока, §20 П11); другое значение — предупреждение и умолчание. С `-thornFlow`/`-thornExpectFlow` открытие всех эшелонов не выполняется: проверка Ф13 требует свежего профиля.
- **З8 (Р). Smoke `-thornProfile=test`.** Меню → герой → сложность: все 8 карточек нажимаются, предвыбран эшелон 8 (Ф6), забег на нём. Строка `THORN_TEST_PROFILE ok=1 unlocked=8 echelon=8 records=<строк в таблице>`; при ошибке `ok=0 failed=<шаг>` и код 18. Затем обычные 30 с smoke.
- **З9 (Р). Проверка файла игрока.** `Tools/mac-smoke.sh --test-profile` берёт `CFBundleIdentifier` плеера, каталог `~/Library/Application Support/<id>/Saves` (так macOS-плеер строит `persistentDataPath`; строка `THORN_PROFILE … root=` в логе должна с ним совпасть), снимает список и SHA-256 файлов `user.save*` до и после запуска. Изменение — код 18.
- **З10 (Р). Цепочка 1→8** доказывается dotnet-тестом обычными победами через `AppFlow` (`DebugWin` заканчивает забег победой, как в `AppFlowTests`), без `OpenAllEchelons`.

## 3. Код
- `Scripts/App/Records.cs` (новый, сборка `ThornThrone.App`): `HeroEchelonRecord` (Hero, Echelon, Runs, Victories, BestScore, HasVictory, FastestVictorySeconds), `RecordRow` (Result, Echelon), статический `Records`: `Of(GameData, Profile, hero, echelon)`, `Top(GameData, Profile, count)`, `TryEchelon(difficulty, count, out n)`. Без LINQ не обязательно (не горячий цикл), но в стиле соседнего кода — циклы.
- `AppFlow`: `OpenAllEchelons()`, `RecordOf(hero, echelon)`, `TopRecords(count)` — тонкие обёртки над `Records` с данными и профилем потока.
- `Scripts/Mobile/RecordsText.cs` (новый): `Menu(rows, heroTitle)`, `Card(record)` — чистые функции, формат чисел `ResultsText.Integer`.
- `GameUi`: имена героев в статических `HeroKeys`/`HeroTitles` + `HeroTitle(key)`; метка «Records» в меню, `ShowRecords(AppFlow)`; метка «Record» на карточке эшелона, заполняется в `ShowDifficulty`.
- `FlowScreens.Show`: меню показывает и таблицу рекордов.
- `MobileGame`: разбор `-thornProfile`, вызов `OpenAllEchelons`, smoke-шаг `TestProfileSmoke`, `root=` в строке `THORN_PROFILE`.
- `Tools/mac-smoke.sh`: `--test-profile`, `--check-test-profile-log`, код 18.

## 4. Риски
- Таблица и карточка строятся при показе экрана (выделения памяти вне забега допустимы, ТЗ §11).
- Профиль с тысячами результатов: подсчёт линейный при открытии меню — терпимо.
- Smoke на эшелоне 8 тяжелее (все модификаторы): герой может погибнуть до 30 с; это не ошибка, smoke требует только убийств.

## 5. Тесты
- dotnet `UnlocksRecordsTests`: `EchelonChainOneToEightOnOneProfile`, `ChainSurvivesReload`, `RecordsGroupResultsByHeroAndEchelon`, `RecordsSkipIncomparableResults`, `TopRecordsSortByScoreThenOrder`, `RecordedRunCountsInItsRecord`, `OpenAllEchelonsOpensTwoToEightOnce`, `OpenAllEchelonsRefusesUserProfile`, `TestProfileNeverWritesUserFiles`.
- EditMode: `RecordsTextTests`; `ScreenFlowTests.MenuShowsRecords`, `ScreenFlowTests.EchelonCardShowsHeroRecord`.
- Smoke: `Tools/mac-smoke.sh --test-profile`; регрессия — обычный smoke и `--flow`.
