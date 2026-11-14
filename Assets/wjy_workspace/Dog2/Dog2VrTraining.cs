using System;
using System.Collections;
using System.Threading.Tasks;
using KeepADog.Demo;
using UnityEngine;
using UnityEngine.UI;

namespace Wjy.DogTraining
{
    public sealed class Dog2VrTraining : MonoBehaviour
    {
        public string model = "deepseek-flash";
        public string endpoint = "https://api.deepseek.com/chat/completions";
        [SerializeField] private string apiKey = "";
        public bool enableOpenXR = true;
        public bool keepDogStationary = true;
        public Vector3 dogStartPosition = new Vector3(1.5f, 0f, -2f);
        public DogCompanion Dog { get; private set; }
        public Dog2SpeechInput Speech { get; private set; }
        public Dog2OpenXR Rig { get; private set; }
        public bool Busy { get; private set; }
        public string LastTranscript { get; private set; } = "";
        public string LastAction { get; private set; } = "";
        public string Status { get; private set; } = "叫一声麦粒。它正在熟悉这里。";
        public Canvas InteractionCanvas { get; private set; }

        private DogAgent agent;
        private DogRoutines routines;
        private DogNeighborhood world;
        private Font font;
        private Text speechCaption, body, voiceButtonLabel;
        private Button rewardButton;
        private Transform captions;
        private string manualText = "";
        private string sessionKey = "";
        private bool useModel;
        private bool showConfig = true;
        private bool modelSettings;
        private bool routineReward;
        private float routineRewardDeadline;
        private string routineName;
        private string[] routineSteps;
        private float petTime, lastPetAt = -10f;
        private Vector3 leftPrevious, rightPrevious;
        private bool leftWasTracked, rightWasTracked;
        private bool lastDisplay;
        private readonly DeepSeekConnectionPanel connectionPanel = new DeepSeekConnectionPanel();
        private GUIStyle labelStyle, buttonStyle, fieldStyle;

        [Serializable] public sealed class Decision { public string action, reason; }

        private void Awake()
        {
            string testSaves = Application.isEditor ? Environment.GetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES") : null;
            agent = new DogAgent(string.IsNullOrEmpty(testSaves) ? null : System.IO.Path.Combine(testSaves, "dog-memory.json"));
            routines = new DogRoutines(string.IsNullOrEmpty(testSaves) ? null : System.IO.Path.Combine(testSaves, "dog-routine.json"));
            sessionKey = Environment.GetEnvironmentVariable("KEEP_A_DOG_API_KEY") ?? apiKey ?? "";
            useModel = !string.IsNullOrWhiteSpace(sessionKey);
            Transform old = transform.Find("Dog Placeholder Sphere");
            if (old != null) old.gameObject.SetActive(false);
            foreach (Light light in FindObjectsOfType<Light>()) light.enabled = false;
            Rig = gameObject.AddComponent<Dog2OpenXR>();
            Rig.SkipInitializationForValidation = !enableOpenXR;
            Rig.ToggleSpeech += ToggleVoice;
            Rig.BeginSpeech += BeginVoice;
            Rig.EndSpeech += EndVoice;
            Rig.CancelSpeech += CancelVoice;
            Speech = gameObject.AddComponent<Dog2SpeechInput>();
            Speech.TranscriptReady += SubmitTranscript;
            world = new DogNeighborhood(transform);
            world.Load(0);
            world.ConfigureCamera(Rig.HeadCamera);
            Vector3 start = world.PlayerStart;
            Rig.Origin.position = new Vector3(start.x, 0, start.z);
            Rig.LimitLocomotion = world.ConstrainPlayerMotion;
            var actor = new GameObject("麦粒 · VR German Shepherd");
            actor.transform.SetParent(transform, false);
            actor.transform.position = world.ConstrainDogPosition(dogStartPosition);
            Dog = actor.AddComponent<DogCompanion>();
            Dog.user = Rig.HeadCamera.transform;
            Dog.PositionLimit = world.ConstrainDogPosition;
            Dog.HasPlayClearance = world.HasDogPlayClearance;
            Dog.SetPracticeArea(actor.transform.position, world.FetchPosition, world.ToyPosition);
            Dog.SetWanderingEnabled(!keepDogStationary);
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 32);
            BuildHud();
        }

        private static readonly Color Paper = new Color32(249, 232, 202, 255);
        private static readonly Color Ink = new Color32(39, 56, 47, 255);
        private static readonly Color Clay = new Color32(185, 91, 60, 255);
        private static readonly Color Sage = new Color32(223, 231, 205, 255);

