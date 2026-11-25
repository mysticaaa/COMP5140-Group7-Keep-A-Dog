using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Wjy.DogTraining
{
    // The headset is another view/controller of the same Dog1 simulation and saves.
    public sealed partial class Dog1Training
    {
        public Dog2OpenXR HeadsetRig { get; private set; }
        public Canvas HeadsetCanvas { get; private set; }
        private Canvas xrProximityCanvas;
        private Camera desktopCamera;
        private Transform apartmentHead;
        private Font xrFont;
        private Text xrSummary, xrVitals, xrReply, xrVoice, xrTitle, xrTalkLabel, xrProximityHint;
        private Text xrStoryHeading, xrStoryPrompt, xrStoryProgress;
        private Button xrTalkButton, xrCancelButton;
        private Image xrInputTrack, xrInputFill;
        private Sprite xrRoundedSprite, xrPawStamp;
        private Texture2D xrPawTexture;
        private readonly List<Button> xrTabs = new List<Button>();
        private readonly Button[] xrStoryOptions = new Button[3];
        private readonly Text[] xrStoryOptionLabels = new Text[3];
        private static readonly Color32 BookCover = new Color32(33, 66, 61, 255);
        private static readonly Color32 BookPage = new Color32(255, 240, 214, 255);
        private static readonly Color32 BookTab = new Color32(241, 220, 178, 255);
        private static readonly Color32 BookNote = new Color32(228, 238, 215, 255);
        private static readonly Color32 BookBookmark = new Color32(161, 69, 49, 255);
        private RectTransform xrPage;
        private bool wasInHeadset, xrInteract;
        private int xrTab, xrBreed, xrActionPage, xrActionFilter;
        private float xrRefresh, petDuration, lastPet = -10;
        private Vector3 leftPetAt, rightPetAt;
        private bool leftPetTracked, rightPetTracked;
        private readonly List<Action> xrBindings = new List<Action>();
        private bool InHeadset => HeadsetRig != null && HeadsetRig.DisplayRunning;

        private void SetupHeadset()
        {
            desktopCamera = viewCamera;
            xrBreed = Mathf.Max(0, Array.IndexOf(DogBreed.Options, DogBreed.Find(life.BreedId)));
            var host = new GameObject("Dog1 headset controls");
            host.SetActive(false); host.transform.SetParent(transform, false);
            HeadsetRig = host.AddComponent<Dog2OpenXR>();
            HeadsetRig.SeparateHeadCamera = true;
            HeadsetRig.SkipInitializationForValidation = Application.isBatchMode ||
                Environment.GetEnvironmentVariable("KEEP_A_DOG_DISABLE_XR") == "1";
            host.SetActive(true);
            HeadsetRig.LimitLocomotion = world.ConstrainPlayerMotion;
            HeadsetRig.BeginSpeech += BeginVoice;
            HeadsetRig.EndSpeech += EndVoice;
            HeadsetRig.ToggleSpeech += ToggleVoice;
            HeadsetRig.CancelSpeech += CancelVoice;
            HeadsetRig.RecenterPanel += () =>
            {
                if (HeadsetCanvas.gameObject.activeSelf) HeadsetCanvas.gameObject.SetActive(false);
                else
                {
                    if (started && StoryChoiceLabels?.Count > 0) xrTab = 0;
                    HeadsetCanvas.gameObject.SetActive(true);
                    BuildHeadsetPage();
                    BringHeadsetMenu();
                }
            };
            apartmentHead = new GameObject("Dog1 apartment interaction pose").transform;
            apartmentHead.SetParent(host.transform, false);
            world.ConfigureCamera(HeadsetRig.HeadCamera);
            xrFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 26);
            var panel = new GameObject("Dog1 空间手记", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            panel.transform.SetParent(host.transform, false);
            panel.transform.localScale = Vector3.one * .0015f;
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(820, 760);
            HeadsetCanvas = panel.GetComponent<Canvas>();
            HeadsetCanvas.renderMode = RenderMode.WorldSpace;
            HeadsetCanvas.worldCamera = HeadsetRig.HeadCamera;
            xrRoundedSprite = Sprite.Create(ButtonTexture(Color.white, Color.white),
                new Rect(0, 0, 18, 18), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect,
                new Vector4(8, 8, 8, 8));
            XrImage(panel.transform, "布面封底", new Vector2(-9,-8), new Vector2(838,776), BookCover);
            XrImage(panel.transform, "底页", new Vector2(5,-2), new Vector2(810,763), BookTab);
            XrImage(panel.transform, "纸张", Vector2.zero, new Vector2(820,760), BookPage);
            XrImage(panel.transform, "书脊", new Vector2(16,19), new Vector2(3,721), BookTab);
            XrImage(panel.transform, "章节色签", new Vector2(754,727), new Vector2(34,42), BookBookmark);
            XrImage(panel.transform, "封面铭牌", new Vector2(36,677), new Vector2(748,67), BookCover);
            var stamp = XrImage(panel.transform, "麦粒的爪印", new Vector2(47,688), new Vector2(43,43), BookNote);
            xrPawStamp = CreateHeadsetPawStamp();
            stamp.sprite = xrPawStamp;
            stamp.type = Image.Type.Simple;
            xrTitle = XrText(panel.transform, "麦粒的手账", new Vector2(99,688), new Vector2(610,43), 34);
            xrTitle.fontStyle = FontStyle.Bold;
            xrTitle.color = LightInk;
            xrTitle.alignment = TextAnchor.MiddleLeft;
            xrSummary = XrText(panel.transform, "", new Vector2(36,632), new Vector2(748,34), 24);
            xrVitals = XrText(panel.transform, "", new Vector2(36,604), new Vector2(748,28), 21);
            xrVitals.color = Umber;
            string[] tabs = { "故事", "照料", "训练", "出门", "设置", "犬种", "动作" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int page = i;
                xrTabs.Add(XrButton(panel.transform, tabs[i], new Vector2(36+i*107,544), new Vector2(100,56),
                    () => { xrTab=page; BuildHeadsetPage(); }));
            }
            xrPage = new GameObject("内容", typeof(RectTransform)).GetComponent<RectTransform>();
            xrPage.SetParent(panel.transform, false);
            xrPage.anchorMin = xrPage.anchorMax = xrPage.pivot = Vector2.zero;
            xrPage.anchoredPosition = new Vector2(36,223); xrPage.sizeDelta = new Vector2(748,309);
            XrImage(panel.transform, "麦粒的回应", new Vector2(36,122), new Vector2(748,92), BookNote);
            xrReply = XrText(panel.transform, "", new Vector2(53,128), new Vector2(714,78), 24);
            xrReply.alignment = TextAnchor.MiddleLeft;
            xrVoice = XrText(panel.transform, "", new Vector2(36,85), new Vector2(748,32), 22);
            xrVoice.color = Olive;
            xrInputTrack = XrImage(panel.transform, "录音音量底", new Vector2(36,79), new Vector2(748,4), BookTab);
            xrInputFill = XrImage(panel.transform, "录音音量", new Vector2(36,79), new Vector2(0,4), BookBookmark);
            xrTalkButton = XrButton(panel.transform, "录音 / 结束 · A", new Vector2(36,20), new Vector2(296,56), ToggleVoice);
            StyleHeadsetPrimary(xrTalkButton);
            xrTalkLabel = xrTalkButton.GetComponentInChildren<Text>();
            xrCancelButton = XrButton(panel.transform, "取消 · B", new Vector2(344,20), new Vector2(172,56), CancelVoice);
            XrButton(panel.transform, "收起 · X", new Vector2(528,20), new Vector2(256,56), () => panel.SetActive(false));
            var hintObject = new GameObject("近处互动提示", typeof(RectTransform), typeof(Canvas));
            hintObject.transform.SetParent(host.transform, false);
            hintObject.transform.localScale = Vector3.one * .00135f;
            hintObject.GetComponent<RectTransform>().sizeDelta = new Vector2(470,72);
            xrProximityCanvas = hintObject.GetComponent<Canvas>();
            xrProximityCanvas.renderMode = RenderMode.WorldSpace;
            xrProximityCanvas.worldCamera = HeadsetRig.HeadCamera;
            XrImage(hintObject.transform, "互动提示底", Vector2.zero, new Vector2(470,72), Dark);
            xrProximityHint = XrText(hintObject.transform, "", new Vector2(16,12), new Vector2(438,48), 28);
            xrProximityHint.color = LightInk;
            xrProximityHint.alignment = TextAnchor.MiddleCenter;
            hintObject.SetActive(false);
            foreach (bool right in new[] { false, true })
            {
                var pointer = new GameObject(right ? "Dog1 right ray" : "Dog1 left ray").AddComponent<Dog2ControllerRay>();
                pointer.transform.SetParent(host.transform, false);
                pointer.Rig = HeadsetRig; pointer.TargetCanvas = HeadsetCanvas; pointer.RightHand = right;
            }
            PlaceHeadset(); BuildHeadsetPage(); panel.SetActive(false);
        }

        private void PlaceHeadset()
        {
            if (HeadsetRig == null) return;
            var rig = HeadsetRig;
            Vector3 trackedOffset = rig.HeadCamera.transform.position - rig.Origin.position;
            rig.Origin.position = world.PlayerStart - Vector3.up * 1.65f -
                new Vector3(trackedOffset.x, 0, trackedOffset.z);
            BringHeadsetMenu();
        }
        private void BringHeadsetMenu()
        {
            if (HeadsetCanvas == null) return;
            Transform head = HeadsetRig.HeadCamera.transform;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 towardBook = forward * 1.85f - right * .57f;
            HeadsetCanvas.transform.SetPositionAndRotation(head.position + towardBook - Vector3.up * .18f,
                Quaternion.LookRotation(towardBook, Vector3.up));
        }
        private void UpdateHeadset()
        {
            if (HeadsetRig == null) return;
            bool active = InHeadset;
            if (active != wasInHeadset)
            {
                CancelVoice(); wasInHeadset = active;
                desktopCamera.enabled = !active;
                var listener = desktopCamera.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = !active;
                desktopCamera.tag = active ? "Untagged" : "MainCamera";
                HeadsetRig.HeadCamera.enabled = active;
                HeadsetRig.HeadCamera.GetComponent<AudioListener>().enabled = active;
                HeadsetRig.HeadCamera.tag = active ? "MainCamera" : "Untagged";
                viewCamera = active ? HeadsetRig.HeadCamera : desktopCamera;
                if (dog != null) dog.user = viewCamera.transform;
                speech.PreferHeadsetMicrophone = active;
                if (view != null) view.enabled = !active;
                HeadsetCanvas.gameObject.SetActive(active);
                if (active)
                {
                    if (started && !choosingBreed && xrTab == 5) { xrTab = 0; BuildHeadsetPage(); }
                    BringHeadsetMenu();
                }
                else if (view != null) view.Place(HeadsetRig.HeadCamera.transform.position);
                IsTextEntryFocused = false;
            }
            HeadsetRig.LocomotionSuspended = !started || pause || choosingBreed || world.IsElevatorMoving;
            string hint = string.IsNullOrEmpty(PhysicalPropHint) ? world.InteractionHint : PhysicalPropHint;
            if (active && string.IsNullOrEmpty(hint) && started && StoryChoiceLabels?.Count > 0)
                hint = "麦粒在等你的回应  ·  X 打开手账";
            bool showHint = active && started && !pause && !choosingBreed &&
                !HeadsetCanvas.gameObject.activeSelf && !string.IsNullOrEmpty(hint);
            xrProximityCanvas.gameObject.SetActive(showHint);
            if (showHint)
            {
                Transform head = HeadsetRig.HeadCamera.transform;
                Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
                xrProximityCanvas.transform.SetPositionAndRotation(head.position + forward * 1.3f - Vector3.up * .38f,
                    Quaternion.LookRotation(forward, Vector3.up));
                xrProximityHint.text = hint.Replace("E  ", "右握把  ");
            }
            if (!active) return;
            if (Time.unscaledTime >= xrRefresh)
            {
                xrRefresh = Time.unscaledTime + .2f;
                RefreshHeadsetText();
            }
            CheckHeadsetPetting();
        }
        private void RefreshHeadsetText()
        {
            xrTitle.text = !started ? "今天接麦粒回家" : xrTab == 6 ? "动作手账" : "麦粒的手账";
            xrSummary.text = DogLifeProgress.GetLocationName(life.CurrentLocation) + "    " + life.Level + " 级    " + life.Bones + " 枚小骨头";
            xrVitals.text = "饱腹 " + Mathf.RoundToInt(life.Satiety) + "    饮水 " + Mathf.RoundToInt(life.Hydration) +
                "    精力 " + Mathf.RoundToInt(life.Energy) + "    心情 " + Mathf.RoundToInt(life.Mood) +
                "    清洁 " + Mathf.RoundToInt(life.Cleanliness);
            bool keepStoryResponse = Time.unscaledTime < storyResponseUntil;
            string response = !keepStoryResponse && !string.IsNullOrEmpty(notice) && Time.unscaledTime < noticeUntil ? notice : reply;
            string purpose = showDeveloperPurpose && started ? "麦粒正在" + DeveloperDogAction : "";
            string hint = string.IsNullOrEmpty(world.InteractionHint) ? "" :
                world.InteractionHint.Replace("E  ", "右握把  ");
            xrReply.text = !string.IsNullOrEmpty(hint) ? hint + "\n" + response :
                !string.IsNullOrEmpty(purpose) ? purpose + "\n" + response : response;
            xrVoice.text = VoiceListening ? "正在听，松开 A 发送。音量 " + Mathf.RoundToInt(speech.InputLevel*100) + "%"
                : VoiceFinalizing ? speech.Status : (string.IsNullOrEmpty(VoiceTranscript) ? speech.Status : "你说  " + VoiceTranscript);
            xrTalkLabel.text = VoiceListening ? "结束录音 · A" : VoiceFinalizing ? "正在识别" : "按住 A 说话";
            xrTalkButton.interactable = VoiceListening || VoiceAvailable;
            xrCancelButton.interactable = VoiceListening || VoiceFinalizing;
            xrInputTrack.enabled = xrInputFill.enabled = VoiceListening;
            xrInputFill.rectTransform.sizeDelta = new Vector2(Mathf.Clamp01(speech.InputLevel * 4f) * 748, 4);
            if (xrTab == 0 && xrStoryHeading != null) RefreshHeadsetStory();
            foreach (var update in xrBindings) update();
        }

        private void BuildHeadsetPage()
        {
            xrBindings.Clear();
            foreach (Transform child in xrPage) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            if (!started && xrTab != 4) xrTab = 5;
            UpdateHeadsetTabs();
            if (xrTab == 0)
            {
                BuildHeadsetStoryPage();
            }
            else if (xrTab == 1)
            {
                var care = new[] {DogCareAction.Feed,DogCareAction.Water,DogCareAction.Walk,DogCareAction.Clean,DogCareAction.Play,DogCareAction.Rest};
                for(int i=0;i<care.Length;i++) { var a=care[i]; PageButton(DogLifeProgress.GetActionName(a),i,()=>StartCare(a),()=>Ready && !VoiceListening && !VoiceFinalizing); }
                XrText(xrPage, life.WellbeingNote, new Vector2(0,251), new Vector2(748,52), 25);
                XrText(xrPage, "看向食水碗，按右握把也能添上。靠近它，轻轻移动手柄就能抚摸。",
                    new Vector2(0,7), new Vector2(748,68), 24);
            }
            else if (xrTab == 2)
            {
                string[] commands={"坐下","趴下","抬右爪","转圈","拜一拜","把玩具拿来"};
                for(int i=0;i<commands.Length;i++) {string command=commands[i]; PageButton(command,i,()=>Submit(command),()=>Ready);}
                XrText(xrPage, "等它做完，再表扬。也可以按住 A 说出你自己的口令。",
                    new Vector2(0,251), new Vector2(748,49), 25);
                StoryQuickButton("真棒 · 奖励", 0, () => ApplyFeedback(true), () => Ready && (CanRewardTrick || agent.CanReward));
            }
            else if (xrTab == 3)
            {
                foreach(DogLocation loc in Enum.GetValues(typeof(DogLocation)))
                {
                    DogLocation at=loc;
                    string name = DogLifeProgress.GetLocationName(at);
                    if (!life.IsUnlocked(at)) name += "\n" + life.GetRequiredLevel(at) + " 级  ·  " + life.GetUnlockCost(at) + " 骨头";
                    PageButton(name,(int)loc,()=>{if(life.IsUnlocked(at)) Visit(at); else Buy(at); BuildHeadsetPage();},()=>Ready && !VoiceListening && !VoiceFinalizing);
                }
                XrText(xrPage, "挑一个你们今天想去的地方。", new Vector2(0,251), new Vector2(748,45), 25);
            }
            else if (xrTab == 4)
            {
                PageButton("切换麦克风",0,()=>{speech.CycleMicrophone(); BuildHeadsetPage();},()=>!VoiceListening&&!VoiceFinalizing);
                PageButton("重新连接 VR",1,()=>HeadsetRig.RetryXR());
                PageButton(useModel?"Agent 模型：开":"Agent 模型：关",2,()=>{useModel=!useModel; BuildHeadsetPage();});
                PageButton(dog.ShowKeypoints?"隐藏骨架":"显示骨架",3,()=>{dog.ShowKeypoints=!dog.ShowKeypoints; BuildHeadsetPage();});
                PageButton(showDeveloperPurpose ? "隐藏当前动作" : "显示当前动作",4,()=>
                {showDeveloperPurpose=!showDeveloperPurpose;BuildHeadsetPage();});
                PageButton("奖励提示音"+(quiet?"：关":"：开"),5,()=>{ToggleRewardAudio();BuildHeadsetPage();});
                XrText(xrPage, speech.SelectedMicrophoneLabel+"\n"+speech.ServiceStatus,new Vector2(0,0),new Vector2(748,82),21);
            }
            else if (xrTab == 6)
            {
                BuildHeadsetActionPage();
            }
            else
            {
                xrBreed = Mathf.Clamp(xrBreed,0,DogBreed.Options.Length-1);
                var breed=DogBreed.Options[xrBreed];
                XrImage(xrPage, "犬种卡", new Vector2(0,190), new Vector2(748,117), BookNote);
                XrText(xrPage, breed.DisplayName, new Vector2(18,243), new Vector2(700,47), 34).fontStyle = FontStyle.Bold;
                XrText(xrPage, (xrBreed + 1) + " / " + DogBreed.Options.Length, new Vector2(18,198), new Vector2(700,36), 24);
                PageButton("上一只",3,()=>{xrBreed=(xrBreed+DogBreed.Options.Length-1)%DogBreed.Options.Length; BuildHeadsetPage();});
                PageButton("下一只",4,()=>{xrBreed=(xrBreed+1)%DogBreed.Options.Length; BuildHeadsetPage();});
                PageButton(started?"确认更换":"开始陪伴",5,()=>{if(ConfirmBreed(breed)){choosingBreed=false; BeginPlay(); xrTab=0; BuildHeadsetPage();} else Notify(breedError);},()=>!started||Ready);
                if (!started) XrText(xrPage, "扳机选手账，X 随时收起。左摇杆走动，右摇杆转身。",
                    new Vector2(0,8), new Vector2(748,67), 24);
            }
        }

        private void BuildHeadsetStoryPage()
        {
            XrImage(xrPage, "当前章节纸卡", new Vector2(0,151), new Vector2(748,157), BookNote);
            xrStoryHeading = XrText(xrPage, "", new Vector2(19,249), new Vector2(710,48), 34);
            xrStoryHeading.fontStyle = FontStyle.Bold;
            xrStoryHeading.alignment = TextAnchor.MiddleLeft;
            xrStoryPrompt = XrText(xrPage, "", new Vector2(19,160), new Vector2(710,84), 26);
            xrStoryPrompt.alignment = TextAnchor.MiddleLeft;
            xrStoryPrompt.resizeTextForBestFit = true;
            xrStoryPrompt.resizeTextMinSize = 22;
            xrStoryPrompt.resizeTextMaxSize = 26;
            xrStoryProgress = XrText(xrPage, "", new Vector2(5,74), new Vector2(738,68), 23);
            xrStoryProgress.color = Umber;
            xrStoryProgress.alignment = TextAnchor.MiddleLeft;
            xrStoryProgress.resizeTextForBestFit = true;
            xrStoryProgress.resizeTextMinSize = 20;
            xrStoryProgress.resizeTextMaxSize = 23;
            for (int i = 0; i < xrStoryOptions.Length; i++)
            {
                int option = i;
                var button = XrButton(xrPage, "", new Vector2(i * 252,73), new Vector2(244,69),
                    () => ChooseHeadsetStoryOption(option));
                StyleHeadsetPrimary(button);
                xrStoryOptions[i] = button;
                var label = button.GetComponentInChildren<Text>();
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 21;
                label.resizeTextMaxSize = 25;
                xrStoryOptionLabels[i] = label;
            }
            StoryQuickButton("叫它过来", 0, () => Submit("过来"), () => Ready);
            StoryQuickButton("真棒 · 奖励", 1, () => ApplyFeedback(true), () => Ready && (CanRewardTrick || agent.CanReward));
            StoryQuickButton(pause ? "继续陪伴" : "暂停", 2, () =>
            {
                pause = !pause;
                Time.timeScale = pause ? 0 : 1;
                if (pause) life.Save();
                BuildHeadsetPage();
            });
            RefreshHeadsetStory();
        }

        private void RefreshHeadsetStory()
        {
            var choices = StoryChoiceLabels;
            bool choosing = choices != null && choices.Count > 0;
            xrStoryHeading.text = StoryTitle;
            xrStoryPrompt.text = StoryPrompt;
            xrStoryProgress.gameObject.SetActive(!choosing);
            if (!choosing) xrStoryProgress.text = StoryProgress;
            for (int i = 0; i < xrStoryOptions.Length; i++)
            {
                var button = xrStoryOptions[i];
                bool visible = choosing && i < choices.Count && !string.IsNullOrWhiteSpace(choices[i]);
                button.gameObject.SetActive(visible);
                if (visible)
                {
                    button.gameObject.name = choices[i];
                    xrStoryOptionLabels[i].text = choices[i];
                    button.interactable = Ready && !VoiceListening && !VoiceFinalizing;
                }
            }
        }

        private void ChooseHeadsetStoryOption(int option)
        {
            var choices = StoryChoiceLabels;
            if (!Ready || choices == null || option >= choices.Count ||
                xrStoryHeading.text != StoryTitle || xrStoryOptionLabels[option].text != choices[option]) return;
            ChooseStoryOption(option);
            RefreshHeadsetStory();
        }

        private void StoryQuickButton(string text, int slot, Action action, Func<bool> enabled = null)
        {
            var button = XrButton(xrPage, text, new Vector2(slot * 252, 5), new Vector2(244, 56), action);
            if (enabled == null) return;
            Action update = () => button.interactable = enabled();
            xrBindings.Add(update);
            update();
        }

        private void StyleHeadsetPrimary(Button button)
        {
            var colors = button.colors;
            colors.normalColor = BookCover;
            colors.highlightedColor = new Color32(47, 89, 75, 255);
            colors.pressedColor = Dark;
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color32(88, 99, 78, 255);
            button.colors = colors;
            button.GetComponentInChildren<Text>().color = LightInk;
        }

        private Sprite CreateHeadsetPawStamp()
        {
            const int size = 48;
            xrPawTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            xrPawTexture.name = "Dog1 paw paper stamp";
            xrPawTexture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float pad = PawEllipse(x, y, 24, 16, 12, 9);
                float edges = Mathf.Max(PawEllipse(x, y, 9, 26, 5, 6), PawEllipse(x, y, 39, 26, 5, 6));
                float middle = Mathf.Max(PawEllipse(x, y, 19, 35, 5, 7), PawEllipse(x, y, 30, 35, 5, 7));
                float toes = Mathf.Max(edges, middle);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Max(pad, toes)));
            }
            xrPawTexture.SetPixels32(pixels);
            xrPawTexture.Apply(false, true);
            return Sprite.Create(xrPawTexture, new Rect(0, 0, size, size), Vector2.one * .5f, 100);
        }

        private static float PawEllipse(float x, float y, float cx, float cy, float rx, float ry)
        {
            var offset = new Vector2((x - cx) / rx, (y - cy) / ry);
            return Mathf.Clamp01((1f - offset.sqrMagnitude) * 5f);
        }

        private void UpdateHeadsetTabs()
        {
            for (int i = 0; i < xrTabs.Count; i++)
            {
                bool active = i == xrTab;
                var colors = xrTabs[i].colors;
                colors.normalColor = active ? BookCover : BookTab;
                colors.highlightedColor = active ? new Color32(67,94,65,255) : new Color32(239,216,183,255);
                colors.pressedColor = active ? Dark : RuleInk;
                colors.selectedColor = colors.normalColor;
                xrTabs[i].colors = colors;
                xrTabs[i].GetComponentInChildren<Text>().color = active ? LightInk : Ink;
            }
        }

        private void BuildHeadsetActionPage()
        {
            const int pageSize = 6;
            var visible = new List<int>();
            for (int i = 0; i < DeveloperActions.Count; i++)
                if (xrActionFilter == 0 || DeveloperActions[i].IsMicro == (xrActionFilter == 2))
                    visible.Add(i);
            int pages = Mathf.Max(1, Mathf.CeilToInt(visible.Count / (float)pageSize));
            xrActionPage = Mathf.Clamp(xrActionPage, 0, pages - 1);
            string filter = xrActionFilter == 0 ? "全部动作" : xrActionFilter == 1 ? "全身与生活" : "头颈与耳朵";
            XrButton(xrPage, filter, new Vector2(0,220), new Vector2(238,44), () =>
            {
                xrActionFilter = (xrActionFilter + 1) % 3;
                xrActionPage = 0;
                BuildHeadsetPage();
            });
            var countLabel = XrText(xrPage, (xrActionPage + 1) + " / " + pages + " 页，共 " + visible.Count + " 项",
                new Vector2(254,228), new Vector2(242,28), 23);
            countLabel.color = Umber;
            XrButton(xrPage, showDeveloperPurpose ? "隐藏当前动作" : "显示当前动作",
                new Vector2(506,220), new Vector2(242,44), () =>
                {showDeveloperPurpose=!showDeveloperPurpose;BuildHeadsetPage();});

            int first = xrActionPage * pageSize;
            for (int slot = 0; slot < pageSize && first + slot < visible.Count; slot++)
            {
                int actionIndex = visible[first + slot];
                var descriptor = DeveloperActions[actionIndex];
                var control = XrButton(xrPage, descriptor.Label,
                    new Vector2(slot % 3 * 252, 145 - slot / 3 * 72), new Vector2(244,64),
                    () => PreviewDeveloperAction(actionIndex));
                Action update = () => control.interactable = CanPreviewDeveloperAction;
                xrBindings.Add(update);
                update();
            }
            var previous = XrButton(xrPage, "上一页", new Vector2(0,0), new Vector2(154,56),
                () => {xrActionPage--;BuildHeadsetPage();});
            previous.interactable = xrActionPage > 0;
            var next = XrButton(xrPage, "下一页", new Vector2(166,0), new Vector2(154,56),
                () => {xrActionPage++;BuildHeadsetPage();});
            next.interactable = xrActionPage < pages - 1;
            var sequence = XrButton(xrPage, "连播全部动作", new Vector2(336,0), new Vector2(412,56),
                PreviewDeveloperSequence);
            var sequenceLabel = sequence.GetComponentInChildren<Text>();
            Action updateSequence = () =>
            {
                sequence.interactable = DeveloperSequencePlaying || CanPreviewDeveloperAction;
                sequenceLabel.text = DeveloperSequencePlaying ? "停止连播" : "连播全部动作";
            };
            xrBindings.Add(updateSequence);
            updateSequence();
        }

        private void CheckHeadsetPetting()
        {
            bool left=PetHand(false,ref leftPetAt,ref leftPetTracked);
            bool right=PetHand(true,ref rightPetAt,ref rightPetTracked);
            if (!Ready || VoiceListening || VoiceFinalizing || (!left && !right)) {petDuration=0;return;}
            petDuration+=Time.unscaledDeltaTime;
            if(petDuration<.35f||Time.unscaledTime-lastPet<3f)return;
            petDuration=0; lastPet=Time.unscaledTime;
            StartCoroutine(HeadsetPet());
        }
        private bool PetHand(bool right,ref Vector3 previous,ref bool wasTracked)
        {
            if (ToyBlocksPetting(right)) { wasTracked = false; return false; }
            bool tracked=HeadsetRig.TryGetHandPosition(right,out Vector3 position);
            float speed=tracked&&wasTracked?Vector3.Distance(position,previous)/Mathf.Max(Time.unscaledDeltaTime,.001f):0;
            previous=position;wasTracked=tracked;
            if(!tracked||dog==null||speed<=.035f||speed>=2.4f)return false;
            Vector3 center=dog.PerceptionHead!=null ? dog.PerceptionHead.position :
                dog.transform.position+dog.transform.forward*.26f+Vector3.up*.56f;
            return Vector3.Distance(position,center) < Mathf.Clamp(dog.Breed.Height * .41f, .23f, .35f);
        }
        private IEnumerator HeadsetPet()
        {
            yield return ReactToPet();
        }
        private DogCareAction? UpdateHeadsetApartment()
        {
            var head=HeadsetRig.HeadCamera.transform;
            apartmentHead.SetPositionAndRotation(head.position,head.rotation);
            bool pressed=xrInteract;xrInteract=false;
            var result=world.UpdateApartmentInteractions(apartmentHead,!IsTextEntryFocused,pressed,Time.deltaTime,true);
            // The lift moves the rig origin, never the tracked eye transform.
            HeadsetRig.Origin.position+=apartmentHead.position-head.position;
            return result;
        }
        private void PageButton(string text,int slot,Action action,Func<bool> enabled=null)
        {
            var b=XrButton(xrPage,text,new Vector2((slot%3)*252,168-(slot/3)*80),new Vector2(244,72),action);
            if(enabled!=null){Action update=()=>b.interactable=enabled();xrBindings.Add(update);update();}
        }
        private static GameObject XrObject(Transform parent,string name,Vector2 at,Vector2 size,Type type)
        {
            var go=new GameObject(name,typeof(RectTransform),type);go.transform.SetParent(parent,false);
            var r=go.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=r.pivot=Vector2.zero;r.anchoredPosition=at;r.sizeDelta=size;return go;
        }
        private Image XrImage(Transform parent,string name,Vector2 at,Vector2 size,Color color)
        {
            var image=XrObject(parent,name,at,size,typeof(Image)).GetComponent<Image>();
            image.color=color;
            image.raycastTarget=false;
            if(xrRoundedSprite!=null && size.x>12 && size.y>12)
            {
                image.sprite=xrRoundedSprite;
                image.type=Image.Type.Sliced;
            }
            return image;
        }
        private Text XrText(Transform parent,string text,Vector2 at,Vector2 size,int fontSize)
        {
            var t=XrObject(parent,text,at,size,typeof(Text)).GetComponent<Text>();
            t.font=xrFont;t.fontSize=fontSize;t.color=Ink;t.text=text;t.supportRichText=false;t.raycastTarget=false;
            t.horizontalOverflow=HorizontalWrapMode.Wrap;
            t.verticalOverflow=VerticalWrapMode.Truncate;
            t.lineSpacing=1.1f;
            return t;
        }
        private Button XrButton(Transform parent,string text,Vector2 at,Vector2 size,Action action)
        {
            var i=XrImage(parent,text,at,size,Color.white);i.raycastTarget=true;
            var b=i.gameObject.AddComponent<Button>();b.targetGraphic=i;
            var colors=b.colors;
            colors.normalColor=Linen;
            colors.highlightedColor=new Color32(228,207,166,255);
            colors.pressedColor=new Color32(183,201,157,255);
            colors.selectedColor=Linen;
            colors.disabledColor=new Color32(224,216,198,255);
            colors.fadeDuration=.06f;
            b.colors=colors;
            b.onClick.AddListener(()=>{PlayMenuTap();action();});
            var t=XrText(i.transform,text,new Vector2(8,4),size-new Vector2(16,8),25);
            t.alignment=TextAnchor.MiddleCenter;
            return b;
        }

        private void DisposeHeadsetVisuals()
        {
            if(xrFont!=null)Destroy(xrFont);
            if(xrRoundedSprite!=null)Destroy(xrRoundedSprite);
            if(xrPawStamp!=null)Destroy(xrPawStamp);
            if(xrPawTexture!=null)Destroy(xrPawTexture);
        }
    }
}
