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
        [SerializeField] private FxService fx;
        [Tooltip("Pause in the event queue while the lid of a completed box snaps on.")]
        [SerializeField] private float lidTime = 0.16f;

        private struct Step
        {
            public bool isPart;
            public int partId;
            public CollectEvent evt;
        }

        private readonly Queue<Step> _queue = new Queue<Step>();
        private Camera _cam;
        private LevelCatalog _catalog;
        private bool _processing;
        private int _running;

        public LevelView Level => levelView;
        public TrayView Tray => trayView;
        /// <summary>No queued events and no animation in progress.</summary>
        public bool IsIdle => _queue.Count == 0 && !_processing && _running == 0;
        /// <summary>Steps waiting in the queue (bots throttle on it).</summary>
        public int PendingSteps => _queue.Count;

        public void Show(Camera cam, LevelCatalog catalog, int sourceNumber, IGameSession session)
        {
            ResetQueue();
            _cam = cam;
            _catalog = catalog;
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
            if (bolt == null) return;
            bolt.Shake();
            Feedback.Play(Sfx.Blocked, 0.9f);
            Feedback.Haptic(15);
        }

        /// <summary>
        /// Queues the results of one Tap/ForceRemove/booster call. Bolt flights (BoltToBox/BoltToBuffer) start
        /// IMMEDIATELY and concurrently — a tap never waits behind box/lid/queue animations. Lifecycle steps
        /// (falling parts, box completion/activation, buffered bolts moving to a box, extra holes) stay ordered:
        /// a buffered bolt flies to its slot only after its buffer flight finished and the target box is Ready
        /// (the slide-in animation played), as the spec requires.
        /// </summary>
        public void Play(List<CollectEvent> events, List<int> releasedParts, float stagger = 0f)
        {
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (e.type == CollectEventType.BoltToBox || e.type == CollectEventType.BoltToBuffer)
                    LaunchBolt(e, stagger);
                else
                    _queue.Enqueue(new Step { evt = e });
                // Parts fall right after the bolt that held them leaves (first event of a move).
                if (i == 0)
                    foreach (var p in releasedParts) _queue.Enqueue(new Step { isPart = true, partId = p });
            }
            if (events.Count == 0)
                foreach (var p in releasedParts) _queue.Enqueue(new Step { isPart = true, partId = p });
            if (!_processing) StartCoroutine(Process());
        }

        /// <summary>Starts a bolt flight now (or after the magnet stagger) — never behind the lifecycle queue.</summary>
        private void LaunchBolt(CollectEvent e, float stagger)
        {
            if (stagger > 0f) StartCoroutine(Track(LaunchDelayed(e, stagger)));
            else StartCoroutine(Track(LaunchNow(e)));
        }

        private IEnumerator LaunchDelayed(CollectEvent e, float delay)
        {
            yield return Wait(delay);
            yield return LaunchNow(e);
        }

        private IEnumerator LaunchNow(CollectEvent e)
        {
            if (e.type == CollectEventType.BoltToBox)
            {
                var bolt = levelView.GetBolt(e.boltId);
                var box = trayView.GetBox(e.boxSlot);
                if (bolt == null || box == null) yield break;
                while (!box.Ready) yield return null; // a box sliding in: let the slot appear first
                var slot = box.ReserveSlot();
                box.Incoming++;
                yield return UnscrewAndFly(bolt, slot, box);
            }
            else
            {
                var bolt = levelView.GetBolt(e.boltId);
                var hole = trayView.GetHole(e.bufferSlot);
                if (bolt == null || hole == null) yield break;
                hole.Bolt = bolt;
                yield return UnscrewAndFly(bolt, hole.Anchor, null);
            }
        }

        private void ResetQueue()
        {
            StopAllCoroutines();
            if (fx != null) fx.ResetAll();
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
                if (part != null)
                {
                    float delay = Anim.UnscrewTime * 0.8f;
                    part.Release(levelView.Pivot.position, _cam, delay, levelView.FallenRoot);
                    StartCoroutine(PartFx(part, delay));
                }
                yield break;
            }

            var e = step.evt;
            switch (e.type)
            {
                case CollectEventType.BufferToBox:
                {
                    var bolt = levelView.GetBolt(e.boltId);
                    var hole = trayView.GetHole(e.bufferSlot);
                    var box = trayView.GetBox(e.boxSlot);
                    if (bolt == null || box == null) yield break;
                    while (bolt.InFlight) yield return null; // the buffer flight finishes first
                    while (!box.Ready) yield return null;    // and the slot-appear animation
                    if (hole != null && hole.Bolt == bolt) hole.Bolt = null;
                    var slot = box.ReserveSlot();
                    box.Incoming++;
                    bolt.InFlight = true;
                    StartCoroutine(Track(FlyTo(bolt, slot, Anim.BufferToBoxTime, box)));
                    yield return Wait(0.06f);
                    break;
                }
                case CollectEventType.BoxCompleted:
                {
                    var box = trayView.GetBox(e.boxSlot);
                    if (box == null) yield break;
                    while (box.Incoming > 0) yield return null;
                    yield return Wait(0.04f);
                    trayView.TakeBox(e.boxSlot);
                    // Lid snaps on (the queue waits so the next box doesn't slide into a closing one), then the box bounces away.
                    yield return box.SnapLid(lidTime);
                    if (fx != null) fx.Sparkle(box.transform.position - box.transform.forward * box.Width * 0.2f, box.Tint, box.Width, 20);
                    Feedback.Play(Sfx.BoxComplete);
                    Feedback.Haptic(30);
                    StartCoroutine(Track(BoxLeave(box, e.boxSlot)));
                    // The next box slides in once this one has bounced and starts leaving.
                    yield return Wait(0.2f);
                    break;
                }
                case CollectEventType.BoxActivated:
                {
                    var box = trayView.SpawnBox(e.boxSlot, e.color, true);
                    box.Ready = false;
                    Feedback.Play(Sfx.Pop, 0.5f, 1.15f);
                    yield return Slide(box.transform, trayView.BoxLocal(e.boxSlot), Anim.BoxEnterTime);
                    box.Ready = true;
                    break;
                }
                case CollectEventType.BufferSlotAdded:
                {
                    var hole = trayView.AddHole();
                    Feedback.Play(Sfx.Pop);
                    StartCoroutine(Track(PopIn(hole.transform)));
                    break;
                }
            }
        }

        private IEnumerator Track(IEnumerator routine)
        {
            _running++;
            yield return StartCoroutine(routine);
            _running--;
        }

        /// <summary><paramref name="box"/> = destination box (null: a buffer hole).</summary>
        private IEnumerator UnscrewAndFly(BoltView bolt, Transform target, BoxVisual box)
        {
            bolt.InFlight = true;
            bolt.Detach(trayView.transform);
            Feedback.Play(Sfx.Unscrew, 1f, Random.Range(0.94f, 1.08f));
            Feedback.Haptic(20);
            var t = bolt.transform;
            var startPos = t.position;
            var startRot = t.rotation;
            var startScale = t.localScale;
            var axis = t.forward;
            float travel = bolt.Length * 2.2f;
            float k = 0f;
            while (k < 1f)
            {
                k += Anim.Delta / Anim.UnscrewTime;
                float e = Anim.OutCubic(k);
                t.position = startPos + axis * (travel * e);
                t.rotation = Quaternion.AngleAxis(900f * e, axis) * startRot;
                // Pop: the bolt swells as it leaves the hole and settles back before flying.
                t.localScale = startScale * (1f + 0.28f * Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI));
                yield return null;
            }
            t.localScale = startScale;
            yield return FlyTo(bolt, target, Anim.FlyTime, box);
        }

        private IEnumerator FlyTo(BoltView bolt, Transform target, float duration, BoxVisual box)
        {
            var t = bolt.transform;
            t.SetParent(trayView.transform, true);
            int trail = fx != null && _catalog != null
                ? fx.AttachTrail(t, bolt.HeadOffsetLocal, _catalog.GetColor(bolt.Color), trayView.BoltHeadSize * 0.6f)
                : -1;
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
            if (fx != null) fx.ReleaseTrail(trail);
            if (box != null)
            {
                box.Incoming--;
                box.OnBoltLanded();
                Feedback.Play(Sfx.LandBox, 1f, 1f + 0.12f * (box.Landed - 1));
            }
            else Feedback.Play(Sfx.LandHole, 0.9f);
            bolt.InFlight = false;
        }

        /// <summary>Dust puff and a whoosh when the part actually leaves (after the release delay).</summary>
        private IEnumerator PartFx(PartView part, float delay)
        {
            yield return Wait(delay);
            if (part == null) yield break;
            var r = part.GetComponent<Renderer>();
            if (r == null) yield break;
            var b = r.bounds;
            if (fx != null) fx.Dust(b.center, Mathf.Clamp(b.extents.magnitude * 0.5f, 0.3f, 4f));
            Feedback.Play(Sfx.PartFall, 0.8f, Random.Range(0.9f, 1.1f));
        }

        /// <summary>New buffer hole: grows from zero with an overshoot (target scale = the layout scale).</summary>
        private static IEnumerator PopIn(Transform t)
        {
            var target = t.localScale;
            float k = 0f;
            while (k < 1f)
            {
                k += Anim.Delta / Anim.HolePopTime;
                t.localScale = target * Anim.OutBack(k);
                yield return null;
            }
            t.localScale = target;
        }

        private IEnumerator BoxLeave(BoxVisual box, int slot)
        {
            var t = box.transform;
            var v = box.Visual;
            // Squash-and-stretch bounce in place, then a wind-up and away.
            float k = 0f;
            while (k < 1f)
            {
                k += Anim.Delta / 0.16f;
                float s = Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI);
                v.localScale = new Vector3(1f + 0.14f * s, 1f - 0.1f * s, 1f);
                yield return null;
            }
            v.localScale = Vector3.one;
            var from = t.localPosition;
            var to = trayView.OffscreenUp(slot);
            var baseRot = t.localRotation;
            float tilt = slot % 2 == 0 ? -12f : 12f;
            k = 0f;
            while (k < 1f)
            {
                k += Anim.Delta / Anim.BoxLeaveTime;
                t.localPosition = Vector3.LerpUnclamped(from, to, Anim.InBack(k));
                t.localRotation = baseRot * Quaternion.Euler(0f, 0f, tilt * Mathf.Clamp01(k));
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
                t.localPosition = Vector3.LerpUnclamped(from, toLocal, Anim.OutBack(k));
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
