using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>One buffer hole. Bolts are parented to the "Anchor" child.</summary>
    public sealed class HoleVisual : MonoBehaviour
    {
        public Transform Anchor { get; private set; }
        public BoltView Bolt { get; set; }

        public void Init()
        {
            Anchor = transform.Find("Anchor");
            if (Anchor == null) Anchor = transform;
        }

        public float Size => transform.lossyScale.x;
    }
}
