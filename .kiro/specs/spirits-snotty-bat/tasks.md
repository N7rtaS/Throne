# Задачи: Spirits, Snotty Bat (Task 25)
- [x] 1. Тесты `CompanionPoolTests` (Т1), затем `Companion`, события `CompanionSpawned`/`CompanionEnded`, `Rules.MaxCompanions`, `ICompanionAbility`, `Run.Companions.cs`, перегрузка `Emit`, 4 строки в `Run.cs` (Т1.1–1.6, Т4.1).
- [x] 2. Тесты `SpiritsTests` (ранги, призыв, круг, вращение, касание, взрыв, `DurationExtension`, снимок), затем `Abilities/Spirits.cs`, строка в фабрике (Т2, Т4.2).
- [x] 3. Тесты `SnottyBatTests` (ранги, тело `snotty`, орбита, скорость, удар, замедление, подбор, упавший герой), затем `Abilities/SnottyBat.cs`, `Run.CollectPickup`, строка в фабрике (Т3, Т4.2).
- [x] 4. Общие тесты: пауза, выбор, выделения памяти, бот-забег на 5 seed (Т1.6, Т4.3, Т4.4, Т4.7).
- [x] 5. `Tools/audit.py`: роль `summon`, уровень реализации призванного врага; `Tools/tests/test_audit.py`; `python3 Tools/audit.py`, `python3 -m unittest discover -s Tools/tests` (Т4.5).
- [x] 6. Представление: духи и мышь в `WorldRenderer` (облик `snotty`), `CompanionPoint`, EditMode-тест; `Tools/unity-tests.sh` (Т4.6).
- [x] 7. Mechanics §32 (Р1–Р19), §25 Р5 (закрыть вопрос Spirits), §1 (роль, строка про `CSurvivorsEnemyDefinition_Snotty`), Differences Б17, `product.md` не трогать (обновляет проверяющий).
- [x] 8. Полный `dotnet test`, smoke `Tools/mac-smoke.sh -thornForce=spirits,snotty_bat` и обычный smoke (Т4.7).
- [x] 9. Roadmap, коммит.