        private void BuildHud()
        {
            InteractionCanvas = MakeCanvas("麦粒的空间手记", new Vector2(840, 530), .0013f);
            Transform page = InteractionCanvas.transform;
            page.gameObject.AddComponent<GraphicRaycaster>();
            AddBlock(page, new Vector2(0, 0), new Vector2(840, 530), Paper);
            AddBlock(page, new Vector2(0, 515), new Vector2(840, 15), Clay);
            AddLabel(page, "麦粒的手记", 53, Ink, new Vector2(40, 415), new Vector2(760, 72));
            body = AddLabel(page, "", 32, Ink, new Vector2(40, 258), new Vector2(760, 149));
            voiceButtonLabel = MakeButton(page, "Voice toggle", "点此录音 · 或按住 A", new Vector2(40, 150), new Vector2(365, 82), ToggleVoice);
            MakeButton(page, "Cancel voice", "取消语音 · B", new Vector2(435, 150), new Vector2(365, 82), CancelVoice);
            rewardButton = MakeButton(page, "Praise", "表扬麦粒", new Vector2(40, 40), new Vector2(230, 78), Praise).GetComponentInParent<Button>();
            MakeButton(page, "Call dog", "叫它过来", new Vector2(304, 40), new Vector2(230, 78), () => SubmitTranscript("过来"));
            MakeButton(page, "Bring panel forward", "摆到眼前", new Vector2(570, 40), new Vector2(230, 78), BringPanelForward);
            BringPanelForward();

            var subtitle = MakeCanvas("空间语音字幕", new Vector2(910, 166), .00118f);
            captions = subtitle.transform;
            AddBlock(captions, Vector2.zero, new Vector2(910, 166), Ink);
            speechCaption = AddLabel(captions, "", 34, Paper, new Vector2(26, 12), new Vector2(858, 145));
            speechCaption.alignment = TextAnchor.MiddleCenter;
            PositionCaptions();
            foreach (bool right in new[] { false, true })
            {
                var pointer = new GameObject(right ? "右手射线" : "左手射线").AddComponent<Dog2ControllerRay>();
                pointer.transform.SetParent(transform, false);
                pointer.RightHand = right;
                pointer.Rig = Rig;
                pointer.TargetCanvas = InteractionCanvas;
            }
        }

