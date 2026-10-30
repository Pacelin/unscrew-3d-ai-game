using System;
using UnityEngine;
using UnityEngine.UI;

namespace Unscrew.Game
{
    /// <summary>iOS-like on/off switch (pill + knob) driven by a Button.</summary>
    public sealed class ToggleSwitch : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private Image track;
        [SerializeField] private RectTransform knob;
        [SerializeField] private Color onColor = new Color(0.3f, 0.78f, 0.36f);
        [SerializeField] private Color offColor = new Color(0.72f, 0.75f, 0.82f);
        [SerializeField] private float knobTravel = 44f;

        public bool Value { get; private set; }
        public event Action<bool> Changed;

        private void Awake() => button.onClick.AddListener(() => { Set(!Value); Changed?.Invoke(Value); });

        public void Set(bool on)
        {
            Value = on;
            track.color = on ? onColor : offColor;
            knob.anchoredPosition = new Vector2(on ? knobTravel : -knobTravel, 0f);
        }
    }
}
