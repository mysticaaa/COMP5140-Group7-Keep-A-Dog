#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Wjy.DogTraining;

[InitializeOnLoad]
public static class Dog2AgentValidation
{
    private const string Active = "CityDog2AgentValidation";
    private static bool started;
    private static double deadline;
    private static HttpListener listener;
    private static volatile bool validWave, validPrompt;
    private static string address;

    static Dog2AgentValidation() => EditorApplication.update += Tick;

    public static void Run()
    {
        Dog2Validation.CheckControllerProfiles();
        EditorSceneManager.OpenScene("Assets/Scenes/Dog2.unity");
        UnityEngine.Object.FindObjectOfType<Dog2VrTraining>().enableOpenXR = false;
        SessionState.SetBool(Active, true);
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying) return;
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 85;
        if (EditorApplication.timeSinceStartup > deadline) { Finish(new TimeoutException()); return; }
        if (started) return;
        var trainer = UnityEngine.Object.FindObjectOfType<Dog2VrTraining>();
        if (trainer == null || trainer.Dog == null || !trainer.Dog.Ready) return;
        started = true;
        trainer.StartCoroutine(RunSteps(trainer));
    }

    private static IEnumerator RunSteps(Dog2VrTraining trainer)
    {
        var steps = Check(trainer);
        while (true)
        {
            object item = null;
            bool next = false;
            Exception error = null;
            try { next = steps.MoveNext(); if (next) item = steps.Current; }
            catch (Exception caught) { error = caught; }
            if (!next) { Finish(error); yield break; }
            yield return item;
        }
    }

    private static IEnumerator Check(Dog2VrTraining trainer)
    {
        Require(trainer.InteractionCanvas.renderMode == RenderMode.WorldSpace, "Missing world journal");
        Canvas.ForceUpdateCanvases();
        var button = trainer.InteractionCanvas.transform.Find("Cancel voice").GetComponent<Button>();
        int clicks = 0;
        button.onClick.AddListener(() => clicks++);
        var pointers = UnityEngine.Object.FindObjectsOfType<Dog2ControllerRay>();
        Require(pointers.Length == 2, "Expected both controller rays");
        var camera = trainer.Rig.HeadCamera;
        var at = button.transform.TransformPoint(button.GetComponent<RectTransform>().rect.center);
        Ray ray = new Ray(camera.transform.position, (at - camera.transform.position).normalized);
        foreach (var pointer in pointers)
        {
            pointer.ManualInputForValidation = true;
            int before = clicks;
            pointer.ProcessRay(ray, true, true);
            pointer.ProcessRay(ray, false, true);
            Require(clicks == before + 1, "Controller did not select world-space button");
            pointer.ProcessRay(ray, true, true);
            pointer.ProcessRay(ray, false, false);
            Require(clicks == before + 1, "Lost tracking incorrectly clicked");
        }

        StartStub();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(Dog2VrTraining).GetField("useModel", flags).SetValue(trainer, true);
        trainer.endpoint = address + "chat";
        trainer.model = "local-fixture";
        trainer.Speech.transcriptionEndpoint = address + "transcribe";
        typeof(Dog2SpeechInput).GetField("serviceReady", flags).SetValue(trainer.Speech, true);
        var samples = new float[16000];
        for (int i = 0; i < samples.Length; i++) samples[i] = .1f * Mathf.Sin(i * .1f);
        byte[] wav = Dog2SpeechInput.EncodeWav(samples, samples.Length, 1, 16000);
        var type = typeof(Dog2SpeechInput);
        int session = (int)type.GetField("session", flags).GetValue(trainer.Speech);
        var routine = (IEnumerator)type.GetMethod("Transcribe", flags).Invoke(trainer.Speech, new object[] { wav, session });
        yield return trainer.Speech.StartCoroutine(routine);
        while (trainer.Busy) yield return null;
        Require(validWave && validPrompt && trainer.LastTranscript == "给我展示你会的两招好吗" && trainer.LastAction == "paw",
            "WAV upload, transcript and LLM planned sequence failed");
        trainer.SubmitTranscript("真棒");
        trainer.SubmitTranscript("晚安：坐下、抬右爪、拜一拜");
        while (trainer.Busy) yield return null;
        Require(trainer.LastAction == "bow", "Named routine did not finish");
        trainer.SubmitTranscript("真棒");
        var path = Path.Combine(Environment.GetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES"), "dog-routine.json");
        Require(new DogRoutines(path).Recall("晚安")?.Length == 3, "Named routine did not reload");
        trainer.SubmitTranscript("晚安");
        while (trainer.Busy) yield return null;
        Require(trainer.LastAction == "bow", "Recalled routine didn't run");
        trainer.SubmitTranscript("麥力,請你坐下,然後抬起你的右腳。");
        while (trainer.Busy) yield return null;
        Require(trainer.LastAction == "paw", "Actual Whisper spelling and separators did not parse as a two-step routine");
        trainer.SubmitTranscript("请飞上天");
        while (trainer.Busy) yield return null;
        Require(string.IsNullOrEmpty(trainer.LastAction), "Unlisted motion was executed");
        trainer.SubmitTranscript(" ");
        Require(!trainer.Busy, "Empty transcript activated Agent");
        Capture(camera);
    }

    private static void StartStub()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        address = "http://127.0.0.1:" + port + "/";
        listener = new HttpListener();
        listener.Prefixes.Add(address);
        listener.Start();
        Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening)
                {
                    var context = await listener.GetContextAsync();
                    var buffer = new MemoryStream();
                    await context.Request.InputStream.CopyToAsync(buffer);
                    byte[] bytes = buffer.ToArray();
                    string answer;
                    if (context.Request.Url.AbsolutePath == "/transcribe")
                    {
                        validWave = bytes.Length > 44 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" &&
                            BitConverter.ToInt32(bytes, 24) == 16000;
                        answer = "{\"text\":\"给我展示你会的两招好吗\"}";
                    }
                    else
                    {
                        string prompt = Encoding.UTF8.GetString(bytes);
                        var reply = new Envelope { choices = new[] { new Choice { message = new Message() } } };
                        if (prompt.Contains("展示") || prompt.Contains("display"))
                        {
                            validPrompt = prompt.Contains("Dog memory");
                            reply.choices[0].message.content = "{\"kind\":\"sequence\",\"action\":\"none\",\"target\":\"none\",\"reply\":\"\",\"steps\":[\"sit\",\"paw\"]}";
                        }
                        else reply.choices[0].message.content = "{\"kind\":\"sequence\",\"action\":\"none\",\"target\":\"none\",\"reply\":\"\",\"steps\":[\"fly\",\"sit\"]}";
                        answer = JsonUtility.ToJson(reply);
                    }
                    byte[] data = Encoding.UTF8.GetBytes(answer);
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = data.Length;
                    await context.Response.OutputStream.WriteAsync(data, 0, data.Length);
                    context.Response.Close();
                }
            }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
        });
    }

    private static void Capture(Camera camera)
    {
        string path = Environment.GetEnvironmentVariable("KEEP_A_DOG_XR_CAPTURE");
        if (string.IsNullOrEmpty(path)) return;
        var target = new RenderTexture(1280, 800, 24);
        var image = new Texture2D(1280, 800, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        var cameraTarget = camera.targetTexture;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = cameraTarget;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static void Require(bool condition, string description) { if (!condition) throw new Exception(description); }
    private static void Finish(Exception error)
    {
        listener?.Close();
        SessionState.SetBool(Active, false);
        if (error == null) Debug.Log("CITY_DOG_XR_AGENT_OK: XR controller profiles, world journal rays, PCM WAV, ASR fixture, model intent, motion chain, persisted recall, reject unknown.");
        else Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    [Serializable] private sealed class Envelope { public Choice[] choices; }
    [Serializable] private sealed class Choice { public Message message; }
    [Serializable] private sealed class Message { public string content; }
}
#endif
