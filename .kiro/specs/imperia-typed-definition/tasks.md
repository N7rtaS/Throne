# План: типизированные параметры Империи

- [x] 1. Тесты до кода в `BossTests` (`SimulationTests.cs`): `ImperiaDefinitionIsReadFromData`, `MissingImperiaFieldFailsLoading`, `BossStepsDoNotAllocate`; запустить, убедиться в падении — _Требования 1, 2, 3, 6_
  - Тесты данных не компилировались (нет `EnemyDef.Imperia`). `BossStepsDoNotAllocate` прошёл и на старом коде: приведения `JToken` к `float` в Newtonsoft не выделяют память. Тест оставлен как защита от регрессии.
- [x] 2. `ImperiaDef` в `Content/Definitions.cs`, поле `EnemyDef.Imperia` — _Требования 1, 3_
- [x] 3. `GameData.ReadImperia` с обязательными полями и `InvalidDataException` — _Требования 1, 2_
- [x] 4. Параметры без данных в `Rules` с пометкой **Р** — _Требование 5_
- [x] 5. `Run.Boss.cs` и `Run.cs`: поля `ImperiaDef` и `Rules` вместо `Raw`/`F`/`Floats`, удалить помощники — _Требования 4, 7_
- [x] 6. Полный `dotnet test`, все тесты зелёные (77) — _Требования 6, 7_
- [x] 7. `Mechanics.md` §7: таблица чисел **Р** из `Rules`; отметка Task 4 в `roadmap.md`; коммит
