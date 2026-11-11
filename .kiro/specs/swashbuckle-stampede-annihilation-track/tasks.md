# Задачи: Swashbuckle, Stampede, Annihilation, Track (Task 23)

- [x] 1. Тесты до кода: `SwashbuckleTests` (реестр, ранги 1/2/3/9, горизонтальные направления при любом `Facing`, 2 удара через 0.18 с, отталкивание, край, стык, без врагов нет серии, прямоугольник за героем, события). _Т1, Т3, Т7.1_
- [x] 2. Тесты `StampedeTests` (реестр, ранги, скорость и возврат, неуязвимость к касанию и снарядам, один удар на врага за набег, отталкивание от героя, пауза, `DurationExtension` по решению Р10). _Т4_
- [x] 3. Тесты `AnnihilationTests` (реестр, ранги, 50 из 80, все из 10, дальность, seed, только цели) и общие случаи `RandomTargets`. _Т2, Т5_
- [x] 4. Тесты `TrackTests` (реестр, ранги, самый сильный, урон и ×1.2 после метки, длительность, Империя, золото помеченного по Р20, враг без наград). _Т6_
- [x] 5. Общие тесты: пауза и выбор, `StepsDoNotAllocateAfterWarmUp` со всеми четырьмя, бот-забег на 5 seed; запуск — новые тесты падают по нужной причине. _Т7.3, Т7.4_
- [x] 6. `Abilities/RectangleTargets.cs`, перевод `EchoStrike`; `EchoStrikeTests` зелёные. _Т1_
- [x] 7. `Abilities/RandomTargets.cs`, наведение в `InstantAttack`, `Supports`; `KezInnateTests` зелёные. _Т2, Т5_
- [x] 8. Новые id в `Stats/Attributes.cs`; `Run.Charge.cs` и строки в `UpdateHero`/`DamageHero`; флаг метки в `Kill`/`DropRewards`. _Т4.2, Т4.3, Т6.3_
- [x] 9. `Abilities/Swashbuckle.cs`, `Stampede.cs`, `Track.cs`, строки в `AbilityFactory`. _Т3, Т4, Т6, Т7.1_
- [x] 10. `dotnet test DotNet/ThornThrone.Simulation.Tests` полностью зелёный.
- [x] 11. `Tools/tests/test_audit.py` (классы фабрики, Annihilation), `python3 -m unittest discover -s Tools/tests`, `python3 Tools/audit.py`. _Т7.5_
- [x] 12. Mechanics §30 (Р1–Р21), закрыть §25 Р5 для Stampede и §26 Р15; Differences Б15 (диски и прямоугольники вместо частиц, неуязвимость и метка не видны); отметка в roadmap, коммит. _Т7.6 проверяет проверяющий smoke `-thornForce=swashbuckle,stampede,annihilation,track`_
