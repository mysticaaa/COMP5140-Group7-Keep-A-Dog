using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Wjy.DogTraining
{
    public sealed class Dog2SpeechInput : MonoBehaviour
    {
        public event Action<string> TranscriptReady;
        public string transcriptionEndpoint = "http://127.0.0.1:8765/transcribe";
        public string microphoneDevice = "";
        public bool autoStartLocalService = true;
        public bool IsListening { get; private set; }
        public bool IsFinalizing { get; private set; }
        public string Status { get; private set; } = "本地 Whisper 语音待机：A 开始，再按 A 停止并发送。";
        public string LiveText { get; private set; } = "";
        public string ActiveMicrophone { get; private set; } = "尚未选择";
        public float InputLevel { get; private set; }
        public string[] MicrophoneDevices => Microphone.devices;
        private AudioClip recording;
        private string recordingDevice;
        private float startedAt, nextMeter;
        private int session;
        private bool checkingService, serviceReady;
        private string serviceError = "";
        private UnityWebRequest activeRequest;
        private System.Diagnostics.Process ownedService;
        private readonly float[] meter = new float[256];
        private const int MaxSeconds = 60;
        [Serializable] private sealed class Health { public string service; public bool ready; public string error; }
        [Serializable] private sealed class Transcript { public string text; public string error; }

        public static string JoinTranscript(string confirmed, string partial)
        {
            confirmed = (confirmed ?? "").Trim(); partial = (partial ?? "").Trim();
            if (string.IsNullOrEmpty(confirmed)) return partial;
            if (string.IsNullOrEmpty(partial) || confirmed.EndsWith(partial, StringComparison.Ordinal)) return confirmed;
            return confirmed + " " + partial;
        }
        public void StartListening()
        {
            if (IsListening || IsFinalizing) return;
            if (Microphone.devices.Length == 0) { Fail("没有找到麦克风。请连接 Quest 麦克风，或在面板选择电脑麦克风测试。"); return; }
            if (!Uri.TryCreate(transcriptionEndpoint, UriKind.Absolute, out Uri address) ||
                (address.Scheme != "http" && address.Scheme != "https"))
            { Fail("语音识别 URL 必须是有效的 HTTP 地址。"); return; }
            session++; LiveText = ""; InputLevel = 0;
            recordingDevice = string.IsNullOrWhiteSpace(microphoneDevice) ? null : microphoneDevice;
            if (recordingDevice == null)
                foreach (string name in Microphone.devices)
                    if (name.IndexOf("Oculus", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Quest", StringComparison.OrdinalIgnoreCase) >= 0)
                    { recordingDevice = name; break; }
            if (recordingDevice != null && Array.IndexOf(Microphone.devices, recordingDevice) < 0)
            { Fail("选中的麦克风已断开，请重新选择。"); return; }
            ActiveMicrophone = recordingDevice ?? "Windows 默认麦克风";
            try
            {
                recording = Microphone.Start(recordingDevice, false, MaxSeconds, 16000);
                if (recording == null) { Fail("麦克风录音启动失败，请检查 Windows 桌面应用麦克风权限。"); return; }
                startedAt = Time.realtimeSinceStartup; IsListening = true;
                Status = "正在录音：" + ActiveMicrophone + "。再按 A 停止，识别后自动发送。";
                serviceReady = false; serviceError = ""; checkingService = true;
                StartCoroutine(EnsureService(session));
            }
            catch (Exception e) { Fail("录音启动失败：" + e.Message); }
        }
        private void Update()
        {
            if (!IsListening || recording == null) return;
            int position = Microphone.GetPosition(recordingDevice);
            if (Time.realtimeSinceStartup >= nextMeter)
            {
                nextMeter = Time.realtimeSinceStartup + 0.1f;
                if (position >= 256)
                {
                    recording.GetData(meter, position - 256);
                    float peak = 0f; foreach (float value in meter) peak = Mathf.Max(peak, Mathf.Abs(value));
                    InputLevel = peak;
                }
            }
            if (position <= 0 && Time.realtimeSinceStartup - startedAt > 3f)
            { Fail("麦克风没有产生录音数据。请在面板换一个输入设备，并检查 Windows 麦克风权限。"); return; }
            if (Time.realtimeSinceStartup - startedAt >= MaxSeconds - 0.5f) StopAndSend();
        }
        public void StopAndSend()
        {
            if (!IsListening || recording == null) return;
            int frames = Microphone.GetPosition(recordingDevice);
            Microphone.End(recordingDevice);
            var clip = recording; recording = null;
            IsListening = false; IsFinalizing = true;
            if (frames <= 0 || frames < clip.frequency / 5)
            { Destroy(clip); Fail("录音太短，本轮未发送。请开始录音后说一句完整的话。"); return; }
            var samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);
            byte[] wav = EncodeWav(samples, frames, clip.channels, clip.frequency);
            Destroy(clip);
            Status = "录音结束，正在用本地 Whisper 转成中文……";
            StartCoroutine(Transcribe(wav, session));
        }
        public static byte[] EncodeWav(float[] samples, int frames, int channels, int frequency)
        {
            int count = Math.Min(samples.Length, frames * channels);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)channels); writer.Write(frequency);
                writer.Write(frequency * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
                for (int i = 0; i < count; i++) writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(samples[i], -1f, 1f) * 32767));
                return stream.ToArray();
            }
        }
        private IEnumerator EnsureService(int current)
        {
            bool triedStart = false;
            var address = new Uri(transcriptionEndpoint);
            string healthUrl = new Uri(address, "/health").AbsoluteUri;
            float deadline = Time.realtimeSinceStartup + 35f;
            while (current == session && Time.realtimeSinceStartup < deadline)
            {
                using (var request = UnityWebRequest.Get(healthUrl))
                {
                    activeRequest = request; request.timeout = 2;
                    yield return request.SendWebRequest();
                    activeRequest = null;
                    if (current != session) yield break;
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        Health health = null;
                        try { health = JsonUtility.FromJson<Health>(request.downloadHandler.text); } catch (Exception) { }
                        if (health == null || health.service != "wjy-dog2-whisper-v1")
                        { serviceError = "语音服务地址上的程序不是 Dog2 Whisper 服务。"; break; }
                        if (!string.IsNullOrEmpty(health.error)) { serviceError = health.error; break; }
                        if (health.ready) { serviceReady = true; break; }
                    }
                    else if (!triedStart)
                    {
                        triedStart = true;
                        if (!autoStartLocalService || !address.IsLoopback) { serviceError = "语音服务未启动，请运行 SpeechService/Start.ps1。"; break; }
                        if (!TryStartService(address.Port)) break;
                    }
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            if (current == session)
            {
                checkingService = false;
                if (!serviceReady && string.IsNullOrEmpty(serviceError)) serviceError = "本地 Whisper 服务启动超时，请查看 SpeechService/Logs~/service.log。";
            }
        }
        private bool TryStartService(int port)
        {
            try
            {
                string folder = Application.isEditor ?
                    Path.Combine(Application.dataPath, "wjy_workspace/Dog2/SpeechService") :
                    Path.GetFullPath(Path.Combine(Application.dataPath, "../SpeechService"));
                string python = Path.Combine(folder, "Runtime~/Scripts/python.exe");
                string script = Path.Combine(folder, "server.py");
                if (!File.Exists(python) || !File.Exists(script))
                { serviceError = "本地 Whisper 尚未安装。请先运行 Assets/wjy_workspace/Dog2/SpeechService/Setup.ps1。"; return false; }
                var start = new System.Diagnostics.ProcessStartInfo(python, ((char)34) + script + ((char)34) + " --port " + port)
                {
                    WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                ownedService = System.Diagnostics.Process.Start(start);
                return ownedService != null;
            }
            catch (Exception e) { serviceError = "无法启动本地 Whisper：" + e.Message; return false; }
        }
        private IEnumerator Transcribe(byte[] wav, int current)
        {
            while (checkingService && current == session) yield return null;
            if (current != session) yield break;
            if (!serviceReady) { Fail(serviceError); yield break; }
            using (var request = new UnityWebRequest(transcriptionEndpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(wav);
                request.downloadHandler = new DownloadHandlerBuffer(); request.timeout = 120;
                request.SetRequestHeader("Content-Type", "audio/wav");
                activeRequest = request;
                yield return request.SendWebRequest(); activeRequest = null;
                if (current != session) yield break;
                if (request.result != UnityWebRequest.Result.Success)
                { Fail("本地语音识别失败（HTTP " + request.responseCode + "），本轮未发送。请查看语音服务日志。"); yield break; }
                Transcript result = null;
                try { result = JsonUtility.FromJson<Transcript>(request.downloadHandler.text); } catch (Exception) { }
                LiveText = result != null ? (result.text ?? "").Trim() : "";
                IsFinalizing = false;
                if (string.IsNullOrWhiteSpace(LiveText))
                { Status = "未识别到人声或文字，本轮未发送。请检查麦克风选择及输入音量。"; yield break; }
                Status = "Whisper 已识别中文，正在发送给 DeepSeek。";
                TranscriptReady?.Invoke(LiveText);
            }
        }
        public void Cancel()
        {
            bool hadSpeech = IsListening || IsFinalizing;
            session++; StopAllCoroutines(); checkingService = false;
            if (activeRequest != null) activeRequest.Abort(); activeRequest = null;
            if (recording != null) { Microphone.End(recordingDevice); Destroy(recording); recording = null; }
            IsListening = IsFinalizing = false; InputLevel = 0; LiveText = "";
            Status = hadSpeech ? "本轮语音已取消，没有发送。" : "当前没有正在采集的语音。";
        }
        private void Fail(string message)
        {
            Cancel(); Status = string.IsNullOrEmpty(message) ? "本地语音识别未就绪。" : message;
            Debug.LogWarning("[Dog2 Speech] " + Status);
        }
        private void OnDisable() { Cancel(); }
        private void OnDestroy()
        {
            Cancel();
            if (ownedService != null)
            {
                try { if (!ownedService.HasExited) ownedService.Kill(); } catch (Exception) { }
                ownedService.Dispose();
            }
        }
    }
}