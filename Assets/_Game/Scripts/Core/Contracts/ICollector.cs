using System.Collections.Generic;

namespace Unscrew.Core
{
    public enum CollectEventType
    {
        /// <summary>Bolt went straight into a box: boltId, color, boxSlot.</summary>
        BoltToBox,
        /// <summary>Bolt went into a buffer hole: boltId, color, bufferSlot.</summary>
        BoltToBuffer,
        /// <summary>Bolt moved from a buffer hole into a box: boltId, color, bufferSlot, boxSlot.</summary>
        BufferToBox,
        /// <summary>Box filled and left: color, boxSlot.</summary>
        BoxCompleted,
        /// <summary>A new box from the queue appeared in a slot: color, boxSlot.</summary>
        BoxActivated,
        /// <summary>Buffer got an extra hole: bufferSlot = index of the new hole.</summary>
        BufferSlotAdded,
    }

    /// <summary>One animation-worthy step. Events are appended in the exact order the view should play them.</summary>
    public struct CollectEvent
    {
        public CollectEventType type;
        public int boltId;
        public int color;
        public int boxSlot;
        public int bufferSlot;

        public override string ToString() =>
            $"{type}(bolt={boltId}, color={color}, box={boxSlot}, buffer={bufferSlot})";
    }

    public struct BoxView
    {
        /// <summary>-1 when the slot is empty (queue exhausted).</summary>
        public int color;
        public int filled;
        public int capacity;
        public bool IsEmpty => color < 0;
    }

    /// <summary>
    /// Boxes on top (ActiveBoxCount slots fed from a color queue) and the buffer of holes below them.
    /// Rules: a bolt goes to the leftmost non-full box of its color, otherwise to the leftmost free hole.
    /// A full box completes, the next queued color takes its slot, then buffered bolts of that color move into it
    /// (which may cascade into further completions).
    /// </summary>
    public interface ICollector
    {
        int BoxCapacity { get; }
        int ActiveBoxCount { get; }
        BoxView GetBox(int boxSlot);
        /// <summary>Queued colors not yet shown, next first.</summary>
        IReadOnlyList<int> UpcomingColors { get; }
        bool AllBoxesDone { get; }

        int BufferCapacity { get; }
        int BufferUsed { get; }
        bool IsBufferFull { get; }
        /// <summary>Bolt id in the hole or -1.</summary>
        int GetBufferBolt(int bufferSlot);
        /// <summary>Color in the hole or -1.</summary>
        int GetBufferColor(int bufferSlot);

        /// <summary>True if a box of this color has room or the buffer has a free hole.</summary>
        bool CanAccept(int color);
        /// <summary>Throws InvalidOperationException when !CanAccept(color).</summary>
        void Add(int boltId, int color, List<CollectEvent> events);
        void AddBufferSlot(List<CollectEvent> events);
    }
}
