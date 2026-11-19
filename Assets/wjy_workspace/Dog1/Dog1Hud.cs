using System.Collections.Generic;
using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class Dog1Training
    {
        private const float JournalWidth = 326f;
        private const float ChapterWidth = 250f;
        private const float Margin = 22f;
        private static readonly Color Paper = new Color32(251, 237, 215, 252);
        private static readonly Color Linen = new Color32(240, 218, 185, 255);
        private static readonly Color Ink = new Color32(37, 57, 46, 255);
        private static readonly Color Olive = new Color32(80, 106, 77, 255);
        private static readonly Color Clay = new Color32(155, 59, 42, 255);
        private static readonly Color Umber = new Color32(83, 68, 54, 255);
        private static readonly Color RuleInk = new Color32(208, 180, 145, 255);
        private static readonly Color Dark = new Color32(33, 54, 42, 244);
        private static readonly Color LightInk = new Color32(255, 242, 214, 255);
        private static readonly Color SoftLightInk = new Color32(237, 221, 193, 255);

        private static readonly string[] CareNotes =
        {
            "先安排食水、休息和活动区。想一想，每天能给狗留多少时间？",
            "散步包括闻嗅、捡便和看路。出门也要考虑牵引和天气。",
            "预算里有食物、清洁和日常用品，也给体检、疫苗和意外留一点余量。",
            "动作做成后尽快给奖励。练累了，就陪它歇一会儿。",
            "加班或出差时，谁能代为照顾？事先也要确认住处的养犬条件。"
        };

        private GUIStyle title, heading, body, small, meta, big, chapterTitle, button, primary, selected, tabStyle;
        private GUIStyle textField, subtitle, subtitleMeta, statusChip, speechLabel, stack;
        private Font journalFont, displayFont;
        private readonly List<Texture2D> hudTextures = new List<Texture2D>();
        private Vector2 scroll;
        private bool queuedVoiceStart, queuedVoiceStop, queuedVoiceCancel;
        private bool showControls, journalOpen = true, chapterOpen = true, memoryOpen;
        private int pointerVoiceControl;
        private bool IsTextEntryFocused { get; set; }
        private float hudWidth;

        private void StyleHud()
        {
            if (title != null) return;
            journalFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 20);
            displayFont = Font.CreateDynamicFontFromOSFont(new[] { "SimSun", "Microsoft YaHei", "Arial" }, 32);
            body = Label(journalFont, 16, Ink);
            small = Label(journalFont, 14, Umber);
            meta = Label(journalFont, 13, Olive, true);
            heading = Label(journalFont, 17, Ink, true);
            big = Label(displayFont, 28, Ink, true);
            chapterTitle = Label(displayFont, 20, Ink, true);
            title = Label(displayFont, 44, Ink, true);
            subtitle = Label(journalFont, 17, LightInk);
            subtitleMeta = Label(journalFont, 14, SoftLightInk);
            statusChip = Label(journalFont, 13, LightInk, true);
            speechLabel = Label(journalFont, 16, LightInk, true);
            speechLabel.alignment = TextAnchor.MiddleLeft;

            button = ButtonStyle(new Color32(252, 242, 224, 255), new Color32(246, 226, 194, 255),
                new Color32(235, 209, 172, 255), RuleInk, Ink);
            primary = ButtonStyle(Olive, new Color32(67, 94, 65, 255),
                Dark, new Color32(60, 81, 57, 255), LightInk);
            selected = ButtonStyle(Linen, new Color32(244, 224, 196, 255),
                Linen, new Color32(155, 110, 65, 255), Clay);
            tabStyle = new GUIStyle(button) { fixedHeight = 34, fontSize = 14 };
            textField = new GUIStyle(GUI.skin.textField)
            {
                font = journalFont, fontSize = 16, fixedHeight = 37,
                normal = { background = ButtonTexture(new Color32(255, 249, 236, 255), RuleInk), textColor = Ink },
                focused = { background = ButtonTexture(new Color32(255, 249, 236, 255), Clay), textColor = Ink },
                hover = { textColor = Ink }, active = { textColor = Ink },
                border = new RectOffset(8, 8, 8, 8),
                padding = new RectOffset(8, 8, 7, 5), margin = new RectOffset(0, 5, 3, 3)
            };
            stack = new GUIStyle(GUI.skin.scrollView);
        }

        private static GUIStyle Label(Font font, int size, Color color, bool bold = false)
        {
            return new GUIStyle(GUI.skin.label)
            {
                font = font, fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                wordWrap = true, richText = false, normal = { textColor = color },
                padding = new RectOffset(0, 0, 2, 2), margin = new RectOffset(0, 0, 0, 0)
            };
        }

        private GUIStyle ButtonStyle(Color fill, Color hover, Color active, Color stroke, Color color)
        {
            return new GUIStyle(GUI.skin.button)
            {
                font = journalFont, fontSize = 15, fixedHeight = 37,
                alignment = TextAnchor.MiddleCenter, padding = new RectOffset(8, 8, 3, 3),
                margin = new RectOffset(0, 4, 3, 3), wordWrap = false, richText = false,
                border = new RectOffset(8, 8, 8, 8),
                normal = { background = ButtonTexture(fill, stroke), textColor = color },
                hover = { background = ButtonTexture(hover, stroke), textColor = color },
                active = { background = ButtonTexture(active, stroke), textColor = color },
                focused = { background = ButtonTexture(fill, Clay), textColor = color }
            };
        }

        private Texture2D ButtonTexture(Color fill, Color edge)
        {
            const int size = 18;
            const float radius = 7f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var point = new Vector2(x + 0.5f, y + 0.5f);
                    var center = new Vector2(
                        Mathf.Clamp(point.x, radius, size - radius),
                        Mathf.Clamp(point.y, radius, size - radius));
                    float distance = (point - center).magnitude;
                    float horizontal = Mathf.Min(point.x, size - point.x);
                    float vertical = Mathf.Min(point.y, size - point.y);
                    bool inner = distance < radius - 1f && horizontal > 1f && vertical > 1f;
                    Color color = inner ? fill : edge;
                    color.a *= Mathf.Clamp01(radius + 0.5f - distance);
                    pixels[y * size + x] = color;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            hudTextures.Add(texture);
            return texture;
        }

        private static void Box(Rect area, Color color, float radius = 0)
        {
            if (Event.current.type != EventType.Repaint || area.width <= 0 || area.height <= 0) return;
            GUI.DrawTexture(area, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, radius);
        }

        private static void Sheet(Rect area, bool backPage = false)
        {
            Box(new Rect(area.x + 2, area.y + 5, area.width, area.height), new Color32(33, 24, 17, 71), 11f);
            if (backPage) Box(new Rect(area.x + 5, area.y + 5, area.width, area.height), Linen, 11f);
            Box(area, Paper, 10f);
        }

        private static void Divider()
        {
            Box(GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true)), RuleInk);
        }

        private void OnGUI()
        {
            if (life == null) return;
            Event current = Event.current;
            if (pointerVoiceControl != 0 && current.rawType == EventType.MouseUp && current.button == 0)
            {
                if (GUIUtility.hotControl == pointerVoiceControl) GUIUtility.hotControl = 0;
                pointerVoiceControl = 0;
                if (VoiceListening) queuedVoiceStop = true;
                current.Use();
            }
            StyleHud();
            Matrix4x4 previous = GUI.matrix;
            int originalDepth = GUI.depth;
            GUI.depth = -10;
            float scale = Mathf.Max(0.1f, Mathf.Min(Screen.height / 740f, Screen.width / 970f));
            hudWidth = Screen.width / scale;
            float height = Screen.height / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            if (choosingBreed || (!started && string.IsNullOrEmpty(life.BreedId))) DrawBreedPicker(height);
            else if (!started || pause) DrawMenu(height);
            else DrawGame(height);
            string focused = GUI.GetNameOfFocusedControl();
            IsTextEntryFocused = started && !pause && !choosingBreed && journalOpen &&
                (tab == 1 && focused == "Conversation" || tab == 3 && focused != null &&
                    focused.StartsWith("Model", System.StringComparison.Ordinal));
            GUI.matrix = previous;
            GUI.depth = originalDepth;
        }

        private void ProcessVoiceButtons()
        {
            if (queuedVoiceStart)
            {
                queuedVoiceStart = false;
                BeginVoice();
            }
            if (queuedVoiceStop)
            {
                queuedVoiceStop = false;
                EndVoice();
            }
            if (queuedVoiceCancel)
            {
                queuedVoiceCancel = false;
                CancelVoice();
            }
        }

        private void DrawMenu(float height)
        {
            Box(new Rect(0, 0, hudWidth, height), new Color32(43, 31, 23, 89));
            var card = new Rect(48, (height - 389f) * 0.48f, 470, 389);
            Sheet(card, true);
            Box(new Rect(card.x + 22, card.y + 28, 3, card.height - 54), Clay, 1f);
            GUILayout.BeginArea(new Rect(card.x + 40, card.y + 35, card.width - 69, card.height - 58));
            GUILayout.Label("城市里有一只狗", title);
            GUILayout.Label("麦粒的第一周", chapterTitle);
            GUILayout.Space(15);
            GUILayout.Label(life.TotalExperience > 0
                ? "麦粒认得回家的路。今天要一起做些什么？"
                : "下班回来，玄关多了一根牵引绳。你和刚到家的麦粒，开始在城市里一起生活。", body);
            GUILayout.Space(14);
            if (GUILayout.Button(pause ? "回到游戏" : life.TotalExperience > 0 ? "继续生活" : "回家见麦粒", primary))
                BeginPlay();
            if (!pause && GUILayout.Button("选择犬种", button)) OpenBreedPicker();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(showControls ? "收起操作" : "操作说明", button)) showControls = !showControls;
            if (GUILayout.Button("保存并退出", button))
            {
                life.Save();
                Application.Quit();
            }
            GUILayout.EndHorizontal();
            if (showControls)
            {
                GUILayout.Space(6);
                GUILayout.Label("按住右键环顾，配合 WASD 移动。对准食盆或水盆按 E 添加狗粮或饮水，靠近门或花洒按 E 开关。按住 V 说话，松开发送。Esc 暂停。", small);
            }
            GUILayout.EndArea();
        }

        private void DrawGame(float height)
        {
            if (journalOpen) DrawJournal(height);
            else
            {
                var open = new Rect(Margin, Margin, 103, 37);
                if (GUI.Button(open, "打开手账", button)) journalOpen = true;
            }

            DrawChapter();
            DrawSceneFeedback(height);
            string hint = world.InteractionHint;
            if (!string.IsNullOrEmpty(hint))
            {
                float left = journalOpen ? Margin + JournalWidth : Margin;
                float right = chapterOpen ? hudWidth - Margin - ChapterWidth : hudWidth - Margin;
                var prompt = new Rect((left + right - 146f) * .5f, height * .62f, 146f, 35f);
                Box(prompt, Dark, 7f);
                GUI.Label(new Rect(prompt.x + 14f, prompt.y + 7f, 124f, 24f), hint, statusChip);
            }
        }

        private void DrawJournal(float height)
        {
            var sheet = new Rect(Margin, Margin, JournalWidth, Mathf.Min(height - 50, 685));
            Sheet(sheet, true);
            Box(new Rect(sheet.x + 13, sheet.y + 15, 2, sheet.height - 29),
                new Color32(165, 98, 62, 115), 1f);
            GUILayout.BeginArea(new Rect(sheet.x + 24, sheet.y + 13, sheet.width - 36, sheet.height - 27));
            GUILayout.BeginHorizontal();
            GUILayout.Label(DogLifeProgress.GetLocationName(life.CurrentLocation), meta);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("收起", tabStyle, GUILayout.Width(52))) journalOpen = false;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(life.Name, big, GUILayout.ExpandWidth(false));
            GUILayout.Label(" " + life.Level + " 级", meta, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.Label(life.Bones + " 枚小骨头", meta, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
            DrawBar(life.ExperienceForNextLevel > 0 ? (float)life.Experience / life.ExperienceForNextLevel : 1f, Olive, 4);
            GUILayout.Label(life.WellbeingNote, small);
            GUILayout.Space(7);
            Divider();
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            Tab("照护", 0); Tab("说话", 1); Tab("地图", 2); Tab("设置", 3);
            GUILayout.EndHorizontal();

            scroll = GUILayout.BeginScrollView(scroll, stack);
            GUILayout.Space(6);
            switch (tab)
            {
                case 0: DrawCare(); break;
                case 1: DrawInteraction(); break;
                case 2: DrawMap(); break;
                default: DrawSettings(); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawChapter()
        {
            float right = hudWidth - ChapterWidth - Margin;
            float panelHeight = 46;
            var chapter = life.CurrentChapter;
            string description = life.ChapterFinished
                ? "生活继续。今天也可以和麦粒留下新的约定。"
                : chapter.Description;
            if (chapterOpen)
                panelHeight += small.CalcHeight(new GUIContent(description), ChapterWidth - 28) + 14
                    + (life.ChapterFinished ? 0 : (chapter.Tasks.Count + (chapter.RequiredLocation.HasValue ? 1 : 0)) * 21);

            var card = new Rect(right, Margin, ChapterWidth, panelHeight);
            Sheet(card);
            var header = new Rect(card.x + 14, card.y + 9, card.width - 53, 30);
            GUI.Label(header, life.ChapterFinished ? "第一周完成" : chapter.Title, chapterTitle);
            if (GUI.Button(new Rect(card.xMax - 43, card.y + 9, 31, 27), chapterOpen ? "－" : "＋", tabStyle))
                chapterOpen = !chapterOpen;
            if (!chapterOpen) return;
            GUILayout.BeginArea(new Rect(card.x + 14, card.y + 43, card.width - 28, card.height - 49));
            GUILayout.Label(description, small);
            GUILayout.Space(6);
            if (!life.ChapterFinished)
            {
                foreach (var task in chapter.Tasks)
                {
                    int current = life.GetTaskProgress(task.Action);
                    DrawTask(DogLifeProgress.GetActionName(task.Action), current, task.Required);
                }
                if (chapter.RequiredLocation.HasValue)
                {
                    var location = chapter.RequiredLocation.Value;
                    DrawTask("到访" + DogLifeProgress.GetLocationName(location), life.HasVisited(location) ? 1 : 0, 1);
                }
            }
            GUILayout.EndArea();
        }

        private void DrawTask(string label, int current, int required)
        {
            var rect = GUILayoutUtility.GetRect(1, 21, GUILayout.ExpandWidth(true));
            bool complete = current >= required;
            Box(new Rect(rect.x, rect.y + 6, 9, 9), complete ? Olive : RuleInk, 2f);
            GUI.Label(new Rect(rect.x + 16, rect.y, rect.width - 70, 21), label, small);
            GUI.Label(new Rect(rect.xMax - 44, rect.y, 44, 21), current + "/" + required, meta);
        }

        private void DrawSceneFeedback(float height)
        {
            float leftBound = journalOpen ? Margin + JournalWidth + 28 : Margin;
            float openSpace = hudWidth - leftBound - Margin;
            float subtitleWidth = Mathf.Min(506f, openSpace - 22f);
            float subtitleX = leftBound + (openSpace - subtitleWidth) * 0.5f;

            string heard = VoiceTranscript;
            bool capturing = VoiceListening || VoiceFinalizing;
            string line = VoiceListening ? (pointerVoiceControl != 0 ? "正在听你说话，松开鼠标即可发送。" : "正在听你说话，松开 V 即可发送。")
                : VoiceFinalizing ? "正在识别…" : reply;
            string attribution = capturing ? (VoiceListening ? "麦克风" : "语音")
                : !string.IsNullOrEmpty(heard) && caption == "你 · " + heard ? "你说  " + heard : caption;
            float padding = 14f;
            float textWidth = subtitleWidth - 2 * padding;
            float lineHeight = Mathf.Min(74f, subtitle.CalcHeight(new GUIContent(line ?? ""), textWidth));
            float captionHeight = string.IsNullOrEmpty(attribution) ? 0 : Mathf.Min(38f, subtitleMeta.CalcHeight(new GUIContent(attribution), textWidth));
            float cardHeight = lineHeight + captionHeight + 18f;
            var dialogue = new Rect(subtitleX, height - cardHeight - 77, subtitleWidth, cardHeight);
            if (capturing || !string.IsNullOrEmpty(line))
            {
                Box(dialogue, Dark, 10f);
                Box(new Rect(dialogue.x + 3, dialogue.y + 8, 3, dialogue.height - 16),
                    capturing ? new Color32(246, 167, 117, 255) : Linen, 1.5f);
                if (captionHeight > 0)
                    GUI.Label(new Rect(dialogue.x + padding, dialogue.y + 6, textWidth, captionHeight), attribution, subtitleMeta);
                GUI.Label(new Rect(dialogue.x + padding, dialogue.y + captionHeight + 8, textWidth, lineHeight), line, subtitle);
            }

            if (Time.unscaledTime < noticeUntil)
            {
                float maxRight = hudWidth - ChapterWidth - Margin - 16f;
                var toast = new Rect(leftBound, Margin, Mathf.Min(345, maxRight - leftBound), 47);
                if (toast.width >= 150)
                {
                    Box(toast, Dark, 8f);
                    GUI.Label(new Rect(toast.x + 11, toast.y + 6, toast.width - 22, toast.height - 9), notice, subtitleMeta);
                }
            }

            if (walking && walkMarker != null && viewCamera != null)
            {
                Vector3 position = viewCamera.WorldToViewportPoint(walkMarker.position + Vector3.up * 0.45f);
                if (position.z > 0 && position.x is >= 0 and <= 1 && position.y is >= 0 and <= 1)
                {
                    var pin = new Rect(Mathf.Clamp(position.x * hudWidth - 46, leftBound, hudWidth - 122),
                        Mathf.Clamp((1 - position.y) * height - 30, 68, dialogue.y - 30), 92, 26);
                    Box(pin, Dark, 6f);
                    GUI.Label(new Rect(pin.x + 9, pin.y + 4, 74, 20), "散步路标", statusChip);
                }
            }

            const float talkWidth = 214f;
            var talk = new Rect(subtitleX + (subtitleWidth - talkWidth) * 0.5f, height - 65, talkWidth, 34);
            DrawPushToTalk(talk);
            GUI.Label(new Rect(talk.center.x - 100f, height - 27, 202, 21),
                "右键＋WASD 走动  /  Esc 暂停", statusChip);
        }

        private void Tab(string name, int index)
        {
            GUIStyle style = tab == index ? selected : tabStyle;
            if (!GUILayout.Button(name, style, GUILayout.MinWidth(55f)) || tab == index) return;
            tab = index;
            scroll = Vector2.zero;
            GUI.FocusControl(null);
        }

        private void DrawCare()
        {
            GUILayout.Label("照护", heading);
            GUILayout.BeginHorizontal();
            CareButton(DogCareAction.Feed); CareButton(DogCareAction.Water);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            CareButton(DogCareAction.Play); CareButton(DogCareAction.Rest);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            CareButton(DogCareAction.Walk); CareButton(DogCareAction.Clean);
            GUILayout.EndHorizontal();
            if (HomeRestSpot(true) != null && HomeRestSpot(false) != null)
            {
                bool enabled = GUI.enabled;
                GUI.enabled = enabled && Ready && !VoiceListening && !VoiceFinalizing;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("去狗窝趴下", button)) StartCoroutine(RestWithFurniture(true));
                if (GUILayout.Button("去软垫趴下", button)) StartCoroutine(RestWithFurniture(false));
                GUILayout.EndHorizontal();
                GUI.enabled = enabled;
            }
            if (walking && GUILayout.Button("取消这次遛弯", button)) CancelWalk();
            if (life.WasteCount > 0)
                GUILayout.Label("附近还有 " + life.WasteCount + " 处需要清理。", small);
            GUILayout.Space(9);
            Divider();
            GUILayout.Space(8);
            GUILayout.Label("麦粒的状态", heading);
            Meter("饱腹", life.Satiety); Meter("饮水", life.Hydration); Meter("体力", life.Energy);
            Meter("心情", life.Mood); Meter("清洁", life.Cleanliness);
            GUILayout.Space(9);
            Divider();
            GUILayout.Space(8);
            GUILayout.Label("照护手记", heading);
            GUILayout.Label(CareNotes[Mathf.Clamp(life.CurrentChapter.Number - 1, 0, CareNotes.Length - 1)], small);
            if (life.ChapterFinished)
                GUILayout.Label("时间、开支、备用照护和住处安全，你准备好哪些了？", small);
        }

        private void CareButton(DogCareAction action)
        {
            bool before = GUI.enabled;
            GUI.enabled = before && Ready;
            float cooldown = life.GetCooldownRemaining(action);
            string label = DogLifeProgress.GetActionName(action);
            if (cooldown > 0f) label += "  " + Mathf.CeilToInt(cooldown) + "秒";
            if (GUILayout.Button(label, button, GUILayout.ExpandWidth(true))) StartCare(action);
            GUI.enabled = before;
        }

        private void Meter(string name, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, small, GUILayout.Width(49));
            DrawBar(value / 100, value < 30 ? Clay : Olive, 6f);
            GUILayout.Space(8);
            GUILayout.Label(Mathf.RoundToInt(value).ToString(), meta, GUILayout.Width(31));
            GUILayout.EndHorizontal();
        }

        private static void DrawBar(float value, Color fill, float height)
        {
            Rect container = GUILayoutUtility.GetRect(30, height + 8, GUILayout.ExpandWidth(true));
            var bar = new Rect(container.x, container.y + 4, container.width, height);
            Box(bar, Linen, height / 2f);
            Box(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(value), height), fill, height / 2f);
        }

        private void DrawInteraction()
        {
            GUILayout.Label("跟麦粒说话", heading);
            DrawVoiceControl();
            GUILayout.Space(4);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && Ready && !VoiceListening && !VoiceFinalizing;
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("Conversation");
            message = GUILayout.TextField(message, 180, textField);
            if (GUILayout.Button("发送", primary, GUILayout.Width(55))) SendField();
            GUILayout.EndHorizontal();
            bool enter = Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (enter && GUI.enabled && GUI.GetNameOfFocusedControl() == "Conversation")
            {
                SendField();
                Event.current.Use();
            }

            GUILayout.Space(7);
            GUILayout.Label("练习口令", heading);
            GUILayout.BeginHorizontal();
            Cue("坐下"); Cue("趴下"); Cue("过来");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Cue("抬右爪"); Cue("伏身邀玩"); Cue("叼球");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Cue("轻扑一下"); Cue("闻一闻");
            GUILayout.EndHorizontal();
            if (GUILayout.Button("教它「晚安」动作串", button)) Submit("晚安：坐下、抬右爪、拜一拜");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("教它叼棒棒糖", button)) Submit(KeepADog.Demo.DogAgent.TeachExample);
            if (GUILayout.Button("我心情不好", button)) Submit("我心情不好");
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUI.enabled = enabled && Ready && !VoiceListening && !VoiceFinalizing && (CanRewardTrick || agent.CanReward);
            if (GUILayout.Button("表扬", primary)) ApplyFeedback(true);
            GUI.enabled = enabled && Ready && !VoiceListening && !VoiceFinalizing && (CanRewardTrick || agent.CanGiveFeedback);
            if (GUILayout.Button("再示范", button)) ApplyFeedback(false);
            GUI.enabled = enabled;
            GUILayout.EndHorizontal();

            GUILayout.Space(9);
            if (GUILayout.Button(memoryOpen ? "收起互动记忆" : "查看互动记忆", selected))
                memoryOpen = !memoryOpen;
            if (!memoryOpen) return;
            GUILayout.Label("它学会了什么", heading);
            GUILayout.Label(agent.LongTermSummary, small);
            GUILayout.Label(routines.Summary, small);
            GUILayout.Space(5);
            GUILayout.Label("最近互动", heading);
            GUILayout.Label(agent.ShortTermSummary, small);
        }

        private void DrawVoiceControl()
        {
            Rect area = GUILayoutUtility.GetRect(1, 44, GUILayout.ExpandWidth(true));
            DrawPushToTalk(area);
            if (VoiceListening)
                DrawBar(speech != null ? Mathf.Clamp01(speech.InputLevel * 4f) : 0f, Clay, 4f);
            if (!string.IsNullOrWhiteSpace(VoiceStatus)) GUILayout.Label(VoiceStatus, small);
            if ((VoiceListening || VoiceFinalizing) && GUILayout.Button("取消这句", button))
                queuedVoiceCancel = true;
        }

        private void DrawPushToTalk(Rect area)
        {
            Event evt = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive, area);
            bool canBegin = GUI.enabled && VoiceAvailable && Ready && !VoiceFinalizing && !VoiceListening;
            switch (evt.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (canBegin && evt.button == 0 && area.Contains(evt.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        GUI.FocusControl(null);
                        pointerVoiceControl = id;
                        queuedVoiceStart = true;
                        evt.Use();
                    }
                    break;
                case EventType.Repaint:
                    Color surface = VoiceListening ? Clay : canBegin ? Dark : new Color32(98, 100, 85, 255);
                    Box(area, surface, 7f);
                    DrawMicrophone(new Vector2(area.x + 19, area.center.y), LightInk);
                    GUI.Label(new Rect(area.x + 38, area.y, area.width - 44, area.height),
                        VoiceListening ? "正在录音" : VoiceFinalizing ? "正在识别" : "按住说话   [V]", speechLabel);
                    break;
            }
        }

        private static void DrawMicrophone(Vector2 center, Color color)
        {
            Box(new Rect(center.x - 4f, center.y - 9f, 8f, 12f), color, 4f);
            Box(new Rect(center.x - 7f, center.y, 2f, 7f), color, 1f);
            Box(new Rect(center.x + 5f, center.y, 2f, 7f), color, 1f);
            Box(new Rect(center.x - 6f, center.y + 6f, 12f, 2f), color, 1f);
            Box(new Rect(center.x - 1f, center.y + 7f, 2f, 4f), color, 1f);
        }

        private void Cue(string cue)
        {
            if (GUILayout.Button(cue, button, GUILayout.ExpandWidth(true))) Submit(cue);
        }

        private void SendField()
        {
            if (Submit(message)) message = "";
        }

        private void DrawMap()
        {
            GUILayout.Label("街区地图", heading);
            GUILayout.Space(4);
            LocationRow(DogLocation.Apartment, "暖灯、食水碗和常躺的地毯。");
            LocationRow(DogLocation.Park, "有步道、长椅和散步标记的街区绿地。");
            LocationRow(DogLocation.Terrace, "在花箱和暖灯旁看城市入夜。");
            GUILayout.Space(6);
            if (life.CanClaimDaily)
            {
                if (GUILayout.Button("领取今日小骨头", primary)) Notify(life.ClaimDailyBonus().Message);
            }
            else GUILayout.Label("今天的回家礼物已领取。", small);
        }

        private void LocationRow(DogLocation location, string description)
        {
            Divider();
            GUILayout.Space(7);
            GUILayout.Label(DogLifeProgress.GetLocationName(location), chapterTitle);
            GUILayout.Label(description, small);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && Ready && !VoiceListening && !VoiceFinalizing;
            if (life.CurrentLocation == location) GUILayout.Label("你在这里", meta);
            else if (life.IsUnlocked(location))
            {
                if (GUILayout.Button("前往", primary)) Visit(location);
            }
            else
            {
                string price = life.GetRequiredLevel(location) + " 级  /  " + life.GetUnlockCost(location) + " 枚小骨头";
                if (GUILayout.Button("解锁  " + price, button)) Buy(location);
            }
            GUI.enabled = enabled;
            GUILayout.Space(9);
        }

        private void DrawSettings()
        {
            GUILayout.Label("声音与显示", heading);
            if (GUILayout.Button(quiet ? "开启奖励提示音" : "关闭奖励提示音", button))
            {
                quiet = !quiet;
                PlayerPrefs.SetInt("CityDogQuiet", quiet ? 1 : 0);
                PlayerPrefs.Save();
            }
            if (GUILayout.Button(dog.ShowKeypoints ? "隐藏骨骼关键点" : "显示骨骼关键点", button))
                dog.ShowKeypoints = !dog.ShowKeypoints;
            GUILayout.Space(9);
            Divider();
            GUILayout.Space(8);
            GUILayout.Label("狗狗模型", heading);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && Ready && !VoiceListening && !VoiceFinalizing;
            if (GUILayout.Button("更换模型", button)) OpenBreedPicker();
            GUI.enabled = enabled;
            GUILayout.Space(9);
            Divider();
            GUILayout.Space(8);
            GUILayout.Label("语音与 Agent", heading);
            if (!string.IsNullOrWhiteSpace(VoiceStatus)) GUILayout.Label(VoiceStatus, small);
            GUI.enabled = enabled && Ready && !VoiceListening && !VoiceFinalizing;
            if (GUILayout.Button(useModel ? "停用可选模型" : "启用可选模型", button)) useModel = !useModel;
            if (useModel)
            {
                GUILayout.Label("需要模型判断时，会发送这一句和训练摘要。", small);
                GUILayout.Label("接口地址", small);
                GUI.SetNextControlName("ModelEndpoint");
                endpoint = GUILayout.TextField(endpoint ?? "", 400, textField);
                GUILayout.Label("模型名", small);
                GUI.SetNextControlName("ModelName");
                model = GUILayout.TextField(model ?? "", 160, textField);
                GUILayout.Label("本次运行的 API Key", small);
                GUI.SetNextControlName("ModelKey");
                sessionKey = GUILayout.PasswordField(sessionKey, '*', 512, textField);
                if (GUILayout.Button("使用本机 Ollama 地址", button))
                {
                    endpoint = "http://127.0.0.1:11434/v1/chat/completions";
                    model = "";
                    sessionKey = "";
                }
            }
            GUILayout.Space(9);
            Divider();
            GUILayout.Space(8);
            GUILayout.Label("存档", heading);
            if (GUILayout.Button(confirmReset ? "确认清除口令记忆" : "清除口令记忆", button))
            {
                if (confirmReset)
                {
                    agent.Reset();
                    dog.ResetCompanion();
                    pendingTrick = false;
                    reply = routines.Clear() ? "可以重新教我口令啦。" : "没能清除保存的动作串。";
                }
                confirmReset = !confirmReset;
            }
            GUI.enabled = enabled;
            if (!string.IsNullOrEmpty(life.SaveError)) GUILayout.Label(life.SaveError, small);
            if (!string.IsNullOrEmpty(agent.SaveError)) GUILayout.Label(agent.SaveError, small);
        }

        private void DisposeHud()
        {
            if (journalFont != null) Destroy(journalFont);
            if (displayFont != null) Destroy(displayFont);
            foreach (var texture in hudTextures) Destroy(texture);
        }
    }
}
