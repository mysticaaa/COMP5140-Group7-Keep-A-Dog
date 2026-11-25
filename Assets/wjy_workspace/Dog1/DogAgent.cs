using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace KeepADog.Demo
{
    public enum DogIntent { Wait, Sit, Crouch, Spin, Come, Fetch, Comfort, Paw, Bow, FetchBall, Pounce, Sniff }

    public sealed class DogAgentDecision
    {
        public string Reply { get; }
        public DogIntent Intent { get; }
        public string Target { get; }
        public bool IsRecall { get; }
        public bool IsRefusal { get; }
        public string[] Steps { get; }

        internal DogAgentDecision(string reply, DogIntent intent, string target, bool isRecall, bool isRefusal = false, string[] steps = null)
        {
            Reply = reply;
            Intent = intent;
            Target = target;
            IsRecall = isRecall;
            IsRefusal = isRefusal;
            Steps = steps;
        }
    }

    public sealed class DogAgent
    {
        public const string Lollipop = "lollipop";
        public const string TeachExample = "我心情不好的时候，给我叼来棒棒糖毛绒玩具";
        public const string RecallExample = "我今天心情不好";

        private const int MaxInputLength = 400;
        private const int HistoryCapacity = 5;
        private const int MemoryVersion = 1;
        private const int MaxStrength = 3;
        private static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(3);

        private readonly string savePath;
        private readonly List<RecentEvent> recent = new List<RecentEvent>(HistoryCapacity);
        private Memory memory;
        private Trial trial;
        private int generation;

        public DogAgent(string savePath = null)
        {
            this.savePath = savePath ?? Path.Combine(Application.persistentDataPath, "dog-memory.json");
            memory = Load();
        }

        public string SaveError { get; private set; } = "";
        public bool CanGiveFeedback => ActiveTrial && trial.Outcome != Outcome.Pending;
        public bool CanReward => ActiveTrial && trial.Outcome == Outcome.Succeeded;

        public string ShortTermSummary
        {
            get
            {
                PruneHistory();
                var lines = recent.ConvertAll(entry => entry.Text);
                if (ActiveTrial && trial.Outcome == Outcome.Pending)
                    lines.Add("正在练习" + ActionLabel(trial.Intent, trial.Target));
                return lines.Count == 0 ? "暂时没有最近互动" : string.Join("\n", lines);
            }
        }

        public string LongTermSummary
        {
            get
            {
                var lines = new List<string>();
                AddSkillSummary(lines, DogIntent.Sit);
                AddSkillSummary(lines, DogIntent.Crouch);
                AddSkillSummary(lines, DogIntent.Spin);
                AddSkillSummary(lines, DogIntent.Come);
                AddSkillSummary(lines, DogIntent.Fetch);
                AddSkillSummary(lines, DogIntent.FetchBall);
                AddSkillSummary(lines, DogIntent.Paw);
                AddSkillSummary(lines, DogIntent.Bow);
                AddSkillSummary(lines, DogIntent.Pounce);
                AddSkillSummary(lines, DogIntent.Sniff);
                if (memory.sadStrength > 0)
                    lines.Add("你心情不好时 → " + ActionLabel(memory.sadAction, memory.sadTarget)
                        + "（" + memory.sadStrength + "/" + MaxStrength + "）");
                return lines.Count == 0 ? "还没有获得奖励的训练记忆" : string.Join("\n", lines);
            }
        }

        private bool ActiveTrial => trial != null && DateTime.UtcNow - trial.Time <= RecentWindow;

        public DogAgentDecision Decide(string input)
        {
            generation++;
            return Process(input, null);
        }

        public Task<DogAgentDecision> DecideAsync(string input, string ollamaModel)
        {
            return DecideAsync(input, "http://127.0.0.1:11434/v1/chat/completions", ollamaModel);
        }

        public async Task<DogAgentDecision> DecideAsync(string input, string endpoint, string model, string apiKey = null)
        {
            int requestGeneration = ++generation;
            input = CleanInput(input);
            Interpretation interpretation = Interpret(input);
            if (interpretation == null && input.Length > 0 && !string.IsNullOrWhiteSpace(model))
                interpretation = await DogLanguageModel.InterpretAsync(input, ModelContext(), endpoint, model, apiKey);

            return requestGeneration == generation ? Process(input, interpretation) : null;
        }

        public void ReportExecution(DogAgentDecision decision, bool success)
        {
            if (!ActiveTrial || trial.Decision != decision || trial.Outcome != Outcome.Pending)
                return;

            trial.Outcome = success ? Outcome.Succeeded : Outcome.Failed;
            trial.Time = DateTime.UtcNow;
            Remember((success ? "完成" : "未能完成") + ActionLabel(trial.Intent, trial.Target));
        }

        public DogAgentDecision Feedback(bool positive)
        {
            if (!ActiveTrial)
                return Wait("汪？先练一个动作吧。");
            if (trial.Outcome == Outcome.Pending)
                return Wait("汪，等我做完再告诉我。");
            if (positive && !CanReward)
                return Wait("刚才没做成功。再教我一次吧。");

            Trial finished = trial;
            trial = null;
            Remember(positive ? "收到奖励" : "收到纠正");
            if (finished.Outcome != Outcome.Succeeded)
                return Wait("汪，下一次我再试试。");

            int difference = positive ? 1 : -1;
            if (finished.IsConditional)
            {
                bool sameRule = memory.sadAction == finished.Intent && memory.sadTarget == finished.Target;
                if (positive && !sameRule)
                {
                    memory.sadAction = finished.Intent;
                    memory.sadTarget = finished.Target;
                    memory.sadStrength = 0;
                }
                if (positive || sameRule)
                    memory.sadStrength = Mathf.Clamp(memory.sadStrength + difference, 0, MaxStrength);
            }

            if (positive || !finished.IsConditional)
            {
                int index = (int)finished.Intent;
                memory.skills[index] = Mathf.Clamp(memory.skills[index] + difference, 0, MaxStrength);
            }
            Save();

            if (!positive)
                return Wait("汪，我会重新练习。" + StorageNote());
            string learned = finished.IsConditional
                ? "汪！你心情不好时，我会" + ActionLabel(finished.Intent, finished.Target) + "。"
                : "汪！我记住了「" + ActionLabel(finished.Intent, finished.Target) + "」。";
            return Wait(learned + StorageNote());
        }

        public void Reset()
        {
            generation++;
            recent.Clear();
            trial = null;
            memory = new Memory();
            Save(clearBackup: true);
        }

        public DogAgentDecision RewardSpontaneousBow()
        {
            var result = StartTrial(DogIntent.Bow, "", false, false);
            ReportExecution(result, true);
            return Feedback(true);
        }

        private DogAgentDecision Process(string input, Interpretation interpretation)
        {
            input = CleanInput(input);
            if (input.Length == 0)
                return Wait("汪？你想教我什么？");

            Remember("你说：" + (input.Length <= 54 ? input : input.Substring(0, 54) + "…"));
            Interpretation outcome = interpretation ?? Interpret(input);
            if (outcome == null)
                return Wait("汪？我还没听懂。可以教我坐下、转圈、闻一闻，或叼回毛绒玩具。");

            switch (outcome.Kind)
            {
                case SpeechKind.Praise: return Feedback(true);
                case SpeechKind.Correct: return Feedback(false);
                case SpeechKind.Sad:
                    return memory.sadStrength > 0
                        ? StartTrial(memory.sadAction, memory.sadTarget, true, true)
                        : StartTrial(DogIntent.Comfort, "", false, false);
                case SpeechKind.Teach:
                    return StartTrial(outcome.Intent, outcome.Target, true, false);
                case SpeechKind.Command:
                    return StartCommand(outcome.Intent, outcome.Target);
                case SpeechKind.Sequence:
                    trial = null;
                    return new DogAgentDecision("麦粒准备试试这几个动作。", DogIntent.Wait, "", false, steps: outcome.Steps);
                default:
                    return Wait(outcome.Reply);
            }
        }

        private DogAgentDecision StartCommand(DogIntent intent, string target)
        {
            bool repeated = ActiveTrial && trial.Outcome == Outcome.Refused && trial.Intent == intent && trial.Target == target;
            if (memory.skills[(int)intent] > 0 || repeated)
                return StartTrial(intent, target, false, false);

            trial = new Trial { Intent = intent, Target = target, Outcome = Outcome.Refused, Time = DateTime.UtcNow };
            Remember("还在学习" + ActionLabel(intent, target));
            return new DogAgentDecision("汪？我还不熟悉「" + ActionLabel(intent, target) + "」。再叫我一次吧。",
                DogIntent.Wait, "", false, true);
        }

        private DogAgentDecision StartTrial(DogIntent intent, string target, bool conditional, bool recall)
        {
            string label = ActionLabel(intent, target);
            if (intent == DogIntent.Comfort)
            {
                trial = null;
                Remember("靠近你，陪你一会儿");
                return new DogAgentDecision("呜……我来陪你。", intent, target, false);
            }

            string reply = recall ? "汪！我想起来了，我去" + label + "。" : "汪！我试试" + label + "。";
            var decision = new DogAgentDecision(reply, intent, target, recall);
            trial = new Trial
            {
                Decision = decision, Intent = intent, Target = target, IsConditional = conditional,
                Outcome = Outcome.Pending, Time = DateTime.UtcNow
            };
            return decision;
        }

        private static Interpretation Interpret(string input)
        {
            string lower = input.ToLowerInvariant();
            if (Matches(lower, @"^(好狗狗|真棒|奖励|做得好|good dog|well done|good boy|good girl)[!！。. ]*$"))
                return new Interpretation(SpeechKind.Praise);
            if (Matches(lower, @"^(不对|做错了|重来|纠正|wrong|try again)[!！。. ]*$"))
                return new Interpretation(SpeechKind.Correct);
            if (Matches(lower, @"不难过|不伤心|心情没有不好|心情不差|心情不坏|(别|不要)(再|去)?(坐|蹲|过来|转|叼|抬|拜|扑|轻扑|跳|闻|嗅|难过|伤心)|\b(don't|do not|not sad|not unhappy)\b"))
                return new Interpretation(SpeechKind.Chat, reply: "汪，我听着呢。");

            bool sad = Matches(lower, @"心情(不太好|不好|很差|低落|糟透了)|难过|伤心|不开心|沮丧|想哭|情绪低落|\b(sad|upset|unhappy|feeling down)\b");
            bool conditional = Matches(lower, @"时候|如果|要是|每当|下次|记住|\b(when|whenever|if|remember|next time)\b");
            var intent = DogIntent.Wait;
            string target = "";
            if (Matches(lower, @"棒棒糖|\blollipop\b") && (conditional || Matches(lower, @"叼|拿|带|给我|取|fetch|bring|get")))
            {
                intent = DogIntent.Fetch;
                target = Lollipop;
            }
            else if (Matches(lower, @"球|\bball\b") && (conditional || Matches(lower, @"叼|拿|带|取|fetch|bring|get")))
            {
                intent = DogIntent.FetchBall;
                target = "ball";
            }
            else if (Matches(lower, @"抬(起)?右(爪|手)|握手|\b(paw|shake)\b")) intent = DogIntent.Paw;
            else if (Matches(lower, @"拜一拜|鞠躬|伏身邀玩|\b(play bow|bow)\b")) intent = DogIntent.Bow;
            else if (Matches(lower, @"轻扑|小扑|扑(一下|向玩具|玩具|过去)|往前扑|\b(pounce|little hop)\b")) intent = DogIntent.Pounce;
            else if (Matches(lower, @"闻一闻|闻闻|嗅一嗅|嗅闻|\bsniff\b")) intent = DogIntent.Sniff;
            else if (Matches(lower, @"坐下|^坐[。！! ]*$|\bsit\b")) intent = DogIntent.Sit;
            else if (Matches(lower, @"蹲下|蹲伏|趴下|\b(crouch|lie down)\b")) intent = DogIntent.Crouch;
            else if (Matches(lower, @"转圈|转一圈|转个圈|\b(spin|turn around)\b")) intent = DogIntent.Spin;
            else if (Matches(lower, @"过来|到我(这|身边)|\b(come here|come to me)\b|^come[.! ]*$")) intent = DogIntent.Come;

            if (sad && conditional && intent != DogIntent.Wait)
                return new Interpretation(SpeechKind.Teach, intent, target);
            if (intent != DogIntent.Wait && Matches(lower, @"^你(会|懂|学会).{1,18}(吗|么|[?？])[。?？ ]*$"))
                return new Interpretation(SpeechKind.Chat, reply: "麦粒歪头听着。你可以给它一个口令，等它试试看。");
            if (intent != DogIntent.Wait)
                return new Interpretation(SpeechKind.Command, intent, target);
            if (sad && conditional)
                return new Interpretation(SpeechKind.Chat, reply: "汪，我听着呢。");
            bool playerIsSad = sad && !Matches(lower, @"你.{0,8}(难过|伤心|不开心|心情不好).{0,4}(吗|么|[?？])|\b(are you|is he|is she)\b");
            return playerIsSad ? new Interpretation(SpeechKind.Sad) : null;
        }

        private static bool Matches(string value, string pattern) => Regex.IsMatch(value, pattern);

        private static string CleanInput(string input)
        {
            input = (input ?? "").Trim();
            return input.Length <= MaxInputLength ? input : input.Substring(0, MaxInputLength);
        }

        private static DogAgentDecision Wait(string reply) => new DogAgentDecision(reply, DogIntent.Wait, "", false);

        private static string ActionLabel(DogIntent intent, string target)
        {
            switch (intent)
            {
                case DogIntent.Sit: return "坐下";
                case DogIntent.Crouch: return "蹲伏";
                case DogIntent.Spin: return "转圈";
                case DogIntent.Come: return "过来";
                case DogIntent.Fetch: return target == Lollipop ? "叼来棒棒糖毛绒玩具" : "取回物品";
                case DogIntent.FetchBall: return "叼来玩具球";
                case DogIntent.Paw: return "抬右爪";
                case DogIntent.Bow: return "伏身邀玩";
                case DogIntent.Pounce: return "轻扑一下";
                case DogIntent.Sniff: return "闻一闻";
                default: return "等待";
            }
        }

        internal static bool IsSupportedAction(DogIntent intent, string target)
        {
            if (intent == DogIntent.Fetch) return target == Lollipop;
            if (intent == DogIntent.FetchBall) return target == "ball";
            return string.IsNullOrEmpty(target) && (intent == DogIntent.Sit || intent == DogIntent.Crouch
                || intent == DogIntent.Spin || intent == DogIntent.Come || intent == DogIntent.Paw || intent == DogIntent.Bow ||
                intent == DogIntent.Pounce || intent == DogIntent.Sniff);
        }

        private void AddSkillSummary(List<string> lines, DogIntent intent)
        {
            int score = memory.skills[(int)intent];
            if (score > 0)
                lines.Add(ActionLabel(intent, Lollipop) + "（" + score + "/" + MaxStrength + "）");
        }

        private void Remember(string note)
        {
            PruneHistory();
            if (recent.Count == HistoryCapacity) recent.RemoveAt(0);
            recent.Add(new RecentEvent { Time = DateTime.UtcNow, Text = note });
        }

        private void PruneHistory()
        {
            DateTime cutoff = DateTime.UtcNow - RecentWindow;
            recent.RemoveAll(entry => entry.Time < cutoff);
        }

        private string ModelContext()
        {
            PruneHistory();
            var context = new ModelMemory
            {
                sit = memory.skills[(int)DogIntent.Sit],
                crouch = memory.skills[(int)DogIntent.Crouch],
                spin = memory.skills[(int)DogIntent.Spin],
                come = memory.skills[(int)DogIntent.Come],
                fetchLollipop = memory.skills[(int)DogIntent.Fetch],
                fetchBall = memory.skills[(int)DogIntent.FetchBall],
                paw = memory.skills[(int)DogIntent.Paw],
                bow = memory.skills[(int)DogIntent.Bow],
                pounce = memory.skills[(int)DogIntent.Pounce],
                sniff = memory.skills[(int)DogIntent.Sniff],
                sadnessAction = memory.sadStrength > 0 ? memory.sadAction.ToString().ToLowerInvariant() : "none",
                sadnessTarget = memory.sadStrength > 0 ? memory.sadTarget : "",
                sadnessStrength = memory.sadStrength,
                recentEventCount = recent.Count,
                currentPractice = ActiveTrial ? trial.Intent.ToString().ToLowerInvariant() : "none",
                currentPracticeStatus = ActiveTrial ? trial.Outcome.ToString().ToLowerInvariant() : "none"
            };
            return JsonUtility.ToJson(context, false);
        }

        private Memory Load()
        {
            try
            {
                if (TryRead(savePath, out Memory primary)) return primary;
                if (TryRead(savePath + ".bak", out Memory backup))
                {
                    SaveError = "已从备份恢复，下一次训练会重新保存。";
                    return backup;
                }
                if (File.Exists(savePath) || File.Exists(savePath + ".bak"))
                    SaveError = "无法读取已保存的训练记忆。";
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            {
                SaveError = "无法读取已保存的训练记忆。";
            }
            return new Memory();
        }

        private static bool TryRead(string path, out Memory result)
        {
            result = null;
            if (!File.Exists(path)) return false;
            try
            {
                if (new FileInfo(path).Length > 8192) return false;
                var saved = JsonUtility.FromJson<Memory>(File.ReadAllText(path));
                if (saved == null || saved.version != MemoryVersion || saved.skills == null ||
                    (saved.skills.Length != Memory.SkillCount && saved.skills.Length != (int)DogIntent.FetchBall + 1 &&
                        saved.skills.Length != (int)DogIntent.Comfort + 1))
                    return false;
                if (saved.skills.Length != Memory.SkillCount) Array.Resize(ref saved.skills, Memory.SkillCount);
                for (int index = 0; index < saved.skills.Length; index++)
                    saved.skills[index] = Mathf.Clamp(saved.skills[index], 0, MaxStrength);
                saved.sadTarget = saved.sadTarget ?? "";
                saved.sadStrength = IsSupportedAction(saved.sadAction, saved.sadTarget)
                    ? Mathf.Clamp(saved.sadStrength, 0, MaxStrength) : 0;
                result = saved;
                return true;
            }
            catch (Exception error) when (error is ArgumentException || error is IOException || error is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private void Save(bool clearBackup = false)
        {
            string tempPath = savePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string folder = Path.GetDirectoryName(Path.GetFullPath(savePath));
                Directory.CreateDirectory(folder);
                File.WriteAllText(tempPath, JsonUtility.ToJson(memory, true));
                if (File.Exists(savePath))
                    File.Replace(tempPath, savePath, clearBackup ? null : savePath + ".bak");
                else
                    File.Move(tempPath, savePath);
                if (clearBackup) File.Delete(savePath + ".bak");
                SaveError = "";
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            {
                SaveError = "训练记忆暂存于本次运行，写入磁盘失败。";
                try { File.Delete(tempPath); }
                catch (Exception cleanupError) when (cleanupError is IOException || cleanupError is UnauthorizedAccessException || cleanupError is ArgumentException) { }
            }
        }

        private string StorageNote() => SaveError.Length == 0 ? "" : " " + SaveError;

        internal enum SpeechKind { Chat, Command, Teach, Sad, Praise, Correct, Sequence }

        internal sealed class Interpretation
        {
            public readonly SpeechKind Kind;
            public readonly DogIntent Intent;
            public readonly string Target;
            public readonly string Reply;
            public readonly string[] Steps;

            public Interpretation(SpeechKind kind, DogIntent intent = DogIntent.Wait, string target = "", string reply = "", string[] steps = null)
            {
                Kind = kind;
                Intent = intent;
                Target = target;
                Reply = reply;
                Steps = steps;
            }
        }

        private enum Outcome { Refused, Pending, Succeeded, Failed }

        private sealed class Trial
        {
            public DogAgentDecision Decision;
            public DogIntent Intent;
            public string Target;
            public bool IsConditional;
            public Outcome Outcome;
            public DateTime Time;
        }

        private struct RecentEvent
        {
            public DateTime Time;
            public string Text;
        }

        [Serializable]
        private sealed class ModelMemory
        {
            public int sit;
            public int crouch;
            public int spin;
            public int come;
            public int fetchLollipop;
            public int fetchBall;
            public int paw;
            public int bow;
            public int pounce;
            public int sniff;
            public string sadnessAction;
            public string sadnessTarget;
            public int sadnessStrength;
            public int recentEventCount;
            public string currentPractice;
            public string currentPracticeStatus;
        }

        [Serializable]
        private sealed class Memory
        {
            public const int SkillCount = (int)DogIntent.Sniff + 1;
            public int version = MemoryVersion;
            public int[] skills = new int[SkillCount];
            public DogIntent sadAction;
            public string sadTarget = "";
            public int sadStrength;
        }
    }
}
