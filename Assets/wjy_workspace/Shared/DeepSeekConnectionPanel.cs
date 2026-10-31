using System;
using UnityEngine;

namespace Wjy.DogTraining
{
    public static class DeepSeekConnectionPresets
    {
        public const string PolyUUrl = "https://genai.comp.polyu.edu.hk/api/v1//chat/completions";
        public const string OfficialUrl = "https://api.deepseek.com/chat/completions";
        public const string PolyUModel = "DeepSeek-V4-Flash";
        public const string OfficialModel = "deepseek-flash";
        public static readonly string[] Urls = { PolyUUrl, OfficialUrl };
        public static readonly string[] Models = { PolyUModel, OfficialModel };
        public static readonly string[] ProviderNames = { "PolyU 校园接口", "DeepSeek 官方接口" };
        public static int FindUrl(string url) => Array.IndexOf(Urls, (url ?? "").Trim());
        public static int FindModel(string model) => Array.IndexOf(Models, (model ?? "").Trim());
        public static void SelectUrl(int index, ref string url, ref string model)
        {
            if (index < 0 || index >= Urls.Length) throw new ArgumentOutOfRangeException(nameof(index));
            url = Urls[index]; model = Models[index];
        }
        public static void EditUrl(string editedUrl, ref string url, ref string model)
        {
            if (string.Equals(url, editedUrl, StringComparison.Ordinal)) return;
            url = editedUrl;
            int index = FindUrl(url);
            if (index >= 0) model = Models[index];
        }
    }

    // A runtime IMGUI combo panel. Expanded options participate in layout, so they cannot
    // click through to training buttons or obscure fields on smaller Game views.
    public sealed class DeepSeekConnectionPanel
    {
        private bool urlExpanded, modelExpanded;
        private GUIStyle selector, option;
        public void Draw(ref string url, ref string model, ref string apiKey, GUIStyle label, GUIStyle field)
        {
            if (selector == null)
            {
                selector = new GUIStyle(GUI.skin.button)
                {
                    font = field.font, fontSize = field.fontSize, alignment = TextAnchor.MiddleLeft,
                    fixedHeight = Mathf.Max(30, field.fixedHeight), padding = new RectOffset(10, 10, 4, 4)
                };
                option = new GUIStyle(selector) { fontSize = Mathf.Max(13, field.fontSize - 2), wordWrap = true, fixedHeight = 50 };
            }
            GUILayout.Label("API Key（手动编辑）", label);
            apiKey = GUILayout.PasswordField(apiKey ?? "", '*', field);
            GUILayout.Label("URL（下拉选择，下方可手动编辑）", label);
            int urlIndex = DeepSeekConnectionPresets.FindUrl(url);
            string provider = urlIndex < 0 ? "当前为手动输入的 URL" : DeepSeekConnectionPresets.ProviderNames[urlIndex];
            if (GUILayout.Button(provider + (urlExpanded ? "  ▲" : "  ▼"), selector))
            { urlExpanded = !urlExpanded; modelExpanded = false; }
            if (urlExpanded)
            {
                for (int i = 0; i < DeepSeekConnectionPresets.Urls.Length; i++)
                {
                    string caption = DeepSeekConnectionPresets.ProviderNames[i] + "\n" + DeepSeekConnectionPresets.Urls[i];
                    if (GUILayout.Button(caption, option))
                    { DeepSeekConnectionPresets.SelectUrl(i, ref url, ref model); urlExpanded = false; }
                }
            }
            string editedUrl = GUILayout.TextField(url ?? "", field);
            DeepSeekConnectionPresets.EditUrl(editedUrl, ref url, ref model);
            GUILayout.Label("Model（随 URL 匹配，也可独立选择或编辑）", label);
            if (GUILayout.Button((string.IsNullOrEmpty(model) ? "选择模型" : model) + (modelExpanded ? "  ▲" : "  ▼"), selector))
            { modelExpanded = !modelExpanded; urlExpanded = false; }
            if (modelExpanded)
            {
                foreach (string preset in DeepSeekConnectionPresets.Models)
                    if (GUILayout.Button(preset, selector)) { model = preset; modelExpanded = false; }
            }
            // Do not re-apply the URL's model every frame: manual overrides must persist.
            model = GUILayout.TextField(model ?? "", field);
        }
    }
}