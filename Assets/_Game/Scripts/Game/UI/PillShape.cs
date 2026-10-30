using UnityEngine;
using UnityEngine.UI;

namespace Unscrew.Game
{
    /// <summary>
    /// Keeps a 9-sliced circle sprite a true pill (radius = half the height): Unity shrinks slice borders per axis,
    /// which turns the ends of wide, short pills into ellipses; scaling the sprite's pixels-per-unit by the height keeps
    /// both borders at height / 2. Runs only when the rect size changes.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Image))]
    public sealed class PillShape : MonoBehaviour
    {
        private Image _image;

        private void OnEnable() => Apply();
        private void OnRectTransformDimensionsChange() => Apply();

        private void Apply()
        {
            if (_image == null) _image = GetComponent<Image>();
            var sprite = _image.sprite;
            float h = ((RectTransform)transform).rect.height;
            if (sprite == null || h <= 0f) return;
            float m = sprite.rect.height / h;
            if (!Mathf.Approximately(_image.pixelsPerUnitMultiplier, m)) _image.pixelsPerUnitMultiplier = m;
        }
    }
}
