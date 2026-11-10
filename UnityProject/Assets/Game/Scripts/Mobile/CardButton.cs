using System;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ThornThrone.Mobile
{
    /// <summary>
    /// Level-up card: selects on release like a Button, but only for a pointer that went down while the touch
    /// filter accepted it, so a finger held from the gameplay does not pick a card
    /// (ТЗ_Мобильная_версия_v2.md §6; design: .kiro/specs/touch-controls-contract/design.md §3.6).
    /// No ISubmitHandler: a keyboard Enter does not pick a card.
    /// </summary>
    public sealed class CardButton : Selectable, IPointerClickHandler
    {
        public event Action Clicked;
        /// <summary>Touch filter by pointer id (<see cref="TouchController.CardAccepts"/>); null accepts all.</summary>
        public Func<int, bool> Accepts;
        private bool pressAccepted;

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            pressAccepted = Accepts == null || Accepts(PointerId(eventData));
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !IsActive() || !IsInteractable()) return;
            bool accepted = pressAccepted;
            pressAccepted = false;
            if (accepted) Clicked?.Invoke();
        }

        /// <summary>
        /// Id of the pointer as the touch controller sees it: the Input System touch id (the same as
        /// EnhancedTouch.Touch.touchId), or <see cref="TouchController.MouseId"/> for the mouse.
        /// </summary>
        public static int PointerId(PointerEventData eventData) =>
            eventData is ExtendedPointerEventData e && e.pointerType == UIPointerType.Touch ? e.touchId : TouchController.MouseId;
    }
}
