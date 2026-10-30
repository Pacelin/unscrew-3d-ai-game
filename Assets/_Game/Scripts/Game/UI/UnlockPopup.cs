using System;
using System.Collections.Generic;
using TMPro;
using Unscrew.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Unscrew.Game
{
    /// <summary>"New booster" popup (Unlock.dc.html) for each newly unlocked booster (with the free piece), one after another.</summary>
    public sealed class UnlockPopup : UiPanel
    {
        [SerializeField] private Image tile;
        [SerializeField] private Image tileEdge;
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
            giftText.text = freePieces > 0 ? $"+{freePieces} free" : "";
            giftText.transform.parent.gameObject.SetActive(freePieces > 0);
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
            icon.color = BoosterInfo.Ink(t);
            tile.color = BoosterInfo.Face(t);
            tileEdge.color = BoosterInfo.Edge(t);
            nameText.text = BoosterInfo.Name(t);
            descText.text = BoosterInfo.HowTo(t);
            Open();
        }
    }
}
