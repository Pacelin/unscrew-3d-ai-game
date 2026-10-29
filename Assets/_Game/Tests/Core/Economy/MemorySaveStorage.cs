using System.Collections.Generic;

namespace Unscrew.Core.Tests
{
    /// <summary>In-memory ISaveStorage for tests (candidate for Tests/Core/Shared).</summary>
    public sealed class MemorySaveStorage : ISaveStorage
    {
        public readonly Dictionary<string, string> Data = new Dictionary<string, string>();
        public int SaveCalls;

        public string Load(string key)
        {
            string value;
            return Data.TryGetValue(key, out value) ? value : null;
        }

        public void Save(string key, string value)
        {
            SaveCalls++;
            Data[key] = value;
        }
    }
}
