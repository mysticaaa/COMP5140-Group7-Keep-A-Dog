using System;
using System.Collections;
using System.Threading.Tasks;
using KeepADog.Demo;
using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class Dog1Training : MonoBehaviour
    {
        public DogCompanion dog;
        public string model = "deepseek-flash";
        public string endpoint = "https://api.deepseek.com/chat/completions";
        [SerializeField] private string apiKey = "";

        private DogAgent agent;
        private Dog2SpeechInput speech;
        private bool keyboardVoice;
        private DogLifeProgress life;
        private DogRoutines routines;
        private DogNeighborhood world;
        private Camera viewCamera;
        private Dog1PlayerView view;
        private Transform waste, walkMarker;
        private Material markerMaterial;
        private string sessionKey;
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
            speech.TranscriptReady += SubmitVoiceTranscript;
            quiet = PlayerPrefs.GetInt("CityDogQuiet", 0) != 0;
            SetupAudio();
            sessionKey = Environment.GetEnvironmentVariable("KEEP_A_DOG_API_KEY") ?? "";
            Transform oldSphere = transform.Find("Dog Placeholder Sphere");
            if (oldSphere != null) oldSphere.gameObject.SetActive(false);
            foreach (Light light in FindObjectsOfType<Light>()) light.enabled = false;
            world = new DogNeighborhood(transform);
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
                GameObject actor = new GameObject("麦粒 · German Shepherd");
                actor.transform.SetParent(transform);
                actor.transform.position = world.DogStart;
                dog = actor.AddComponent<DogCompanion>();
            }
            PlaceActors();
            var bonus = life.ClaimDailyBonus();
            if (bonus.Succeeded)
            {
                reply = bonus.Message + " 先看看麦粒今天需要什么。";
                Notify(bonus.Message);
            }
        }

        private static string SaveOverride(string folder, string file) =>
            Application.isEditor && !string.IsNullOrEmpty(folder) ? System.IO.Path.Combine(folder, file) : null;

        private void PlaceActors()
        {
            if (view != null)
            {
                world.ConfigureCamera(viewCamera);
                view.Place(world.PlayerStart);
            }
            dog.PositionLimit = world.ConstrainDogPosition;
            dog.HasPlayClearance = world.HasDogPlayClearance;
            dog.SetPracticeArea(world.DogStart, world.FetchPosition, world.ToyPosition);
            RefreshWaste();
            nextAutonomy = Time.unscaledTime + 20f;
            nextBurst = Mathf.Max(nextBurst, Time.unscaledTime + 34f);
            sniffAfterPlayUntil = 0f;
            if (pendingInvitation) ClearTrickReward();
        }

        private bool Ready => started && !pause && !busy && !walking && dog != null && dog.Ready && !dog.IsPerforming;

        private void Update()
        {
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
                if (Input.GetKeyDown(KeyCode.Escape) && (VoiceListening || VoiceFinalizing))
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
            life.Tick(Time.deltaTime);
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
            if (Ready && !VoiceListening && !VoiceFinalizing && Time.unscaledTime >= nextAutonomy && !agent.CanGiveFeedback && !CanRewardTrick)
            {
                nextAutonomy = Time.unscaledTime + 26f;
                StartCoroutine(Autonomy());
            }
        }

        private IEnumerator Autonomy()
        {
            if (life.Satiety < 30 || life.Hydration < 30)
            {
                bool thirsty = life.Hydration < life.Satiety;
                reply = thirsty ? "麦粒在水碗边徘徊。" : "麦粒朝饭碗闻了闻，又回头看你。";
                yield return dog.VisitStation(thirsty ? world.WaterPosition : world.FoodPosition, "检查碗里还有没有", false);
            }
            else if (life.Energy < 30 || life.Mood < 22)
            {
                reply = life.Energy < 30 ? "麦粒自己趴下，想歇一歇。" : "麦粒今天有点没精神，先在你附近安静一会儿。";
                yield return dog.PerformAction("rest", 1f);
            }
            else if (Time.unscaledTime < sniffAfterPlayUntil)
            {
                sniffAfterPlayUntil = 0f;
                yield return dog.PerformAction("sniff", 0.25f);
                if (dog.LastPlaySucceeded) reply = "麦粒玩了一会儿，停下来闻闻地面。";
            }
            else if (life.CurrentLocation == DogLocation.Park && life.Energy >= 72f && life.Mood >= 74f &&
                Time.unscaledTime >= nextBurst && UnityEngine.Random.value < 0.16f && dog.CanPlayBurst())
            {
                nextBurst = Time.unscaledTime + BurstCooldown;
                reply = "麦粒在空地上轻快地跑了个弯。";
                yield return dog.PerformAction("burst", 0.2f);
                if (dog.LastPlaySucceeded) sniffAfterPlayUntil = Time.unscaledTime + 42f;
            }
            else if (CanInviteOwner() && (life.Mood < 55f || UnityEngine.Random.value < 0.44f))
            {
                yield return dog.PerformAction("invite", 0.25f);
                if (dog.LastPlaySucceeded)
                {
                    OpenTrickReward();
                    pendingInvitation = true;
                    reply = "麦粒前身伏低，望着你等回应。可以陪它玩，也可以表扬这个动作。";
                }
            }
            else if (IsFacingNearbyToy() && life.Mood >= 50f && UnityEngine.Random.value < 0.4f)
            {
                yield return dog.PerformAction("pounce", 0.2f);
                if (dog.LastPlaySucceeded)
                {
                    reply = "麦粒看见玩具，抬起前爪朝它轻轻一扑。";
                    sniffAfterPlayUntil = Time.unscaledTime + 42f;
                }
                else
                {
                    yield return dog.PerformAction("sniff", 0.25f);
                    if (dog.LastPlaySucceeded) reply = "麦粒低头闻了闻附近的气味。";
                }
            }
            else if (UnityEngine.Random.value < 0.72f)
            {
                yield return dog.PerformAction("sniff", 0.25f);
                if (dog.LastPlaySucceeded) reply = "麦粒低头闻了闻附近的气味。";
            }
            else
            {
                reply = "麦粒自己去翻玩具，看看你会不会陪它。";
                yield return dog.PerformAction("fetch_toy", 0.28f);
                if (dog.LastFetchSucceeded) reply = "麦粒自己叼来球，放在你面前。";
            }
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
            if (action == DogCareAction.Walk)
            {
                walking = true;
                dog.SetWalkingTogether(true);
                reply = "和麦粒一起走到发光路标。按住右键，用 WASD 往前走。";
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
            FinishCare(DogCareAction.Walk, dog.LastComeSucceeded);
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
                    reply = food ? "你添了一份狗粮，麦粒小跑去饭碗。" : "你换了一碗干净的水，麦粒闻声跑来。";
                    yield return dog.VisitStation(food ? world.FoodPosition : world.WaterPosition, food ? "去吃饭" : "去喝水", true);
                    success = dog.LastActivitySucceeded;
                    break;
                case DogCareAction.Clean:
                    reply = "用拾便袋清理地面，再把袋子放进垃圾桶。";
                    yield return new WaitForSeconds(0.7f);
                    break;
                case DogCareAction.Play:
                    reply = "你指了指玩具球，麦粒跑过去叼球。";
                    yield return dog.PerformAction("fetch_toy", 0.25f);
                    success = dog.LastFetchSucceeded;
                    break;
                case DogCareAction.Rest:
                    reply = "麦粒找了个舒服的姿势，趴下来歇气。";
                    yield return dog.PerformAction("rest", 0.35f);
                    break;
            }
            FinishCare(action, success);
        }

        private void FinishCare(DogCareAction action, bool success)
        {
            if (success)
            {
                var outcome = life.Complete(action);
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
            if (!Ready || VoiceListening || VoiceFinalizing || string.IsNullOrWhiteSpace(input)) return false;
            StartCoroutine(Train(input.Trim()));
            return true;
        }

        private bool VoiceAvailable => Ready && speech != null && !speech.IsListening && !speech.IsFinalizing;
        private bool VoiceListening => speech != null && speech.IsListening;
        private bool VoiceFinalizing => speech != null && speech.IsFinalizing;
        private string VoiceStatus => speech != null ? speech.Status : "语音尚未就绪";
        private string VoiceTranscript => speech != null ? speech.LiveText : "";
        private void BeginVoice() { if (VoiceAvailable) speech.StartListening(); }
        private void EndVoice() { if (VoiceListening) speech.StopAndSend(); }
        private void CancelVoice()
        {
            keyboardVoice = false;
            if (speech != null) speech.Cancel();
        }
        private void ToggleVoice()
        {
            if (VoiceListening) EndVoice();
            else BeginVoice();
        }
        private void SubmitVoiceTranscript(string transcript)
        {
            if (!Submit(DogSpeechText.Normalize(transcript))) Notify("已经识别这句话。请等麦粒完成正在做的事，然后再说一次。");
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && VoiceListening) CancelVoice();
        }

        private IEnumerator Train(string input)
        {
            busy = true;
            caption = "你 · " + input;
            if (input == "真棒" || input == "好狗狗" || input == "奖励" || input == "做得好")
            {
                ApplyFeedback(true);
                busy = false;
                yield break;
            }
            ClearTrickReward();
            string[] recalled = routines.Recall(input);
            if (recalled != null)
            {
                yield return RunRoutine(recalled, null, true);
            }
            else if (routines.TryParse(input, out string name, out string[] steps))
            {
                yield return RunRoutine(steps, name, false);
            }
            else if (input.IndexOf("：", StringComparison.Ordinal) >= 0 || input.IndexOf(":", StringComparison.Ordinal) >= 0)
            {
                reply = "请给动作串起一个名字，再写两到三个口令，例如：晚安：坐下、抬右爪、拜一拜。";
            }
            else
            {
                DogAgentDecision decision;
                if (useModel && !string.IsNullOrWhiteSpace(model))
                {
                    reply = "麦粒侧耳听着……";
                    string key = string.IsNullOrWhiteSpace(sessionKey) ? apiKey : sessionKey;
                    Task<DogAgentDecision> request = agent.DecideAsync(input, endpoint, model, key);
                    while (!request.IsCompleted) yield return null;
                    decision = request.Status == TaskStatus.RanToCompletion && request.Result != null ? request.Result : agent.Decide(input);
                }
                else decision = agent.Decide(input);
                reply = decision.Reply;
                if (decision.Steps != null)
                {
                    yield return RunRoutine(decision.Steps, null, false);
                }
                else
                {
                    string motion = DogActionSelection.MotionFor(decision);
                    if (motion != null)
                    {
                        yield return dog.PerformAction(motion, 0.55f);
                        bool succeeded = DogActionSelection.Succeeded(dog, motion);
                        agent.ReportExecution(decision, succeeded);
                        if (motion.StartsWith("fetch_", StringComparison.Ordinal))
                            reply = succeeded ? (decision.IsRecall ? "麦粒记起约定，把东西叼回来了。" : "麦粒把东西叼回来了。") : "麦粒这次没能把东西叼回来。";
                        else if (motion == "pounce" && !succeeded)
                            reply = "麦粒停住了。前面要留一点空地，它才会轻扑。";
                    }
                }
            }
            busy = false;
            nextAutonomy = Time.unscaledTime + 18f;
        }

        private IEnumerator RunRoutine(string[] steps, string name, bool recalled)
        {
            reply = (recalled ? "麦粒记得这套动作。" : "麦粒准备试试。") + " " + DogRoutines.Describe(steps);
            for (int i = 0; i < steps.Length; i++)
            {
                Notify("动作 " + (i + 1) + "/" + steps.Length + " · " + DogRoutines.Label(steps[i]));
                yield return dog.PerformAction(steps[i], 0.42f);
                if (!DogActionSelection.Succeeded(dog, steps[i]))
                {
                    reply = "动作串停在「" + DogRoutines.Label(steps[i]) + "」。等麦粒准备好再试一次。";
                    yield break;
                }
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
                var result = life.Complete(DogCareAction.Train);
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
            if (speech != null) speech.TranscriptReady -= SubmitVoiceTranscript;
            if (pause) Time.timeScale = 1f;
            if (started) life?.Save();
            world?.Dispose();
            if (markerMaterial != null) Destroy(markerMaterial);
            if (rewardSound != null) Destroy(rewardSound);
            DisposeHud();
        }
    }
}
