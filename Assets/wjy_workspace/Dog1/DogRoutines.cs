using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed class DogRoutines
    {
        private readonly string path;
        private const int Capacity = 8;
        private List<Routine> saved = new List<Routine>();
        public string Summary => saved.Count == 0 ? "还没有学会命名的动作串。" :
            string.Join("\n", saved.Select(routine => routine.name + " · " + Describe(routine.steps)));
        public DogRoutines(string savePath = null)
        {
            path = savePath ?? Path.Combine(Application.persistentDataPath, "dog-routine.json");
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length <= 4096)
                {
                    string json = File.ReadAllText(path);
                    var loaded = JsonUtility.FromJson<Collection>(json);
                    if (loaded?.routines != null)
                        saved = loaded.routines.Where(IsValidRoutine).Take(Capacity).ToList();
                    else
                    {
                        var old = JsonUtility.FromJson<Routine>(json);
                        if (IsValidRoutine(old)) saved.Add(old);
                    }
                }
            }
            catch (Exception error) when (error is ArgumentException || error is IOException || error is UnauthorizedAccessException) { }
        }

        public string[] Recall(string input)
        {
            string cue = input.Trim().TrimEnd('。', '！', '!');
            var match = saved.Find(routine => string.Equals(cue, routine.name, StringComparison.OrdinalIgnoreCase));
            if (match == null && Regex.IsMatch(cue, @"^我(今天|现在|有点)?(心情不好|难过|不开心)$"))
                match = saved.Find(routine => routine.name == "我心情不好");
            return match == null ? null : (string[])match.steps.Clone();
        }

        public bool TryParse(string input, out string name, out string[] steps)
        {
            name = "";
            steps = null;
            if (string.IsNullOrWhiteSpace(input) || input.Length > 180) return false;
            var parts = input.Split(new[] { ':' , '：' }, 2);
            string phrase = input;
            if (parts.Length == 2)
            {
                name = parts[0].Trim();
                if (!ValidName(name)) return false;
                phrase = parts[1];
            }
            string[] tokens = Regex.Split(phrase.Trim(), "然后|接着|再|[，,、；;]")
                .Select(token => token.Trim()).Where(token => token.Length > 0).ToArray();
            if (tokens.Length < 2 || tokens.Length > 3) return false;
            var parsed = tokens.Select(token => Step(token.Trim().TrimEnd('。', '！', '!'))).ToArray();
            if (!Valid(parsed)) return false;
            steps = parsed;
            return true;
        }

        public bool Remember(string name, string[] steps)
        {
            if (!ValidName(name) || !Valid(steps)) return false;
            var candidate = new List<Routine>(saved);
            candidate.RemoveAll(routine => string.Equals(routine.name, name, StringComparison.OrdinalIgnoreCase));
            if (candidate.Count >= Capacity) candidate.RemoveAt(0);
            candidate.Add(new Routine { name = name.Trim(), steps = (string[])steps.Clone() });
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllText(temp, JsonUtility.ToJson(new Collection { routines = candidate.ToArray() }, true));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                saved = candidate;
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            {
                try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                return false;
            }
        }

        public bool Clear()
        {
            try
            {
                File.Delete(path);
                saved.Clear();
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                return false;
            }
        }

        public static string Describe(string[] steps) => string.Join(" → ", steps.Select(Label));
        public static string Label(string motion)
        {
            switch (motion)
            {
                case "sit": return "坐下";
                case "crouch": return "趴下";
                case "spin": return "转圈";
                case "come": return "过来";
                case "paw": return "抬右爪";
                case "bow": return "伏身邀玩";
                case "pounce": return "轻扑一下";
                case "sniff": return "闻一闻";
                case "fetch_toy": return "叼球";
                case "fetch_lollipop": return "叼棒棒糖";
                default: return "等待";
            }
        }

        public static string Step(string text)
        {
            text = Regex.Replace(text, @"^(请你|请|先|能不能|可以|帮我)+\s*", "");
            switch (text)
            {
                case "坐": case "坐下": return "sit";
                case "趴下": case "趴": return "crouch";
                case "转圈": case "转个圈": return "spin";
                case "过来": return "come";
                case "抬右爪": case "抬起右爪": case "抬右手": case "抬起右手": case "握手": return "paw";
                case "拜一拜": case "鞠躬": case "伏身邀玩": return "bow";
                case "轻扑": case "轻扑一下": case "往前扑一下": return "pounce";
                case "闻一闻": case "闻闻": case "嗅闻": return "sniff";
                case "叼球": case "叼来球": case "叼来玩具球": return "fetch_toy";
                case "叼棒棒糖": case "叼来棒棒糖": return "fetch_lollipop";
                default: return null;
            }
        }

        private static bool ValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 20 &&
            Regex.IsMatch(name, @"^[\p{L}\p{N} ]+$") && Step(name) == null;

        private static bool IsValidRoutine(Routine routine) => routine != null && Valid(routine.steps) && ValidName(routine.name);

        internal static bool Valid(string[] steps) => steps != null && steps.Length >= 2 && steps.Length <= 3 &&
            steps.All(step => step == "sit" || step == "crouch" || step == "spin" || step == "come" ||
                step == "paw" || step == "bow" || step == "pounce" || step == "sniff" || step == "fetch_toy" || step == "fetch_lollipop") &&
            steps.Count(step => step.StartsWith("fetch_", StringComparison.Ordinal)) <= 1;

        [Serializable] private sealed class Routine { public string name; public string[] steps; }
        [Serializable] private sealed class Collection { public Routine[] routines; }
    }
}
