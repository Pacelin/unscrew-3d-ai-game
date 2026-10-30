using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Unscrew.Game
{
    /// <summary>Level complete: reward from PlayerState.CompleteLevel, coins fly into the counter, Next.</summary>
    public sealed class WinScreen : UiPanel
    {
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text rewardText;
        [SerializeField] private RectTransform rewardIcon;
        [SerializeField] private CoinCounter coins;
        [SerializeField] private Button nextButton;
        [SerializeField] private Image coinTemplate;
        [SerializeField] private RectTransform flyLayer;
        [SerializeField] private int flyingCoins = 10;

        private readonly List<Image> _pool = new List<Image>();
        private Action _onFlown;

        public void Bind(UiRoot root)
        {
            coins.Bind(root.Player.Wallet);
            nextButton.onClick.AddListener(root.NextLevel);
            coinTemplate.gameObject.SetActive(false);
        }

        /// <summary><paramref name="onFlown"/> runs after the coins landed (unlock popup).</summary>
        public void Show(int levelNumber, int reward, int coinsAfter, Action onFlown)
        {
            Open();
            levelText.text = "Level " + levelNumber;
            rewardText.text = "+" + reward;
            coins.Hold(coinsAfter - reward);
            _onFlown = onFlown;
            StartCoroutine(FlyCoins(reward));
        }

        private IEnumerator FlyCoins(int reward)
        {
            yield return Wait(0.45f);
            var target = coins.Icon;
            int n = Mathf.Clamp(reward / 4, 4, flyingCoins);
            int landed = 0;
            for (int i = 0; i < n; i++)
            {
                StartCoroutine(FlyOne(GetCoin(i), target, () => { landed++; coins.Bump(); }));
                yield return Wait(0.05f);
            }
            while (landed < n) yield return null;
            coins.CountUp(0.4f);
            yield return Wait(0.35f);
            var cb = _onFlown;
            _onFlown = null;
            cb?.Invoke();
        }

        private Image GetCoin(int i)
        {
            while (_pool.Count <= i)
            {
                var c = Instantiate(coinTemplate, flyLayer);
                _pool.Add(c);
            }
            return _pool[i];
        }

        private IEnumerator FlyOne(Image coin, RectTransform target, Action onLand)
        {
            var t = coin.rectTransform;
            coin.gameObject.SetActive(true);
            Vector3 from = rewardIcon.position + (Vector3)(UnityEngine.Random.insideUnitCircle * 60f * rewardIcon.lossyScale.x);
            Vector3 to = target.position;
            Vector3 ctrl = Vector3.Lerp(from, to, 0.5f) + new Vector3(UnityEngine.Random.Range(-250f, 250f), 120f, 0f) * rewardIcon.lossyScale.x;
            yield return UiTween.Run(0.55f, k =>
            {
                float e = Anim.InOutQuad(k);
                t.position = Anim.Bezier(from, ctrl, to, e);
                t.localScale = Vector3.one * Mathf.Lerp(1.2f, 0.8f, e);
            });
            coin.gameObject.SetActive(false);
            onLand();
        }

        private static IEnumerator Wait(float s)
        {
            float t = 0f;
            while (t < s) { t += Time.unscaledDeltaTime; yield return null; }
        }
    }
}
