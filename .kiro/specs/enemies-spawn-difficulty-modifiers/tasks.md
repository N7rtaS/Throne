# Task 29. Задачи

Решения В1–В11 утверждены владельцем 12.11.2026 («дальше»).

- [x] 1. Перенос без изменения поведения: `Run.Spawning.cs`, `Run.Enemies.cs`; `Tools/audit.py` ищет `SpawnPosition` в `Run*.cs`; полный `dotnet test` и `python3 -m unittest discover -s Tools/tests` зелёные (В11).
- [x] 2. Тесты до кода (`EnemyFieldTests`, `SpawnRuleTests`, `DifficultyTests`, элитные комнаты, неиспользуемые записи, бот по 8 сложностям и `echelon_8`). Убедиться, что падают по нужной причине (Т1–Т8). Тесты написаны до кода; до реализации они не компилировались (нет новых полей и API), отдельный прогон «красных» тестов не делался.
- [x] 3. Данные: новые поля `EnemyDef`, `SpawnerDef`, `EnemyAttackDef`, `PickupDef`; `Diagnostics.Keys`; числа **Р** в `Rules` (design §1–§4).
- [x] 4. Поворот врагов по `m_flTurnRate` и множителю сложности; `WorldRenderer.Facing` по курсу, EditMode-тест (Т1).
- [x] 5. Перелив, сброс интервала при убийстве, спавнер разрушаемых, буфер деспавна и перенос неисчезаемых, мин. дистанция расталкивания (Т2).
- [x] 6. Масса в расталкивании, смещение выстрела вперёд (Т3).
- [x] 7. Начальная скорость подбираемых (Т4).
- [x] 8. Замедление от касания, метеоры сложности, удалить диагностику `difficulty:meteor`/`difficulty:slow` (Т5).
- [x] 9. `CloseEliteRooms` при начале боя с Империей, `EliteRoomState.Closed` (Т6).
- [x] 10. Заметки неиспользуемых записей в `Tools/audit.py`, перегенерировать `ContentRegistry.json`, все 8 сложностей не ниже `implemented` (Т7, Т8.4).
- [x] 11. `Mechanics.md` §36 (правила Р этой задачи), правки §5.3, §6, §8, §10, §14; `Differences.md` A18.
- [x] 12. Прогоны: `dotnet test DotNet/ThornThrone.Simulation.Tests`, тесты инструментов, `Tools/unity-tests.sh` (Т8).
- [x] 13. `roadmap.md` — отметка Task 29; коммит.
