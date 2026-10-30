using UnityEngine;
using UnityEngine.UI;

namespace Unscrew.Game
{
    /// <summary>
    /// UI confetti over the overlay canvas (world particles would be hidden by Screen Space Overlay popups).
    /// A fixed pool of Images simulated in Update with plain arrays: no allocations while it plays.
    /// </summary>
    public sealed class UiConfetti : MonoBehaviour
    {
        [SerializeField] private Image template;
        [SerializeField] private int count = 70;
        [SerializeField] private float duration = 2.8f;
        [SerializeField] private float gravity = 2600f;
        [SerializeField] private Color[] colors = (Color[])UiTokens.Colors.Confetti.Clone();

        private RectTransform[] _pieces;
        private Vector2[] _pos;
        private Vector2[] _vel;
        private float[] _rot;
        private float[] _spin;
        private float[] _phase;
        private float _time = -1f;

        private void EnsurePool()
        {
            if (_pieces != null) return;
            template.gameObject.SetActive(false);
            _pieces = new RectTransform[count];
            _pos = new Vector2[count];
            _vel = new Vector2[count];
            _rot = new float[count];
            _spin = new float[count];
            _phase = new float[count];
            for (int i = 0; i < count; i++)
            {
                var img = Instantiate(template, transform);
                img.color = colors[i % colors.Length];
                img.rectTransform.sizeDelta = new Vector2(Random.Range(18f, 30f), Random.Range(30f, 48f));
                _pieces[i] = img.rectTransform;
            }
        }

        /// <summary>Two cannons from the lower corners shooting up and inward.</summary>
        public void Play()
        {
            EnsurePool();
            var size = ((RectTransform)transform).rect.size;
            for (int i = 0; i < count; i++)
            {
                bool left = i % 2 == 0;
                _pos[i] = new Vector2(left ? -size.x * 0.5f : size.x * 0.5f, -size.y * 0.25f);
                float angle = Random.Range(55f, 80f) * Mathf.Deg2Rad;
                float speed = Random.Range(1700f, 2700f);
                _vel[i] = new Vector2(Mathf.Cos(angle) * speed * (left ? 1f : -1f), Mathf.Sin(angle) * speed);
                _rot[i] = Random.Range(0f, 360f);
                _spin[i] = Random.Range(-720f, 720f);
                _phase[i] = Random.Range(0f, 6.28f);
                _pieces[i].gameObject.SetActive(true);
            }
            _time = 0f;
            Apply(0f);
        }

        public void Stop()
        {
            _time = -1f;
            if (_pieces == null) return;
            foreach (var p in _pieces) p.gameObject.SetActive(false);
        }

        private void OnDisable() => Stop();

        private void Update()
        {
            if (_time < 0f) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            _time += dt;
            if (_time > duration) { Stop(); return; }
            Apply(dt);
        }

        private void Apply(float dt)
        {
            float fade = Mathf.Clamp01((duration - _time) / 0.5f);
            for (int i = 0; i < _pieces.Length; i++)
            {
                var v = _vel[i];
                v.y -= gravity * dt;
                // Air drag: paper flutters down slowly once the burst is spent.
                v *= 1f - 2.2f * dt;
                if (v.y < -420f) v.y = -420f;
                _vel[i] = v;
                _pos[i] += v * dt + new Vector2(Mathf.Sin(_time * 7f + _phase[i]) * 90f * dt, 0f);
                _rot[i] += _spin[i] * dt;
                var p = _pieces[i];
                p.anchoredPosition = _pos[i];
                p.localRotation = Quaternion.Euler(0f, 0f, _rot[i]);
                // Fake 3D tumble: the width oscillates.
                p.localScale = new Vector3(Mathf.Abs(Mathf.Sin(_time * 9f + _phase[i])) * fade + 0.1f, fade, 1f);
            }
        }
    }
}
