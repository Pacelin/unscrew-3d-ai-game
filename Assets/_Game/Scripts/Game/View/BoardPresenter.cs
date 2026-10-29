using System.Collections;
using System.Collections.Generic;
using Unscrew.Core;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// Plays the results of IGameSession moves. CollectEvents are processed strictly in order;
    /// bolt flights run concurrently, but a box completes only after all its bolts landed and
    /// a buffered bolt moves to a box only after it reached its hole, so the visual state always
    /// converges to the Core state. Input is never blocked by animations.
    /// </summary>
    public sealed class BoardPresenter : MonoBehaviour
    {
        [SerializeField] private LevelView levelView;
        [SerializeField] private TrayView trayView;

        private struct Step
        {
            public bool isPart;
            public int partId;
            public CollectEvent evt;
        }

        private readonly Queue<Step> _queue = new Queue<Step>();
        private Camera _cam;
        private bool _processing;
        private int _running;

        public LevelView Level => levelView;
        public TrayView Tray => trayView;
        /// <summary>No queued events and no animation in progress.</summary>
        public bool IsIdle => _queue.Count == 0 && !_processing && _running == 0;

        public void Show(Camera cam, LevelCatalog catalog, int sourceNumber, IGameSession session)
        {
            ResetQueue();
            _cam = cam;
            levelView.Build(cam, catalog, sourceNumber, session);
            trayView.Setup(cam, catalog, session.Collector);
        }

        public void Clear()
        {
            ResetQueue();
            levelView.Clear();
            trayView.Clear();
        }

        /// <summary>Feedback for a tap the session refused (blocked bolt).</summary>
        public void RejectTap(int boltId)
        {
            var bolt = levelView.GetBolt(boltId);
            if (bolt != null) bolt.Shake();
        }

        /// <summary>Queues the results of one Tap/ForceRemove/booster call.</summary>
        public void Play(List<CollectEvent> events, List<int> releasedParts)
        {
            for (int i = 0; i < events.Count; i++)
            {
                _queue.Enqueue(new Step { evt = events[i] });
                // Parts fall right after the bolt that held them leaves (first event of a move).
                if (i == 0)
                    foreach (var p in releasedParts) _queue.Enqueue(new Step { isPart = true, partId = p });
            }
            if (events.Count == 0)
                foreach (var p in releasedParts) _queue.Enqueue(new Step { isPart = true, partId = p });
            if (!_processing) StartCoroutine(Process());
        }

        private void ResetQueue()
        {
            StopAllCoroutines();
            _queue.Clear();
            _processing = false;
            _running = 0;
        }

        private IEnumerator Process()
        {
            _processing = true;
            while (_queue.Count > 0)
            {
                var step = _queue.Dequeue();
                yield return RunStep(step);
            }
            _processing = false;
        }

        private IEnumerator RunStep(Step step)
        {
            if (step.isPart)
            {
                var part = step.partId < levelView.Parts.Length ? levelView.Parts[step.partId] : null;
                if (part != null) part.Release(levelView.Pivot.position, _cam, Anim.UnscrewTime * 0.8f);
                yield break;
            }

            var e = step.evt;
            switch (e.type)
            {
                case CollectEventType.BoltToBox:
                {
                    var bolt = levelView.GetBolt(e.boltId);
                    var box = trayView.GetBox(e.boxSlot);
                    if (bolt == null || box == null) yield break;
                    var slot = box.ReserveSlot();
                    box.Incoming++;
                    StartCoroutine(Track(UnscrewAndFly(bolt, slot, () => box.Incoming--)));
                    break;
                }
                case CollectEventType.BoltToBuffer:
                {
                    var bolt = levelView.GetBolt(e.boltId);
                    var hole = trayView.GetHole(e.bufferSlot);
                    if (bolt == null || hole == null) yield break;
                    hole.Bolt = bolt;
                    StartCoroutine(Track(UnscrewAndFly(bolt, hole.Anchor, null)));
                    break;
                }
                case CollectEventType.BufferToBox:
                {
                    var bolt = levelView.GetBolt(e.boltId);
                    var hole = trayView.GetHole(e.bufferSlot);
                    var box = trayView.GetBox(e.boxSlot);
                    if (bolt == null || box == null) yield break;
                    while (bolt.InFlight) yield return null;
                    if (hole != null && hole.Bolt == bolt) hole.Bolt = null;
                    var slot = box.ReserveSlot();
                    box.Incoming++;
                    bolt.InFlight = true;
                    StartCoroutine(Track(FlyTo(bolt, slot, Anim.BufferToBoxTime, () => box.Incoming--)));
                    yield return Wait(0.06f);
                    break;
                }
                case CollectEventType.BoxCompleted:
                {
                    var box = trayView.GetBox(e.boxSlot);
                    if (box == null) yield break;
                    while (box.Incoming > 0) yield return null;
                    yield return Wait(0.08f);
                    trayView.TakeBox(e.boxSlot);
                    StartCoroutine(Track(BoxLeave(box, e.boxSlot)));
                    break;
                }
                case CollectEventType.BoxActivated:
                {
                    var box = trayView.SpawnBox(e.boxSlot, e.color, true);
                    yield return Slide(box.transform, trayView.BoxLocal(e.boxSlot), Anim.BoxEnterTime);
                    break;
                }
                case CollectEventType.BufferSlotAdded:
                    trayView.AddHole();
                    break;
            }
        }

        private IEnumerator Track(IEnumerator routine)
        {
            _running++;
            yield return StartCoroutine(routine);
            _running--;
        }

        private IEnumerator UnscrewAndFly(BoltView bolt, Transform target, System.Action onLand)
        {
            bolt.InFlight = true;
            bolt.Detach(trayView.transform);
            var t = bolt.transform;
            var startPos = t.position;
            var startRot = t.rotation;
            var axis = t.forward;
            float travel = bolt.Length * 2.2f;
            float k = 0f;
            while (k < 1f)
            {
                k += Anim.Delta / Anim.UnscrewTime;
                float e = Anim.OutCubic(k);
                t.position = startPos + axis * (travel * e);
                t.rotation = Quaternion.AngleAxis(720f * e, axis) * startRot;
                yield return null;
            }
            yield return FlyTo(bolt, target, Anim.FlyTime, onLand);
        }

        private IEnumerator FlyTo(BoltView bolt, Transform target, float duration, System.Action onLand)
        {
            var t = bolt.transform;
            t.SetParent(trayView.transform, true);
            var startPos = t.position;
            var startRot = t.rotation;
            var startScale = t.lossyScale;
            var endScale = trayView.BoltWorldScale(bolt);
            var camT = _cam.transform;
            float k = 0f;
            while (k < 1f)
            {
                k += Anim.Delta / duration;
                float e = Anim.InOutQuad(k);
                var endRot = trayView.BoltRotationAt(target.position);
                var rot = Quaternion.Slerp(startRot, endRot, e);
                var scale = Vector3.Lerp(startScale, endScale, e);
                // Root position that puts the head center onto the target at the final pose.
                var end = target.position - endRot * Vector3.Scale(bolt.HeadOffsetLocal, endScale);
                float span = Vector3.Distance(startPos, end);
                var ctrl = (startPos + end) * 0.5f + camT.up * (span * 0.15f) - camT.forward * (span * 0.15f);
                t.position = Anim.Bezier(startPos, ctrl, end, e);
                t.rotation = rot;
                SetWorldScale(t, scale);
                yield return null;
            }
            var finalRot = trayView.BoltRotationAt(target.position);
            t.SetParent(target, true);
            t.rotation = finalRot;
            SetWorldScale(t, endScale);
            t.position = target.position - finalRot * Vector3.Scale(bolt.HeadOffsetLocal, endScale);
            onLand?.Invoke();
            bolt.InFlight = false;
        }

        private IEnumerator BoxLeave(BoxVisual box, int slot)
        {
            var t = box.transform;
            var from = t.localPosition;
            var to = trayView.OffscreenUp(slot);
            var baseScale = t.localScale;
            float k = 0f;
            while (k < 1f)
            {
                k += Anim.Delta / Anim.BoxLeaveTime;
                // Small "pop" first, then accelerate away.
                float pop = 1f + 0.12f * Mathf.Sin(Mathf.Clamp01(k * 2f) * Mathf.PI);
                t.localScale = baseScale * pop;
                t.localPosition = Vector3.LerpUnclamped(from, to, Anim.InQuad(k));
                yield return null;
            }
            Destroy(box.gameObject);
        }

        private static IEnumerator Slide(Transform t, Vector3 toLocal, float duration)
        {
            var from = t.localPosition;
            float k = 0f;
            while (k < 1f)
            {
                k += Anim.Delta / duration;
                t.localPosition = Vector3.LerpUnclamped(from, toLocal, Anim.OutCubic(k));
                yield return null;
            }
        }

        private static IEnumerator Wait(float seconds)
        {
            float k = 0f;
            while (k < seconds)
            {
                k += Anim.Delta;
                yield return null;
            }
        }

        private static void SetWorldScale(Transform t, Vector3 world)
        {
            var p = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(world.x / p.x, world.y / p.y, world.z / p.z);
        }
    }
}
