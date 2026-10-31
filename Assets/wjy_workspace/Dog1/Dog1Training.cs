using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Wjy.DogTraining
{
    public sealed class Dog1Training : MonoBehaviour
    {
        public DogCompanion dog;
        [Header("DeepSeek configuration (fill in before Play)")]
        public string model = "deepseek-flash";
        public string endpoint = "https://api.deepseek.com/chat/completions";
        [Tooltip("Saved in the scene. Clear the real key before publishing to GitHub.")] public string apiKey = "";
        private string status = "请填写 API Key、model 和 URL，然后选择训练口令。";
        private string lastPrompt = "尚未发送";
        private string lastResponse = "尚未收到回复";
        private string actionLabel = "自由散步";
        private bool busy;
        private Material groundMaterial;
        private UnityWebRequest activeRequest;
        private GUIStyle titleStyle, bodyStyle, smallStyle, buttonStyle, fieldStyle;
        private Font chineseFont;
        private Vector2 responseScroll;
        private readonly List<Message> history = new List<Message>();
        private const string SystemPrompt =
            "你正在扮演一只接受训练的小狗。你可以愿意执行，也可以因为分心或不情愿而拒绝。" +
            "理解玩家本次训练口令，根据本次真实意愿选择动作。不要默认每次拒绝或每次服从。" +
            "你只能执行本轮指定的动作，或者拒绝；不得执行其他动作。" +
            "必须仅返回一个 JSON 对象，不要 Markdown，不要额外文字。" +
            "格式示例：{\"action\":\"sit\",\"reason\":\"我愿意坐下。\"}。" +
            "action 只能是 crouch（趴下）、sit（坐下）、spin（转圈）、refuse（这次不想执行）。" +
            "reason 用简短中文描述小狗反应。refuse 不是接口错误，只用于你主动拒绝本次口令。";

        [Serializable] public sealed class Message
        {
            public string role;
            public string content;
            public Message(string role, string content) { this.role = role; this.content = content; }
        }
        [Serializable] public sealed class Format { public string type = "json_object"; }
        [Serializable] public sealed class RequestBody
        {
            public string model;
            public Message[] messages;
            public bool stream = false;
            public Format response_format = new Format();
            public int max_tokens = 300;
        }
        [Serializable] public sealed class Choice { public Message message; }
        [Serializable] public sealed class Completion { public Choice[] choices; }
        [Serializable] public sealed class Decision { public string action; public string reason; }

        private void Awake()
        {
            Transform oldSphere = transform.Find("Dog Placeholder Sphere");
            if (oldSphere != null) oldSphere.gameObject.SetActive(false);
            Camera camera = Camera.main;
            if (camera != null)
            {
                camera.transform.position = new Vector3(0, 1.65f, -5f);
                camera.transform.rotation = Quaternion.Euler(12f, 0, 0);
                camera.fieldOfView = 65f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.52f, 0.67f, 0.78f);
                if (camera.GetComponent<Dog1PlayerView>() == null) camera.gameObject.AddComponent<Dog1PlayerView>();
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.6f, 0.63f, 0.68f);
            GameObject fillObject = new GameObject("Dog1 soft fill light");
            fillObject.transform.SetParent(transform);
            fillObject.transform.rotation = Quaternion.Euler(40f, 145f, 0);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = 0.7f;
            fill.color = new Color(0.85f, 0.91f, 1f); fill.shadows = LightShadows.None;
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Dog1 Training Yard"; ground.transform.SetParent(transform);
            ground.transform.position = new Vector3(0, -0.02f, 0);
            ground.transform.localScale = Vector3.one * 3f;
            groundMaterial = new Material(Shader.Find("Standard"));
            groundMaterial.color = new Color(0.32f, 0.40f, 0.29f);
            groundMaterial.SetFloat("_Glossiness", 0.05f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            if (dog == null)
            {
                GameObject actor = new GameObject("AI German Shepherd");
                actor.transform.SetParent(transform);
                actor.transform.position = camera != null ? camera.transform.position + camera.transform.forward * 3.4f + camera.transform.right * 1.7f : new Vector3(1.8f, 0, 0);
                Vector3 position = actor.transform.position; position.y = 0; actor.transform.position = position;
                dog = actor.AddComponent<DogCompanion>();
            }
        }
        private void SetupStyles()
        {
            if (titleStyle != null) return;
            chineseFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "SimHei", "Arial" }, 20);
            bodyStyle = new GUIStyle(GUI.skin.label) { font = chineseFont, fontSize = 18, wordWrap = true };
            titleStyle = new GUIStyle(bodyStyle) { fontSize = 29, fontStyle = FontStyle.Bold };
            smallStyle = new GUIStyle(bodyStyle) { fontSize = 15 };
            buttonStyle = new GUIStyle(GUI.skin.button) { font = chineseFont, fontSize = 21, fixedHeight = 52 };
            fieldStyle = new GUIStyle(GUI.skin.textField) { font = chineseFont, fontSize = 17, fixedHeight = 33 };
        }

        private readonly DeepSeekConnectionPanel connectionPanel = new DeepSeekConnectionPanel();
        private Vector2 panelScroll;

        private void OnGUI()
        {
            SetupStyles();
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                new Vector3(Screen.width / 1280f, Screen.height / 800f, 1));
            GUILayout.BeginArea(new Rect(24, 24, 490, 752), GUI.skin.box);
            panelScroll = GUILayout.BeginScrollView(panelScroll);
            GUILayout.Space(12);
            GUILayout.Label("AI 德牧 · 训练与陪伴", titleStyle);
            GUILayout.Label("点击口令 → LLM 决策 → 德牧执行骨骼动作", smallStyle);
            GUILayout.Space(14);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = !busy && (dog == null || !dog.IsPerforming);
            connectionPanel.Draw(ref endpoint, ref model, ref apiKey, smallStyle, fieldStyle);
            GUILayout.Space(12);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("趴下", buttonStyle)) Train("趴下", "crouch");
            if (GUILayout.Button("坐下", buttonStyle)) Train("坐下", "sit");
            if (GUILayout.Button("转圈", buttonStyle)) Train("转圈", "spin");
            GUILayout.EndHorizontal();
            if (GUILayout.Button("重置状态和对话记录", GUILayout.Height(30))) ResetTraining();
            GUI.enabled = previousEnabled;
            GUILayout.Space(10);
            GUILayout.Label(status, bodyStyle);
            GUILayout.Space(10);
            GUILayout.Label("本次发送的训练提示词", smallStyle);
            GUILayout.Label(lastPrompt, bodyStyle);
            GUILayout.Space(10);
            GUILayout.Label("模型回复", smallStyle);
            responseScroll = GUILayout.BeginScrollView(responseScroll, GUILayout.Height(165));
            GUILayout.Label(lastResponse, bodyStyle);
            GUILayout.EndScrollView();
            GUILayout.FlexibleSpace();
            GUILayout.Label("右键拖动：环顾四周；按住右键 + WASD：移动", bodyStyle);
            GUILayout.Label("动作完成后等待 3 秒恢复散步；拒绝时头顶冒烟。", smallStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUILayout.BeginArea(new Rect(750, 90, 480, 100));
            GUILayout.Label("德牧伙伴", titleStyle);
            GUILayout.Label(dog != null ? dog.StateLabel : actionLabel, bodyStyle);
            GUILayout.EndArea();
            GUI.matrix = previous;
        }

        private void Train(string command, string expectedAction)
        {
            if (busy || (dog != null && dog.IsPerforming)) return;
            if (dog == null || !dog.Ready) { status = "狗模型尚未准备完成，请等待导入。"; return; }
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model))
            {
                status = "尚未发送：请先填写 API Key 和 model。";
                return;
            }
            if (!Uri.TryCreate((endpoint ?? "").Trim(), UriKind.Absolute, out Uri requestUri) ||
                (requestUri.Scheme != Uri.UriSchemeHttps && requestUri.Scheme != Uri.UriSchemeHttp))
            {
                status = "尚未发送：URL 必须是完整的 http:// 或 https:// 接口地址。";
                return;
            }
            lastPrompt = "你现在是小狗的身份，我要训练你" + command +
                "。本轮允许执行的 action 为 " + expectedAction +
                "；如果这次不想执行，请返回 refuse。请按系统要求返回 JSON。";
            lastPrompt += " 当前狗的状态：" + dog.StateLabel + "。";
            StartCoroutine(SendTraining(lastPrompt, expectedAction));
        }

        private IEnumerator SendTraining(string prompt, string expectedAction)
        {
            busy = true;
            status = "正在等待 DeepSeek 的真实回复……";
            var messages = new List<Message> { new Message("system", SystemPrompt) };
            messages.AddRange(history);
            messages.Add(new Message("user", prompt));
            string payload = JsonUtility.ToJson(new RequestBody
            {
                model = model.Trim(), messages = messages.ToArray()
            });
            using (var request = new UnityWebRequest(endpoint.Trim(), "POST"))
            {
                activeRequest = request;
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + apiKey.Trim());
                request.timeout = 60;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    status = "请求失败（HTTP " + request.responseCode + "）。请检查网络、Key、model 和额度；不触发狗的命令动作。";
                    lastResponse = "没有可执行的模型回复。";
                }
                else
                {
                    try
                    {
                        var completion = JsonUtility.FromJson<Completion>(request.downloadHandler.text);
                        if (completion == null || completion.choices == null || completion.choices.Length == 0 ||
                            completion.choices[0].message == null)
                            throw new FormatException("响应缺少 choices[0].message。");
                        string content = completion.choices[0].message.content;
                        lastResponse = string.IsNullOrEmpty(content) ? "（空回复）" : content;
                        Decision decision = ParseDecision(content, expectedAction);
                        ApplyDecision(decision);
                        history.Add(new Message("user", prompt));
                        history.Add(new Message("assistant", content));
                        while (history.Count > 12) history.RemoveRange(0, 2);
                        status = "已根据模型返回的 action 执行动作。";
                    }
                    catch (Exception error)
                    {
                        status = "回复未通过校验：" + error.Message + " 不触发狗的命令动作。";
                    }
                }
                activeRequest = null;
            }
            busy = false;
        }

        public static Decision ParseDecision(string content, string expectedAction)
        {
            if (expectedAction != "crouch" && expectedAction != "sit" && expectedAction != "spin")
                throw new FormatException("未知训练口令。");
            if (string.IsNullOrWhiteSpace(content)) throw new FormatException("模型返回空内容。");
            var decision = JsonUtility.FromJson<Decision>(content.Trim());
            if (decision == null || (decision.action != expectedAction && decision.action != "refuse"))
                throw new FormatException("action 必须是本轮口令或 refuse。");
            return decision;
        }

        private void ApplyDecision(Decision decision)
        {
            StartCoroutine(dog.PerformAction(decision.action));
        }
        private void ResetTraining()
        {
            history.Clear();
            if (dog != null) dog.ResetCompanion();
            actionLabel = "自由散步";
            lastPrompt = "尚未发送";
            lastResponse = "尚未收到回复";
            status = "已重置，可以开始新一轮训练。";
        }

        private void OnDestroy()
        {
            if (activeRequest != null) activeRequest.Abort();
            if (groundMaterial != null) Destroy(groundMaterial);
            if (chineseFont != null) Destroy(chineseFont);
        }
    }
}