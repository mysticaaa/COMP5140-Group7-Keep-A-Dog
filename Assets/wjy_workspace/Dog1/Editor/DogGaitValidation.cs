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
using Wjy.DogTraining;

[InitializeOnLoad]
public static class DogGaitValidation
{
    private const string SessionKey = "CityDogGaitValidation";
    private static readonly string[] Paws =
        { "DEF-front_toes.L", "DEF-front_toes.R", "DEF-hind_paw.L", "DEF-hind_paw.R" };
    private static readonly BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly MethodInfo Walk = typeof(DogCompanion).GetMethod("WalkTowards", Instance);
    private static readonly MethodInfo Contact = typeof(DogCompanion).GetMethod("TryGetFootContact", Instance);
    private static PlayerLoopSystem originalLoop;
    private static Action afterLateUpdate;
    private static bool running, installed;
    private static double deadline;

    static DogGaitValidation() => EditorApplication.update += Tick;

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(SessionKey, true);
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(SessionKey, false) || !EditorApplication.isPlaying) return;
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 85;
        if (EditorApplication.timeSinceStartup > deadline) { Finish(new TimeoutException("Gait test timed out.")); return; }
        if (running) return;
        running = true;
        try
        {
            Require(Walk != null && Contact != null, "Movement or foot-contact hook is unavailable.");
            Time.captureFramerate = 60;
            InstallSampler();
            var dog = new GameObject("Gait validation dog").AddComponent<DogCompanion>();
            Require(dog.Ready, "Dog skeleton did not initialize.");
            dog.SetWanderingEnabled(false);
            dog.StartCoroutine(Check(dog));
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
            var part = loop.subSystemList[i];
            if (part.type != typeof(UnityEngine.PlayerLoop.PreLateUpdate)) continue;
            var systems = new List<PlayerLoopSystem>(part.subSystemList)
            {
                new PlayerLoopSystem { type = typeof(DogGaitValidation), updateDelegate = SampleFrame }
            };
            part.subSystemList = systems.ToArray();
            loop.subSystemList[i] = part;
            PlayerLoop.SetPlayerLoop(loop);
            installed = true;
            return;
        }
        throw new InvalidOperationException("Cannot sample the solved pose after LateUpdate.");
    }

    private static void SampleFrame()
    {
        try { afterLateUpdate?.Invoke(); }
        catch (Exception error) { Finish(error); }
    }

    private static IEnumerator Check(DogCompanion dog)
    {
        var frames = new Stack<IEnumerator>();
        frames.Push(CheckDog(dog));
        while (frames.Count > 0)
        {
            var current = frames.Peek();
            bool more = false;
            Exception error = null;
            try { more = current.MoveNext(); }
            catch (Exception caught) { error = caught; }
            if (error != null) { Finish(error); yield break; }
            if (!more) frames.Pop();
            else if (current.Current is IEnumerator child) frames.Push(child);
            else yield return current.Current;
        }
        Finish(null);
    }

    private static IEnumerator CheckDog(DogCompanion dog)
    {
        string captureOutput = Environment.GetEnvironmentVariable("CITY_DOG_GAIT_CAPTURES");
        var capture = string.IsNullOrWhiteSpace(captureOutput) ? null : new CaptureView(captureOutput);
        var joints = new Dictionary<string, Transform>();
        foreach (var joint in dog.VisualRoot.GetComponentsInChildren<Transform>())
            if (joint.name.StartsWith("DEF-", StringComparison.Ordinal)) joints[joint.name] = joint;
        var paws = Array.ConvertAll(Paws, name => joints[name]);
        Vector3[] originalScales = Array.ConvertAll(paws, paw => paw.localScale);
        Vector3[] neutral = null;
        var results = new List<Profile>();
        foreach (var spec in new[] { ("slow", .55f), ("fast", 1.55f), ("turn", .82f) })
        {
            dog.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            dog.ResetCompanion();
            yield return new WaitForSeconds(.4f);
            if (neutral == null) neutral = Array.ConvertAll(paws, paw => dog.transform.InverseTransformPoint(paw.position));
            var result = new Profile(spec.Item1, dog, paws, joints["DEF-chest"], joints["DEF-head"], neutral);
            results.Add(result);
            bool capturedDuringTurn = false, capturedAfterTurn = false;
            afterLateUpdate = () =>
            {
                result.Add();
                if (capture == null || result.Name != "turn") return;
                if (!capturedDuringTurn && result.NegativeTurn >= 45f)
                {
                    capturedDuringTurn = true;
                    capture.Take("turn-during");
                }
                if (!capturedAfterTurn && result.RestSeconds >= 1.2f)
                {
                    capturedAfterTurn = true;
                    capture.Take("turn-settled");
                }
            };
            if (spec.Item1 == "turn")
            {
                yield return Move(dog, new Vector3(0f, 0f, 1.1f), spec.Item2, result);
                yield return Move(dog, new Vector3(1.05f, 0f, 1.1f), spec.Item2, result);
                yield return Move(dog, new Vector3(.72f, 0f, 2.25f), spec.Item2, result);
            }
            else
                yield return Move(dog, new Vector3(0f, 0f, 3.60f), spec.Item2, result);

            result.Stop();
            yield return new WaitForSeconds(1.55f);
            afterLateUpdate = null;
            result.Report();
            if (capture != null && result.Name == "turn")
                Require(capturedDuringTurn && capturedAfterTurn, "Turn screenshots did not sample both phases.");
        }
        for (int i = 0; i < paws.Length; i++)
            Require(paws[i].localScale == originalScales[i], "A paw grew or shrank during locomotion.");

        if (Environment.GetEnvironmentVariable("CITY_DOG_GAIT_MEASURE_ONLY") == "1") yield break;
        Profile slow = results[0], fast = results[1], turn = results[2];
        Require(slow.Meters > 3.45f && fast.Meters > 3.45f && turn.Meters > 2.8f,
            "The dog did not complete the controlled routes.");
        Require(slow.Seconds > fast.Seconds * 1.9f && fast.MeanSpeed > slow.MeanSpeed * 1.9f,
            "The change in requested pace did not change actual walking speed.");
        Require(fast.Cadence > slow.Cadence * 1.2f, "Faster movement did not increase footfall cadence.");
        Require(slow.EligibleLandings >= 12 && fast.EligibleLandings >= 8 &&
            slow.DiagonalPairRatio < .28f && fast.DiagonalPairRatio > .66f,
            "Sustained slow steps did not remain separate, or sustained fast steps did not pair diagonally.");
        Require(turn.PositiveTurn > 55f && turn.NegativeTurn > 55f && turn.MaximumChestAngle > 1f,
            "The dog did not physically turn both directions or the torso did not participate.");
        foreach (var result in results)
        {
            Require(result.Landings >= 8 && result.LongestPlant > .095f && result.HighestLift > .035f,
                result.Name + ": sustained independent stance and swing phases were not detected.");
            for (int i = 0; i < Paws.Length; i++)
                Require(result.PlantTimes[i] > .095f && result.PawLandings[i] > 0,
                    result.Name + ": missing sustained contact or a landing for " + Paws[i] + ".");
            Require(result.SupportFrameRatio > .83f && result.HighestSupportSlip < .15f,
                result.Name + ": insufficient ground support or excessive planted paw drift.");
            Require(result.MinimumSignedWidth > .02f && result.WorstNeutralWidthError < .047f &&
                result.WorstNeutralPawError < .085f,
                result.Name + ": paws crossed or failed to settle close to the initial stance.");
            Require(result.SettledFrames >= 20 && result.SettledFrames == result.SettledPlantedFrames &&
                result.SettledDrift < .010f && result.SettledRootDrift < .001f,
                result.Name + ": front or rear paws kept shuffling after one second at rest.");
        }
    }

    private static IEnumerator Move(DogCompanion dog, Vector3 target, float speed, Profile result)
    {
        for (int frame = 0; frame < 580 && Horizontal(dog.transform.position, target) > .018f; frame++)
        {
            Require(dog.CurrentAction != "spin", "Normal path turning ran a trained spin.");
            Walk.Invoke(dog, new object[] { target, speed, Time.deltaTime });
            result.AddStepTime(Time.deltaTime);
            yield return null;
        }
        Require(Horizontal(dog.transform.position, target) < .026f, "Motion stalled at " + target);
    }

    private sealed class Profile
    {
        private readonly DogCompanion dog;
        private readonly Transform[] paws;
        private readonly Transform chest, head;
        private readonly Vector3[] neutral, plantAnchors = new Vector3[4], settleAnchors = new Vector3[4];
        private readonly float[] plantStarted = new float[4];
        private readonly bool[] planted = new bool[4];
        private readonly List<float>[] landings = { new List<float>(), new List<float>(), new List<float>(), new List<float>() };
        private readonly Quaternion idleChest, idleHead;
        private Vector3 lastRoot, settleRoot;
        private Quaternion lastRotation;
        private int activeFrames, supportFrames;
        private float stopTime = -1f;
        private readonly float startTime = Time.time;

        public readonly string Name;
        public float Meters, Seconds, PositiveTurn, NegativeTurn, MaximumChestAngle, MaximumHeadAngle;
        public float LongestPlant, HighestSupportSlip, HighestLift, MinimumSignedWidth = float.PositiveInfinity;
        public float WorstNeutralWidthError, WorstNeutralPawError, SettledDrift, SettledRootDrift;
        private float worstSlipTime;
        private string worstSlipPaw = "";
        public int Landings, SettledFrames, SettledPlantedFrames;
        public readonly float[] PlantTimes = new float[4];
        public readonly int[] PawLandings = new int[4];
        public float MeanSpeed => Meters / Mathf.Max(Seconds, .001f);
        public float Cadence => Landings / Mathf.Max(Seconds, .001f);
        public float SupportFrameRatio => (float)supportFrames / Mathf.Max(activeFrames, 1);
        public float RestSeconds => stopTime < 0 ? 0f : Time.time - stopTime;
        public int EligibleLandings { get; private set; }
        public float DiagonalPairRatio { get; private set; }

        public Profile(string name, DogCompanion dog, Transform[] paws, Transform chest, Transform head, Vector3[] neutral)
        {
            Name = name;
            this.dog = dog;
            this.paws = paws;
            this.chest = chest;
            this.head = head;
            this.neutral = neutral;
            lastRoot = dog.transform.position;
            lastRotation = dog.transform.rotation;
            idleChest = Quaternion.Inverse(lastRotation) * chest.rotation;
            idleHead = Quaternion.Inverse(lastRotation) * head.rotation;
            for (int i = 0; i < paws.Length; i++)
            {
                planted[i] = IsPlanted(dog, i);
                plantStarted[i] = Time.time;
                plantAnchors[i] = paws[i].position;
            }
        }

        public void AddStepTime(float seconds) => Seconds += seconds;
        public void Stop() => stopTime = Time.time;

        public void Add()
        {
            Vector3 root = dog.transform.position;
            Quaternion rotation = dog.transform.rotation;
            float distance = Horizontal(root, lastRoot);
            float yaw = Vector3.SignedAngle(lastRotation * Vector3.forward, rotation * Vector3.forward, Vector3.up);
            lastRoot = root;
            lastRotation = rotation;
            bool moving = stopTime < 0f && (distance > .0001f || Mathf.Abs(yaw) > .04f);
            if (moving)
            {
                Meters += distance;
                PositiveTurn += Mathf.Max(yaw, 0f);
                NegativeTurn += Mathf.Max(-yaw, 0f);
                if (Mathf.Abs(yaw) > .15f)
                {
                    MaximumChestAngle = Mathf.Max(MaximumChestAngle,
                        Quaternion.Angle(idleChest, Quaternion.Inverse(rotation) * chest.rotation));
                    MaximumHeadAngle = Mathf.Max(MaximumHeadAngle,
                        Quaternion.Angle(idleHead, Quaternion.Inverse(rotation) * head.rotation));
                }
                activeFrames++;
            }

            bool resting = stopTime >= 0f && Time.time - stopTime >= 1f;
            if (resting && SettledFrames == 0)
            {
                settleRoot = root;
                for (int i = 0; i < paws.Length; i++) settleAnchors[i] = paws[i].position;
            }

            int reliableSupport = 0, contactCount = 0;
            float left = 0f;
            for (int i = 0; i < paws.Length; i++)
            {
                Vector3 position = paws[i].position;
                Vector3 local = dog.transform.InverseTransformPoint(position);
                bool contact = IsPlanted(dog, i);
                if (contact)
                {
                    contactCount++;
                    if (!planted[i])
                    {
                        plantStarted[i] = Time.time;
                        plantAnchors[i] = position;
                        if (moving)
                        {
                            Landings++;
                            PawLandings[i]++;
                            if (Name != "turn") landings[i].Add(Time.time);
                        }
                    }
                    if (moving && Time.time - plantStarted[i] > .033f)
                    {
                        float interval = Time.time - plantStarted[i];
                        PlantTimes[i] = Mathf.Max(PlantTimes[i], interval);
                        LongestPlant = Mathf.Max(LongestPlant, interval);
                        float drift = Horizontal(position, plantAnchors[i]);
                        if (drift > HighestSupportSlip)
                        {
                            HighestSupportSlip = drift;
                            worstSlipTime = Time.time - startTime;
                            worstSlipPaw = Paws[i];
                        }
                        if (position.y - root.y < .105f && drift < .10f) reliableSupport++;
                    }
                }
                else if (moving)
                    HighestLift = Mathf.Max(HighestLift, local.y - neutral[i].y);
                planted[i] = contact;

                if (resting)
                {
                    SettledDrift = Mathf.Max(SettledDrift, Vector3.Distance(settleAnchors[i], position));
                    WorstNeutralPawError = Mathf.Max(WorstNeutralPawError, Horizontal(neutral[i], local));
                }

                if ((i & 1) == 0) left = local.x;
                else
                {
                    float originalWidth = neutral[i].x - neutral[i - 1].x;
                    float signedWidth = (local.x - left) * Mathf.Sign(originalWidth);
                    MinimumSignedWidth = Mathf.Min(MinimumSignedWidth, signedWidth);
                    if (resting) WorstNeutralWidthError = Mathf.Max(WorstNeutralWidthError, Mathf.Abs(signedWidth - Mathf.Abs(originalWidth)));
                }
            }
            if (moving && reliableSupport > 0) supportFrames++;
            if (resting)
            {
                SettledFrames++;
                if (contactCount == 4) SettledPlantedFrames++;
                SettledRootDrift = Mathf.Max(SettledRootDrift, Vector3.Distance(settleRoot, root));
            }
        }

        public void Report()
        {
            const float transitionWindow = .65f;
            float begin = startTime + transitionWindow, end = stopTime - transitionWindow;
            var steadyLandings = Array.ConvertAll(landings, entries => entries.FindAll(t => t >= begin && t <= end));
            EligibleLandings = steadyLandings[0].Count + steadyLandings[1].Count +
                steadyLandings[2].Count + steadyLandings[3].Count;
            (int count, float lag) a = PairLandings(steadyLandings[0], steadyLandings[3]);
            (int count, float lag) b = PairLandings(steadyLandings[1], steadyLandings[2]);
            int matchedPairs = a.count + b.count;
            float averageLag = matchedPairs == 0 ? 0f : (a.lag + b.lag) / matchedPairs;
            DiagonalPairRatio = 2f * matchedPairs / Mathf.Max(EligibleLandings, 1);
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
            "DOG_GAIT {0} travel={1:F3}m time={2:F3}s speed={3:F3}m/s cadence={4:F2}/s landings={5} " +
            "longestPlant={6:F3}s support={7:P1} maxPlantDrift={8:F3}m maxLift={9:F3}m yaw={10:F1}/-{11:F1} " +
            "chest={12:F1}deg head={13:F1}deg minSignedWidth={14:F3}m restWidthError={15:F3}m " +
            "restPawError={16:F3}m settled={17}/{18} restPawDrift={19:F4}m restRootDrift={20:F4}m " +
            "landingsByPaw={21} longestPlantsByPaw={22}s steadyLandings={23} diagonalPairs={24}+{25} pairRatio={26:P1} avgPairDt={27:F3}s maxSlipAt={28:F3}s/{29}",
            Name, Meters, Seconds, MeanSpeed, Cadence, Landings, LongestPlant, SupportFrameRatio,
            HighestSupportSlip, HighestLift, PositiveTurn, NegativeTurn, MaximumChestAngle, MaximumHeadAngle,
            MinimumSignedWidth, WorstNeutralWidthError, WorstNeutralPawError, SettledPlantedFrames, SettledFrames,
            SettledDrift, SettledRootDrift, string.Join("/", PawLandings),
            string.Join("/", Array.ConvertAll(PlantTimes, interval => interval.ToString("F3", CultureInfo.InvariantCulture))),
            EligibleLandings, a.count, b.count, DiagonalPairRatio, averageLag, worstSlipTime, worstSlipPaw));
        }

        private static (int count, float lag) PairLandings(List<float> left, List<float> right)
        {
            const float tolerance = .042f; // A little over two frames at 60 Hz.
            int l = 0, r = 0, count = 0;
            float lag = 0f;
            while (l < left.Count && r < right.Count)
            {
                float difference = left[l] - right[r];
                if (Mathf.Abs(difference) <= tolerance)
                {
                    count++;
                    lag += Mathf.Abs(difference);
                    l++;
                    r++;
                }
                else if (difference < 0f) l++;
                else r++;
            }
            return (count, lag);
        }
    }

    private sealed class CaptureView
    {
        private readonly string output;
        private readonly Camera camera;

        public CaptureView(string output)
        {
            this.output = output;
            Directory.CreateDirectory(output);
            camera = new GameObject("Gait test camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.54f, .59f, .52f);
            camera.orthographic = true;
            var light = new GameObject("Gait test light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Gait test floor";
            floor.transform.position = new Vector3(0f, -.012f, 1.5f);
            floor.GetComponent<Collider>().enabled = false;
            var material = new Material(Shader.Find("Standard")) { color = new Color(.58f, .61f, .49f) };
            floor.GetComponent<Renderer>().sharedMaterial = material;
        }

        public void Take(string stage)
        {
            Vector3 center = new Vector3(.88f, .12f, 1.82f);
            camera.transform.position = center + new Vector3(0f, 3.5f, 0f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            camera.orthographicSize = 1.18f;
            Save(stage + "-top.png");
            camera.transform.position = center + new Vector3(-2.85f, 2.3f, -2.9f);
            camera.transform.LookAt(center + Vector3.up * .17f);
            camera.orthographicSize = 1.07f;
            Save(stage + "-rear.png");
        }

        private void Save(string file)
        {
            var target = new RenderTexture(960, 720, 24);
            var image = new Texture2D(960, 720, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(output, file), image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                UnityEngine.Object.Destroy(image);
                UnityEngine.Object.Destroy(target);
            }
        }
    }

    private static bool IsPlanted(DogCompanion dog, int index)
    {
        object[] args = { Paws[index], false };
        Require((bool)Contact.Invoke(dog, args), "Missing contact for " + Paws[index]);
        return (bool)args[1];
    }

    private static float Horizontal(Vector3 a, Vector3 b)
    {
        Vector3 delta = a - b;
        delta.y = 0f;
        return delta.magnitude;
    }

    private static void Require(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }

    private static void Finish(Exception error)
    {
        SessionState.SetBool(SessionKey, false);
        afterLateUpdate = null;
        if (installed) PlayerLoop.SetPlayerLoop(originalLoop);
        Time.captureFramerate = 0;
        if (error == null) Debug.Log(Environment.GetEnvironmentVariable("CITY_DOG_GAIT_MEASURE_ONLY") == "1"
            ? "DOG_GAIT_MEASURED" : "DOG_GAIT_OK");
        else Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
#endif
