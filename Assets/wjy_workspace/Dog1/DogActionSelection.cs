using System;
using KeepADog.Demo;

namespace Wjy.DogTraining
{
    internal static class DogActionSelection
    {
        public static string MotionFor(DogAgentDecision decision)
        {
            if (decision.IsRefusal) return "refuse";
            switch (decision.Intent)
            {
                case DogIntent.Sit: return "sit";
                case DogIntent.Crouch: return "crouch";
                case DogIntent.Spin: return "spin";
                case DogIntent.Paw: return "paw";
                case DogIntent.Bow: return "bow";
                case DogIntent.Pounce: return "pounce";
                case DogIntent.Sniff: return "sniff";
                case DogIntent.Come:
                case DogIntent.Comfort: return "come";
                case DogIntent.Fetch when decision.Target == "lollipop": return "fetch_lollipop";
                case DogIntent.FetchBall when decision.Target == "ball": return "fetch_toy";
                default: return null;
            }
        }

        public static bool Succeeded(DogCompanion dog, string motion)
        {
            if (motion.StartsWith("fetch_", StringComparison.Ordinal)) return dog.LastFetchSucceeded;
            if (motion == "come") return dog.LastComeSucceeded;
            if (motion == "pounce") return dog.LastPlaySucceeded;
            return motion != "refuse";
        }
    }
}
