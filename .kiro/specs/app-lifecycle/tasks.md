# План: жизненный цикл приложения (Task 15)
- [x] 1. Тесты до кода в `Tests/EditMode/AppLifecycleTests.cs` (список в `design.md` §5); запустить `Tools/unity-tests.sh`, убедиться, что падают из-за отсутствия `AppLifecycle`, `RetryFailedSave`, `RunLossWarning` — _Требования 1–7, 10, 11_
- [x] 2. `Scripts/Mobile/AppLifecycle.cs`: признаки фона и фокуса, одна приостановка и одно возобновление на переход, `FrameDelta` с отбрасыванием одного кадра — _Требования 1–3, 5_
- [x] 3. `GameSettings.RetryFailedSave()` — _Требование 10_
- [x] 4. `GameUi`: строка `RunLossWarning` на экране паузы — _Требование 11_
- [x] 5. `MobileGame`: пересылка событий в `AppLifecycle` (с проверкой `smoke`), `Suspend` (звук, `LoseFocus`, повтор записи), `FrameDelta` в каждом кадре, возобновление звука — _Требования 4, 6–8_
- [x] 6. `Tools/unity-tests.sh` — все EditMode-тесты зелёные; полный `dotnet test`; `Tools/mac-smoke.sh` — _Требование 9, критерии приёмки_
- [x] 7. `Mechanics.md` §22 (Р1–Р7), `Differences.md` Б10, `product.md` «Текущее состояние», отметить задачу в `roadmap.md`, коммит
