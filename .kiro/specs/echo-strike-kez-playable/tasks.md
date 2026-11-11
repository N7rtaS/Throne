# Задачи: Echo Strike и врождённая Kez (Task 19)

- [x] 1. Тесты Echo Strike (`EchoStrikeTests`) до кода: стартовый набор, урон по данным, область и промахи, `AoEIncrease`, повтор по той же области, ранги 2 и 9, отталкивание, пауза/выбор, нет применения без цели, без выделений. _Т1, Т2_
- [x] 2. Тесты врождённой Kez (`KezInnateTests`): самая сильная цель, радиус метки, параметры метки и `DurationExtension`, крит ×2 и оглушение от Echo Strike, нет урона, скорость 300, Annihilation не реализована; замена тестов Task 3. _Т3, Т5.2_
- [x] 3. Бот-забег Kez на 5 seed. _Т5.1_
- [x] 4. `AbilityFactory.Has(PowerUpDef)` с проверкой наведения, переход `Run.IsImplemented` / `CreateAbilities`. _Т3.6_
- [x] 5. `Abilities/EchoStrike.cs`: перезарядка, очередь на фиксированных массивах, прямоугольник, урон, отталкивание, событие `AbilityStrike` (`SimEventKind`, `SimEvent.Extent`, `Emit`). _Т2_
- [x] 6. `Abilities/InstantAttack.cs`: выбор самых сильных, радиус, метка, урон при > 0. _Т3.1–3.4_
- [x] 7. `Build.Recalculate`: `MovementSpeed` врождённой способности в `Global`. _Т3.5_
- [x] 8. `dotnet test` полностью зелёный.
- [x] 9. Unity: `PlayableHeroes` += kez, подпись «Кез», капсула для героя без модели, прямоугольник удара и кольцо метки в `WorldRenderer`, `-thornHero=` в `SmokeStart`; EditMode-тесты. _Т4_
- [x] 10. `Tools/unity-tests.sh`, `Tools/mac-smoke.sh -thornHero=kez`, обычный smoke, скриншот в `Research/Скриншоты/`. _Т4.4_
- [x] 11. Mechanics §26 (Р1–Р16), правка §3 и §24 «Не сделано», Differences (капсула Kez, временные эффекты), README/Tools: `-thornHero`. Отметка в roadmap, коммит.
- [x] 12. Дополнительно: `Tools/audit.py` учитывает наведение обработчика (`AbilityFactory.supports`), Annihilation остаётся «none»; `Tools/tests/test_audit.py` обновлён (120 тестов).
