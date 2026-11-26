#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wjy.DogTraining;

[InitializeOnLoad]
public static class DogHouseRestValidation
{
    private const string Flag = "DogHouseRestValidation.Play";
    private static bool running;
    private static double deadline;

    static DogHouseRestValidation() => EditorApplication.update += Tick;

    public static void Run()
    {
        string saves = Path.Combine(Path.GetTempPath(), "DogHouseRestValidation-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES", saves);
        EditorSceneManager.OpenScene("Assets/Scenes/Dog1.unity");
        SessionState.SetBool(Flag, true);
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Flag, false) || !EditorApplication.isPlaying) return;
        if (deadline == 0d) deadline = EditorApplication.timeSinceStartup + 40d;
        if (EditorApplication.timeSinceStartup >= deadline)
        {
            Finish(new TimeoutException("Doghouse behavior did not finish."));
            return;
        }
        if (running) return;
        var trainer = UnityEngine.Object.FindObjectOfType<Dog1Training>();
        if (trainer == null || trainer.dog == null || !trainer.dog.Ready) return;
        running = true;
        Time.captureFramerate = 60;
        trainer.StartCoroutine(Guard(Check(trainer.dog)));
    }

    private static IEnumerator Guard(IEnumerator routine)
    {
        while (true)
        {
            bool advance;
            object current;
            try
            {
                advance = routine.MoveNext();
                current = advance ? routine.Current : null;
            }
            catch (Exception exception)
            {
                Finish(exception);
                yield break;
            }
            if (!advance) break;
            yield return current;
        }
        Finish(null);
    }

    private static IEnumerator Check(DogCompanion dog)
    {
        var layout = UnityEngine.Object.FindObjectOfType<DogApartmentLayout>();
        Require(layout != null, "Dog1 apartment exists");
        var getSpot = typeof(DogApartmentLayout).GetMethod("RestSpot", BindingFlags.Instance | BindingFlags.NonPublic);
        var house = (Transform)getSpot.Invoke(layout, new object[] { true });
        var cushion = (Transform)getSpot.Invoke(layout, new object[] { false });
        Require(house != null, "Serialized doghouse exists");

        dog.SetWanderingEnabled(false);
        dog.transform.SetPositionAndRotation(house.TransformPoint(new Vector3(.10f, 0f, -1.62f)),
            Quaternion.AngleAxis(43f, Vector3.up) * house.rotation);
        dog.ResetCompanion();

        var method = typeof(DogCompanion).GetMethod("RestAt", BindingFlags.Instance | BindingFlags.NonPublic);
        var bone = typeof(DogCompanion).GetMethod("Bone", BindingFlags.Instance | BindingFlags.NonPublic);
        var chest = (Transform)bone.Invoke(dog, new object[] { "DEF-chest" });
        dog.StartCoroutine((IEnumerator)method.Invoke(dog, new object[] { house, true, cushion }));
        Vector3 previous = dog.transform.position;
        bool entered = false, turned = false, rested = false, exited = false, showedSide = false;
        float lowestTranslationDot = 1f;
        for (float start = Time.time; dog.IsPerforming && Time.time - start < 24f;)
        {
            yield return null;
            Vector3 pos = dog.transform.position;
            Vector3 local = house.InverseTransformPoint(pos);
            Vector3 move = Vector3.ProjectOnPlane(pos - previous, Vector3.up);
            previous = pos;
            if (move.sqrMagnitude > .000016f)
                lowestTranslationDot = Mathf.Min(lowestTranslationDot, Vector3.Dot(move.normalized, dog.transform.forward));

            float heading = Vector3.Dot(dog.transform.forward, house.forward);
            if (!entered && dog.StateLabel == "走进狗窝" && local.z > -.67f && heading > .95f)
            {
                entered = true;
                Capture(house, "01-enter-forward");
            }
            if (dog.StateLabel == "在狗窝里转身" && local.z > -.40f && local.y > .105f)
            {
                turned = true;
                if (!showedSide && Mathf.Abs(heading) < .24f)
                {
                    showedSide = true;
                    Capture(house, "02-turn-inside");
                }
            }
            if (!rested && dog.StateLabel == "在狗窝里趴着休息" && heading < -.95f && local.z > -.40f)
            {
                rested = true;
                float standingChest = chest.position.y;
                yield return new WaitForSeconds(.85f);
                Require(dog.StateLabel == "在狗窝里趴着休息", "Rest held rather than immediately restarting the gait");
                Require(standingChest - chest.position.y > .12f, "Chest settles into the resting pose after the inside turn");
                Capture(house, "03-rest-facing-door");
            }
            if (!exited && dog.StateLabel == "起身回到客厅" && heading < -.95f && local.z < -.70f && move.sqrMagnitude > .000016f)
            {
                exited = true;
                Capture(house, "04-exit-forward");
            }
        }

        Require(entered && turned && showedSide && rested && exited, "Entry, inside turn, rest and exit all observed");
        Require(lowestTranslationDot > .90f, "No reverse translation during the action");
        Require(!dog.IsPerforming && dog.LastActivitySucceeded, "Rest completes and reports success");
        Debug.Log("DOG_HOUSE_DIRECTION: min forward dot=" + lowestTranslationDot.ToString("F3"));
    }

    private static void Capture(Transform house, string name)
    {
        string output = Environment.GetEnvironmentVariable("CITYDOG_HOUSE_OUTPUT");
        if (string.IsNullOrEmpty(output) || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        Directory.CreateDirectory(output);
        var cameraObject = new GameObject("Doghouse validation camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.transform.position = house.TransformPoint(new Vector3(.21f, .52f, -2.18f));
        camera.transform.LookAt(house.TransformPoint(new Vector3(0f, .43f, -.38f)));
        camera.fieldOfView = 53f;
        var texture = new RenderTexture(960, 720, 24);
        var image = new Texture2D(960, 720, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0f, 0f, image.width, image.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(image);
            UnityEngine.Object.Destroy(cameraObject);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Debug.Log("DOG_HOUSE_REST_CHECK: " + message);
    }

    private static void Finish(Exception failure)
    {
        SessionState.SetBool(Flag, false);
        Time.captureFramerate = 0;
        if (failure == null) Debug.Log("DOG_HOUSE_REST_OK");
        else Debug.LogException(failure);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
#endif
