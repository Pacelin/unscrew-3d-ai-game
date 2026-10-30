using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// A screen or popup: root with a CanvasGroup (fade) and an optional "Window" child that pops in.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class UiPanel : MonoBehaviour
    {
        [SerializeField] protected RectTransform window;

        private CanvasGroup _group;
        private Coroutine _anim;

        public bool IsOpen => gameObject.activeSelf;

        protected CanvasGroup Group => _group != null ? _group : (_group = GetComponent<CanvasGroup>());

        public virtual void Open()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Group.interactable = true;
            Group.blocksRaycasts = true;
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(window != null ? UiTween.PopIn(window, Group) : UiTween.Fade(Group, 0f, 1f));
        }

        public virtual void Close()
        {
            if (!gameObject.activeSelf) return;
            if (_anim != null) StopCoroutine(_anim);
            _anim = null;
            gameObject.SetActive(false);
        }
    }
}
