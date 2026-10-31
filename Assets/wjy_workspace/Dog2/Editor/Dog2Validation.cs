#if UNITY_EDITOR
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Management;
using UnityEditor.XR.Management;
using Wjy.DogTraining;

[InitializeOnLoad]
public static class Dog2Validation
{
    private static Dog2VrTraining trainer;
    private static HttpListener listener;
    private static volatile int requests;
    private static volatile int speechRequests;
    private static bool validSpeechWav;
    private static string body, endpoint;
    private static int stage;
    private static double since;
    private static Vector3 fixedDogPosition;
    static Dog2Validation() { EditorApplication.update += Tick; }
    public static void CheckControllerProfiles()
    {
        for (int pass = 0; pass < 2; pass++)
        {
            Dog2XrSetup.Configure();
            var settings = UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            // Exercise the loader-library scan that previously discarded enabled profiles.
            UnityEngine.Object package;
            if (!EditorBuildSettings.TryGetConfigObject("com.unity.xr.openxr.settings4", out package)) throw new Exception("Missing package settings.");
            var provider = (UnityEngine.XR.OpenXR.IPackageSettings)package;
            provider.GetActiveLoaderLibraryPath();
            UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(BuildTargetGroup.Standalone);
            settings = UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            var touch = settings.GetFeature<UnityEngine.XR.OpenXR.Features.Interactions.OculusTouchControllerProfile>();
            var plus = settings.GetFeature<UnityEngine.XR.OpenXR.Features.Interactions.MetaQuestTouchPlusControllerProfile>();
            if (touch == null || plus == null || !touch.enabled || !plus.enabled)
                throw new Exception("Referenced Quest controller profiles are missing or disabled.");
        }
        Debug.Log("DOG2_CONTROLLER_PROFILE_REPAIR_PASS");
    }

    public static void Run()
    {
        Dog2XrSetup.Configure();
        var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
        if (settings == null || settings.InitManagerOnStart || settings.Manager.activeLoaders.Count == 0)
            throw new Exception("OpenXR settings are not configured for manual initialization.");
        if (Dog2SpeechInput.JoinTranscript("坐下", "坐下") != "坐下" ||
            Dog2SpeechInput.JoinTranscript("小狗", "坐下") != "小狗 坐下") throw new Exception("Speech assembly failed.");
        foreach (string a in new[] { "sit", "crouch", "spin", "refuse", "idle" }) Dog2VrTraining.ParseDecision("{\"action\":\"" + a + "\"}");
        bool rejected = false; try { Dog2VrTraining.ParseDecision("{\"action\":\"fly\"}"); } catch (FormatException) { rejected = true; }
        if (!rejected) throw new Exception("Unsafe action accepted.");
        EditorSceneManager.OpenScene("Assets/Scenes/Dog2.unity");
        var sceneTrainer = UnityEngine.Object.FindObjectOfType<Dog2VrTraining>();
        sceneTrainer.enableOpenXR = false;
        SessionState.SetBool("WjyDog2Validation", true); EditorApplication.isPlaying = true;
    }
    private static void CheckRayInteraction(Dog2VrTraining owner)
    {
        Canvas.ForceUpdateCanvases();
        var button = owner.InteractionCanvas.transform.Find("Cancel voice").GetComponent<Button>();
        int clicks = 0;
        UnityEngine.Events.UnityAction countClick = () => clicks++;
        button.onClick.AddListener(countClick);
        var pointers = UnityEngine.Object.FindObjectsOfType<Dog2ControllerRay>();
        if (pointers.Length != 2 || owner.InteractionCanvas.GetComponentsInChildren<Button>().Length < 6)
            throw new Exception("VR pointers or panel buttons are missing.");
        Vector3 target = button.GetComponent<RectTransform>().TransformPoint(button.GetComponent<RectTransform>().rect.center);
        var camera = owner.Rig.HeadCamera;
        var hitRay = new Ray(camera.transform.position, (target - camera.transform.position).normalized);
        var missRay = new Ray(camera.transform.position, -camera.transform.forward);
        foreach (var pointer in pointers)
        {
            pointer.ManualInputForValidation = true;
            int before = clicks;
            pointer.ProcessRay(hitRay, false, true);
            pointer.ProcessRay(hitRay, true, true);
            pointer.ProcessRay(hitRay, true, true);
            if (clicks != before) throw new Exception("A held trigger clicked before release.");
            pointer.ProcessRay(hitRay, false, true);
            if (clicks != before + 1) throw new Exception("Ray-to-world-panel trigger click failed.");
            pointer.ProcessRay(hitRay, true, true);
            pointer.ProcessRay(missRay, false, true);
            if (clicks != before + 1) throw new Exception("Releasing away from a button clicked it.");
            pointer.ProcessRay(hitRay, true, true);
            pointer.ProcessRay(hitRay, false, false);
            if (clicks != before + 1) throw new Exception("Tracking loss clicked a button.");
            button.interactable = false;
            pointer.ProcessRay(hitRay, true, true);
            pointer.ProcessRay(hitRay, false, true);
            if (clicks != before + 1) throw new Exception("A disabled button was clicked.");
            button.interactable = true;
        }
        button.onClick.RemoveListener(countClick);
        Debug.Log("DOG2_RAY_INTERACTION_PASS: both rays, click on release, no repeat, off-target release, tracking loss, disabled button.");
    }

