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
public static class Dog1PhysicalPropsValidation
{
    private const string Session = "Dog1PhysicalPropsValidation";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool running;
    private static double deadline;

    static Dog1PhysicalPropsValidation() => EditorApplication.update += Tick;

    public static void Run()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "CityDog-prop-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES", scratch);
        Environment.SetEnvironmentVariable("KEEP_A_DOG_DISABLE_XR", "1");
        SessionState.SetBool(Session, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Dog1.unity");
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Session, false)) return;
        if (deadline == 0d) deadline = EditorApplication.timeSinceStartup + 60d;
        if (EditorApplication.timeSinceStartup > deadline)
        {
            Finish(new TimeoutException("Dog1 toy validation timed out."));
            return;
        }
        if (!EditorApplication.isPlaying || running) return;
        var trainer = UnityEngine.Object.FindObjectOfType<Dog1Training>();
        if (trainer == null || trainer.dog == null || !trainer.dog.Ready) return;
        running = true;
        trainer.StartCoroutine(Guard(Check(trainer)));
    }

    private static IEnumerator Guard(IEnumerator routine)
    {
        while (true)
        {
            object wait;
            try
            {
                if (!routine.MoveNext()) break;
                wait = routine.Current;
            }
            catch (Exception exception)
            {
                Finish(exception);
                yield break;
            }
            yield return wait;
        }
        Finish(null);
    }

    private static IEnumerator Check(Dog1Training trainer)
    {
        Invoke(trainer, "BeginPlay");
        trainer.dog.SetWanderingEnabled(false);
        var props = CheckScene(trainer, "小布骨");
        var toy = (Component)props.GetValue(0);
        var body = toy.GetComponent<Rigidbody>();
        var type = toy.GetType().GetProperty("Holder", Private).PropertyType;
        object right = Enum.Parse(type, "RightHand");
        object left = Enum.Parse(type, "LeftHand");
        object dog = Enum.Parse(type, "Dog");

        Vector3 home = body.position;
        var rightGrip = Field(trainer, "rightToyGrip");
        Invoke(trainer, "UpdateGrabButtons", true, rightGrip, home, Quaternion.identity, false, true);
        Require(Equals(toy.GetType().GetProperty("Holder", Private).GetValue(toy), right) &&
            (bool)Invoke(trainer, "PropBlocksUi", true), "the trigger grabs the ball and owns the input");
        rightGrip.GetType().GetField("Velocity").SetValue(rightGrip, Vector3.forward * 3f);
        rightGrip.GetType().GetField("PreviousPosition").SetValue(rightGrip, home);
        Invoke(trainer, "UpdateGrabButtons", true, rightGrip, home, Quaternion.identity, false, false);
        Require(body.velocity.z > 2.9f && (bool)Invoke(trainer, "PropBlocksUi", true),
            "trigger release throws the ball without clicking the menu");
        Invoke(toy, "SetHome", home, Quaternion.identity);
        Invoke(trainer, "UpdateGrabButtons", true, rightGrip, home, Quaternion.identity, false, false);
        Require(!(bool)Invoke(trainer, "PropBlocksUi", true), "the UI unlocks after a completed trigger release");

        Invoke(trainer, "UpdateGrabButtons", true, rightGrip, home, Quaternion.identity, false, true);
        Invoke(trainer, "ResetGrip", rightGrip, true);
        rightGrip.GetType().GetField("TriggerPressed").SetValue(rightGrip, true);
        Invoke(trainer, "UpdateGrabButtons", true, rightGrip, home, Quaternion.identity, false, true);
        Require(Field(rightGrip, "Toy") == null && (bool)Invoke(trainer, "PropBlocksUi", true),
            "a held trigger after pause or tracking reset cannot grab again or press the menu");
        Invoke(trainer, "UpdateGrabButtons", true, rightGrip, home, Quaternion.identity, false, false);
        Require((bool)Invoke(trainer, "PropBlocksUi", true), "the reset trigger release is still consumed by the prop");
        Invoke(trainer, "UpdateGrabButtons", true, rightGrip, home, Quaternion.identity, false, false);
        Require(!(bool)Invoke(trainer, "PropBlocksUi", true), "a fresh menu press is allowed after the reset trigger release");
        Invoke(toy, "SetHome", home, Quaternion.identity);

        Invoke(trainer, "UseGripPress", true, rightGrip, toy, home, Quaternion.identity);
        Require(Equals(toy.GetType().GetProperty("Holder", Private).GetValue(toy), right) &&
            !(bool)Field(trainer, "xrInteract"), "right grip takes the toy without also using the fixture");
        Invoke(trainer, "DropGrip", rightGrip, false);
        Invoke(trainer, "UseGripPress", true, rightGrip, null, home, Quaternion.identity);
        Require((bool)Field(trainer, "xrInteract"), "an empty right grip falls back to the aimed fixture");
        Invoke(toy, "SetHome", home, Quaternion.identity);
        Require((bool)Invoke(toy, "TryHold", right) && body.isKinematic, "right grip owns one dynamic toy");
        Require(!(bool)Invoke(toy, "TryHold", left) && !(bool)Invoke(toy, "TryHold", dog),
            "second hand and dog cannot take a toy already held in a hand");
        Invoke(toy, "SetGripPose", home + Vector3.up * .25f, Quaternion.identity);
        for (int i = 0; i < 7; i++) yield return new WaitForFixedUpdate();
        Require(body.position.y > home.y + .19f, "fixed-step grip follows a tracked hand upward");

        Invoke(toy, "Drop", Vector3.forward * 8f);
        float releaseZ = body.position.z;
        Require(!body.isKinematic && Mathf.Abs(body.velocity.magnitude - 4.2f) < .01f,
            "release returns a bounded throw to collision physics");
        for (int i = 0; i < 5; i++) yield return new WaitForFixedUpdate();
        Require(body.position.z > releaseZ + .19f && body.velocity.y < 0f,
            "released toy has horizontal travel and gravity");
        Invoke(toy, "SetHome", home, Quaternion.identity);
        Require((bool)Invoke(toy, "TryHold", dog) && body.isKinematic, "dog can take a released toy");
        Require(!(bool)Invoke(toy, "TryHold", right), "a hand cannot take a toy from the dog's mouth");
        Invoke(toy, "ReleaseFromDog", home, Quaternion.identity);
        Require(!body.isKinematic && body.velocity.sqrMagnitude < .001f, "dog puts toy down without adding throw velocity");

        var world = Field(trainer, "world");
        // The test drives tracked input directly. Skip the desktop update that clears headset grips.
        trainer.enabled = false;
        foreach (string name in new[] { "FoodBowl", "WaterBowl" })
        {
            var bowl = (Component)world.GetType().GetProperty(name, Private).GetValue(world);
            Vector3 start = bowl.transform.position;
            var bowlBody = bowl.GetComponent<Rigidbody>();
            Quaternion tiltedHand = Quaternion.Euler(39f, 50f, 23f);
            Invoke(trainer, "UpdateGrabButtons", true, rightGrip, start, tiltedHand, false, true);
            Require(Equals(Field(rightGrip, "Toy"), bowl) && bowlBody.isKinematic,
                name + " is a reachable trigger target");
            Require(!(bool)bowl.GetType().GetProperty("CanFetch", Private).GetValue(bowl) &&
                !(bool)bowl.GetType().GetProperty("IsSettled", Private).GetValue(bowl),
                name + " held by a hand is unavailable for eating or fetching");
            Invoke(trainer, "UpdateGrabButtons", true, rightGrip, start + Vector3.up * .35f, Quaternion.identity, false, true);
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            Require(bowlBody.position.y > start.y + .25f && Vector3.Dot(bowl.transform.up, Vector3.up) > .99f,
                name + " follows the hand and stays level: start " + start.ToString("F3") + ", current " +
                bowlBody.position.ToString("F3") + ", up " + bowl.transform.up.ToString("F3"));
            string positionProperty = name == "FoodBowl" ? "FoodPosition" : "WaterPosition";
            Vector3 livePosition = (Vector3)world.GetType().GetProperty(positionProperty).GetValue(world);
            Require(Vector3.Distance(livePosition, bowl.transform.position) < .001f,
                name + " care station follows the bowl's actual moved position");
            rightGrip.GetType().GetField("Velocity").SetValue(rightGrip, Vector3.right * 4f);
            Vector3 dropped = bowlBody.position;
            Invoke(trainer, "UpdateGrabButtons", true, rightGrip, start, Quaternion.identity, false, false);
            for (int i = 0; i < 7; i++) yield return new WaitForFixedUpdate();
            Require(!bowlBody.isKinematic && bowlBody.position.y < dropped.y - .04f &&
                Vector3.ProjectOnPlane(bowlBody.position - dropped, Vector3.up).sqrMagnitude < .001f &&
                Vector3.Dot(bowl.transform.up, Vector3.up) > .99f,
                name + " drops vertically and refuses horizontal throwing velocity");
        }
        trainer.enabled = true;

        var life = Field(trainer, "life");
        object progress = Field(life, "data");
        progress.GetType().GetField("totalExperience").SetValue(progress, 285);
        progress.GetType().GetField("bones").SetValue(progress, 100);
        Invoke(trainer, "UseGripPress", true, rightGrip, toy, home, Quaternion.identity);
        Invoke(trainer, "Buy", DogLocation.Park);
        Require(Field(rightGrip, "Toy") == null, "switching scene releases the held toy");
        CheckScene(trainer, "软飞盘");
        Invoke(trainer, "Buy", DogLocation.Terrace);
        CheckScene(trainer, "云朵布偶");
    }

    private static Array CheckScene(Dog1Training trainer, string expectedToy)
    {
        var props = (Array)Field(trainer, "physicalProps");
        Require(props.Length == 3, "exactly three explicit grip targets in the neighborhood");
        for (int i = 0; i < props.Length; i++)
        {
            var toy = props.GetValue(i) as Component;
            Require(toy != null && toy.GetComponent<Rigidbody>() != null && toy.GetComponent<Collider>().enabled,
                "toy has an active collider and rigid body");
            string label = (string)toy.GetType().GetProperty("Label", Private).GetValue(toy);
            Require(label == (i == 0 ? "玩具球" : i == 1 ? "棒棒糖毛绒玩具" : expectedToy), "toy roster includes " + label);
        }
        return props;
    }

    private static object Field(object target, string name) =>
        target.GetType().GetField(name, Private | BindingFlags.Public).GetValue(target);

    private static object Invoke(object target, string name, params object[] values) =>
        target.GetType().GetMethod(name, Private | BindingFlags.Static).Invoke(target, values);

    private static void Require(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
        Debug.Log("DOG1_PROPS_CHECK: " + message);
    }

    private static void Finish(Exception exception)
    {
        SessionState.SetBool(Session, false);
        if (exception != null) Debug.LogException(exception);
        else Debug.Log("DOG1_PROPS_OK");
        EditorApplication.Exit(exception != null ? 1 : 0);
    }
}
#endif
