using System;
using System.Collections.Generic;

namespace Wjy.DogTraining
{
    [Serializable]
    public sealed class DogCommandOption
    {
        public string id;
        public string target;
        public string label;
        public string[] tags;
        public bool available;
        public string reason;
    }

    [Serializable]
    public class DogCommandAvailability
    {
        public float satiety;
        public float hydration;
        public float energy;
        public float mood;
        public float cleanliness;
        public bool dogReady;
        public bool ownerAvailable;
        public string ownerUnavailableReason;
        public bool ballAvailable;
        public bool lollipopAvailable;
        public bool pounceClear;
        public bool burstClear;
        public bool houseAvailable;
        public bool cushionAvailable;
        public bool foodAvailable, waterAvailable;
        public string foodReason, waterReason;
    }

    public static class DogCommandCatalog
    {
        public static DogCommandOption[] Build(DogCommandAvailability state, DogSceneObservation observation = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new List<DogCommandOption>();
            string busy = state.dogReady ? "" : "先等麦粒完成当前动作并站稳。";
            string effort = EffortBlockReason(state);

            result.Add(Option("sit", "none", "坐下", busy, "posture", "calm", "training"));
            result.Add(Option("crouch", "none", "短暂趴下，也能恢复体力；想歇一会儿应选 rest", busy, "posture", "calm", "training", "rest"));
            result.Add(Option("spin", "none", "转一圈", First(busy, effort), "movement", "spin", "training"));
            result.Add(Option("come", "none", "走到主人身边", First(busy,
                state.ownerAvailable ? "" : state.ownerUnavailableReason ?? "当前找不到主人的位置。"), "attention", "approach", "owner"));
            result.Add(Option("paw", "none", "抬起右前爪", busy, "gesture", "paw", "training"));
            result.Add(Option("bow", "none", "伏身邀玩（旧口令拜一拜），前身伏低后等待主人回应", First(busy, effort), "play", "bow", "gesture", "invitation"));
            result.Add(Option("invite", "none", "伏身邀玩后看向主人等待回应", First(busy, effort), "play", "invite", "wait"));
            result.Add(Option("pounce", "none", "向前轻扑", First(busy, effort,
                state.pounceClear ? "" : "前方空间不足，不能轻扑。"), "play", "pounce", "clear_space"));
            result.Add(Option("burst", "none", "沿空旷弧线兴奋地跑一小段", First(busy, effort,
                state.burstClear ? "" : "附近没有可安全跑动的路线。"), "play", "run", "clear_space"));
            result.Add(Option("sniff", "none", "低头闻一闻", busy, "explore", "sniff", "calm"));
            result.Add(Option("fetch_toy", "none", "叼回玩具球", First(busy, effort,
                state.ownerAvailable ? "" : state.ownerUnavailableReason ?? "当前找不到主人的位置。",
                state.ballAvailable ? "" : "玩具球暂不可取，先放下并等它落稳。"), "toy", "ball", "fetch"));
            result.Add(Option("fetch_lollipop", "none", "叼回棒棒糖毛绒玩具", First(busy, effort,
                state.ownerAvailable ? "" : state.ownerUnavailableReason ?? "当前找不到主人的位置。",
                state.lollipopAvailable ? "" : "毛绒玩具暂不可取，先放下并等它落稳。"), "toy", "plush", "fetch"));
            result.Add(Option("eat", "none", "走到当前饭碗位置吃饭", First(busy,
                state.foodAvailable ? "" : state.foodReason ?? "饭碗目前不可用。"), "care", "food", "bowl"));
            result.Add(Option("drink", "none", "走到当前水碗位置喝水", First(busy,
                state.waterAvailable ? "" : state.waterReason ?? "水碗目前不可用。"), "care", "water", "bowl"));
            result.Add(Option("rest", "none", "就地趴一会儿、歇一会儿，恢复体力并记录休息", busy, "rest", "calm", "care"));
            result.Add(Option("rest", "house", "走进狗屋，转身后休息", First(busy,
                state.houseAvailable ? "" : "当前场景没有可用的狗屋休息点。"), "rest", "house", "care"));
            result.Add(Option("rest", "cushion", "走到软垫上休息", First(busy,
                state.cushionAvailable ? "" : "当前场景没有可用的软垫休息点。"), "rest", "cushion", "care"));
            result.Add(Option("pet", "none", "探头并摇尾，回应亲近", First(busy,
                state.ownerAvailable ? "" : state.ownerUnavailableReason ?? "当前找不到主人的位置。"), "attention", "affection", "head"));
            result.Add(Option("refuse", "none", "疑惑地歪头看看主人", busy, "expression", "confused", "calm"));

            foreach (DogMicroAction action in DogCompanion.MicroActions)
            {
                string target = MicroTarget(action.Id);
                DogPerceptionCandidate candidate = FindCandidate(observation, action.Id, target);
                string unavailable = candidate != null ? "" : "当前姿态或视野不适合这个微动作。";
                var tags = new string[action.Tags.Count];
                for (int i = 0; i < tags.Length; i++) tags[i] = action.Tags[i];
                result.Add(Option(action.Id, target, action.Label, First(busy, unavailable), tags));
            }
            return result.ToArray();
        }

        public static DogCommandOption Find(DogCommandOption[] catalog, string id, string target)
        {
            if (catalog == null || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(target)) return null;
            foreach (DogCommandOption option in catalog)
                if (option != null && option.id == id && option.target == target) return option;
            return null;
        }

        public static bool CanExecute(DogCommandOption[] catalog, string id, string target, out string reason)
        {
            DogCommandOption option = Find(catalog, id, target);
            if (option == null)
            {
                reason = "这个动作或目标没有注册。";
                return false;
            }
            reason = option.reason ?? "";
            return option.available;
        }

        public static bool IsMicroAction(string id)
        {
            foreach (DogMicroAction action in DogCompanion.MicroActions)
                if (action.Id == id) return true;
            return false;
        }

        private static DogCommandOption Option(string id, string target, string label, string blocked, params string[] tags) =>
            new DogCommandOption
            {
                id = id,
                target = target,
                label = label,
                tags = tags,
                available = string.IsNullOrEmpty(blocked),
                reason = blocked ?? ""
            };

        private static string EffortBlockReason(DogCommandAvailability state)
        {
            if (state.energy < 15f) return "麦粒很累，先让它休息一会儿。";
            if (state.hydration < 15f) return "麦粒很渴，先给它一点清水。";
            if (state.satiety < 15f) return "麦粒很饿，想先吃一点东西再玩。";
            return "";
        }

        private static string First(params string[] reasons)
        {
            foreach (string reason in reasons)
                if (!string.IsNullOrEmpty(reason)) return reason;
            return "";
        }

        private static string MicroTarget(string id)
        {
            if (id == "gaze_exchange") return "ball";
            if (id == "head_turn" || id == "head_tilt" || id == "chest_lean") return "owner";
            return "none";
        }

        private static DogPerceptionCandidate FindCandidate(DogSceneObservation observation, string id, string target)
        {
            if (observation == null || observation.candidates == null) return null;
            foreach (DogPerceptionCandidate candidate in observation.candidates)
                if (candidate != null && candidate.id == id && candidate.targetId == target) return candidate;
            return null;
        }
    }
}
