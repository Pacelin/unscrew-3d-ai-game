using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using B = Unscrew.Game.UiTokens.Button;

namespace Unscrew.Game
{
    /// <summary>
    /// Button feel from the spec: the face (and its content) sinks onto the darker edge layer
    /// (translateY 4 design px, edge 6 -> 2) and the button shrinks to 0.96 while pressed.
    /// </summary>
    public sealed class PressScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField] private RectTransform face;
        [SerializeField] private RectTransform content;

        private Selectable _selectable;
        private float _target;
        private float _k;

        private void Awake() => _selectable = GetComponent<Selectable>();

        private void OnDisable()
        {
            _target = _k = 0f;
            Apply();
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (_selectable != null && !_selectable.interactable) return;
            _target = 1f;
            Feedback.Play(Sfx.Click, 0.7f);
        }

        public void OnPointerUp(PointerEventData e) => _target = 0f;
        public void OnPointerExit(PointerEventData e) => _target = 0f;

        private void Update()
        {
            if (Mathf.Approximately(_k, _target)) return;
            _k = Mathf.MoveTowards(_k, _target, Time.unscaledDeltaTime * B.PressSpeed);
            Apply();
        }

        private void Apply()
        {
            transform.localScale = Vector3.one * Mathf.Lerp(1f, B.PressedScale, _k);
            var offset = new Vector2(0f, -B.PressOffset * _k);
            if (face != null) face.anchoredPosition = offset;
            if (content != null) content.anchoredPosition = offset;
        }
    }
}
