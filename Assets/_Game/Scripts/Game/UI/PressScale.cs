using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Unscrew.Game
{
    /// <summary>Button feel: shrinks while pressed, springs back on release.</summary>
    public sealed class PressScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField] private float pressed = 0.92f;

        private Selectable _selectable;
        private float _target = 1f;
        private float _scale = 1f;

        private void Awake() => _selectable = GetComponent<Selectable>();

        private void OnDisable()
        {
            _target = _scale = 1f;
            transform.localScale = Vector3.one;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (_selectable == null || _selectable.interactable) _target = pressed;
        }

        public void OnPointerUp(PointerEventData e) => _target = 1f;
        public void OnPointerExit(PointerEventData e) => _target = 1f;

        private void Update()
        {
            if (Mathf.Approximately(_scale, _target)) return;
            _scale = Mathf.MoveTowards(_scale, _target, Time.unscaledDeltaTime * 2.2f);
            transform.localScale = Vector3.one * _scale;
        }
    }
}
