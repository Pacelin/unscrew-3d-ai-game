using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// Visual of one box on top (Box.prefab from GameSceneBuilder): rounded Body, per slot a recessed Cup{i} with a Rim{i}
    /// and an anchor Slot{i} for the bolt head, and a hidden Lid that snaps on when the box is complete.
    /// Everything is tinted from the palette color through MaterialPropertyBlocks.
    /// </summary>
    public sealed class BoxVisual : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public int Color { get; private set; }
        /// <summary>Slots handed out to bolts (flying or landed).</summary>
        public int Reserved { get; private set; }
        /// <summary>Bolts still flying into this box.</summary>
        public int Incoming { get; set; }
        /// <summary>Bolts that already landed.</summary>
        public int Landed { get; private set; }
        /// <summary>False while the box is sliding in (BoltActivated): buffered bolts wait so the slot-appear animation finishes first.</summary>
        public bool Ready { get; set; } = true;
        public Color Tint { get; private set; }

        private readonly List<Transform> _slots = new List<Transform>();
        private Transform _lid;
        private Transform _visual;
        private Coroutine _punch;

        public void Init(int color, Color tint)
        {
            Color = color;
            Tint = tint;
            // Children live under "Visual" so the squash/bounce never touches the root scale (TrayView layout).
            _visual = transform.Find("Visual");
            if (_visual == null) _visual = transform;
            _slots.Clear();
            for (int i = 0; ; i++)
            {
                var s = _visual.Find("Slot" + i);
                if (s == null) break;
                _slots.Add(s);
            }
            _lid = _visual.Find("Lid");
            if (_lid != null) _lid.gameObject.SetActive(false);

            var block = new MaterialPropertyBlock();
            Paint("Body", tint, block);
            var lidTint = UnityEngine.Color.Lerp(tint, UnityEngine.Color.white, 0.12f);
            Paint("Lid", lidTint, block);
            var cup = UnityEngine.Color.Lerp(tint * 0.42f, new Color(0.08f, 0.09f, 0.16f), 0.35f);
            cup.a = 1f;
            var rim = UnityEngine.Color.Lerp(tint, UnityEngine.Color.white, 0.5f);
            for (int i = 0; i < _slots.Count; i++)
            {
                Paint("Cup" + i, cup, block);
                Paint("Rim" + i, rim, block);
            }
        }

        private void Paint(string child, Color c, MaterialPropertyBlock block)
        {
            var t = _visual.Find(child);
            if (t == null) return;
            var r = t.GetComponent<Renderer>();
            if (r == null) return;
            block.SetColor(BaseColorId, c);
            r.SetPropertyBlock(block);
        }

        public Transform ReserveSlot()
        {
            var slot = _slots[Mathf.Min(Reserved, _slots.Count - 1)];
            Reserved++;
            return slot;
        }

        /// <summary>A bolt landed: small squash of the box.</summary>
        public void OnBoltLanded()
        {
            Landed++;
            if (!isActiveAndEnabled) return; // tray hidden (menu showcase)
            if (_punch != null) StopCoroutine(_punch);
            _punch = StartCoroutine(Punch(0.07f, 0.16f));
        }

        private IEnumerator Punch(float amount, float duration)
        {
            float k = 0f;
            while (k < 1f)
            {
                k += Anim.Delta / duration;
                float s = Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI) * amount;
                _visual.localScale = new Vector3(1f + s, 1f - s * 0.8f, 1f);
                yield return null;
            }
            _visual.localScale = Vector3.one;
            _punch = null;
        }

        /// <summary>Lid drops onto the box from above with a small overshoot (bolts get covered).</summary>
        public IEnumerator SnapLid(float duration)
        {
            if (_lid == null) yield break;
            var home = _lid.localPosition;
            _lid.gameObject.SetActive(true);
            var from = home + new Vector3(0f, 0.55f, -0.25f);
            float k = 0f;
            while (k < 1f)
            {
                k += Anim.Delta / duration;
                float e = Anim.OutBack(k);
                _lid.localPosition = Vector3.LerpUnclamped(from, home, e);
                _lid.localScale = Vector3.one * Mathf.Lerp(0.8f, 1f, Mathf.Clamp01(k * 1.5f));
                yield return null;
            }
            _lid.localPosition = home;
            _lid.localScale = Vector3.one;
        }

        /// <summary>Scale of the visual (not the root): completion bounce.</summary>
        public Transform Visual => _visual;

        /// <summary>Box width in world units: the scale of the root.</summary>
        public float Width => transform.lossyScale.x;
    }
}
