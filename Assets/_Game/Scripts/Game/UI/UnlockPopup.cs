using System;
using System.Collections.Generic;
using TMPro;
using Unscrew.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Unscrew.Game
{
    /// <summary>"New booster!" popup for each newly unlocked booster (with the free piece), shown one after another.</summary>
    public sealed class UnlockPopup : UiPanel
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text descText;
        [SerializeField] private TMP_Text giftText;
        [SerializeField] private Button okButton;

        private readonly Queue<BoosterType> _queue = new Queue<BoosterType>();
        private UiRoot _root;
        private Action _onDone;

        public void Bind(UiRoot root)
        {
            _root = root;
            okButton.onClick.AddListener(Next);
        }

        public void Show(IList<BoosterType> types, int freePieces, Action onDone)
        {
            _queue.Clear();
            foreach (var t in types) _queue.Enqueue(t);
            giftText.text = freePieces > 0 ? $"+{freePieces} free!" : "";
            _onDone = onDone;
            Next();
        }

        private void Next()
        {
            if (_queue.Count == 0)
            {
                Close();
                var cb = _onDone;
                _onDone = null;
                cb?.Invoke();
                return;
            }
            var t = _queue.Dequeue();
            icon.sprite = _root.BoosterIcon(t);
            nameText.text = BoosterInfo.Name(t);
            descText.text = BoosterInfo.Description(t);
            Open();
        }
    }
}
