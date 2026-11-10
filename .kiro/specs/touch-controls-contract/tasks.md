# Задачи: контракт сенсорного управления (Task 11)

- [x] 1. Тесты до кода: `Tests/EditMode/TouchControllerTests.cs` — привязка и два пальца, переходы пальцев, мёртвая зона и длина, фиксированный джойстик, зеркало, отмена, `Reset`, `overUi`, карточки, dp, «Назад». Запуск `Tools/unity-tests.sh`: не компилируются (типов нет). (Тр. 1–8)
- [x] 2. `Scripts/Mobile/TouchController.cs`: `TouchController`, `TouchSettings`, `TouchLayout`, `TouchMetrics`, `BackNavigation`; хранилище на массиве без выделений. Тесты из п. 1 зелёные. (Тр. 1.1, 2, 3.1–3.4, 4, 5.1, 6, 7.1, 8.1)
- [x] 3. `GameInput` поверх контроллера: фазы `EnhancedTouch`, `overUi` через `IsPointerOverGameObject(touchId)`, мышь, клавиатура, сброс при смене экрана, удалить `Blocked`. (Тр. 1.2, 3.5, 5.2, 5.3, 9)
- [x] 4. `GameUi`: `TouchLayout` из safe area и кнопки рывка с зеркалом, `ApplyLayout(dpi, scale)` с минимумом 48 dp, `CardButton`, панель подтверждения выхода. Тест `InteractiveTargetsAreAtLeast48Dp`. (Тр. 4, 6.2–6.3, 7.2–7.3, 8.2)
- [x] 5. `MobileGame`: `[DefaultExecutionOrder(100)]`, состояние `ConfirmQuit`, `BackNavigation`, `Reset` в переходах и жизненном цикле, `OpenCards` при каждом показе карточек. (Тр. 5.1, 6.1, 8, 9)
- [x] 6. `InputScript` и `-thornInputScript`, сценарий `Tools/smoke/input-two-fingers.txt`. (Тр. 10)
- [x] 7. Проверки: `Tools/unity-tests.sh` (все зелёные, `HudUpdateDoesNotAllocate` тоже), `dotnet test DotNet/ThornThrone.Simulation.Tests`, `Tools/mac-smoke.sh` без аргументов и со сценарием. (Критерии)
- [x] 8. Документы: `Mechanics.md` §18, `Differences.md` Б1.1, `README.md` (сценарий ввода), `product.md` (число EditMode-тестов). (Тр. 11)
- [x] 9. Отметить Task 11 в `roadmap.md`, один коммит по правилам `tech.md` (`Roadmap: Task 11`).
