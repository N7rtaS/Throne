# Документ требований к исправлению
## Introduction
EditMode-тест `HudUpdateDoesNotAllocate` (`UnityProject/Assets/Game/Tests/EditMode/HudTextTests.cs`, цель Task 10 «HUD без аллокаций») после Task 13 падал примерно в 2 прогонах `Tools/unity-tests.sh` из 10. В упавших прогонах замер насчитывал одно лишнее выделение GC.Alloc в кадре, где не менялась ни одна надпись: `frame 272, time 22.10, hud 1, canvas 0, changed labels:` (фаза с боссом, окно `UpdateHud`) и в другом прогоне лишнее выделение в окне перестройки Canvas на кадре 230. Симуляция детерминирована (seed 7), но кадр сбоя каждый раз другой.
Кроме того, ноль выделений HUD в собранном плеере не был измерен: он выводился только из исходника TMP (зеркало строки в `TMP_Text.SetText` есть только под `#if UNITY_EDITOR`).
Обозначения:
- «окно» — один замер `Recorder("GC.Alloc")`, отфильтрованного на главный поток, вокруг `GameUi.UpdateHud` или вокруг `Canvas.ForceUpdateCanvases`;
- «ожидаемое число» — 2 выделения на каждую изменившуюся надпись в окне `UpdateHud` (редакторное зеркало строки TMP) и 0 в окне Canvas;
- «шум редактора» — выделение, которое Recorder засчитал окну, хотя код главного потока в окне ничего не выделял.
## Bug Analysis
### Current Behavior (Defect)
1.1 WHEN `HudUpdateDoesNotAllocate` выполняется в batchmode-редакторе и фоновые потоки редактора выделяют управляемую память во время окна, THEN the system изредка засчитывает окну лишнее выделение (в пробе — даже окну с пустым делегатом) и тест падает в случайном кадре
1.2 WHEN `HudUpdateWithoutChangesDoesNotAllocate` выполняет своё единственное окно (`Is.Not.AllocatingGCMemory`) в такой же момент, THEN the system может так же засчитать выделение, которого нет в коде HUD
1.3 WHEN нужно подтвердить цель Task 10 в плеере, THEN the system не даёт способа измерить выделения HUD в собранном плеере: smoke-сборка всегда Development, ключа замера нет
### Expected Behavior (Correct)
2.1 WHEN в одном прогоне сценария окно `UpdateHud` или Canvas отличается от ожидаемого числа, THEN the system SHALL повторить детерминированный сценарий (до 3 прогонов) и считать тест проваленным только если то же окно (фаза, кадр, вид окна) отличается от ожидаемого в двух прогонах; чистый прогон завершает проверку
2.2 WHEN `HudUpdateWithoutChangesDoesNotAllocate` видит выделение в окне без изменения состояния, THEN the system SHALL повторить то же окно (до 3 раз) и провалить тест, только если выделение есть в каждом из них
2.3 WHEN тест провален, THEN the system SHALL показать контекст всех прогонов, включая число выделений всех потоков по счётчику `GC Allocation In Frame Count`
2.4 WHEN игрок запущен с `-thornSmoke -thornHudAlloc`, THEN the system SHALL после прогрева (с 5 до 25 с времени забега) измерить байты, выделенные в каждом вызове `GameUi.UpdateHud` и в последующем `Canvas.ForceUpdateCanvases`, откалибровать замер тем же числом окон с известным выделением и записать в лог строку `THORN_HUD_ALLOC bytes=… canvas=… frames=… changes=… method=… calibration=…`
2.5 WHEN `Tools/mac-smoke.sh --hud-alloc`, THEN the system SHALL собрать плеер без Development Build (`MobileBuilder.BuildMacSmoke` с `-thornRelease`, `Builds/MacRelease`), запустить его с `-thornHudAlloc` и завершиться кодом 11, если строки `THORN_HUD_ALLOC` нет, `bytes` или `canvas` не 0, `frames`, `changes` или `calibration` не больше 0 либо `method=none`
### Unchanged Behavior (Regression Prevention)
3.1 WHEN код HUD выделяет память в каком-либо кадре сценария (каждый кадр или однократно), THEN the system SHALL CONTINUE TO проваливать `HudUpdateDoesNotAllocate`: выделение повторяется в том же окне каждого прогона
3.2 WHEN надпись меняется, THEN the system SHALL CONTINUE TO допускать в редакторе ровно 2 выделения на надпись в окне `UpdateHud` и 0 в окне Canvas; меньше 2 на надпись — немедленный провал
3.3 WHEN сценарий выполняется, THEN the system SHALL CONTINUE TO проверять, что время меняется не реже 9 раз за 10 с без босса и с боссом, число убийств меняется без босса, HP босса меняется и метка босса показана
3.4 WHEN игра запущена без `-thornHudAlloc`, THEN the system SHALL CONTINUE TO вызывать `GameUi.UpdateHud` один раз в кадр без дополнительного `Canvas.ForceUpdateCanvases` и без замеров
3.5 WHEN `Tools/mac-smoke.sh` запущен без `--hud-alloc`, THEN the system SHALL CONTINUE TO собирать Development-плеер в `Builds/MacSmoke` и проверять лог с прежними кодами 2–10
3.6 WHEN `HudText.Set` получает прежнее значение, THEN the system SHALL CONTINUE TO не трогать надпись и возвращать `false`
## Шаги воспроизведения
1. Временно добавить в `Assets/Game/Tests/EditMode` пробу: 12 раз подряд сценарий `HudUpdateDoesNotAllocate` (seed 7), в каждом кадре четыре окна — пустой делегат, `UpdateLayout`, `UpdateHud`, `Canvas.ForceUpdateCanvases`.
2. Запустить только её: `Unity -batchmode -nographics -projectPath UnityProject -runTests -testPlatform EditMode -testFilter <имя пробы>`.
3. Результат пробы (два запуска): 7 и 2 окна с лишним выделением на ~34 000 окон, среди них 4 окна пустого делегата, в разных кадрах и повторах; без лишних выделений в остальных повторах.
4. Отдельная проба: 100 000 окон с циклом сложения float без выделений — счётчик всех потоков `GC Allocation In Frame Count` насчитал 248 выделений в 76 окнах (их сделали другие потоки), отфильтрованный Recorder засчитал одно из них окну главного потока.
