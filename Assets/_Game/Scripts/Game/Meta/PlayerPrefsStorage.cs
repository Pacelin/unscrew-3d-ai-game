using Unscrew.Core;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// ISaveStorage over PlayerPrefs. Writes are cheap (in memory); <see cref="Flush"/> writes to disk and is called
    /// by GameBootstrap once per frame when something changed, and on pause/quit.
    /// </summary>
    public sealed class PlayerPrefsStorage : ISaveStorage
    {
        public bool Dirty { get; private set; }

        public string Load(string key) => PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key, null) : null;

        public void Save(string key, string value)
        {
            if (PlayerPrefs.HasKey(key) && PlayerPrefs.GetString(key) == value) return;
            PlayerPrefs.SetString(key, value);
            Dirty = true;
        }

        public void Flush()
        {
            if (!Dirty) return;
            PlayerPrefs.Save();
            Dirty = false;
        }
    }
}
