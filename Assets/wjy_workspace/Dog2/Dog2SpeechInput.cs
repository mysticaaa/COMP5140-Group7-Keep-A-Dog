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
        public string transcriptionEndpoint = "http://127.0.0.1:18765/transcribe";
        public string microphoneDevice = "";
        public bool autoStartLocalService = true;
        public bool PreferHeadsetMicrophone;
        public string ServiceStatus { get; private set; } = "本地语音准备中";
        public string SelectedMicrophoneLabel => "麦克风：" +
            (string.IsNullOrEmpty(microphoneDevice) ? (PreferHeadsetMicrophone ? "自动选择头显 / 系统默认" : "系统默认") : microphoneDevice);
        public bool IsListening { get; private set; }
        public bool IsFinalizing { get; private set; }
        public string Status { get; private set; } = "语音待机";
        public string LiveText { get; private set; } = "";
        public string ActiveMicrophone { get; private set; } = "尚未选择";
        public float InputLevel { get; private set; }
        public string[] MicrophoneDevices => Microphone.devices;
        internal string TranscriptLogDirectory { get; set; }
        private AudioClip recording;
        private string recordingDevice;
        private float startedAt, nextMeter;
        private int session;
        private bool checkingService, serviceReady;
        private string serviceError = "";
        private UnityWebRequest activeRequest;
        private System.Diagnostics.Process ownedService;
        private VoiceTranscriptRecord transcriptAttempt;
        private string transcriptLogFile;
        private readonly float[] meter = new float[256];
        private const int MaxSeconds = 60;
        private const int MaxTranscriptionAttempts = 2;
        [Serializable] private sealed class Health { public string service; public bool ready; public string error; public string model; }
        [Serializable] private sealed class Transcript { public string text; public string error; }
        [Serializable]
        private sealed class VoiceTranscriptRecord
        {
            public string timestamp;
            public string microphone;
            public string status;
            public string text;
            public string detail;
            public float durationSeconds;
        }
        private void Awake() { microphoneDevice = PlayerPrefs.GetString("CityDogMicrophone", ""); }
        public void CycleMicrophone()
        {
            if (IsListening || IsFinalizing) return;
            string[] devices = Microphone.devices;
            int next = Array.IndexOf(devices, microphoneDevice) + 1;
            microphoneDevice = next >= devices.Length ? "" : devices[next];
            PlayerPrefs.SetString("CityDogMicrophone", microphoneDevice); PlayerPrefs.Save();
            Status = SelectedMicrophoneLabel;
        }
        public void WarmUp()
        {
            if (checkingService || serviceReady) return;
            if (!ValidEndpoint()) { ServiceStatus = "语音地址须为本机 HTTP 或 HTTPS 服务。"; return; }
            serviceError = ""; checkingService = true; ServiceStatus = "正在预热本地中文语音";
            StartCoroutine(EnsureService(session));
        }
        private bool ValidEndpoint() => Uri.TryCreate(transcriptionEndpoint, UriKind.Absolute, out Uri address) &&
            (address.Scheme == "https" || (address.Scheme == "http" && address.IsLoopback)) && string.IsNullOrEmpty(address.UserInfo);

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
            BeginTranscriptAttempt();
            LiveText = "";
            if (Microphone.devices.Length == 0) { Fail("没有找到麦克风。请检查设备连接和 Windows 麦克风权限。"); return; }
            if (!ValidEndpoint())
            { Fail("语音地址须为本机 HTTP 或 HTTPS 服务。"); return; }
            // A warm-up health request belongs to this capture until it completes.
            InputLevel = 0;
            recordingDevice = string.IsNullOrWhiteSpace(microphoneDevice) ? null : microphoneDevice;
            if (recordingDevice == null && PreferHeadsetMicrophone)
                foreach (string name in Microphone.devices)
                    if (name.IndexOf("Oculus", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Quest", StringComparison.OrdinalIgnoreCase) >= 0)
                    { recordingDevice = name; break; }
            if (recordingDevice != null && Array.IndexOf(Microphone.devices, recordingDevice) < 0)
            { Fail("选中的麦克风已断开，请重新选择。"); return; }
            ActiveMicrophone = recordingDevice ?? "Windows 默认麦克风";
            if (transcriptAttempt != null) transcriptAttempt.microphone = ActiveMicrophone;
            try
            {
                Microphone.GetDeviceCaps(recordingDevice, out int minimum, out int maximum);
                int frequency = maximum == 0 ? 16000 : Mathf.Clamp(16000, minimum, maximum);
                recording = Microphone.Start(recordingDevice, false, MaxSeconds, frequency);
                if (recording == null) { Fail("麦克风录音启动失败，请检查 Windows 桌面应用麦克风权限。"); return; }
                startedAt = Time.realtimeSinceStartup; IsListening = true;
                Status = "麦克风启动中，看到音量后开始说话 · " + ActiveMicrophone;
                WarmUp();
            }
            catch (Exception e) { Fail("录音启动失败：" + e.Message); }
        }
        private void Update()
        {
            if (!IsListening || recording == null) return;
            int position = Microphone.GetPosition(recordingDevice);
            if (position > 0) Status = "正在录音 · " + ActiveMicrophone;
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
            IsListening = false; IsFinalizing = true;
            Status = "正在收好句尾……";
            StartCoroutine(FinishCapture(session));
        }
        private IEnumerator FinishCapture(int current)
        {
            // Keep the last consonant/syllable when the controller or V is released.
            yield return new WaitForSecondsRealtime(.18f);
            if (current != session || recording == null) yield break;
            int frames = Microphone.GetPosition(recordingDevice);
            Microphone.End(recordingDevice);
            var clip = recording; recording = null;
            if (transcriptAttempt != null)
                transcriptAttempt.durationSeconds = (float)Mathf.Clamp(frames, 0, clip.samples) / clip.frequency;
            if (frames <= 0 || frames < clip.frequency / 5)
            { Destroy(clip); Fail("录音太短，本轮未发送。请开始录音后说一句完整的话。"); yield break; }
            frames = Mathf.Min(frames, clip.samples);
            var samples = new float[frames * clip.channels];
            if (!clip.GetData(samples, 0))
            { Destroy(clip); Fail("读取麦克风录音失败。"); yield break; }
            byte[] wav = EncodeWav(samples, frames, clip.channels, clip.frequency);
            Destroy(clip);
            Status = "录音结束，正在用本地 Whisper 转成中文……";
            yield return Transcribe(wav, current);
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
                    activeRequest = request; request.timeout = 2; request.redirectLimit = 0;
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
                        if (health.ready) { serviceReady = true; ServiceStatus = "本地 Whisper " + (health.model ?? "base") + " 已就绪"; break; }
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
                if (!serviceReady) ServiceStatus = serviceError;
            }
        }
        private bool TryStartService(int port)
        {
            try
            {
                if (ownedService != null)
                {
                    if (!ownedService.HasExited) return true;
                    ownedService.Dispose();
                    ownedService = null;
                }
                string folder = Application.isEditor ?
                    Path.Combine(Application.dataPath, "wjy_workspace/Dog2/SpeechService") :
                    Path.GetFullPath(Path.Combine(Application.dataPath, "../SpeechService"));
                if (!Application.isEditor && !File.Exists(Path.Combine(folder, "Runtime~/Scripts/python.exe")))
                {
                    string projectService = Path.GetFullPath(Path.Combine(Application.dataPath,
                        "../../../Assets/wjy_workspace/Dog2/SpeechService"));
                    if (File.Exists(Path.Combine(projectService, "Runtime~/Scripts/python.exe")))
                        folder = projectService;
                }
                string python = Path.Combine(folder, "Runtime~/Scripts/python.exe");
                string script = Path.Combine(folder, "server.py");
                if (!File.Exists(python) || !File.Exists(script))
                { serviceError = "本地 Whisper 尚未安装。请运行游戏旁 SpeechService/Setup.ps1。"; return false; }
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
            for (int attempt = 1; attempt <= MaxTranscriptionAttempts; attempt++)
            {
                while (checkingService && current == session) yield return null;
                if (current != session) yield break;
                if (!serviceReady) { Fail(serviceError); yield break; }

                using (var request = new UnityWebRequest(transcriptionEndpoint, "POST"))
                {
                    request.uploadHandler = new UploadHandlerRaw(wav);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.timeout = 90;
                    request.redirectLimit = 0;
                    request.SetRequestHeader("Content-Type", "audio/wav");
                    activeRequest = request;
                    yield return request.SendWebRequest();
                    activeRequest = null;
                    if (current != session) yield break;
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        ReceiveTranscript(request.downloadHandler.text);
                        yield break;
                    }

                    bool disconnected = request.result == UnityWebRequest.Result.ConnectionError ||
                        request.responseCode == 503;
                    if (disconnected)
                    {
                        serviceReady = false;
                        ServiceStatus = "本地语音连接中断";
                    }
                    string detail = request.responseCode == 0 ? "未收到 HTTP 响应" : "HTTP " + request.responseCode;
                    if (!string.IsNullOrWhiteSpace(request.error)) detail += " · " + request.error;
                    if (!disconnected || attempt == MaxTranscriptionAttempts)
                    {
                        Fail("本地语音识别失败（" + detail + "）。本轮语音未提交。");
                        yield break;
                    }
                    Debug.Log("[Dog2 Speech] " + detail + "。重连后重试当前录音。");
                }

                Status = "本地语音连接中断，正在重连并保留本轮录音……";
                WarmUp();
            }
        }

        private void ReceiveTranscript(string json)
        {
            Transcript result = null;
            try { result = JsonUtility.FromJson<Transcript>(json); } catch (Exception) { }
            if (result == null || result.text == null || !string.IsNullOrEmpty(result.error))
            {
                Fail("本地语音服务没有返回有效的转写结果。");
                return;
            }
            LiveText = result.text.Trim();
            IsFinalizing = false;
            if (string.IsNullOrWhiteSpace(LiveText))
            {
                Status = "未识别到人声或文字，本轮未发送。请检查麦克风选择及输入音量。";
                FinishTranscriptAttempt("no_speech", "", Status);
                return;
            }
            Status = "识别完成";
            FinishTranscriptAttempt("recognized", result.text, "");
            TranscriptReady?.Invoke(LiveText);
        }

        private void BeginTranscriptAttempt()
        {
            if (string.IsNullOrEmpty(TranscriptLogDirectory)) return;
            var timestamp = DateTimeOffset.Now;
            transcriptAttempt = new VoiceTranscriptRecord
            {
                timestamp = timestamp.ToString("O"),
                microphone = SelectedMicrophoneLabel
            };
            if (transcriptLogFile == null)
            {
                string name = timestamp.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".jsonl";
                transcriptLogFile = Path.Combine(TranscriptLogDirectory, name);
            }
        }

        private void FinishTranscriptAttempt(string status, string text, string detail)
        {
            var attempt = transcriptAttempt;
            transcriptAttempt = null;
            if (attempt == null) return;
            attempt.status = status;
            attempt.text = text;
            attempt.detail = detail;
            if (recording != null)
                attempt.durationSeconds = Mathf.Clamp(Time.realtimeSinceStartup - startedAt, 0, MaxSeconds);
            try
            {
                Directory.CreateDirectory(TranscriptLogDirectory);
                File.AppendAllText(transcriptLogFile, JsonUtility.ToJson(attempt) + "\n");
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning("[Dog2 Speech] 无法写入本机语音记录：" + error.Message);
            }
        }

        public void Cancel()
        {
            bool hadSpeech = IsListening || IsFinalizing;
            FinishTranscriptAttempt("cancelled", "", "本轮语音已取消。");
            session++;
            if (activeRequest != null) activeRequest.Abort(); activeRequest = null;
            StopAllCoroutines(); checkingService = false;
            if (recording != null) { Microphone.End(recordingDevice); Destroy(recording); recording = null; }
            IsListening = IsFinalizing = false; InputLevel = 0; LiveText = "";
            Status = hadSpeech ? "本轮语音已取消，没有发送。" : "当前没有正在采集的语音。";
        }
        private void Fail(string message)
        {
            string error = string.IsNullOrEmpty(message) ? "本地语音识别未就绪。" : message;
            FinishTranscriptAttempt("failed", "", error);
            Cancel(); Status = error;
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
