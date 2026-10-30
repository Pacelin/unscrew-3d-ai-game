using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>One buffer hole (Hole.prefab): recessed Cup with a Rim. Bolts are parented to the "Anchor" child.</summary>
    public sealed class HoleVisual : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color WarnCup = new Color(0.85f, 0.12f, 0.2f);
        private static readonly Color WarnRim = new Color(1f, 0.42f, 0.45f);

        public Transform Anchor { get; private set; }
        public BoltView Bolt { get; set; }

        private Renderer _cup;
        private Renderer _rim;
        private Color _cupColor;
        private Color _rimColor;
        private MaterialPropertyBlock _block;
        private float _warn = -1f;

        public void Init()
        {
            Anchor = transform.Find("Anchor");
            if (Anchor == null) Anchor = transform;
            _cup = Find("Cup");
            _rim = Find("Rim");
            _cupColor = _cup != null ? _cup.sharedMaterial.GetColor(BaseColorId) : Color.black;
            _rimColor = _rim != null ? _rim.sharedMaterial.GetColor(BaseColorId) : Color.white;
            _block = new MaterialPropertyBlock();
            SetWarning(0f);
        }

        private Renderer Find(string name)
        {
            var t = transform.Find(name);
            return t != null ? t.GetComponent<Renderer>() : null;
        }

        /// <summary>0 = normal, 1 = full red (last free hole pulse). Cheap no-op when unchanged.</summary>
        public void SetWarning(float k)
        {
            if (Mathf.Abs(k - _warn) < 0.01f) return;
            _warn = k;
            if (_cup != null)
            {
                _block.SetColor(BaseColorId, Color.Lerp(_cupColor, WarnCup, k));
                _cup.SetPropertyBlock(_block);
            }
            if (_rim != null)
            {
                _block.SetColor(BaseColorId, Color.Lerp(_rimColor, WarnRim, k));
                _rim.SetPropertyBlock(_block);
            }
        }

        public float Size => transform.lossyScale.x;
    }
}
