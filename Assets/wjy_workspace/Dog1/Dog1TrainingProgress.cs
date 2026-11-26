using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed class Dog1TrainingProgress
    {
        private const int CurrentVersion = 3;
        private const int InitialRate = 40;
        private const int TrainingIncrease = 10;
        private const int MaximumRate = 90;
        private static readonly ReadOnlyCollection<string> Skills = Array.AsReadOnly(new[]
        {
            "sit", "crouch", "paw", "spin", "come", "fetch_toy"
        });

        private readonly string savePath;
        private readonly SaveData data;
        private bool recoveredFromBackup;
        private bool pendingSave;

        public Dog1TrainingProgress(string savePath = null)
        {
            this.savePath = savePath ?? Path.Combine(Application.persistentDataPath, "dog-training-skills.json");
            string backupPath = this.savePath + ".bak";
            data = ReadSave(this.savePath);
            if (data == null)
            {
                data = ReadSave(backupPath);
                if (data != null)
                {
                    recoveredFromBackup = true;
                    SaveError = "训练成功率已从上次备份恢复。";
                }
            }

            if (data?.version == CurrentVersion) return;

            bool resetOldRates = data != null;
            data = new SaveData { version = CurrentVersion, skills = new SkillRate[Skills.Count] };
            for (int i = 0; i < Skills.Count; i++)
                data.skills[i] = new SkillRate { id = Skills[i], successRate = InitialRate };

            if (!resetOldRates && (File.Exists(this.savePath) || File.Exists(backupPath)))
                SaveError = "无法读取以前的训练成功率，暂用初始成功率。";
            else
            {
                pendingSave = true;
                SavePendingChanges();
            }
        }

        public static IReadOnlyList<string> SkillIds => Skills;
        public static bool IsSkill(string id) => Skills.Contains(id);
        public string SaveError { get; private set; } = "";

        public int GetSuccessRate(string id) => GetSkill(id).successRate;

        public int IncreaseRate(string id)
        {
            SkillRate skill = GetSkill(id);
            if (skill.successRate < MaximumRate)
            {
                skill.successRate = Math.Min(MaximumRate, skill.successRate + TrainingIncrease);
                pendingSave = true;
            }
            SavePendingChanges();
            return skill.successRate;
        }

        public void SavePendingChanges()
        {
            if (!pendingSave) return;
            string temporary = savePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(savePath)));
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
                if (File.Exists(savePath))
                    File.Replace(temporary, savePath, recoveredFromBackup ? null : savePath + ".bak");
                else File.Move(temporary, savePath);
                pendingSave = false;
                recoveredFromBackup = false;
                SaveError = "";
            }
            catch (Exception error) when (IsFileError(error))
            {
                SaveError = "训练成功率保存失败，本次进度暂留在游戏中。";
                try { File.Delete(temporary); }
                catch (Exception cleanupError) when (IsFileError(cleanupError)) { }
            }
        }

        private SkillRate GetSkill(string id) =>
            Array.Find(data.skills, skill => skill.id == id) ?? throw new ArgumentOutOfRangeException(nameof(id));

        private static SaveData ReadSave(string file)
        {
            try
            {
                if (!File.Exists(file) || new FileInfo(file).Length > 4096) return null;
                var loaded = JsonUtility.FromJson<SaveData>(File.ReadAllText(file));
                if (loaded?.skills?.Length != Skills.Count || loaded.version < 1 || loaded.version > CurrentVersion)
                    return null;

                int minimumRate = loaded.version == 1 ? 20 : InitialRate;
                int maximumRate = loaded.version == CurrentVersion ? MaximumRate : 100;
                var seen = new HashSet<string>();
                foreach (SkillRate skill in loaded.skills)
                    if (skill == null || !IsSkill(skill.id) || !seen.Add(skill.id) ||
                        skill.successRate < minimumRate || skill.successRate > maximumRate) return null;
                return loaded;
            }
            catch (Exception error) when (IsFileError(error)) { return null; }
        }

        private static bool IsFileError(Exception error) =>
            error is IOException || error is UnauthorizedAccessException || error is ArgumentException;

        [Serializable]
        private sealed class SaveData
        {
            public int version;
            public SkillRate[] skills;
        }

        [Serializable]
        private sealed class SkillRate
        {
            public string id;
            public int successRate;
        }
    }
}
