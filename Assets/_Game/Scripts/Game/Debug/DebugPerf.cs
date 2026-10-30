using System.Collections;
using Unity.Profiling;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// QA perf probe (Play Mode, via execute_code): frame time and managed allocations per frame from ProfilerRecorder,
    /// plus allocations attributed to player taps. <c>DebugPerf.Begin()</c> … <c>DebugPerf.End()</c> returns a summary line.
    /// In the editor the numbers include editor overhead, so they are an upper bound for a device.
    /// </summary>
    public static class DebugPerf
    {
        private static ProfilerRecorder _gc;
        private static ProfilerRecorder _gcCount;
        private static Coroutine _routine;
        private static int _frames;
        private static double _time;
        private static float _worst;
        private static long _gcBytes;
        private static long _gcAllocs;
        private static long _maxFrameGc;
        private static int _framesWithGc;
        private static int _startMoves;
        private static GameObject _probe;
        internal static long LoopBytes;
        internal static long LoopMax;
        internal static int LoopFramesWithAlloc;
        internal static long FrameStart;

        public static string Begin()
        {
            var boot = GameBootstrap.Instance;
            if (!Application.isPlaying || boot == null) return "DebugPerf: Play Mode only";
            End();
            _gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _gcCount = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count");
            _frames = 0; _time = 0; _worst = 0; _gcBytes = 0; _gcAllocs = 0; _maxFrameGc = 0; _framesWithGc = 0;
            _startMoves = boot.Flow.Session != null ? boot.Flow.Session.MovesMade : 0;
            LoopBytes = 0; LoopMax = 0; LoopFramesWithAlloc = 0;
            if (_probe == null)
            {
                _probe = new GameObject("DebugPerfProbe");
                _probe.AddComponent<PerfLoopStart>();
                _probe.AddComponent<PerfLoopEnd>();
            }
            _routine = boot.StartCoroutine(Sample());
            return "DebugPerf: recording (gc recorder valid=" + _gc.Valid + ")";
        }

        private static readonly System.Collections.Generic.List<int> Accessible = new System.Collections.Generic.List<int>(256);

        /// <summary>
        /// Taps <paramref name="count"/> bolts through GameFlow.TapBolt like a player (a bolt that fits an open box, else any
        /// accessible one), without the bot's PuzzleSim, so the probe measures the game's own cost per tap.
        /// </summary>
        public static string RunTaps(int count, float interval)
        {
            var boot = GameBootstrap.Instance;
            if (!Application.isPlaying || boot == null) return "DebugPerf: Play Mode only";
            boot.StartCoroutine(Taps(boot.Flow, count, new WaitForSecondsRealtime(interval)));
            return "DebugPerf: tapping " + count;
        }

        public static bool Tapping { get; private set; }

        private static IEnumerator Taps(GameFlow flow, int count, WaitForSecondsRealtime wait)
        {
            Tapping = true;
            for (int n = 0; n < count && flow.State == FlowState.Playing; n++)
            {
                var s = flow.Session;
                s.Board.GetAccessibleBolts(Accessible);
                int pick = -1;
                for (int i = 0; i < Accessible.Count && pick < 0; i++)
                {
                    int c = s.GetBoltColor(Accessible[i]);
                    for (int b = 0; b < s.Collector.ActiveBoxCount; b++)
                    {
                        var box = s.Collector.GetBox(b);
                        if (!box.IsEmpty && box.color == c && box.filled < box.capacity) { pick = Accessible[i]; break; }
                    }
                }
                if (pick < 0 && Accessible.Count > 0 && !s.Collector.IsBufferFull) pick = Accessible[0];
                if (pick < 0 || !flow.TapBolt(pick)) break;
                yield return wait;
            }
            Tapping = false;
        }

        private static IEnumerator Sample()
        {
            yield return null;
            while (true)
            {
                float dt = Time.unscaledDeltaTime;
                _frames++;
                _time += dt;
                if (dt > _worst) _worst = dt;
                long b = _gc.Valid ? _gc.LastValue : 0;
                _gcBytes += b;
                _gcAllocs += _gcCount.Valid ? _gcCount.LastValue : 0;
                if (b > 0) _framesWithGc++;
                if (b > _maxFrameGc) _maxFrameGc = b;
                yield return null;
            }
        }

        public static string End()
        {
            var boot = GameBootstrap.Instance;
            if (_routine != null && boot != null) boot.StopCoroutine(_routine);
            _routine = null;
            if (!_gc.Valid && _frames == 0) return "DebugPerf: not running";
            int moves = boot != null && boot.Flow.Session != null ? boot.Flow.Session.MovesMade - _startMoves : 0;
            string s = $"frames={_frames} avgFps={(_time > 0 ? _frames / _time : 0):F1} worstFrameMs={_worst * 1000f:F1} " +
                       $"gcTotalKB={_gcBytes / 1024f:F1} gcAllocs={_gcAllocs} framesWithGc={_framesWithGc} maxFrameGcKB={_maxFrameGc / 1024f:F2} " +
                       $"taps={moves} gcPerTapKB={(moves > 0 ? _gcBytes / 1024f / moves : 0):F2} | " +
                       $"playerLoop(Update..LateUpdate): totalKB={LoopBytes / 1024f:F1} framesWithAlloc={LoopFramesWithAlloc} maxFrameKB={LoopMax / 1024f:F2} " +
                       $"perTapKB={(moves > 0 ? LoopBytes / 1024f / moves : 0):F2}";
            if (_probe != null) Object.Destroy(_probe);
            _probe = null;
            if (_gc.Valid) _gc.Dispose();
            if (_gcCount.Valid) _gcCount.Dispose();
            _frames = 0;
            return s;
        }
    }
}

namespace Unscrew.Game
{
    /// <summary>First Update of the frame: remembers the used Mono heap size.</summary>
    [DefaultExecutionOrder(-32000)]
    internal sealed class PerfLoopStart : MonoBehaviour
    {
        private void Update() => DebugPerf.FrameStart = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
    }

    /// <summary>Last LateUpdate: growth of the Mono heap during game scripts, coroutines and animation (frames with a GC in between are skipped).</summary>
    [DefaultExecutionOrder(32000)]
    internal sealed class PerfLoopEnd : MonoBehaviour
    {
        private void LateUpdate()
        {
            long d = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - DebugPerf.FrameStart;
            if (d <= 0) return;
            DebugPerf.LoopBytes += d;
            DebugPerf.LoopFramesWithAlloc++;
            if (d > DebugPerf.LoopMax) DebugPerf.LoopMax = d;
        }
    }
}
