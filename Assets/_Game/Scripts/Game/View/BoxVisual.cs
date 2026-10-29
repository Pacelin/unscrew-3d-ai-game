using System.Collections.Generic;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>Visual of one box on top. Slots are child anchors named Slot0..SlotN (from Box.prefab).</summary>
    public sealed class BoxVisual : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public int Color { get; private set; }
        /// <summary>Slots handed out to bolts (flying or landed).</summary>
        public int Reserved { get; private set; }
        /// <summary>Bolts still flying into this box.</summary>
        public int Incoming { get; set; }

        private readonly List<Transform> _slots = new List<Transform>();

        public void Init(int color, Color tint)
        {
            Color = color;
            _slots.Clear();
            for (int i = 0; ; i++)
            {
                var s = transform.Find("Slot" + i);
                if (s == null) break;
                _slots.Add(s);
            }
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, tint);
            var body = transform.Find("Body");
            if (body != null) body.GetComponent<Renderer>().SetPropertyBlock(block);
        }

        public Transform ReserveSlot()
        {
            var slot = _slots[Mathf.Min(Reserved, _slots.Count - 1)];
            Reserved++;
            return slot;
        }

        /// <summary>Box width in world units: the scale of the root.</summary>
        public float Width => transform.lossyScale.x;
    }
}
