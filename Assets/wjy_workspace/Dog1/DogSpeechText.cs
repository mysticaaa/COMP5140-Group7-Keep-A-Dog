using System.Text.RegularExpressions;

namespace Wjy.DogTraining
{
    internal static class DogSpeechText
    {
        public static string Normalize(string text)
        {
            string value = text.Replace("麥", "麦").Replace("請", "请").Replace("從", "从")
                .Replace("來", "来").Replace("後", "后").Replace("腳", "脚").Replace("轉", "转")
                .Replace("開", "开").Replace("難", "难").Replace("傷", "伤").Replace("顆", "颗")
                .Replace("這", "这").Replace("給", "给").Replace("讓", "让").Replace("還", "还");
            value = Regex.Replace(value.Trim(), @"^麦[粒力丽莉][，,。！!\s]*", "");
            return Regex.Replace(value, @"抬(起)?(你的|妳的)?右(手|脚|爪)", "抬右爪").Trim();
        }
    }
}
