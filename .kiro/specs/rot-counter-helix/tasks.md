# Задачи: Rot и Counter Helix (Task 21)
- [x] 1. Тесты Rot (`RotTests`) до кода: реестр и диагностики, урон по данным, удар при входе, личная перезарядка (30 и 18 шагов), два врага, выход и возврат, край круга и промах, `AoEIncrease`, ранги 2, 3, 9, пауза и выбор, без отталкивания. _Т1, Т2, Т4.1, Т5.1_
- [x] 2. Тесты Counter Helix (`CounterHelixTests`): нет применения без цели, задержка `InitialTickDelay`, урон и отталкивание, перезарядка 3.5 с, ранги 2, 3, 9, событие `AbilityBurst`, нет срабатывания от урона по герою, пауза. _Т3, Т4.1_
- [x] 3. Тест пропуска врага с занятым слотом, бот-забег на 5 seed, вариант `StepsDoNotAllocateAfterWarmUp`. _Т1.3, Т4.2, Т5.2_
- [x] 4. `Abilities/CircleTargets.cs`. _Т1_
- [x] 5. `Abilities/Rot.cs`, `Abilities/CounterHelix.cs`, строки в `AbilityFactory`, `SimEventKind.AbilityBurst`, `Run.EmitBurst`. _Т2, Т3, Т5_
- [x] 6. `dotnet test` полностью зелёный.
- [x] 7. Unity: круг Rot и диск `AbilityBurst` в `WorldRenderer`, `-thornForce=` и `THORN_FORCE` в `MobileGame`, код 14 в `Tools/mac-smoke.sh`; EditMode-тесты. _Т6_
- [x] 8. `Tools/tests/test_audit.py` (классы фабрики), `python3 -m unittest discover -s Tools/tests`, `Tools/unity-tests.sh`, `Tools/mac-smoke.sh -thornForce=rot,counter_helix`, обычный smoke. _Т6.3_
- [x] 9. Mechanics §28 (Р1–Р11), Differences Б13, README/Tools: `-thornForce`. Отметка в roadmap, коммит.
