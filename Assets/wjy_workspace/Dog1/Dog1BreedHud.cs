using System.Collections.Generic;
using UnityEngine;

namespace Wjy.DogTraining
{
    public sealed partial class Dog1Training
    {
        private bool choosingBreed;
        private DogBreed selectedBreed;
        private string breedError;
        private float timeScaleBeforeBreedPicker;
        private readonly Dictionary<string, Texture2D> dogPortraits = new Dictionary<string, Texture2D>();

        private void OpenBreedPicker()
        {
            selectedBreed = dog?.Breed ?? DogBreed.Find(life.BreedId) ?? DogBreed.Options[0];
            breedError = null;
            choosingBreed = true;
            if (!started) return;

            timeScaleBeforeBreedPicker = Time.timeScale;
            Time.timeScale = 0f;
            if (view != null) view.InputSuspended = true;
        }

        private void CloseBreedPicker()
        {
            choosingBreed = false;
            breedError = null;
            if (!started) return;

            Time.timeScale = timeScaleBeforeBreedPicker;
            if (view != null) view.InputSuspended = pause || world.IsElevatorMoving;
        }

        private void DrawBreedPicker(float height)
        {
            selectedBreed ??= DogBreed.Find(life.BreedId) ?? DogBreed.Options[0];
            Box(new Rect(0, 0, hudWidth, height), new Color32(36, 48, 38, 165));
            const float width = 824f;
            const float panelHeight = 635f;
            var panel = new Rect((hudWidth - width) * 0.5f, (height - panelHeight) * 0.5f, width, panelHeight);
            Sheet(panel, true);

            GUI.Label(new Rect(panel.x + 30f, panel.y + 24f, 520f, 49f), started ? "更换狗狗模型" : "选择你的狗狗", title);
            const int columns = 4;
            const float gap = 12f;
            float tileWidth = (width - 60f - (columns - 1) * gap) / columns;
            for (int i = 0; i < DogBreed.Options.Length; i++)
            {
                DogBreed breed = DogBreed.Options[i];
                var tile = new Rect(panel.x + 30f + (i % columns) * (tileWidth + gap),
                    panel.y + 91f + i / columns * 153f, tileWidth, 139f);
                DrawBreedTile(tile, breed);
            }

            if (!string.IsNullOrEmpty(breedError))
                GUI.Label(new Rect(panel.x + 30f, panel.yMax - 70f, 330f, 48f), breedError, small);

            if (choosingBreed &&
                GUI.Button(new Rect(panel.xMax - 327f, panel.yMax - 63f, 102f, 38f), "返回", button))
            {
                CloseBreedPicker();
                return;
            }
            if (GUI.Button(new Rect(panel.xMax - 212f, panel.yMax - 63f, 182f, 38f),
                    started ? "确认更换" : "带它回家", primary) &&
                ConfirmBreed(selectedBreed))
            {
                CloseBreedPicker();
                if (!started) BeginPlay();
            }
        }

        private void DrawBreedTile(Rect tile, DogBreed breed)
        {
            bool active = breed == selectedBreed;
            Box(tile, active ? Olive : RuleInk, 9f);
            var inner = new Rect(tile.x + 2f, tile.y + 2f, tile.width - 4f, tile.height - 4f);
            Box(inner, active ? new Color32(228, 232, 206, 255) : new Color32(250, 240, 218, 255), 7f);

            if (!dogPortraits.TryGetValue(breed.Id, out Texture2D portrait))
            {
                portrait = Resources.Load<Texture2D>("Dog1/BreedPortraits/" + breed.Id);
                dogPortraits.Add(breed.Id, portrait);
            }
            if (portrait != null)
                GUI.DrawTexture(new Rect(tile.x + 6f, tile.y + 6f, tile.width - 12f, 100f), portrait, ScaleMode.ScaleToFit);

            GUI.Label(new Rect(tile.x + 11f, tile.yMax - 28f, tile.width - 20f, 22f), breed.DisplayName, heading);
            if (GUI.Button(tile, GUIContent.none, GUIStyle.none))
            {
                selectedBreed = breed;
                breedError = null;
            }
        }
    }
}
