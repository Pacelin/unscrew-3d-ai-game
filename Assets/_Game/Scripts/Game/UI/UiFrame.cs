using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// Content frame of a screen: a <see cref="UiTokens.ContentWidth"/>-wide column inside the safe area (+48-unit inset),
    /// placed from <see cref="UiLayout"/>. Children anchor to its top (top bar, boxes, holes) or bottom (booster panel).
    /// The parent must be stretched over the whole canvas.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class UiFrame : MonoBehaviour
    {
        private UiLayout _applied;
        private bool _hasApplied;

        private void OnEnable() => Apply(UiLayout.Current);

        private void Update()
        {
            var layout = UiLayout.Current;
            if (!_hasApplied || !layout.Equals(_applied)) Apply(layout);
        }

        public void Apply(UiLayout layout)
        {
            _applied = layout;
            _hasApplied = true;
            var rt = (RectTransform)transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = layout.Frame.position;
            rt.sizeDelta = new Vector2(UiTokens.ContentWidth, layout.LocalHeight);
            rt.localScale = new Vector3(layout.Scale, layout.Scale, 1f);
        }
    }
}
