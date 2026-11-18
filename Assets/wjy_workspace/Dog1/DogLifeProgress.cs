using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Wjy.DogTraining
{
    public enum DogCareAction { Feed, Water, Walk, Clean, Play, Train, Rest }
    public enum DogLocation { Apartment, Park, Terrace }

    public readonly struct DogCareResult
    {
        public bool Succeeded { get; }
        public string Message { get; }
        public int ExperienceEarned { get; }
        public int BonesEarned { get; }

        internal DogCareResult(bool succeeded, string message, int experienceEarned = 0, int bonesEarned = 0)
        {
            Succeeded = succeeded;
            Message = message;
            ExperienceEarned = experienceEarned;
            BonesEarned = bonesEarned;
        }
    }

    public readonly struct DogLifeTask
    {
        public DogCareAction Action { get; }
        public int Required { get; }

        internal DogLifeTask(DogCareAction action, int required)
        {
            Action = action;
            Required = required;
        }
    }

    public sealed class DogLifeChapter
    {
        public int Number { get; }
        public string Title { get; }
        public string Description { get; }
        public IReadOnlyList<DogLifeTask> Tasks { get; }
        public DogLocation? RequiredLocation { get; }
        internal int ExperienceReward { get; }
        internal int BonesReward { get; }

        internal DogLifeChapter(int number, string title, string description, DogLocation? requiredLocation,
            int experienceReward, int bonesReward, params DogLifeTask[] tasks)
        {
            Number = number;
            Title = title;
            Description = description;
            RequiredLocation = requiredLocation;
            ExperienceReward = experienceReward;
            BonesReward = bonesReward;
            Tasks = Array.AsReadOnly(tasks);
        }
    }

    public sealed class DogLifeProgress
    {
        private const int ActionCount = (int)DogCareAction.Rest + 1;
        private const int LocationCount = (int)DogLocation.Terrace + 1;
        private const int ApartmentMask = 1 << (int)DogLocation.Apartment;
        private const int AllLocationsMask = (1 << LocationCount) - 1;
        private const int DailyBonus = 3;
        private const int BonesPerLevel = 3;
        private const int MaxWaste = 3;
        private const int MaxBones = 99999;
        private const float AutosaveInterval = 30f;

        private static readonly int[] LevelThresholds = { 0, 32, 95, 180, 285 };
        private static readonly ActionSettings[] Actions =
        {
            new ActionSettings("喂食", 8, 1, 45f),
            new ActionSettings("换水", 6, 1, 35f),
            new ActionSettings("遛弯", 16, 2, 65f),
            new ActionSettings("清理便便", 10, 1, 0f),
            new ActionSettings("陪它玩", 14, 2, 35f),
            new ActionSettings("训练", 16, 2, 25f),
            new ActionSettings("陪它休息", 8, 1, 50f)
        };

        private static readonly LocationSettings[] Locations =
        {
            new LocationSettings("城市公寓", 1, 0),
            new LocationSettings("街区公园", 2, 8),
            new LocationSettings("露台小屋", 4, 28)
        };

        private static readonly DogLifeChapter[] Chapters =
        {
            new DogLifeChapter(1, "到家的第一晚", "今天接它回家。先准备食物和清水，再陪它玩一小会儿，等它熟悉这里。",
                null, 12, 2, Task(DogCareAction.Feed), Task(DogCareAction.Water), Task(DogCareAction.Play)),
            new DogLifeChapter(2, "熟悉这条街", "第一次遛弯。给它留些时间闻一闻，再拾起便便。试着在它愿意时练一个口令。",
                DogLocation.Park, 16, 3, Task(DogCareAction.Walk), Task(DogCareAction.Clean), Task(DogCareAction.Train)),
            new DogLifeChapter(3, "下班后的日常", "再忙的日子也有吃饭、喝水、散步和休息。试着找到你和它都适应的节奏。",
                null, 22, 4, Task(DogCareAction.Feed, 2), Task(DogCareAction.Water, 2), Task(DogCareAction.Walk, 2),
                Task(DogCareAction.Clean, 2), Task(DogCareAction.Play), Task(DogCareAction.Rest)),
            new DogLifeChapter(4, "它也懂你", "它越来越熟悉你的声音。练习后给它及时的反馈，也留时间让它自己玩。",
                null, 26, 4, Task(DogCareAction.Train, 3), Task(DogCareAction.Play, 2), Task(DogCareAction.Rest)),
            new DogLifeChapter(5, "一起生活", "带它看看露台小屋。新场景有新鲜气味，喂水、散步和陪玩仍是每天的约定。",
                DogLocation.Terrace, 30, 6, Task(DogCareAction.Water), Task(DogCareAction.Walk), Task(DogCareAction.Clean),
                Task(DogCareAction.Play), Task(DogCareAction.Train), Task(DogCareAction.Rest))
        };

        private readonly string savePath;
        private readonly Func<DateTime> localClock;
        private SaveData data;
        private bool recoveredFromBackup;
        private float secondsToSave = AutosaveInterval;

        public DogLifeProgress(string savePath = null, Func<DateTime> localClock = null)
        {
            this.savePath = savePath ?? Path.Combine(Application.persistentDataPath, "dog-life.json");
            this.localClock = localClock ?? (() => DateTime.Now);
            data = Load();
        }

        public string Name => "麦粒";
        internal string BreedId => data.dogBreed;

        internal void SelectBreed(DogBreed breed)
        {
            if (breed == null || data.dogBreed == breed.Id) return;
            data.dogBreed = breed.Id;
            Save();
        }
        public string SaveError { get; private set; } = "";
        public int Level => GetLevel(data.totalExperience);
        public int MaxLevel => LevelThresholds.Length;
        public int TotalExperience => data.totalExperience;
        public int Experience => data.totalExperience - LevelThresholds[Level - 1];
        public int ExperienceForNextLevel => Level < MaxLevel ? LevelThresholds[Level] - LevelThresholds[Level - 1] : 0;
        public int Bones => data.bones;
        public float Satiety => data.satiety;
        public float Hydration => data.hydration;
        public float Energy => data.energy;
        public float Mood => data.mood;
        public float Cleanliness => data.cleanliness;
        public int WasteCount => data.waste[(int)data.location];
        public DogLocation CurrentLocation => data.location;
        public DogLifeChapter CurrentChapter => Chapters[data.chapterIndex];
        public bool ChapterFinished => data.storyFinished;
        public bool CanClaimDaily => string.CompareOrdinal(ClaimDate(), data.lastClaimDate) > 0;

        public string WellbeingNote
        {
            get
            {
                if (Hydration < 36f) return "麦粒有点渴，正在找水碗。";
                if (Satiety < 32f) return "麦粒饿了，看看它的饭碗吧。";
                if (Energy < 26f) return "麦粒累了，想在你身边睡一会儿。";
                if (WasteCount > 0) return "附近有便便，散步后记得清理。";
                if (Mood < 37f) return "麦粒在等你，找个小玩具陪它玩吧。";
                if (Cleanliness < 45f) return "找个时间照料一下麦粒和它常待的地方。";
                return "麦粒会闻一闻周围，累了就自己歇一会儿。";
            }
        }

        public static string GetActionName(DogCareAction action) => IsAction(action) ? Actions[(int)action].Name : "未知操作";
        public static string GetLocationName(DogLocation location) => IsLocation(location) ? Locations[(int)location].Name : "未知场景";
        public int GetUnlockCost(DogLocation location) => IsLocation(location) ? Locations[(int)location].Price : 0;
        public int GetRequiredLevel(DogLocation location) => IsLocation(location) ? Locations[(int)location].Level : 0;
        public bool IsUnlocked(DogLocation location) => IsLocation(location) && (data.unlockedLocations & LocationBit(location)) != 0;
        public bool HasVisited(DogLocation location) => IsLocation(location) && (data.visitedLocations & LocationBit(location)) != 0;

        public int GetTaskProgress(DogCareAction action)
        {
            if (!IsAction(action)) return 0;
            foreach (DogLifeTask task in CurrentChapter.Tasks)
                if (task.Action == action) return Math.Min(task.Required, data.chapterCounts[(int)action]);
            return 0;
        }

        public float GetCooldownRemaining(DogCareAction action) => IsAction(action) ? data.cooldowns[(int)action] : 0f;

        public string GetBlockReason(DogCareAction action)
        {
            if (!IsAction(action)) return "这个照护动作还不可用。";
            float cooldown = GetCooldownRemaining(action);
            if (cooldown > 0f) return "再等 " + Mathf.CeilToInt(cooldown) + " 秒。";
            switch (action)
            {
                case DogCareAction.Feed when Satiety >= 90f: return "麦粒现在已经吃饱了。";
                case DogCareAction.Water when Hydration >= 95f: return "麦粒刚喝过水。";
                case DogCareAction.Walk when WasteCount >= MaxWaste: return "先清理附近的便便。";
                case DogCareAction.Clean when WasteCount == 0: return "这里没有要清理的便便。";
                case DogCareAction.Rest when Energy >= 94f: return "麦粒现在精神很好。";
                case DogCareAction.Walk:
                case DogCareAction.Play:
                case DogCareAction.Train:
                    if (Energy < 15f) return "先让麦粒休息一会儿。";
                    if (Hydration < 15f) return "先给麦粒一点清水。";
                    break;
            }
            return "";
        }

        public DogCareResult Complete(DogCareAction action)
        {
            string reason = GetBlockReason(action);
            if (reason.Length != 0) return new DogCareResult(false, reason);

            int previousExperience = TotalExperience;
            int previousBones = Bones;
            int previousChapter = CurrentChapter.Number;
            int actionIndex = (int)action;
            ActionSettings settings = Actions[actionIndex];
            string message = ApplyCare(action);
            data.cooldowns[actionIndex] = settings.Cooldown;
            if (!data.storyFinished) data.chapterCounts[actionIndex] = Math.Min(data.chapterCounts[actionIndex] + 1, 10);

            Award(settings.Experience, settings.Bones);
            if (TryFinishChapter())
                message += ChapterFinished ? " 你们一起走过了收养后的第一周。" : " 已完成第 " + previousChapter + " 章。";
            Save();
            return new DogCareResult(true, message, TotalExperience - previousExperience, Bones - previousBones);
        }

        public DogCareResult ClaimDailyBonus()
        {
            string date = ClaimDate();
            if (string.CompareOrdinal(date, data.lastClaimDate) <= 0)
                return new DogCareResult(false, "今天的小骨头已经领过了。");
            data.lastClaimDate = date;
            int previous = Bones;
            Award(0, DailyBonus);
            Save();
            return new DogCareResult(true, "欢迎回来，收下今天的 " + (Bones - previous) + " 枚小骨头。", 0, Bones - previous);
        }

        public DogCareResult Unlock(DogLocation location)
        {
            if (!IsLocation(location)) return new DogCareResult(false, "这个场景还没开放。 ");
            string name = GetLocationName(location);
            if (IsUnlocked(location)) return new DogCareResult(false, name + "已经解锁了。");
            int requiredLevel = GetRequiredLevel(location);
            if (Level < requiredLevel) return new DogCareResult(false, "达到 " + requiredLevel + " 级后可以解锁" + name + "。");
            int price = GetUnlockCost(location);
            if (Bones < price) return new DogCareResult(false, "还差 " + (price - Bones) + " 枚小骨头。");

            data.bones -= price;
            data.unlockedLocations |= LocationBit(location);
            Save();
            return new DogCareResult(true, "已解锁" + name + "。", 0, -price);
        }

        public bool Visit(DogLocation location)
        {
            if (!IsUnlocked(location)) return false;
            if (data.location == location && HasVisited(location)) return true;
            data.location = location;
            data.visitedLocations |= LocationBit(location);
            TryFinishChapter();
            Save();
            return true;
        }

        public void Tick(float seconds)
        {
            if (!(seconds > 0f) || float.IsInfinity(seconds)) return;
            // A long editor pause, frame stall, or suspended app should never drain needs.
            seconds = Mathf.Min(seconds, 2f);
            data.satiety = Need(data.satiety - 0.050f * seconds);
            data.hydration = Need(data.hydration - 0.070f * seconds);
            data.energy = Need(data.energy - 0.023f * seconds);
            data.mood = Need(data.mood - 0.045f * seconds);
            data.cleanliness = Need(data.cleanliness - (0.014f + WasteCount * 0.015f) * seconds);
            for (int i = 0; i < data.cooldowns.Length; i++)
                data.cooldowns[i] = Mathf.Max(0f, data.cooldowns[i] - seconds);

            secondsToSave -= seconds;
            if (secondsToSave <= 0f) Save();
        }

        public void Save()
        {
            secondsToSave = AutosaveInterval;
            string temporary = savePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(savePath)));
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
                if (File.Exists(savePath)) File.Replace(temporary, savePath, recoveredFromBackup ? null : savePath + ".bak");
                else File.Move(temporary, savePath);
                recoveredFromBackup = false;
                SaveError = "";
            }
            catch (Exception error) when (IsFileError(error))
            {
                SaveError = "照护进度写入失败，当前进度暂留在这次游戏。";
                try { File.Delete(temporary); }
                catch (Exception cleanupError) when (IsFileError(cleanupError)) { }
            }
        }

        private string ApplyCare(DogCareAction action)
        {
            switch (action)
            {
                case DogCareAction.Feed:
                    data.satiety = Need(data.satiety + 30f);
                    data.mood = Need(data.mood + 4f);
                    return "麦粒吃过饭了。";
                case DogCareAction.Water:
                    data.hydration = Need(data.hydration + 32f);
                    return "水碗里有清水了。";
                case DogCareAction.Walk:
                    data.waste[(int)data.location]++;
                    data.mood = Need(data.mood + 16f);
                    data.energy = Need(data.energy - 16f);
                    data.satiety = Need(data.satiety - 9f);
                    data.hydration = Need(data.hydration - 14f);
                    data.cleanliness = Need(data.cleanliness - 9f);
                    return "麦粒闻了闻路边，散步后记得清理便便。";
                case DogCareAction.Clean:
                    data.waste[(int)data.location]--;
                    data.cleanliness = Need(data.cleanliness + 21f);
                    return "把便便捡起来，附近干净多了。";
                case DogCareAction.Play:
                    data.mood = Need(data.mood + 24f);
                    data.energy = Need(data.energy - 12f);
                    data.hydration = Need(data.hydration - 6f);
                    return "麦粒玩得起劲，摇起了尾巴。";
                case DogCareAction.Train:
                    data.mood = Need(data.mood + 7f);
                    data.energy = Need(data.energy - 6f);
                    return "麦粒练了一次，耐心和反馈都很重要。";
                case DogCareAction.Rest:
                    data.energy = Need(data.energy + 36f);
                    data.mood = Need(data.mood + 5f);
                    return "麦粒在你身边休息了一会儿。";
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        }

        private bool TryFinishChapter()
        {
            if (data.storyFinished) return false;
            DogLifeChapter chapter = CurrentChapter;
            if (chapter.RequiredLocation.HasValue && !HasVisited(chapter.RequiredLocation.Value)) return false;
            foreach (DogLifeTask task in chapter.Tasks)
                if (GetTaskProgress(task.Action) < task.Required) return false;

            Award(chapter.ExperienceReward, chapter.BonesReward);
            if (data.chapterIndex == Chapters.Length - 1) data.storyFinished = true;
            else
            {
                data.chapterIndex++;
                Array.Clear(data.chapterCounts, 0, data.chapterCounts.Length);
            }
            return true;
        }

        private void Award(int experience, int bones)
        {
            int priorLevel = Level;
            data.totalExperience = Math.Min(LevelThresholds[MaxLevel - 1], data.totalExperience + experience);
            data.bones = Mathf.Clamp(data.bones + bones + (Level - priorLevel) * BonesPerLevel, 0, MaxBones);
        }

        private SaveData Load()
        {
            if (TryRead(savePath, out SaveData primary)) return primary;
            if (TryRead(savePath + ".bak", out SaveData backup))
            {
                recoveredFromBackup = true;
                SaveError = "照护进度已从上次备份恢复。";
                return backup;
            }
            if (File.Exists(savePath) || File.Exists(savePath + ".bak"))
                SaveError = "无法读取以前的照护进度。";
            return new SaveData { version = SaveData.CurrentVersion };
        }

        private bool TryRead(string file, out SaveData restored)
        {
            restored = null;
            try
            {
                if (!File.Exists(file) || new FileInfo(file).Length > 8192) return false;
                SaveData parsed = JsonUtility.FromJson<SaveData>(File.ReadAllText(file));
                if (parsed == null || parsed.version != SaveData.CurrentVersion || parsed.cooldowns?.Length != ActionCount ||
                    parsed.chapterCounts?.Length != ActionCount || parsed.waste?.Length != LocationCount) return false;
                if (DogBreed.Find(parsed.dogBreed) == null) parsed.dogBreed = null;
                parsed.totalExperience = Mathf.Clamp(parsed.totalExperience, 0, LevelThresholds[MaxLevel - 1]);
                parsed.bones = Mathf.Clamp(parsed.bones, 0, MaxBones);
                parsed.chapterIndex = Mathf.Clamp(parsed.chapterIndex, 0, Chapters.Length - 1);
                parsed.storyFinished &= parsed.chapterIndex == Chapters.Length - 1;
                parsed.satiety = RecoverNeed(parsed.satiety, 65f);
                parsed.hydration = RecoverNeed(parsed.hydration, 65f);
                parsed.energy = RecoverNeed(parsed.energy, 85f);
                parsed.mood = RecoverNeed(parsed.mood, 58f);
                parsed.cleanliness = RecoverNeed(parsed.cleanliness, 90f);
                if (!DateTime.TryParseExact(parsed.lastClaimDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _)) parsed.lastClaimDate = "";
                parsed.unlockedLocations = (parsed.unlockedLocations & AllLocationsMask) | ApartmentMask;
                parsed.visitedLocations = (parsed.visitedLocations & parsed.unlockedLocations) | ApartmentMask;
                if (!IsLocation(parsed.location) || (parsed.unlockedLocations & LocationBit(parsed.location)) == 0)
                    parsed.location = DogLocation.Apartment;
                parsed.visitedLocations |= LocationBit(parsed.location);
                for (int i = 0; i < ActionCount; i++)
                {
                    parsed.cooldowns[i] = float.IsNaN(parsed.cooldowns[i]) ? 0f : Mathf.Clamp(parsed.cooldowns[i], 0f, Actions[i].Cooldown);
                    parsed.chapterCounts[i] = Mathf.Clamp(parsed.chapterCounts[i], 0, 10);
                }
                for (int i = 0; i < LocationCount; i++) parsed.waste[i] = Mathf.Clamp(parsed.waste[i], 0, MaxWaste);
                restored = parsed;
                return true;
            }
            catch (Exception error) when (IsFileError(error)) { return false; }
        }

        private string ClaimDate() => localClock().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        private static DogLifeTask Task(DogCareAction action, int required = 1) => new DogLifeTask(action, required);
        private static bool IsAction(DogCareAction action) => (int)action >= 0 && (int)action < ActionCount;
        private static bool IsLocation(DogLocation location) => (int)location >= 0 && (int)location < LocationCount;
        private static int LocationBit(DogLocation location) => 1 << (int)location;
        private static float Need(float value) => Mathf.Clamp(value, 0f, 100f);
        private static float RecoverNeed(float value, float fallback) => float.IsNaN(value) ? fallback : Need(value);
        private static bool IsFileError(Exception error) => error is IOException || error is UnauthorizedAccessException || error is ArgumentException;

        private static int GetLevel(int experience)
        {
            for (int level = LevelThresholds.Length; level > 1; level--)
                if (experience >= LevelThresholds[level - 1]) return level;
            return 1;
        }

        private readonly struct ActionSettings
        {
            public readonly string Name;
            public readonly int Experience;
            public readonly int Bones;
            public readonly float Cooldown;

            public ActionSettings(string name, int experience, int bones, float cooldown)
            {
                Name = name;
                Experience = experience;
                Bones = bones;
                Cooldown = cooldown;
            }
        }

        private readonly struct LocationSettings
        {
            public readonly string Name;
            public readonly int Level;
            public readonly int Price;

            public LocationSettings(string name, int level, int price)
            {
                Name = name;
                Level = level;
                Price = price;
            }
        }

        [Serializable]
        private sealed class SaveData
        {
            public string dogBreed;
            public const int CurrentVersion = 1;
            public int version;
            public int totalExperience;
            public int bones;
            public int chapterIndex;
            public bool storyFinished;
            public int[] chapterCounts = new int[ActionCount];
            public float[] cooldowns = new float[ActionCount];
            public int[] waste = new int[LocationCount];
            public int unlockedLocations = ApartmentMask;
            public int visitedLocations = ApartmentMask;
            public DogLocation location;
            public string lastClaimDate = "";
            public float satiety = 65f;
            public float hydration = 66f;
            public float energy = 85f;
            public float mood = 58f;
            public float cleanliness = 90f;
        }
    }
}
