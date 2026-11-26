using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using KeepADog.Demo;
using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class Dog1Training : MonoBehaviour
    {
        public DogCompanion dog;
        [SerializeField] private DogApartmentLayout apartment;
        public string model = "deepseek-flash";
        public string endpoint = "https://api.deepseek.com/chat/completions";
        [SerializeField] private string apiKey = "";

        private DogAgent agent;
        private Dog2SpeechInput speech;
        private bool keyboardVoice;
        private DogLifeProgress life;
        private DogRoutines routines;
        private DogNeighborhood world;
        private DogScenePerception perception;
        private Camera viewCamera;
        private Dog1PlayerView view;
        private Transform waste, walkMarker;
        private Material markerMaterial;
        private string sessionKey;
        private string credentialNotice = "";
        private bool busy, useModel, confirmReset, walking;
        private bool quiet;
        private AudioSource feedbackAudio;
        private AudioClip rewardSound;
        private bool started;
        private bool pause;
        private int tab;
        private string reply = "欢迎回家。麦粒正等你带它熟悉新家。";
        private string caption = "";
        private string message = "";
        private CommandTrace activeCommandTrace;
        private readonly List<CommandTrace> commandTraces = new List<CommandTrace>();
        private string pendingName;
        private string[] pendingRoutine;
        private bool pendingTrick, pendingInvitation;
        private float rewardUntil, nextAutonomy;
        private float nextBurst, sniffAfterPlayUntil;
        private const float BurstCooldown = 105f;
        private string notice = "";
        private float noticeUntil;
        [Serializable] public sealed class Decision { public string action; public string reason; }

        private void Awake()
        {
            string testSaves = Environment.GetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES");
            agent = new DogAgent(SaveOverride(testSaves, "dog-memory.json"));
            routines = new DogRoutines(SaveOverride(testSaves, "dog-routine.json"));
            life = new DogLifeProgress(SaveOverride(testSaves, "dog-life.json"));
            speech = gameObject.AddComponent<Dog2SpeechInput>();
            speech.TranscriptLogDirectory = SaveOverride(testSaves, "VoiceTranscripts") ??
                Path.Combine(Application.persistentDataPath, "VoiceTranscripts");
            speech.TranscriptReady += SubmitVoiceTranscript;
            quiet = PlayerPrefs.GetInt("CityDogQuiet", 0) != 0;
            SetupAudio();
            perception = gameObject.AddComponent<DogScenePerception>();
            sessionKey = Environment.GetEnvironmentVariable("KEEP_A_DOG_API_KEY") ?? "";
            if (string.IsNullOrWhiteSpace(sessionKey) && string.IsNullOrWhiteSpace(testSaves) &&
                DogModelCredentials.TryLoad(endpoint, out string savedKey))
            {
                sessionKey = savedKey;
                credentialNotice = "已加载本机加密保存的 API Key。";
            }
            useModel = !string.IsNullOrWhiteSpace(sessionKey) || !string.IsNullOrWhiteSpace(apiKey);
            Transform oldSphere = transform.Find("Dog Placeholder Sphere");
            if (oldSphere != null) oldSphere.gameObject.SetActive(false);
            if (apartment != null) apartment.gameObject.SetActive(false);
            foreach (Light light in FindObjectsOfType<Light>()) light.enabled = false;
            world = new DogNeighborhood(transform, apartment);
            world.Load((int)life.CurrentLocation);
            viewCamera = Camera.main;
            if (viewCamera != null)
            {
                view = viewCamera.GetComponent<Dog1PlayerView>();
                if (view == null) view = viewCamera.gameObject.AddComponent<Dog1PlayerView>();
                view.Neighborhood = world;
                view.InputSuspended = true;
            }
            if (dog == null)
            {
                DogBreed savedBreed = DogBreed.Find(life.BreedId) ?? DogBreed.Options[0];
                dog = CreateCompanion(savedBreed);
                if (!dog.Ready && !savedBreed.IsOriginal)
                {
                    Destroy(dog.gameObject);
                    dog = CreateCompanion(DogBreed.Shepherd);
                    choosingBreed = true;
                }
            }
            PlaceActors();
            SetupHeadset();
            speech.WarmUp();
            var bonus = life.ClaimDailyBonus();
            if (bonus.Succeeded)
            {
                reply = bonus.Message + " 先看看麦粒今天需要什么。";
                Notify(bonus.Message);
            }
        }

        private static string SaveOverride(string folder, string file) =>
            Application.isEditor && !string.IsNullOrEmpty(folder) ? System.IO.Path.Combine(folder, file) : null;

        private DogCompanion CreateCompanion(DogBreed breed, Transform pose = null)
        {
            var actor = new GameObject("麦粒 · " + breed.DisplayName);
            actor.SetActive(false);
            actor.transform.SetParent(transform);
            if (pose != null) actor.transform.SetPositionAndRotation(pose.position, pose.rotation);
            else actor.transform.position = world.DogStart;
            DogCompanion companion = actor.AddComponent<DogCompanion>();
            companion.SetBreedBeforeActivation(breed);
            actor.SetActive(true);
            return companion;
        }

        private bool ConfirmBreed(DogBreed breed)
        {
            if (dog == null || !dog.Ready || dog.Breed != breed)
            {
                bool replaceInPlace = started && dog != null;
                DogCompanion next = CreateCompanion(breed, replaceInPlace ? dog.transform : null);
                if (!next.Ready)
                {
                    Destroy(next.gameObject);
                    breedError = "这只狗狗暂时无法加载，请选择其他犬种。";
                    return false;
                }
                if (dog != null)
                {
                    next.user = dog.user;
                    next.ShowKeypoints = dog.ShowKeypoints;
                    next.SetWanderingEnabled(dog.WanderingEnabled);
                    dog.gameObject.SetActive(false);
                    Destroy(dog.gameObject);
                }
                dog = next;
                if (replaceInPlace) ConfigureCompanion(keepPose: true);
                else PlaceActors();
            }
            life.SelectBreed(breed);
            return true;
        }

        private void PlaceActors()
        {
            if (view != null)
            {
                world.ConfigureCamera(viewCamera);
                view.Place(world.PlayerStart);
            }
            PlaceHeadset();
            if (HeadsetRig != null) world.ConfigureCamera(HeadsetRig.HeadCamera);
            ConfigureCompanion();
            RefreshWaste();
            nextAutonomy = Time.unscaledTime + 20f;
            nextBurst = Mathf.Max(nextBurst, Time.unscaledTime + 34f);
            sniffAfterPlayUntil = 0f;
            quietStoryUntil = 0f;
            if (pendingInvitation) ClearTrickReward();
        }

        private void ConfigureCompanion(bool keepPose = false)
        {
            dog.PositionLimit = world.ConstrainDogPosition;
            dog.HasPlayClearance = world.HasDogPlayClearance;
            if (keepPose) dog.ConfigurePracticeArea(world.FetchPosition, world.ToyPosition);
            else dog.SetPracticeArea(world.DogStart, world.FetchPosition, world.ToyPosition);
            perception.Dog = dog;
            perception.RefreshTargets();
            ConfigurePhysicalProps();
            var points = new System.Collections.Generic.List<DogCompanion.ExplorationPoint>
            {
                new DogCompanion.ExplorationPoint("水碗", world.WaterPosition),
                new DogCompanion.ExplorationPoint("食盆", world.FoodPosition)
            };
            foreach (var toy in physicalProps)
                if (toy != null) points.Add(new DogCompanion.ExplorationPoint(toy.Label, toy.transform.position, toy.transform));
            Transform cushion = HomeRestSpot(false);
            if (cushion != null) points.Add(new DogCompanion.ExplorationPoint("软垫", cushion.position));
            else points.Add(new DogCompanion.ExplorationPoint("散步路标", world.WalkDestination));
            dog.SetExplorationPoints(points.ToArray());
        }

        private bool Ready => started && !pause && !choosingBreed && !busy && !walking && activeCommandTrace == null &&
            dog != null && dog.Ready && !dog.IsPerforming;

        private void Update()
        {
            ExpirePendingCommands();
            UpdatePhysicalProps();
            UpdateHeadset();
            if (choosingBreed)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) CloseBreedPicker();
                return;
            }
            ProcessVoiceButtons();
            if (started && !pause)
            {
                if (Input.GetKeyDown(KeyCode.V) && !IsTextEntryFocused && VoiceAvailable)
                {
                    BeginVoice();
                    keyboardVoice = VoiceListening;
                }
                if (keyboardVoice && Input.GetKeyUp(KeyCode.V))
                {
                    keyboardVoice = false;
                    EndVoice();
                }
                if (Input.GetKeyDown(KeyCode.Escape) && VoiceInteractionActive)
                {
                    CancelVoice();
                    return;
                }
            }
            if (Input.GetKeyDown(KeyCode.Escape) && started && (!busy || pause))
            {
                pause = !pause;
                Time.timeScale = pause ? 0f : 1f;
                if (pause) life.Save();
            }
            if (view != null) view.InputSuspended = !started || pause;
            if (!started || pause) return;
            TryRunPendingCommand();
            if (viewCamera != null)
            {
                DogCareAction? refill = InHeadset ? UpdateHeadsetApartment() :
                    world.UpdateApartmentInteractions(viewCamera.transform, !IsTextEntryFocused,
                    Input.GetKeyDown(KeyCode.E), Time.deltaTime);
                if (refill.HasValue) world.FillBowl(refill.Value);
            }
            if (view != null && world.IsElevatorMoving) view.InputSuspended = true;
            life.Tick(Time.deltaTime);
            if (Ready && !dog.IsFinishingIdleAction && !VoiceInteractionActive && Time.time >= nextBowlCheck)
            {
                nextBowlCheck = Time.time + 2f;
                if (world.HasServing(DogCareAction.Feed) && string.IsNullOrEmpty(life.GetBlockReason(DogCareAction.Feed)))
                    StartCare(DogCareAction.Feed);
                else if (world.HasServing(DogCareAction.Water) && string.IsNullOrEmpty(life.GetBlockReason(DogCareAction.Water)))
                    StartCare(DogCareAction.Water);
            }
            if (walking && viewCamera != null)
            {
                Vector3 player = viewCamera.transform.position;
                player.y = 0;
                if (Vector3.Distance(player, world.WalkDestination) < 0.85f)
                {
                    walking = false;
                    dog.SetWalkingTogether(false);
                    if (walkMarker != null) Destroy(walkMarker.gameObject);
                    StartCoroutine(FinishWalk());
                }
            }
            if (!Ready || dog.IsFinishingIdleAction || VoiceInteractionActive || CanRewardTrick || HasPlayInvitation) return;
            if (dog.TryConsumeArrivalInvitation() && CanInviteOwner() && life.Energy >= 36f && life.Hydration >= 34f && life.Satiety >= 30f)
            {
                nextAutonomy = Time.unscaledTime + 26f;
                StartCoroutine(OfferPlay());
            }
            else if (!agent.CanGiveFeedback && Time.unscaledTime >= nextAutonomy)
            {
                nextAutonomy = Time.unscaledTime + 26f;
                StartCoroutine(Autonomy());
            }
        }

        private bool nextRestInHouse = true;
        private float nextBowlCheck;
        private Transform HomeRestSpot(bool house) =>
            life.CurrentLocation == DogLocation.Apartment && apartment != null ? apartment.RestSpot(house) : null;

        private IEnumerator RestWithFurniture(bool house) => RestWithFurnitureCore(house, true);

        private IEnumerator RestWithFurnitureCore(bool house, bool playerInitiated)
        {
            if (playerInitiated) ClearTrickReward();
            bool wasBusy = busy;
            busy = true;
            try
            {
                Transform spot = HomeRestSpot(house);
                if (spot == null) yield return dog.PerformAction("rest", .35f);
                else
                {
                    nextRestInHouse = !house;
                    yield return dog.RestAt(spot, house, HomeRestSpot(false));
                }
                RecordRestCompletion(playerInitiated);
            }
            finally { busy = wasBusy; }
        }

        private IEnumerator Autonomy()
        {
            if (life.Satiety < 30 || life.Hydration < 30)
            {
                bool thirsty = life.Hydration < life.Satiety;
                reply = thirsty ? "麦粒在水碗边徘徊。" : "麦粒朝饭碗闻了闻，又回头看你。";
                yield return dog.VisitStation(thirsty ? world.WaterPosition : world.FoodPosition, "检查碗里还有没有", false);
            }
            else if (life.Energy < 30 || life.Mood < 22 || (HomeRestSpot(nextRestInHouse) != null && UnityEngine.Random.value < .25f))
            {
                reply = life.Energy < 30 ? "麦粒自己趴下，想歇一歇。" : "麦粒今天有点没精神，先在你附近安静一会儿。";
                yield return RestWithFurnitureCore(nextRestInHouse, playerInitiated: false);
                quietStoryUntil = Time.unscaledTime + 18f;
            }
            else if (Time.unscaledTime < sniffAfterPlayUntil)
            {
                sniffAfterPlayUntil = 0f;
                yield return dog.PerformAction("sniff", 0.25f);
                if (dog.LastCompletedAction == "sniff" && dog.LastPlaySucceeded) reply = "麦粒玩了一会儿，停下来闻闻地面。";
            }
            else if (life.CurrentLocation == DogLocation.Park && life.Energy >= 72f && life.Mood >= 74f &&
                Time.unscaledTime >= nextBurst && UnityEngine.Random.value < 0.16f && dog.CanPlayBurst())
            {
                nextBurst = Time.unscaledTime + BurstCooldown;
                reply = "麦粒在空地上轻快地跑了个弯。";
                yield return dog.PerformAction("burst", 0.2f);
                if (dog.LastCompletedAction == "burst" && dog.LastPlaySucceeded) sniffAfterPlayUntil = Time.unscaledTime + 42f;
            }
            else if (CanInviteOwner() && (life.Mood < 55f || UnityEngine.Random.value < 0.44f))
            {
                yield return OfferPlay();
            }
            else if (IsFacingNearbyToy() && life.Mood >= 50f && UnityEngine.Random.value < 0.4f)
            {
                yield return dog.PerformAction("pounce", 0.2f);
                if (DogActionSelection.Succeeded(dog, "pounce"))
                {
                    reply = "麦粒看见玩具，抬起前爪朝它轻轻一扑。";
                    sniffAfterPlayUntil = Time.unscaledTime + 42f;
                }
                else
                {
                    yield return dog.PerformAction("sniff", 0.25f);
                    if (dog.LastCompletedAction == "sniff" && dog.LastPlaySucceeded) reply = "麦粒低头闻了闻附近的气味。";
                }
            }
        }

        private IEnumerator OfferPlay()
        {
            yield return dog.PerformAction("invite", .25f);
            if (dog.LastCompletedAction != "invite" || !dog.LastPlaySucceeded) yield break;
            OpenPlayInvitation();
        }

        private bool CanInviteOwner()
        {
            if (viewCamera == null || !world.HasDogPlayClearance(dog.transform.position, 0.48f)) return false;
            Vector3 towardDog = dog.transform.position - viewCamera.transform.position;
            towardDog.y = 0f;
            Vector3 playerForward = viewCamera.transform.forward;
            playerForward.y = 0f;
            return towardDog.sqrMagnitude >= 1.2f * 1.2f && towardDog.sqrMagnitude <= 3.7f * 3.7f &&
                Vector3.Dot(towardDog.normalized, playerForward.normalized) >= 0.47f;
        }

        private bool IsFacingNearbyToy()
        {
            Vector3 towardToy = world.ToyPosition - dog.transform.position;
            towardToy.y = 0f;
            return towardToy.sqrMagnitude >= 0.38f * 0.38f && towardToy.sqrMagnitude <= 1.2f * 1.2f &&
                Vector3.Dot(towardToy.normalized, dog.transform.forward) >= 0.80f;
        }

        internal bool Visit(DogLocation location)
        {
            if (location == life.CurrentLocation) return true;
            if (!life.Visit(location)) return false;
            world.Load((int)location);
            PlaceActors();
            Notify("已抵达" + DogLifeProgress.GetLocationName(location));
            return true;
        }

        internal void Buy(DogLocation location)
        {
            var result = life.Unlock(location);
            Notify(result.Message);
            if (result.Succeeded) Visit(location);
        }

        internal void StartCare(DogCareAction action)
        {
            if (!Ready || VoiceListening || VoiceFinalizing) return;
            string blocked = life.GetBlockReason(action);
            if (!string.IsNullOrEmpty(blocked))
            {
                Notify(blocked);
                return;
            }
            if (action == DogCareAction.Play)
            {
                string playBlocked = CaptureCommandState().BlockReason("fetch_toy");
                if (playBlocked != null) { Notify(playBlocked); return; }
            }
            if (action == DogCareAction.Walk)
            {
                walking = true;
                dog.SetWalkingTogether(true);
                reply = InHeadset ? "和麦粒一起走到发光路标，用左摇杆走动。" : "和麦粒一起走到发光路标。按住右键，用 WASD 往前走。";
                walkMarker = CreateMarker("Walk destination", world.WalkDestination, PrimitiveType.Cylinder,
                    new Color(0.35f, 0.95f, 0.53f), new Vector3(0.55f, 0.055f, 0.55f));
                return;
            }
            if (action == DogCareAction.Play && pendingInvitation) ClearTrickReward();
            StartCoroutine(Care(action));
        }

        private IEnumerator FinishWalk()
        {
            busy = true;
            reply = "麦粒正往你这边来。等它到了，一起看看周围。";
            yield return dog.PerformAction("come", 0.25f);
            FinishCare(DogCareAction.Walk, DogActionSelection.Succeeded(dog, "come"));
        }

        private void CancelWalk()
        {
            walking = false;
            dog.SetWalkingTogether(false);
            if (walkMarker != null) Destroy(walkMarker.gameObject);
            reply = "麦粒停下来闻了闻。想遛弯时，再去路标就好。";
        }

        private IEnumerator Care(DogCareAction action)
        {
            busy = true;
            bool success = true;
            switch (action)
            {
                case DogCareAction.Feed:
                case DogCareAction.Water:
                    bool food = action == DogCareAction.Feed;
                    world.FillBowl(action);
                    reply = food ? "你添了一份狗粮，麦粒小跑去饭碗。" : "你换了一碗干净的水，麦粒闻声跑来。";
                    yield return dog.VisitStation(food ? world.FoodPosition : world.WaterPosition, food ? "去吃饭" : "去喝水", true);
                    success = dog.LastActivitySucceeded;
                    if (success) world.EmptyBowl(action);
                    break;
                case DogCareAction.Clean:
                    reply = "用拾便袋清理地面，再把袋子放进垃圾桶。";
                    yield return new WaitForSeconds(0.7f);
                    break;
                case DogCareAction.Play:
                    reply = "你指了指玩具球，麦粒跑过去叼球。";
                    yield return dog.PerformAction("fetch_toy", 0.25f);
                    success = DogActionSelection.Succeeded(dog, "fetch_toy");
                    break;
                case DogCareAction.Rest:
                    reply = "麦粒找了个舒服的姿势，趴下来歇气。";
                    bool house = nextRestInHouse;
                    yield return RestWithFurniture(house);
                    busy = false;
                    nextAutonomy = Time.unscaledTime + 18f;
                    yield break;
            }
            FinishCare(action, success);
        }

        private void FinishCare(DogCareAction action, bool success)
        {
            if (success)
            {
                var outcome = life.RecordCompletedCare(action);
                if (outcome.Succeeded)
                {
                    if (action == DogCareAction.Play) sniffAfterPlayUntil = Time.unscaledTime + 42f;
                    reply = outcome.Message;
                    if (outcome.BonesEarned > 0)
                    {
                        Notify("获得 " + outcome.BonesEarned + " 小骨头");
                        PlayReward();
                    }
                    RefreshWaste();
                }
                else Notify(outcome.Message);
            }
            else Notify("麦粒这次没做到。可以先呼唤它到身边，再试一次。");
            busy = false;
            nextAutonomy = Time.unscaledTime + 18f;
        }

        private void RecordRestCompletion(bool playerInitiated = true)
        {
            if (!dog.LastRestSucceeded)
            {
                if (playerInitiated) Notify("麦粒没能到达休息位置，请再试一次。");
                return;
            }
            DogCareResult outcome = life.CompleteRest(playerInitiated);
            if (playerInitiated)
            {
                ClearTrickReward();
                reply = outcome.Message;
                activeCommandTrace?.Actions.Add("休息 · 已恢复体力并记录陪伴");
            }
            if (outcome.BonesEarned > 0)
            {
                Notify("获得 " + outcome.BonesEarned + " 小骨头");
                PlayReward();
            }
        }

        private Transform CreateMarker(string label, Vector3 location, PrimitiveType shape, Color color, Vector3 scale)
        {
            var item = GameObject.CreatePrimitive(shape);
            item.name = label;
            item.transform.SetParent(transform, false);
            item.transform.position = location + Vector3.up * 0.065f;
            item.transform.localScale = scale;
            item.GetComponent<Collider>().enabled = false;
            if (markerMaterial == null) markerMaterial = new Material(Shader.Find("Standard"));
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_Color", color);
            Renderer renderer = item.GetComponent<Renderer>();
            renderer.sharedMaterial = markerMaterial;
            renderer.SetPropertyBlock(properties);
            return item.transform;
        }

        private void RefreshWaste()
        {
            if (waste != null) Destroy(waste.gameObject);
            waste = null;
            if (life.WasteCount == 0) return;
            waste = CreateMarker("Waste to collect", world.WastePosition, PrimitiveType.Sphere,
                new Color(0.22f, 0.10f, 0.055f), new Vector3(0.26f, 0.14f, 0.24f));
            for (int i = 1; i < life.WasteCount; i++)
            {
                Transform extra = CreateMarker("Waste to collect", world.WastePosition + Vector3.right * (0.22f * i),
                    PrimitiveType.Sphere, new Color(0.22f, 0.10f, 0.055f), new Vector3(0.18f, 0.10f, 0.18f));
                extra.SetParent(waste, true);
            }
        }

        internal bool Submit(string input)
        {
            if (!CanSubmitInput || string.IsNullOrWhiteSpace(input)) return false;
            var trace = AddCommandTrace(input.Trim(), null);
            if (HasPendingCommands || !SubmitCommand(trace.Input, trace)) EnqueueCommand(trace);
            return true;
        }

        private bool SubmitPracticeCue(string input)
        {
            if (!CanSubmitInput || string.IsNullOrWhiteSpace(input)) return false;
            var trace = AddCommandTrace(input.Trim(), null, localControls: true);
            if (HasPendingCommands || !SubmitCommand(trace.Input, trace)) EnqueueCommand(trace);
            return true;
        }

        private bool CanSubmitInput => started && !pause && !choosingBreed && dog != null &&
            dog.Ready && !VoiceListening && !VoiceFinalizing;

        private bool SubmitCommand(string input, CommandTrace trace = null)
        {
            if (!Ready || VoiceListening || VoiceFinalizing || string.IsNullOrWhiteSpace(input)) return false;
            dog.AttendToUser();
            var command = trace ?? AddCommandTrace(input.Trim(), null);
            caption = "你 · " + command.Input;
            busy = true;
            StartCoroutine(TrainWithTrace(command));
            return true;
        }

        private bool VoiceAvailable => started && !pause && !choosingBreed && speech != null &&
            dog != null && dog.Ready && !VoiceListening && !VoiceFinalizing;
        private bool VoiceListening => speech != null && speech.IsListening;
        private bool VoiceFinalizing => speech != null && speech.IsFinalizing;
        private bool VoicePending => HasPendingCommands;
        private bool VoiceInteractionActive => VoiceListening || VoiceFinalizing || VoicePending;
        private string VoiceStatus => VoicePending && !VoiceListening && !VoiceFinalizing
            ? "文字已识别，等麦粒做完当前动作后发送。" :
            speech != null ? speech.Status : "语音尚未就绪";
        private string VoiceTranscript => speech != null ? speech.LiveText : "";
        private void BeginVoice()
        {
            if (!VoiceAvailable) return;
            speech.StartListening();
            if (speech.IsListening && Ready) dog.AttendToUser(4f);
        }
        private void EndVoice() { if (VoiceListening) speech.StopAndSend(); }
        private void CancelVoice()
        {
            keyboardVoice = false;
            if (VoicePending)
            {
                ClearPendingCommands();
                Notify("已取消待处理口令。");
            }
            if (speech != null) speech.Cancel();
        }
        private void ToggleVoice()
        {
            if (VoiceListening) EndVoice();
            else BeginVoice();
        }
        private void SubmitVoiceTranscript(string transcript)
        {
            string input = DogSpeechText.Normalize(transcript);
            var trace = AddCommandTrace(input, transcript);
            if (string.IsNullOrWhiteSpace(input))
            {
                trace.Result = "识别到语音，但没有可以发送的文字。";
                return;
            }
            if (!HasPendingCommands && SubmitCommand(input, trace)) return;
            EnqueueCommand(trace);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && !InHeadset && VoiceListening) CancelVoice();
        }

        private sealed class CommandTrace
        {
            public string Input, Transcript, State, Json, ModelError;
            public string Source = "本地规则";
            public string Result = "处理中……";
            public bool ModelRequested;
            public bool LocalControls;
            public float ExpiresAt = float.PositiveInfinity;
            public readonly List<string> Actions = new List<string>();
        }

        private CommandTrace AddCommandTrace(string input, string transcript, bool localControls = false)
        {
            var trace = new CommandTrace { Input = input, Transcript = transcript, LocalControls = localControls };
            commandTraces.Insert(0, trace);
            if (commandTraces.Count > 6) commandTraces.RemoveAt(commandTraces.Count - 1);
            return trace;
        }

        private string DeepSeekTraceText
        {
            get
            {
                if (commandTraces.Count == 0) return "说一句话，或用上面的输入框发送口令。执行链会显示在这里。";
                var display = new StringBuilder();
                foreach (var trace in commandTraces)
                {
                    if (display.Length > 0) display.AppendLine().AppendLine();
                    if (trace.Transcript != null) display.Append("语音原文 · ").AppendLine(trace.Transcript);
                    display.Append("输入 · ").AppendLine(trace.Input)
                        .Append("决策来源 · ").AppendLine(trace.Source);
                    if (!string.IsNullOrEmpty(trace.ModelError)) display.Append("模型错误 · ").AppendLine(trace.ModelError);
                    if (trace.ModelRequested)
                    {
                        if (!string.IsNullOrEmpty(trace.State)) display.Append("请求状态 · ").AppendLine(trace.State);
                        display.Append("模型返回 · ").AppendLine(trace.Json ?? "没有收到可解析的响应");
                    }
                    foreach (string action in trace.Actions) display.Append("执行 · ").AppendLine(action);
                    display.Append("回应 · ").Append(trace.Result);
                }
                return display.ToString();
            }
        }

        private void TraceModelResponse(CommandTrace trace, string rawJson)
        {
            if (trace == null) return;
            trace.ModelRequested = true;
            if (string.IsNullOrEmpty(rawJson)) return;
            if (!string.IsNullOrWhiteSpace(sessionKey)) rawJson = rawJson.Replace(sessionKey.Trim(), "[密钥已隐藏]");
            if (!string.IsNullOrWhiteSpace(apiKey)) rawJson = rawJson.Replace(apiKey.Trim(), "[密钥已隐藏]");
            trace.Json = rawJson.Length > 1200 ? rawJson.Substring(0, 1200) + "…" : rawJson;
        }

        private void TraceModelError(CommandTrace trace, string error)
        {
            if (trace == null) return;
            trace.ModelError = error;
            trace.Source = "模型请求失败";
        }

        private void SaveModelCredential()
        {
            string key = string.IsNullOrWhiteSpace(sessionKey) ? apiKey : sessionKey;
            credentialNotice = DogModelCredentials.TrySave(endpoint, key, out string error)
                ? "API Key 已加密保存到本机，重开 Unity 会自动恢复。"
                : error;
        }

        private void TraceModelChoice(CommandTrace trace, bool usedModel)
        {
            if (trace == null) return;
            if (!usedModel)
            {
                trace.Source = trace.ModelRequested ? "本地回退" : "本地规则";
                return;
            }
            bool deepSeek = Uri.TryCreate(endpoint, UriKind.Absolute, out var address) &&
                address.Host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase);
            trace.Source = (deepSeek ? "DeepSeek" : "模型") + " · " + model;
        }

        private IEnumerator TrainWithTrace(CommandTrace trace)
        {
            activeCommandTrace = trace;
            trace.Result = "处理中……";
            bool wander = dog.WanderingEnabled;
            bool heldIdle = dog.HoldIdleGestures;
            dog.HoldIdleGestures = true;
            dog.SetWanderingEnabled(false);
            try { yield return Train(trace.Input); }
            finally
            {
                trace.Result = reply;
                activeCommandTrace = null;
                busy = false;
                if (dog != null)
                {
                    dog.HoldIdleGestures = heldIdle;
                    dog.SetWanderingEnabled(wander);
                    if (HasPlayInvitation) dog.HoldForInvitation();
                }
            }
        }

        private IEnumerator Train(string input)
        {
            busy = true;
            caption = "你 · " + input;
            if (CancelExpiredCommand()) yield break;
            if (input == "真棒" || input == "好狗狗" || input == "奖励" || input == "做得好")
            {
                ApplyFeedback(true);
                busy = false;
                yield break;
            }
            bool explicitControls = activeCommandTrace?.LocalControls == true;
            bool useCommandModel = useModel && !string.IsNullOrWhiteSpace(model);
            if (!useCommandModel && !explicitControls)
            {
                reply = "请先在设置里启用 DeepSeek，再用语音或文字告诉麦粒你想做什么。";
                busy = false;
                yield break;
            }
            if (explicitControls && (input == DogAgent.TeachExample || input == DogAgent.RecallExample || input == "我心情不好"))
            {
                ClearTrickReward();
                yield return ExecuteCommandDecision(agent.Decide(input));
                nextAutonomy = Time.unscaledTime + 18f;
                yield break;
            }
            if (explicitControls && IsInvitationReply(input))
            {
                yield return ResolveInvitationSpeech(input);
                yield break;
            }
            ClearTrickReward();
            if (explicitControls && (input.StartsWith("场景回应", StringComparison.Ordinal) || input.StartsWith("看看我", StringComparison.Ordinal) ||
                input.StartsWith("看着我", StringComparison.Ordinal) || input.StartsWith("你在看什么", StringComparison.Ordinal)))
            {
                yield return RespondToScene(input);
                busy = false;
                nextAutonomy = Time.unscaledTime + 18f;
                yield break;
            }
            string[] recalled = explicitControls ? routines.Recall(input) : null;
            string name = null;
            string[] steps = null;
            if (recalled != null)
            {
                yield return RunRoutine(recalled, null, true);
            }
            else if (explicitControls && routines.TryParse(input, out name, out steps) &&
                (!useCommandModel || !string.IsNullOrEmpty(name)))
            {
                yield return RunRoutine(steps, name, false);
            }
            else if (explicitControls && (input.IndexOf("：", StringComparison.Ordinal) >= 0 || input.IndexOf(":", StringComparison.Ordinal) >= 0))
            {
                reply = "请给动作串起一个名字，再写两到三个口令，例如：打招呼：坐下、抬右爪、伏身邀玩。";
            }
            else
            {
                DogAgentDecision decision;
                if (useCommandModel)
                {
                    reply = "麦粒侧耳听着……";
                    string key = string.IsNullOrWhiteSpace(sessionKey) ? apiKey : sessionKey;
                    yield return WaitForControllerPose(activeCommandTrace?.ExpiresAt ?? float.PositiveInfinity);
                    if (CancelExpiredCommand()) yield break;
                    string stateJson = JsonUtility.ToJson(CaptureCommandState());
                    var trace = activeCommandTrace;
                    if (trace != null)
                    {
                        trace.State = stateJson;
                        trace.Source = "判断中";
                    }
                    Task<DogAgentDecision> request = agent.DecideAsync(input, endpoint, model, key, steps, stateJson,
                        json => TraceModelResponse(trace, json), error => TraceModelError(trace, error));
                    while (!request.IsCompleted)
                    {
                        if (CancelExpiredCommand()) yield break;
                        yield return null;
                    }
                    if (CancelExpiredCommand()) yield break;
                    decision = request.Status == TaskStatus.RanToCompletion ? request.Result : null;
                    if (decision == null)
                        decision = new DogAgentDecision("这次没有收到有效的模型计划，请再说一次。", DogIntent.Wait, "", false)
                        {
                            Actions = Array.Empty<DogAgentAction>(),
                            ControllerDecision = "reply",
                            ValidationError = "模型请求未完成，未执行动作。"
                        };
                }
                else decision = agent.Decide(input);
                TraceModelChoice(activeCommandTrace, decision.UsedModel);
                if (!decision.UsedModel && decision.Actions != null && activeCommandTrace != null)
                    activeCommandTrace.Source = string.IsNullOrEmpty(activeCommandTrace.ModelError)
                        ? "模型不可用或无有效结果" : "模型请求失败";
                yield return ExecuteCommandDecision(decision);
            }
            busy = false;
            nextAutonomy = Time.unscaledTime + 18f;
        }

        private IEnumerator ExecuteCommandDecision(DogAgentDecision decision)
        {
            reply = decision.Reply;
            if (decision.Actions != null && !decision.IsRefusal)
            {
                if (string.IsNullOrWhiteSpace(reply) && !string.IsNullOrEmpty(decision.ValidationError))
                    reply = "麦粒收到了回应，但动作计划没有通过检查。";
                if (!string.IsNullOrEmpty(decision.ValidationError))
                    activeCommandTrace?.Actions.Add("计划校验未通过 · " + decision.ValidationError);
                activeCommandTrace?.Actions.Add("模型决定 · " + decision.ControllerDecision);
                yield return ExecuteControllerPlan(decision);
                yield break;
            }
            if (decision.Steps != null)
            {
                if (!DogRoutines.Valid(decision.Steps))
                {
                    reply = "麦粒没收到完整的动作串。";
                    yield break;
                }
                if (!string.IsNullOrEmpty(decision.ModelReply)) reply = decision.ModelReply;
                if (decision.UsedModel) Debug.Log("[Dog1 Agent] 模型返回动作串：" + string.Join(", ", decision.Steps));
                yield return RunRoutine(decision.Steps, null, false, decision.ModelReply);
                yield break;
            }

            string motion = DogActionSelection.MotionFor(decision);
            if (motion == null) yield break;
            string blocked = CaptureCommandState().BlockReason(motion);
            if (blocked != null)
            {
                activeCommandTrace?.Actions.Add(motion + " · 场景校验阻塞");
                reply = blocked;
                agent.ReportExecution(decision, false);
                yield break;
            }

            if (!string.IsNullOrEmpty(decision.ModelReply)) reply = decision.ModelReply;
            activeCommandTrace?.Actions.Add(motion == "refuse" ? "refuse · 口令已映射，熟练度不足" : motion + " · 开始");
            if (decision.UsedModel && motion != "refuse") Debug.Log("[Dog1 Agent] 模型返回动作：" + motion);
            yield return dog.PerformAction(motion, 0.55f);
            bool succeeded = DogActionSelection.Succeeded(dog, motion);
            if (motion != "refuse") activeCommandTrace?.Actions.Add(motion + (succeeded ? " · 完成" : " · 未完成"));
            agent.ReportExecution(decision, succeeded);
            if (succeeded && motion == "crouch") RecordRestCompletion();
            if (succeeded && motion == "fetch_toy") FinishCare(DogCareAction.Play, true);
            if (succeeded && motion == "bow") OpenPlayInvitation(preserveReply: true);
            if (motion.StartsWith("fetch_", StringComparison.Ordinal))
                reply = succeeded ? (decision.IsRecall ? "麦粒记起约定，把东西叼回来了。" : "麦粒把东西叼回来了。") : "麦粒这次没能把东西叼回来。";
            else if (motion == "pounce" && !succeeded)
                reply = "麦粒停住了。前面要留一点空地，它才会轻扑。";
        }

        [Serializable]
        private sealed class CommandState : DogCommandAvailability
        {
            public string location;
            public string learnedRoutines;
            public DogCommandOption[] actionCatalog;

            public string BlockReason(string motion)
            {
                DogCommandOption registered = DogCommandCatalog.Find(actionCatalog, motion, "none");
                if (registered != null) return registered.available ? null : registered.reason;
                if (!dogReady) return "麦粒现在无法完成这个动作。";
                switch (motion)
                {
                    case "come" when !ownerAvailable: return "麦粒现在找不到你的位置。";
                    case "fetch_toy" when !ballAvailable: return "等玩具球放下并落稳后再让麦粒叼。";
                    case "fetch_lollipop" when !lollipopAvailable: return "等棒棒糖毛绒玩具放下并落稳后再让麦粒叼。";
                    case "pounce" when !pounceClear: return "麦粒前面需要留一点空地，才能轻扑。";
                    default: return null;
                }
            }
        }

        private CommandState CaptureCommandState()
        {
            bool dogReady = dog != null && dog.Ready && !dog.IsPerforming && !pause &&
                !choosingBreed && !world.IsElevatorMoving;
            bool ownerAvailable = dog != null && dog.user != null;
            bool canCarry = ownerAvailable && dog.MuzzleAnchor != null;
            var state = new CommandState
            {
                location = life.CurrentLocation.ToString(),
                learnedRoutines = routines.Summary,
                satiety = life.Satiety,
                energy = life.Energy,
                hydration = life.Hydration,
                mood = life.Mood,
                cleanliness = life.Cleanliness,
                dogReady = dogReady,
                ownerAvailable = ownerAvailable,
                ballAvailable = canCarry && dog.CanUsePhysicalBall,
                lollipopAvailable = canCarry && physicalProps[1] != null && physicalProps[1].CanFetch,
                pounceClear = dogReady && dog.CanPounce(),
                burstClear = dogReady && dog.CanPlayBurst(),
                houseAvailable = HomeRestSpot(true) != null,
                cushionAvailable = HomeRestSpot(false) != null
            };
            state.actionCatalog = DogCommandCatalog.Build(state, CapturePerception());
            return state;
        }

        private DogSceneObservation CapturePerception()
        {
            Vector3? left = null, right = null;
            if (HeadsetRig != null && HeadsetRig.DisplayRunning)
            {
                if (HeadsetRig.TryGetHandPosition(false, out Vector3 leftPosition)) left = leftPosition;
                if (HeadsetRig.TryGetHandPosition(true, out Vector3 rightPosition)) right = rightPosition;
            }
            return perception.Capture(left, right);
        }

        private IEnumerator RespondToScene(string utterance)
        {
            bool wander = dog.WanderingEnabled;
            dog.HoldIdleGestures = true;
            dog.SetWanderingEnabled(false);
            try
            {
                float until = Time.time + 5.1f;
                var observation = CapturePerception();
                while (!DogPerceptionSelection.CanOffer(observation) && Time.time < until)
                {
                    yield return null;
                    observation = CapturePerception();
                }
                DogPerceptionChoice choice;
                var trace = activeCommandTrace;
                if (useModel && DogPerceptionSelection.CanOffer(observation))
                {
                    reply = "麦粒正在看看周围……";
                    string key = string.IsNullOrWhiteSpace(sessionKey) ? apiKey : sessionKey;
                    if (trace != null)
                    {
                        trace.State = observation.Describe();
                        trace.Source = "判断中";
                    }
                    var task = DogPerceptionSelection.SelectAsync(observation, endpoint, model, key, utterance,
                        json => TraceModelResponse(trace, json));
                    while (!task.IsCompleted) yield return null;
                    choice = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                }
                else choice = DogPerceptionSelection.SelectLocal(observation);

                var current = CapturePerception();
                if (!DogPerceptionSelection.CanApply(choice, current))
                    choice = DogPerceptionSelection.SelectLocal(current);
                TraceModelChoice(trace, choice?.UsedModel == true);
                if (choice == null || !dog.TryPlayMicroAction(choice.ActionId))
                {
                    reply = "麦粒还没找到合适的回应，先停一会儿。";
                    yield break;
                }
                reply = (choice.UsedModel ? "场景选择 · " : "麦粒 · ") + choice.Reply;
                trace?.Actions.Add(choice.ActionId + " · 开始");
                float finish = Time.time + 4.2f;
                while (dog.IsPlayingMicroAction && Time.time < finish) yield return null;
                trace?.Actions.Add(choice.ActionId + (dog.IsPlayingMicroAction ? " · 等待结束" : " · 完成"));
            }
            finally
            {
                if (dog != null)
                {
                    dog.HoldIdleGestures = false;
                    dog.SetWanderingEnabled(wander);
                }
            }
        }

        private IEnumerator ReactToPet()
        {
            busy = true;
            dog.AttendToUser(2.6f, allowTilt: false);
            reply = "麦粒把头凑向你的手，尾巴轻轻摆动。";
            try { yield return dog.PerformAction("pet", .22f); }
            finally { busy = false; nextAutonomy = Time.unscaledTime + 8f; }
        }

        private IEnumerator RunRoutine(string[] steps, string name, bool recalled, string modelReply = null)
        {
            reply = (string.IsNullOrEmpty(modelReply) ? recalled ? "麦粒记得这套动作。" : "麦粒准备试试。" : modelReply) +
                " " + DogRoutines.Describe(steps);
            for (int i = 0; i < steps.Length; i++)
            {
                string blocked = CaptureCommandState().BlockReason(steps[i]);
                if (blocked != null)
                {
                    activeCommandTrace?.Actions.Add(steps[i] + " · 场景校验阻塞");
                    reply = "动作串停在「" + DogRoutines.Label(steps[i]) + "」。" + blocked;
                    yield break;
                }
                activeCommandTrace?.Actions.Add(steps[i] + " · 开始");
                Notify("动作 " + (i + 1) + "/" + steps.Length + " · " + DogRoutines.Label(steps[i]));
                yield return dog.PerformAction(steps[i], 0.42f);
                bool succeeded = DogActionSelection.Succeeded(dog, steps[i]);
                activeCommandTrace?.Actions.Add(steps[i] + (succeeded ? " · 完成" : " · 未完成"));
                if (!succeeded)
                {
                    reply = "动作串停在「" + DogRoutines.Label(steps[i]) + "」。等麦粒准备好再试一次。";
                    yield break;
                }
                if (steps[i] == "crouch") RecordRestCompletion();
                if (steps[i] == "fetch_toy") FinishCare(DogCareAction.Play, true);
                if (steps[i] == "bow") OpenPlayInvitation(preserveReply: true);
            }
            OpenTrickReward();
            pendingName = name;
            pendingRoutine = steps;
            reply = string.IsNullOrEmpty(name) ? "动作串完成了。可以现在表扬麦粒。" : "动作串完成了。表扬麦粒，就能记住「" + name + "」。";
        }

        private void OpenTrickReward()
        {
            pendingTrick = true;
            rewardUntil = Time.unscaledTime + 38f;
        }

        private void ClearTrickReward()
        {
            pendingTrick = false;
            pendingInvitation = false;
            invitationResponseUntil = 0f;
            pendingName = null;
            pendingRoutine = null;
        }

        private bool CanRewardTrick => pendingTrick && Time.unscaledTime <= rewardUntil;


        internal void ApplyFeedback(bool positive)
        {
            bool rewarded = positive && (CanRewardTrick || agent.CanReward);
            if (CanRewardTrick)
            {
                if (positive && !string.IsNullOrEmpty(pendingName))
                    reply = routines.Remember(pendingName, pendingRoutine) ? "麦粒记住了「" + pendingName + "」。下次说出名字就能唤起。" : "动作已完成，但保存约定时出了问题。";
                else if (positive && pendingRoutine == null)
                    reply = agent.RewardSpontaneousBow().Reply;
                else reply = positive ? "麦粒听懂了表扬，轻轻摇了摇尾巴。" : "麦粒停下来，等你再示范。";
                ClearTrickReward();
            }
            else reply = agent.Feedback(positive).Reply;
            if (rewarded)
            {
                dog.PlayTailWag();
                var result = life.RecordCompletedCare(DogCareAction.Train);
                if (result.Succeeded && result.BonesEarned > 0)
                {
                    Notify(result.Message);
                    PlayReward();
                }
            }
        }

        private void Notify(string text)
        {
            notice = text;
            noticeUntil = Time.unscaledTime + 8f;
        }

        internal DogLifeProgress Profile => life;
        internal DogNeighborhood Neighborhood => world;
        internal bool WaitingForPlayer => Ready;
        internal void BeginPlay()
        {
            if (string.IsNullOrEmpty(life.BreedId) && dog != null && dog.Ready) life.SelectBreed(dog.Breed);
            started = true;
            pause = false;
            Time.timeScale = 1f;
            nextAutonomy = Time.unscaledTime + 22f;
        }

        private void SetupAudio()
        {
            feedbackAudio = gameObject.AddComponent<AudioSource>();
            feedbackAudio.playOnAwake = false;
            feedbackAudio.spatialBlend = 0f;
            const int sampleRate = 22050;
            float[] samples = new float[(int)(0.46f * sampleRate)];
            float[] notes = { 523.25f, 659.25f, 783.99f };
            for (int i = 0; i < samples.Length; i++)
            {
                float seconds = (float)i / sampleRate;
                for (int n = 0; n < notes.Length; n++)
                {
                    float t = seconds - n * 0.075f;
                    if (t < 0 || t > 0.27f) continue;
                    float attack = Mathf.Min(t * 90f, 1f);
                    float fade = Mathf.Pow(1f - t / 0.27f, 2.2f);
                    samples[i] += Mathf.Sin(2 * Mathf.PI * notes[n] * t) * attack * fade * 0.095f;
                }
            }
            rewardSound = AudioClip.Create("Care reward chime", samples.Length, 1, sampleRate, false);
            rewardSound.SetData(samples, 0);
        }

        private void PlayReward()
        {
            if (!quiet && feedbackAudio != null && rewardSound != null) feedbackAudio.PlayOneShot(rewardSound);
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

        private void OnApplicationPause(bool suspended)
        {
            if (suspended && started) life?.Save();
        }

        private void OnDestroy()
        {
            DisposePhysicalProps();
            if (speech != null) speech.TranscriptReady -= SubmitVoiceTranscript;
            if (choosingBreed && started) Time.timeScale = timeScaleBeforeBreedPicker;
            if (pause) Time.timeScale = 1f;
            if (started) life?.Save();
            world?.Dispose();
            if (markerMaterial != null) Destroy(markerMaterial);
            if (rewardSound != null) Destroy(rewardSound);
            DisposeHud();
        }
    }
}
