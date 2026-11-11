# План: глобальные атрибуты, пассивки, жизни (Task 18)

- [x] 1. Тесты до кода: `PassiveItemTests.cs`, `LivesTests.cs`, `LuckTests.cs` по `design.md` §5 (минимальные заглушки API: `Attr.*`, `Build.MaxActiveSlots`, `Run.LivesLeft`/`Downed`/`ReviveTimer`, `ModeDef.LuckyLoot`, `SimEventKind.HeroDowned`/`HeroRevived`, чтобы сборка шла); запустить, убедиться, что новые тесты падают по нужной причине, а старые зелёные — _Требования 1–20_
- [x] 2. `Stats/Attributes.cs`: `AoEIncrease`, `ActiveAbilitySlots`, `KnockbackDistanceMultiplier`, `DurationExtension`; `Run.SupportedHeroAttributes` (+14); `Run.IsImplemented` с авторскими записями — _Требования 1, 2_
- [x] 3. `Build.Recalculate`: авторские записи пассивок (Р1); `Build.MaxActiveSlots` в `CanUpgrade` (Р6); `KezInnateTests.ReferenceGlobal` по Р1 — _Требования 3–6, 11_
- [x] 4. `AbilityBase.Area`, `KnockbackDistance`, `EffectDuration`; `MagicMissile` (радиус, оглушение) и `ArcaneBolt` (отталкивание) на них — _Требования 7–10_
- [x] 5. `Run.Lives.cs`: падение, неуязвимость, возрождение, события; `DamageHero` → `HeroDown`; 3–4 строки в `Tick`, `Heal` в падении — _Требования 13–18, 23_
- [x] 6. `ModeDef.LuckyLoot` и общий разбор коллекций в `GameData`; `DropCollections` и бросок удачи в `DropRewards` — _Требования 19, 20_
- [x] 7. `.meta` для `Run.Lives.cs` (тестам в `DotNet` не нужен); `Tools/audit.py` `passive()` с авторскими записями, `test_audit` (`blade_mail` не ниже `implemented`) — _Требование 2_
- [x] 8. Полный `dotnet test DotNet/ThornThrone.Simulation.Tests` (включая проверки выделений) и `python3 -m unittest discover -s Tools/tests`; записать в `design.md` §6 тесты, которые задела смена последовательности `Rng`; проверить прирост `Run.cs` — _Требования 21–23_
- [x] 9. `Mechanics.md`: §25 (Р1–Р12), §4 и §12.9 «Удача» → ссылка на §25 Р11, §24 «Не сделано» обновить; `Differences.md` A16 и строка «Не сделано»; отметить Task 18 в `roadmap.md`; коммит — _Требование 24_
