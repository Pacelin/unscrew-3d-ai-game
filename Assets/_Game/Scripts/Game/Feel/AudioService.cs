using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>Sound effects. Index = clip index in <see cref="AudioService"/> (filled by GameSceneBuilder).</summary>
    public enum Sfx
    {
        Unscrew,
        LandBox,
        LandHole,
        BoxComplete,
        PartFall,
        Win,
        Lose,
        Click,
        Coin,
        Blocked,
        Warning,
        Pop,
        Star,
    }

    /// <summary>
    /// Pooled one-shot SFX (round robin over a few AudioSources, no allocations per play) and one music loop.
    /// The sound toggle mutes everything through AudioListener.volume (GameSettings).
    /// </summary>
    public sealed class AudioService : MonoBehaviour
    {
        public const int SfxCount = 13;

        [Tooltip("Index = (int)Sfx")]
        [SerializeField] private AudioClip[] clips = new AudioClip[SfxCount];
        [SerializeField] private AudioClip music;
        [SerializeField] private int voices = 10;
        [SerializeField] private float sfxVolume = 0.9f;
        [SerializeField] private float musicVolume = 0.22f;
        [Tooltip("The same clip is not restarted more often than this (magnet / cascades land many bolts at once).")]
        [SerializeField] private float minInterval = 0.035f;

        private AudioSource[] _pool;
        private AudioSource _music;
        private float[] _lastPlay;
        private int _next;

        private void Awake()
        {
            _pool = new AudioSource[voices];
            for (int i = 0; i < voices; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                _pool[i] = s;
            }
            _lastPlay = new float[SfxCount];
            for (int i = 0; i < _lastPlay.Length; i++) _lastPlay[i] = -1f;
            _music = gameObject.AddComponent<AudioSource>();
            _music.playOnAwake = false;
            _music.loop = true;
            _music.volume = musicVolume;
            _music.priority = 0;
        }

        private void Start()
        {
            if (music == null) return;
            _music.clip = music;
            _music.Play();
        }

        public void Play(Sfx sfx, float volume = 1f, float pitch = 1f)
        {
            int i = (int)sfx;
            if (clips == null || i >= clips.Length || clips[i] == null) return;
            float now = Time.unscaledTime;
            if (now - _lastPlay[i] < minInterval) return;
            _lastPlay[i] = now;
            // Prefer a free voice; otherwise steal the oldest one (round robin).
            AudioSource src = null;
            for (int k = 0; k < _pool.Length; k++)
            {
                var s = _pool[(_next + k) % _pool.Length];
                if (!s.isPlaying) { src = s; break; }
            }
            if (src == null) src = _pool[_next];
            _next = (_next + 1) % _pool.Length;
            src.clip = clips[i];
            src.volume = sfxVolume * volume;
            src.pitch = pitch;
            src.Play();
        }
    }
}