        private Canvas MakeCanvas(string title, Vector2 size, float scale)
        {
            var go = new GameObject(title, typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * scale;
            go.GetComponent<RectTransform>().sizeDelta = size;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Rig.HeadCamera;
            return canvas;
        }

        private static GameObject UiObject(Transform parent, string name, Vector2 at, Vector2 size, Type component)
        {
            var go = new GameObject(name, typeof(RectTransform), component);
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = at;
            rect.sizeDelta = size;
            return go;
        }

        private static void AddBlock(Transform parent, Vector2 at, Vector2 size, Color color)
        {
            var image = UiObject(parent, "纸张", at, size, typeof(Image)).GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private Text AddLabel(Transform parent, string text, int size, Color color, Vector2 at, Vector2 bounds)
        {
            Text label = UiObject(parent, "文字", at, bounds, typeof(Text)).GetComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.color = color;
            label.text = text;
            label.supportRichText = false;
            label.raycastTarget = false;
            return label;
        }

        private Text MakeButton(Transform parent, string name, string caption, Vector2 at, Vector2 bounds, UnityEngine.Events.UnityAction action)
        {
            var go = UiObject(parent, name, at, bounds, typeof(Image));
            var button = go.AddComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();
            ColorBlock colors = button.colors;
            colors.normalColor = Sage;
            colors.highlightedColor = new Color32(174, 198, 150, 255);
            colors.pressedColor = new Color32(140, 170, 118, 255);
            colors.disabledColor = new Color32(212, 207, 183, 255);
            button.colors = colors;
            button.onClick.AddListener(action);
            var label = AddLabel(go.transform, caption, 31, Ink, Vector2.zero, bounds);
            label.alignment = TextAnchor.MiddleCenter;
            return label;
        }

        private Vector3 FlatForward()
        {
            Vector3 forward = Vector3.ProjectOnPlane(Rig.HeadCamera.transform.forward, Vector3.up);
            return forward.sqrMagnitude > 0.01f ? forward.normalized : Rig.Origin.forward;
        }

        private void BringPanelForward()
        {
            var head = Rig.HeadCamera.transform;
            Vector3 forward = FlatForward();
            Vector3 left = Vector3.Cross(forward, Vector3.up);
            InteractionCanvas.transform.SetPositionAndRotation(head.position + forward * 1.8f + left * .72f - Vector3.up * .2f,
                Quaternion.LookRotation(forward, Vector3.up));
            if (captions != null) PositionCaptions();
        }

        private void PositionCaptions()
        {
            Vector3 forward = FlatForward();
            captions.SetPositionAndRotation(Rig.HeadCamera.transform.position + forward * 1.83f - Vector3.up * .67f,
                Quaternion.LookRotation(forward, Vector3.up));
        }

        private void Update()
        {
            if (body == null) return;
            if (lastDisplay && !Rig.DisplayRunning) CancelVoice();
            lastDisplay = Rig.DisplayRunning;
            if (routineReward && Time.unscaledTime > routineRewardDeadline) routineReward = false;
            voiceButtonLabel.text = Speech.IsListening ? "结束并识别" : Speech.IsFinalizing ? "正在转写" : "点此录音 · 或按住 A";
            rewardButton.interactable = CanInteract && (agent.CanReward || routineReward);
            body.text = Status + "\n" + Dog.StateLabel;
            speechCaption.text = Speech.IsListening ? "正在听  " + Mathf.RoundToInt(Speech.InputLevel * 100) + "%" :
                Speech.IsFinalizing ? "正在识别…" :
                string.IsNullOrEmpty(LastTranscript) ? Speech.Status : "你 · " + LastTranscript;
            captions.gameObject.SetActive(Speech.IsListening || Speech.IsFinalizing || !string.IsNullOrEmpty(LastTranscript) || Speech.Status != "语音待机");
            CheckPetting();
        }

        private void CheckPetting()
        {
            bool left = HandMovedNear(false, ref leftPrevious, ref leftWasTracked);
            bool right = HandMovedNear(true, ref rightPrevious, ref rightWasTracked);
            if (!CanInteract || !left && !right)
            {
                petTime = Mathf.Max(0f, petTime - 2f * Time.deltaTime);
                return;
            }
            petTime += Time.deltaTime;
            if (petTime < .35f || Time.unscaledTime - lastPetAt < 3f) return;
            lastPetAt = Time.unscaledTime;
            petTime = 0f;
            Status = "麦粒感到了你的手，抬头摇了摇尾巴。";
            StartCoroutine(ShowPetReply());
        }

        private IEnumerator ShowPetReply()
        {
            Busy = true;
            yield return Dog.PerformAction("pet", 0.15f);
            Busy = false;
        }

        private bool HandMovedNear(bool right, ref Vector3 previous, ref bool wasTracked)
        {
            bool tracked = Rig.TryGetHandPosition(right, out Vector3 at);
            float speed = tracked && wasTracked ? Vector3.Distance(at, previous) / Mathf.Max(Time.deltaTime, .001f) : 0f;
            previous = at;
            wasTracked = tracked;
            return tracked && speed > .035f && speed < 2.4f &&
                Vector3.Distance(at, Dog.transform.position + Dog.transform.forward * .26f + Vector3.up * .56f) < .35f;
        }

        private bool CanInteract => Dog != null && Dog.Ready && !Busy && !Dog.IsPerforming && !Speech.IsListening && !Speech.IsFinalizing;

        private void BeginVoice()
        {
            if (!CanInteract) return;
            LastTranscript = "";
            PositionCaptions();
            Speech.StartListening();
        }
        private void EndVoice() { if (Speech.IsListening) Speech.StopAndSend(); }
        private void CancelVoice() { if (Speech != null && (Speech.IsListening || Speech.IsFinalizing)) Speech.Cancel(); }
        public void ToggleVoice()
        {
            if (Speech.IsListening) EndVoice();
            else BeginVoice();
        }

        public void SubmitTranscript(string transcript)
        {
            if (!CanInteract || string.IsNullOrWhiteSpace(transcript)) return;
            LastTranscript = transcript.Trim();
            if (LastTranscript.Length > 180) LastTranscript = LastTranscript.Substring(0, 180);
            PositionCaptions();
            string command = DogSpeechText.Normalize(LastTranscript);
            if (command.TrimEnd('。', '！', '!', '.') == "真棒" || command == "好狗狗" || command == "表扬")
            {
                Praise();
                return;
            }
            if (!string.IsNullOrWhiteSpace(command)) StartCoroutine(Respond(command));
        }

        private IEnumerator Respond(string line)
        {
            Busy = true;
            LastAction = "";
            routineReward = false;
            routineName = null;
            routineSteps = routines.Recall(line);
            if (routineSteps == null && routines.TryParse(line, out string name, out var learned))
            {
                routineName = name;
                routineSteps = learned;
            }
            DogAgentDecision decision = null;
            if (routineSteps == null)
            {
                if (useModel)
                {
                    Status = "麦粒正在听……";
                    Task<DogAgentDecision> request = agent.DecideAsync(line, endpoint, model, sessionKey);
                    while (!request.IsCompleted) yield return null;
                    if (request.Status == TaskStatus.RanToCompletion) decision = request.Result;
                }
                if (decision == null)
                {
                    if (useModel) Status = "模型暂不可用，先用本地理解。";
                    decision = agent.Decide(line);
                }
                routineSteps = decision.Steps;
            }
            if (routineSteps != null)
            {
                Status = DogRoutines.Describe(routineSteps);
                bool success = true;
                foreach (string motion in routineSteps)
                {
                    LastAction = motion;
                    yield return Dog.PerformAction(motion, .45f);
                    success = DogActionSelection.Succeeded(Dog, motion);
                    if (!success) break;
                }
                routineReward = success;
                routineRewardDeadline = Time.unscaledTime + 40f;
                Status = success ? "做完了。可以表扬麦粒。" : "这步没做成，麦粒会停下来等你。";
            }
            else
            {
                Status = decision.Reply;
                string motion = DogActionSelection.MotionFor(decision);
                if (motion != null)
                {
                    LastAction = motion;
                    yield return Dog.PerformAction(motion, .6f);
                    bool success = DogActionSelection.Succeeded(Dog, motion);
                    agent.ReportExecution(decision, success);
                    if (motion.StartsWith("fetch_", StringComparison.Ordinal))
                        Status = success ? "麦粒把东西叼到你面前。" : "麦粒这次没把东西叼回来。";
                    else if (motion == "pounce" && !success) Status = "前面空间有点小，麦粒停了下来。";
                }
            }
            Busy = false;
        }

        private void Praise()
        {
            if (!CanInteract) return;
            if (routineReward || agent.CanReward) Dog.PlayTailWag();
            if (routineReward)
            {
                Status = string.IsNullOrEmpty(routineName) ? "麦粒听到了你的表扬。" :
                    routines.Remember(routineName, routineSteps) ? "麦粒记住了「" + routineName + "」。" : "动作串保存失败。";
                routineReward = false;
            }
            else Status = agent.Feedback(true).Reply;
        }

        public static Decision ParseDecision(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new FormatException("空回复。");
            var result = JsonUtility.FromJson<Decision>(json);
            if (result == null || (result.action != "crouch" && result.action != "sit" && result.action != "spin" && result.action != "refuse" && result.action != "idle"))
                throw new FormatException("不支持的 action。");
            return result;
        }

        private void OnGUI()
        {
            if (Rig == null) return;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = 17, wordWrap = true };
                buttonStyle = new GUIStyle(GUI.skin.button) { font = font, fontSize = 16, fixedHeight = 35 };
                fieldStyle = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 16, fixedHeight = 32 };
            }
            GUILayout.BeginArea(new Rect(20, 20, 390, showConfig ? Mathf.Min(Screen.height - 40, 600) : 48), GUI.skin.box);
            if (GUILayout.Button(showConfig ? "收起 PC 调试面板" : "打开 PC 调试面板", buttonStyle)) showConfig = !showConfig;
            if (showConfig)
            {
                GUILayout.Label(Rig.DisplayStatus, labelStyle);
                GUILayout.Label(Speech.Status, labelStyle);
                GUILayout.Label(Status, labelStyle);
                if (GUILayout.Button("重新连接头显 · F9", buttonStyle)) Rig.RetryXR();
                if (GUILayout.Button(Speech.IsListening ? "结束并识别 · F8" : "开始说话 · F8", buttonStyle)) ToggleVoice();
                manualText = GUILayout.TextField(manualText, 180, fieldStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("发送文字", buttonStyle)) SubmitTranscript(manualText);
                if (GUILayout.Button("表扬麦粒", buttonStyle)) Praise();
                GUILayout.EndHorizontal();
                if (GUILayout.Button(modelSettings ? "收起模型与输入" : "模型与输入", buttonStyle)) modelSettings = !modelSettings;
                if (modelSettings)
                {
                    useModel = GUILayout.Toggle(useModel, "使用可选 LLM", labelStyle);
                    connectionPanel.Draw(ref endpoint, ref model, ref sessionKey, labelStyle, fieldStyle);
                    if (GUILayout.Button("切换麦克风 · " + (string.IsNullOrEmpty(Speech.microphoneDevice) ? "自动" : Speech.microphoneDevice), buttonStyle))
                    {
                        string[] devices = Speech.MicrophoneDevices;
                        int next = Array.IndexOf(devices, Speech.microphoneDevice) + 1;
                        Speech.microphoneDevice = next < devices.Length ? devices[next] : "";
                    }
                }
            }
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            if (Speech != null) Speech.TranscriptReady -= SubmitTranscript;
            world?.Dispose();
            if (font != null) Destroy(font);
        }
    }
}
