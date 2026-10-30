using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Unscrew.Game
{
    /// <summary>Rewarded-ad stub: a fake 1.5 s "ad" with a progress bar, then the reward callback.</summary>
    public sealed class AdOverlay : UiPanel
    {
        [SerializeField] private Image progressFill;
        [SerializeField] private TMP_Text label;
        [SerializeField] private float duration = 1.5f;

        public bool IsPlaying { get; private set; }

        public void Play(Action onRewarded)
        {
            if (IsPlaying) return;
            Open();
            StartCoroutine(Routine(onRewarded));
        }

        private IEnumerator Routine(Action onRewarded)
        {
            IsPlaying = true;
            label.text = "Ad is playing...";
            yield return UiTween.Run(duration, k => progressFill.fillAmount = k);
            IsPlaying = false;
            Close();
            onRewarded?.Invoke();
        }
    }
}