    private static void StartServer()
    {
        var portProbe = new TcpListener(IPAddress.Loopback, 0); portProbe.Start();
        int port = ((IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
        endpoint = "http://127.0.0.1:" + port + "/";
        listener = new HttpListener(); listener.Prefixes.Add(endpoint); listener.Start();
        Task.Run(async () =>
        {
            try
            {
                while (listener != null && listener.IsListening)
                {
                    var context = await listener.GetContextAsync();
                    byte[] requestBytes;
                    using (var buffer = new MemoryStream())
                    {
                        await context.Request.InputStream.CopyToAsync(buffer);
                        requestBytes = buffer.ToArray();
                    }
                    if (context.Request.Url.AbsolutePath.EndsWith("/transcribe"))
                    {
                        speechRequests++;
                        validSpeechWav = requestBytes.Length > 44 &&
                            Encoding.ASCII.GetString(requestBytes, 0, 4) == "RIFF" &&
                            Encoding.ASCII.GetString(requestBytes, 8, 4) == "WAVE" &&
                            BitConverter.ToInt32(requestBytes, 24) == 16000;
                        byte[] recognized = Encoding.UTF8.GetBytes("{'text':'小狗，请坐下'}".Replace((char)39, (char)34));
                        context.Response.ContentType = "application/json";
                        context.Response.ContentLength64 = recognized.Length;
                        await context.Response.OutputStream.WriteAsync(recognized, 0, recognized.Length);
                        context.Response.Close();
                        continue;
                    }
                    body = Encoding.UTF8.GetString(requestBytes);
                    int n = ++requests;
                    string json = n == 1 ? "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"{\\\"action\\\":\\\"sit\\\",\\\"reason\\\":\\\"我坐下了\\\"}\"}}]}" :
                        "{\"choices\":[{\"message\":{\"content\":\"{\\\"action\\\":\\\"fly\\\"}\"}}]}";
                    if (n == 3) { context.Response.StatusCode = 401; json = "{}"; }
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length); context.Response.Close();
                }
            }
            catch (Exception) { }
        });
    }
    private static void Tick()
    {
        if (!SessionState.GetBool("WjyDog2Validation", false) || !EditorApplication.isPlaying) return;
        try
        {
            if (trainer == null)
            {
                trainer = UnityEngine.Object.FindObjectOfType<Dog2VrTraining>();
                if (trainer == null || trainer.Dog == null || !trainer.Dog.Ready) { trainer = null; return; }
                StartServer(); trainer.apiKey = "validation-placeholder"; trainer.model = "validation"; trainer.endpoint = endpoint;
                var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
                if (canvases.Length == 0 || canvases[0].renderMode != RenderMode.WorldSpace) throw new Exception("Missing stereo-space HUD.");
                if (trainer.Dog.WanderingEnabled) throw new Exception("Dog2 wandering should be disabled.");
                fixedDogPosition = trainer.Dog.transform.position;
                var synchronize = typeof(Dog2OpenXR).GetMethod("SynchronizeSessionState", BindingFlags.NonPublic | BindingFlags.Instance);
                synchronize.Invoke(trainer.Rig, new object[] { false });
                if (trainer.Rig.IsSessionRunning) throw new Exception("Waiting XR session reported ready.");
                synchronize.Invoke(trainer.Rig, new object[] { true });
                if (!trainer.Rig.IsSessionRunning) throw new Exception("Late XR display readiness did not enable controller updates.");
                synchronize.Invoke(trainer.Rig, new object[] { false });
                if (trainer.Rig.IsSessionRunning) throw new Exception("Paused XR display did not suspend controller updates.");
                synchronize.Invoke(trainer.Rig, new object[] { true });
                if (!trainer.Rig.IsSessionRunning) throw new Exception("Resumed XR display did not restore controller updates.");
                synchronize.Invoke(trainer.Rig, new object[] { false });
                Debug.Log("DOG2_LATE_SESSION_RECOVERY_PASS: waiting, delayed readiness, pause, resume.");
                CheckRayInteraction(trainer);
                since = EditorApplication.timeSinceStartup;
                // Validate real WAV upload -> ASR JSON -> automatic LLM HTTP -> action.
                // Only the microphone is substituted; the actual client requests run normally.
                trainer.Speech.transcriptionEndpoint = endpoint + "transcribe";
                typeof(Dog2SpeechInput).GetField("serviceReady", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(trainer.Speech, true);
                var samples = new float[16000];
                for (int i = 0; i < samples.Length; i++) samples[i] = 0.1f * Mathf.Sin(i * 0.1f);
                byte[] wav = Dog2SpeechInput.EncodeWav(samples, samples.Length, 1, 16000);
                var routine = (System.Collections.IEnumerator)typeof(Dog2SpeechInput).GetMethod("Transcribe", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(trainer.Speech, new object[] { wav, (int)typeof(Dog2SpeechInput).GetField("session", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(trainer.Speech) });
                trainer.Speech.StartCoroutine(routine);
                stage = 1;
            }
            if (Vector3.Distance(trainer.Dog.transform.position, fixedDogPosition) > 0.001f)
                throw new Exception("Stationary Dog2 changed position.");
            double elapsed = EditorApplication.timeSinceStartup - since;
            if (stage == 1 && !trainer.Busy && requests >= 1)
            {
                if (speechRequests != 1 || !validSpeechWav || trainer.Speech.LiveText != "小狗，请坐下" || trainer.LastAction != "sit" || !trainer.Dog.IsPerforming || body == null || !body.Contains("小狗，请坐下"))
                    throw new Exception("WAV -> ASR HTTP -> recognized text -> LLM HTTP -> dog action flow failed.");
                Capture();
                trainer.Rig.Origin.position += new Vector3(3f, 0, 0);
                stage = 2; since = EditorApplication.timeSinceStartup;
            }
            else if (stage == 2 && elapsed > 5.5)
            {
                if (trainer.Dog.IsPerforming || trainer.Dog.CurrentAction != "idle") throw new Exception("Dog2 did not return to stationary idle after action.");
                trainer.SubmitTranscript("");
                if (requests != 1 || trainer.Busy) throw new Exception("Empty transcript was sent.");
                trainer.SubmitTranscript("请飞起来"); stage = 3; since = EditorApplication.timeSinceStartup;
            }
            else if (stage == 3 && !trainer.Busy && requests >= 2)
            {
                if (trainer.Dog.IsPerforming || !trainer.Status.Contains("校验失败")) throw new Exception("Invalid model action was not rejected.");
                trainer.SubmitTranscript("坐下"); stage = 4; since = EditorApplication.timeSinceStartup;
            }
            else if (stage == 4 && !trainer.Busy && requests >= 3)
            {
                if (trainer.Dog.IsPerforming || !trainer.Status.Contains("401")) throw new Exception("HTTP error incorrectly triggered dog action.");
                Debug.Log("DOG2_VALIDATION_OK: OpenXR configuration, world-space UI, transcript join, PCM WAV upload to ASR, automatic recognized text to LLM HTTP, action execution and stationary idle after player movement, empty input, invalid action and HTTP failure.");
                Finish(0);
            }
            if (elapsed > 25) throw new Exception("Validation stage timed out: " + stage);
        }
        catch (Exception e) { Debug.LogException(e); Finish(1); }
    }
    private static void Finish(int code)
    {
        SessionState.SetBool("WjyDog2Validation", false); if (listener != null) listener.Close(); listener = null;
        EditorApplication.Exit(code);
    }
    private static void Capture()
    {
        var camera = Camera.main; var target = new RenderTexture(1280, 800, 24);
        camera.targetTexture = target; camera.Render(); var previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(1280, 800, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(Application.dataPath, "wjy_workspace/Dog2/ValidationPreview.png"), image.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
    }
}
#endif