using System;
using System.Collections;
using TMPro;
using Unscrew.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Unscrew.Game
{
    /// <summary>Coin pill bound to IWallet. Can be held (coin fly in progress) and then counted up.</summary>
    public sealed class CoinCounter : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private RectTransform icon;
        [SerializeField] private Button plusButton;

        private IWallet _wallet;
        private int _shown;
        private bool _held;
        private Coroutine _count;

        public RectTransform Icon => icon;
        public event Action PlusClicked;

        public void Bind(IWallet wallet)
        {
            if (_wallet != null) _wallet.Changed -= OnChanged;
            _wallet = wallet;
            _wallet.Changed += OnChanged;
            if (plusButton != null) plusButton.onClick.AddListener(() => PlusClicked?.Invoke());
            Show(wallet.Coins);
        }

        private void OnDestroy()
        {
            if (_wallet != null) _wallet.Changed -= OnChanged;
        }

        private void OnEnable()
        {
            if (_wallet != null && !_held) Show(_wallet.Coins);
        }

        /// <summary>Freeze the displayed value at <paramref name="value"/> until <see cref="CountUp"/>.</summary>
        public void Hold(int value)
        {
            _held = true;
            Show(value);
        }

        /// <summary>Counts from the shown value to the wallet balance.</summary>
        public void CountUp(float duration = 0.6f)
        {
            _held = false;
            if (!isActiveAndEnabled) { Show(_wallet.Coins); return; }
            if (_count != null) StopCoroutine(_count);
            _count = StartCoroutine(CountRoutine(_shown, _wallet.Coins, duration));
        }

        /// <summary>Tick on every coin that lands.</summary>
        public void Bump()
        {
            if (isActiveAndEnabled && icon != null) StartCoroutine(UiTween.Punch(icon, 0.25f, 0.15f));
        }

        private void OnChanged(int coins)
        {
            if (_held || !isActiveAndEnabled) { if (!_held) Show(coins); return; }
            if (_count != null) StopCoroutine(_count);
            _count = StartCoroutine(CountRoutine(_shown, coins, 0.35f));
        }

        private IEnumerator CountRoutine(int from, int to, float duration)
        {
            yield return UiTween.Run(duration, k => Show(Mathf.RoundToInt(Mathf.Lerp(from, to, k))));
            if (to > from) yield return UiTween.Punch(transform, 0.08f, 0.2f);
            _count = null;
        }

        private void Show(int value)
        {
            _shown = value;
            label.text = value.ToString();
        }
    }
}
