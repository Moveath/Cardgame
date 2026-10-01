using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CardGameClient
{
    /// <summary>A new pointer-down is required; a finger held before the signal cannot slap.</summary>
    public sealed class GovnoTapTarget : MonoBehaviour, IPointerDownHandler
    {
        public Action Press;
        public bool Enabled = true;
        public void OnPointerDown(PointerEventData eventData)
        {
            if (Enabled && eventData.button == PointerEventData.InputButton.Left) Press?.Invoke();
        }
    }
}
