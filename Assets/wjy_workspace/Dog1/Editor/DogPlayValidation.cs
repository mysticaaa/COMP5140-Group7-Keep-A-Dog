#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using Wjy.DogTraining;

[InitializeOnLoad]
public static class DogPlayValidation
{
    private const string SessionKey = "CityDogPlayValidation";
    private static readonly string[] Paws =
    {
        "DEF-front_toes.L", "DEF-front_toes.R", "DEF-hind_paw.L", "DEF-hind_paw.R"
    };
    private static readonly MethodInfo Contact = typeof(DogCompanion).GetMethod("TryGetFootContact",
        BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly Vector3 Practice = new Vector3(.8f, 0f, -2.9f);
    private static bool started;
    private static double deadline;
    private static Action afterLateUpdate;
    private static PlayerLoopSystem originalLoop;
    private static bool loopInstalled;

    static DogPlayValidation() => EditorApplication.update += Tick;

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Dog1.unity");
        SessionState.SetBool(SessionKey, true);
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(SessionKey, false) || !EditorApplication.isPlaying) return;
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 90;
        if (EditorApplication.timeSinceStartup > deadline)
        {
            Finish(new TimeoutException("Play-motion validation timed out."));
            return;
        }
        if (started) return;
        var trainer = UnityEngine.Object.FindObjectOfType<Dog1Training>();
        if (trainer == null || trainer.dog == null || !trainer.dog.Ready) return;
        started = true;
        try
        {
            InstallSampler();
            Time.captureFramerate = 60;
            trainer.StartCoroutine(Validate(trainer.dog));
        }
        catch (Exception error) { Finish(error); }
    }

    private static void InstallSampler()
    {
        originalLoop = PlayerLoop.GetCurrentPlayerLoop();
        var loop = originalLoop;
        loop.subSystemList = (PlayerLoopSystem[])loop.subSystemList.Clone();
        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            var phase = loop.subSystemList[i];
            if (phase.type != typeof(UnityEngine.PlayerLoop.PreLateUpdate)) continue;
            var systems = new List<PlayerLoopSystem>(phase.subSystemList);
            systems.Add(new PlayerLoopSystem { type = typeof(DogPlayValidation), updateDelegate = SampleFrame });
            phase.subSystemList = systems.ToArray();
            loop.subSystemList[i] = phase;
            PlayerLoop.SetPlayerLoop(loop);
            loopInstalled = true;
            return;
        }
        throw new InvalidOperationException("Cannot sample the dog after LateUpdate.");
    }

    private static void SampleFrame()
    {
        try { afterLateUpdate?.Invoke(); }
        catch (Exception error) { Finish(error); }
    }

    private static IEnumerator Validate(DogCompanion dog)
    {
        var frames = new Stack<IEnumerator>();
        frames.Push(PlayThrough(dog));
        while (frames.Count > 0)
        {
            var current = frames.Peek();
            bool hasNext = false;
            Exception error = null;
            try { hasNext = current.MoveNext(); }
            catch (Exception caught) { error = caught; }
            if (error != null)
            {
                Finish(error);
                yield break;
            }
            if (!hasNext) frames.Pop();
            else if (current.Current is IEnumerator child) frames.Push(child);
            else yield return current.Current;
        }
        Finish(null);
    }

    private static IEnumerator PlayThrough(DogCompanion dog)
    {
        bool baseline = Environment.GetEnvironmentVariable("CITY_DOG_SPIN_BASELINE") == "1";
        string output = Environment.GetEnvironmentVariable("CITY_DOG_PLAY_CAPTURES");
        if (!string.IsNullOrWhiteSpace(output)) Directory.CreateDirectory(output);
        var skeleton = new Dictionary<string, Transform>();
        foreach (Transform bone in dog.VisualRoot.GetComponentsInChildren<Transform>())
            if (bone.name.StartsWith("DEF-", StringComparison.Ordinal)) skeleton[bone.name] = bone;
        var paws = Array.ConvertAll(Paws, name => skeleton[name]);
        var bones = new List<Transform>(skeleton.Values) { dog.VisualRoot, dog.transform };
        var originalScales = bones.ConvertAll(bone => bone.localScale);
        dog.SetWanderingEnabled(false);
        // Keep the recorded course independent of the furnished apartment's walkable area.
        dog.PositionLimit = point => point;
        dog.HasPlayClearance = (point, radius) => true;
        var owner = new GameObject("Play validation owner").transform;
        owner.position = Practice + Vector3.back * 3f + Vector3.up * 1.65f;
        dog.user = owner;
        dog.SetPracticeArea(Practice, Practice + new Vector3(2f, .11f, 2f), Practice + Vector3.forward * 3f);
        yield return new WaitForSeconds(.5f);

        using (var view = new CaptureView(dog, output))
        {
            var motion = new MotionSamples(dog, paws);
            string prefix = baseline ? "baseline-spin" : "spin";
            view.Take(prefix + "-rest");
            yield return SampleAction(dog, motion, "spin", view, prefix, new[] { .45f, 1.10f, 1.65f, 2.18f });
            view.Take(prefix + "-final");
            motion.Print("DOG_PLAY_SPIN", baseline ? "baseline" : "revised");
            if (baseline)
            {
                Debug.Log("DOG_PLAY_SPIN_BASELINE: captured original spin metrics.");
                yield break;
            }

            Require(motion.Completed && motion.Samples >= 120, "Spin did not complete its nominal 2.2 s and settle.");
            Require(Mathf.Abs(motion.TotalYaw - 360f) <= 3f && motion.FinalAngle < 3f,
                "Spin did not rotate 360° and face its starting heading.");
            Require(motion.CrossedFrames == 0, "At least one paw crossed its side lane during spin.");
            Require(motion.FinalFrontWidth >= motion.RestFrontWidth * .72f &&
                motion.FinalHindWidth >= motion.RestHindWidth * .72f, "Spin did not restore standing paw width.");
            Require(motion.FinalPawClearance <= .055f && motion.FinalPawSeparation >= .065f,
                "Spin settled with a raised or crowded paw.");
            Require(motion.FinalAllPlanted && motion.QuietRootDrift <= .006f && motion.QuietPawDrift <= .012f,
                "The dog kept moving after spin should have settled.");

            motion = new MotionSamples(dog, paws);
            var pounce = new PounceSamples(skeleton["DEF-pelvis"], skeleton["DEF-chest"], motion);
            view.Take("pounce-rest");
            yield return SampleAction(dog, motion, "pounce", view, "pounce", new[] { .18f, .35f, .61f, .94f }, pounce);
            view.Take("pounce-final");
            motion.Print("DOG_PLAY_POUNCE", "revised");
            pounce.Print();

            Require(motion.Completed && motion.Samples > 65, "Pounce did not complete.");
            Require(motion.PeakLift[0] >= .06f && motion.PeakLift[0] <= .14f &&
                motion.PeakLift[1] >= .06f && motion.PeakLift[1] <= .14f,
                "Front lift should peak between 6 and 14 cm on both paws.");
            Require(motion.PeakLift[2] <= .035f && motion.PeakLift[3] <= .035f &&
                motion.PeakHorizontal[2] <= .055f && motion.PeakHorizontal[3] <= .055f &&
                pounce.HindUnsupportedFrames <= 2, "Pounce lost hind paw support.");
            Require(pounce.LoadDepth >= .012f && pounce.Compression >= .015f && pounce.AbsorbedAfterApex,
                "Pounce must show an anticipation dip, front lift and landing compression in order.");
            Require(motion.FinalAllPlanted && motion.FinalPawClearance <= .045f &&
                motion.QuietRootDrift <= .006f && motion.QuietPawDrift <= .012f, "Pounce did not come to a quiet planted stop.");
            for (int i = 0; i < bones.Count; i++)
                Require((bones[i].localScale - originalScales[i]).sqrMagnitude < .00000001f, "Scale changed on " + bones[i].name);

            yield return ValidatePlayClearance(dog, paws, owner);
        }
    }

    private static IEnumerator ValidatePlayClearance(DogCompanion dog, Transform[] paws, Transform owner)
    {
        // Discover the new hooks after the baseline branch so the same script runs on both versions.
        var clearance = typeof(DogCompanion).GetProperty("HasPlayClearance");
        var succeeded = typeof(DogCompanion).GetProperty("LastPlaySucceeded");
        Require(clearance != null && succeeded != null, "Missing play clearance and outcome hooks.");
        bool PlaySucceeded() => (bool)succeeded.GetValue(dog);

        var blocked = new MotionSamples(dog, paws);
        Vector3 before = dog.transform.position;
        clearance.SetValue(dog, new Func<Vector3, float, bool>((point, radius) => false));
        afterLateUpdate = () => blocked.Observe(0f, false);
        dog.StartCoroutine(dog.PerformAction("pounce", 0f));
        bool accepted = dog.IsPerforming;
        yield return new WaitForSeconds(.35f);
        afterLateUpdate = null;
        Debug.Log($"DOG_PLAY_BLOCKED accepted={accepted} succeeded={PlaySucceeded()} " +
            $"rootTravel={Horizontal(before, dog.transform.position):F4}m foreLift={blocked.PeakLift[0]:F4}/{blocked.PeakLift[1]:F4}m");
        Require(!accepted && !dog.IsPerforming && !PlaySucceeded(), "Blocked pounce started or claimed success.");
        Require(Vector3.Distance(before, dog.transform.position) < .002f &&
            Array.TrueForAll(blocked.PeakHorizontal, drift => drift < .008f) &&
            Array.TrueForAll(blocked.PeakLift, lift => lift < .008f), "Blocked pounce moved the dog.");

        // Use a clear test route so scripted travel and guards are independent of prop layout.
        owner.position = Practice + Vector3.forward * 8f + Vector3.up * 1.65f;
        clearance.SetValue(dog, new Func<Vector3, float, bool>((point, radius) => true));
        ResetPractice(dog);
        yield return new WaitForSeconds(.45f);
        before = dog.transform.position;
        var running = new MotionSamples(dog, paws);
        float traveled = 0f;
        Vector3 previous = before;
        afterLateUpdate = () =>
        {
            traveled += Horizontal(previous, dog.transform.position);
            previous = dog.transform.position;
            running.Observe(0f, false);
        };
        dog.StartCoroutine(dog.PerformAction("burst", 0f));
        bool startedBurst = dog.IsPerforming;
        while (dog.IsPerforming) yield return null;
        yield return new WaitForSeconds(.60f);
        afterLateUpdate = null;
        float chord = Horizontal(before, dog.transform.position);
        running.Print("DOG_PLAY_BURST", "clear");
        Debug.Log($"DOG_PLAY_BURST_ROUTE traveled={traveled:F3}m chord={chord:F3}m " +
            $"turn={running.TotalYaw:F1}deg succeeded={PlaySucceeded()}");
        Require(startedBurst && PlaySucceeded() && traveled >= 2f && traveled <= 2.6f &&
            chord < traveled * .96f && Mathf.Abs(running.TotalYaw) >= 90f && Mathf.Abs(running.TotalYaw) <= 115f,
            "Burst did not complete the 2.0–2.6 m curved route.");
        Require(Array.Exists(running.PeakLift, lift => lift > .035f), "Burst moved without lifting any paw.");
        Require(running.FinalAllPlanted && running.CrossedFrames == 0 && running.FinalPawClearance < .035f,
            "Burst legs crossed or did not settle into a supported stance.");

        ResetPractice(dog);
        yield return new WaitForSeconds(.45f);
        before = dog.transform.position;
        dog.StartCoroutine(dog.PerformAction("burst", 0f));
        Require(dog.IsPerforming, "Interruption trial could not begin.");
        yield return new WaitForSeconds(.6f);
        Vector3 obstructed = dog.transform.position;
        owner.position = obstructed + dog.transform.forward * .23f + Vector3.up * 1.65f;
        while (dog.IsPerforming) yield return null;
        float beforeObstruction = Horizontal(before, obstructed);
        float afterObstruction = Horizontal(obstructed, dog.transform.position);
        Debug.Log($"DOG_PLAY_BURST_INTERRUPTED before={beforeObstruction:F3}m after={afterObstruction:F3}m succeeded={PlaySucceeded()}");
        Require(beforeObstruction > .08f && afterObstruction < .055f && !PlaySucceeded(),
            "Burst did not stop when the owner moved into its planned path.");
    }

    private static void ResetPractice(DogCompanion dog) =>
        dog.SetPracticeArea(Practice, Practice + new Vector3(2f, .11f, 2f), Practice + Vector3.forward * 3f);

    private static IEnumerator SampleAction(DogCompanion dog, MotionSamples motion, string action, CaptureView view,
        string prefix, float[] captureTimes, PounceSamples pounce = null)
    {
        float begun = Time.time;
        float stopped = -1f;
        int nextCapture = 0;
        afterLateUpdate = () =>
        {
            float age = Time.time - begun;
            motion.Observe(age, stopped >= 0f && Time.time - stopped >= .35f);
            pounce?.Observe(age);
            if (nextCapture < captureTimes.Length && age >= captureTimes[nextCapture])
            {
                view.Take(prefix + "-" + (nextCapture + 1).ToString("D2", CultureInfo.InvariantCulture));
                nextCapture++;
            }
        };

        dog.StartCoroutine(dog.PerformAction(action, 0f));
        motion.Completed = dog.IsPerforming;
        while (dog.IsPerforming && Time.time - begun < 8f) yield return null;
        motion.Completed &= !dog.IsPerforming;
        stopped = Time.time;
        yield return new WaitForSeconds(.6f);
        afterLateUpdate = null;
    }

    private sealed class MotionSamples
    {
        private readonly DogCompanion dog;
        private readonly Transform[] paws;
        private readonly Vector3[] neutral;
        private readonly Vector3[] previous = new Vector3[4];
        private readonly Vector3[] contacts = new Vector3[4];
        private readonly Vector3[] quietPaws = new Vector3[4];
        private readonly Vector3[] local = new Vector3[4];
        private readonly bool[] previousPlanted = new bool[4];
        private readonly bool[] planted = new bool[4];
        private readonly float[] minimum = { float.MaxValue, float.MaxValue };
        private readonly float[] pairDirection = new float[2];
        private readonly float[] pairCenter = new float[2];
        private readonly int[] ownSideCrossings = new int[4];
        private readonly Quaternion initialRotation;
        private Quaternion previousRotation;
        private Vector3 quietRoot;
        private bool quietStarted;
        private int contactFrames;
        private float maxContactFrameDrift, maxContactIntervalDrift;
        public bool Completed;
        public int Samples, CrossedFrames;
        public float TotalYaw, FinalAngle, FinalPawClearance, FinalPawSeparation, QuietRootDrift, QuietPawDrift;
        public readonly float[] PeakLift = new float[4];
        public readonly float[] PeakHorizontal = new float[4];
        public float RestFrontWidth { get; }
        public float RestHindWidth { get; }
        public float FinalFrontWidth => PairWidth(0);
        public float FinalHindWidth => PairWidth(1);
        public bool FinalAllPlanted => Array.TrueForAll(planted, state => state);
        public bool HindPlanted => planted[2] && planted[3];

        public MotionSamples(DogCompanion dog, Transform[] paws)
        {
            this.dog = dog;
            this.paws = paws;
            initialRotation = previousRotation = dog.transform.rotation;
            neutral = Array.ConvertAll(paws, paw => paw.position);
            for (int i = 0; i < paws.Length; i++)
            {
                contacts[i] = previous[i] = neutral[i];
                local[i] = dog.transform.InverseTransformPoint(neutral[i]);
                previousPlanted[i] = IsPlanted(i);
            }
            for (int pair = 0; pair < 2; pair++)
            {
                int left = pair * 2, right = left + 1;
                pairDirection[pair] = Mathf.Sign(local[right].x - local[left].x);
                pairCenter[pair] = (local[right].x + local[left].x) * .5f;
            }
            RestFrontWidth = PairWidth(0);
            RestHindWidth = PairWidth(1);
        }

        public void Observe(float age, bool quiet)
        {
            Samples++;
            Quaternion current = dog.transform.rotation;
            TotalYaw += Vector3.SignedAngle(previousRotation * Vector3.forward, current * Vector3.forward, Vector3.up);
            previousRotation = current;
            FinalAngle = Quaternion.Angle(initialRotation, current);
            FinalPawClearance = 0f;
            FinalPawSeparation = float.MaxValue;
            if (quiet && !quietStarted)
            {
                quietStarted = true;
                quietRoot = dog.transform.position;
                for (int i = 0; i < paws.Length; i++) quietPaws[i] = paws[i].position;
            }
            if (quiet) QuietRootDrift = Mathf.Max(QuietRootDrift, Horizontal(quietRoot, dog.transform.position));

            bool crossed = false;
            for (int i = 0; i < paws.Length; i++)
            {
                Vector3 position = paws[i].position;
                local[i] = dog.transform.InverseTransformPoint(position);
                float lift = position.y - neutral[i].y;
                PeakLift[i] = Mathf.Max(PeakLift[i], lift);
                PeakHorizontal[i] = Mathf.Max(PeakHorizontal[i], Horizontal(neutral[i], position));
                FinalPawClearance = Mathf.Max(FinalPawClearance, Mathf.Abs(lift));
                if (quiet) QuietPawDrift = Mathf.Max(QuietPawDrift, Vector3.Distance(quietPaws[i], position));
                for (int j = 0; j < i; j++)
                    FinalPawSeparation = Mathf.Min(FinalPawSeparation, Horizontal(position, paws[j].position));

                planted[i] = IsPlanted(i);
                if (planted[i] && previousPlanted[i])
                {
                    contactFrames++;
                    maxContactFrameDrift = Mathf.Max(maxContactFrameDrift, Horizontal(previous[i], position));
                    maxContactIntervalDrift = Mathf.Max(maxContactIntervalDrift, Horizontal(contacts[i], position));
                }
                else if (planted[i]) contacts[i] = position;
                previousPlanted[i] = planted[i];
                previous[i] = position;

                int pair = i / 2;
                float ownSide = (local[i].x - pairCenter[pair]) * pairDirection[pair] * (i % 2 == 0 ? -1f : 1f);
                if (ownSide < -.005f)
                {
                    ownSideCrossings[i]++;
                    crossed = true;
                }
            }
            if (crossed || FinalFrontWidth < 0f || FinalHindWidth < 0f) CrossedFrames++;
            minimum[0] = Mathf.Min(minimum[0], FinalFrontWidth);
            minimum[1] = Mathf.Min(minimum[1], FinalHindWidth);
        }

        private bool IsPlanted(int index)
        {
            if (Contact != null)
            {
                object[] keys = { Paws[index], false };
                if ((bool)Contact.Invoke(dog, keys)) return (bool)keys[1];
            }
            return Mathf.Abs(paws[index].position.y - neutral[index].y) < .015f;
        }

        private float PairWidth(int pair) => (local[2 * pair + 1].x - local[2 * pair].x) * pairDirection[pair];

        public void Print(string marker, string mode)
        {
            Debug.Log($"{marker} mode={mode} frames={Samples} yaw={TotalYaw:F2}deg headingError={FinalAngle:F2}deg " +
                $"frontWidthRestMinFinal={RestFrontWidth:F3}/{minimum[0]:F3}/{FinalFrontWidth:F3}m " +
                $"hindWidthRestMinFinal={RestHindWidth:F3}/{minimum[1]:F3}/{FinalHindWidth:F3}m " +
                $"crossFrames={CrossedFrames} pawCross={string.Join(",", ownSideCrossings)} contactFrames={contactFrames} " +
                $"contactFrameDrift={maxContactFrameDrift:F4}m contactIntervalDrift={maxContactIntervalDrift:F4}m " +
                $"finalPawClearance={FinalPawClearance:F4}m finalPawSeparation={FinalPawSeparation:F4}m " +
                $"quietRoot={QuietRootDrift:F4}m quietPaw={QuietPawDrift:F4}m finalAllPlanted={FinalAllPlanted}");
        }
    }

    private sealed class PounceSamples
    {
        private readonly Transform pelvis, chest;
        private readonly MotionSamples motion;
        private readonly float pelvisRest;
        private float aerialChest = float.MinValue;
        private float landChest = float.MaxValue;
        private float liftAt = -1f, compressedAt = -1f;
        private float largestPairedLift;
        public float LoadDepth { get; private set; }
        public float Compression => aerialChest == float.MinValue || landChest == float.MaxValue ? 0f : aerialChest - landChest;
        public int HindUnsupportedFrames { get; private set; }
        public bool AbsorbedAfterApex => liftAt > .22f && liftAt < .53f && compressedAt > liftAt;

        public PounceSamples(Transform pelvis, Transform chest, MotionSamples motion)
        {
            this.pelvis = pelvis;
            this.chest = chest;
            this.motion = motion;
            pelvisRest = pelvis.position.y;
        }

        public void Observe(float age)
        {
            if (!motion.HindPlanted) HindUnsupportedFrames++;
            if (age <= .23f) LoadDepth = Mathf.Max(LoadDepth, pelvisRest - pelvis.position.y);
            float lift = Mathf.Min(motion.PeakLift[0], motion.PeakLift[1]);
            if (lift > largestPairedLift)
            {
                largestPairedLift = lift;
                liftAt = age;
            }
            if (age >= .23f && age <= .48f) aerialChest = Mathf.Max(aerialChest, chest.position.y);
            if (age >= .50f && age <= .83f && chest.position.y < landChest)
            {
                landChest = chest.position.y;
                compressedAt = age;
            }
        }

        public void Print()
        {
            Debug.Log($"DOG_PLAY_PHASES load={LoadDepth:F3}m apex={liftAt:F3}s " +
                $"compression={Compression:F3}m at {compressedAt:F3}s hindUnsupportedFrames={HindUnsupportedFrames} " +
                $"frontLift={motion.PeakLift[0]:F3}/{motion.PeakLift[1]:F3}m " +
                $"hindLift={motion.PeakLift[2]:F3}/{motion.PeakLift[3]:F3}m " +
                $"hindTravel={motion.PeakHorizontal[2]:F3}/{motion.PeakHorizontal[3]:F3}m");
        }
    }

    private sealed class CaptureView : IDisposable
    {
        private readonly string output;
        private readonly Camera view;
        private readonly RenderTexture target;
        private readonly Texture2D frame;

        public CaptureView(DogCompanion dog, string output)
        {
            this.output = output;
            if (string.IsNullOrWhiteSpace(output)) return;
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Image capture requires Unity graphics.");
            view = new GameObject("Fixed rear three-quarter validation camera").AddComponent<Camera>();
            view.CopyFrom(Camera.main);
            view.enabled = false;
            // Determine the view once at rest so turn progression is visible against fixed scenery.
            view.transform.position = dog.transform.position - dog.transform.forward * 1.48f +
                dog.transform.right * 1.55f + Vector3.up * 1.12f;
            view.transform.LookAt(dog.transform.position + Vector3.up * .35f);
            view.fieldOfView = 39f;
            target = new RenderTexture(960, 640, 24);
            frame = new Texture2D(960, 640, TextureFormat.RGB24, false);
            view.targetTexture = target;
        }

        public void Take(string name)
        {
            if (view == null) return;
            var previous = RenderTexture.active;
            try
            {
                view.Render();
                RenderTexture.active = target;
                frame.ReadPixels(new Rect(0, 0, frame.width, frame.height), 0, 0);
                frame.Apply();
                File.WriteAllBytes(Path.Combine(output, name + ".png"), frame.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; }
        }

        public void Dispose()
        {
            if (view == null) return;
            view.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(frame);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(view.gameObject);
        }
    }

    private static float Horizontal(Vector3 a, Vector3 b)
    {
        Vector3 delta = a - b;
        delta.y = 0f;
        return delta.magnitude;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Finish(Exception error)
    {
        afterLateUpdate = null;
        if (loopInstalled) PlayerLoop.SetPlayerLoop(originalLoop);
        loopInstalled = false;
        Time.captureFramerate = 0;
        SessionState.SetBool(SessionKey, false);
        if (error == null) Debug.Log("DOG_PLAY_VALIDATION_OK");
        else Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
#endif
