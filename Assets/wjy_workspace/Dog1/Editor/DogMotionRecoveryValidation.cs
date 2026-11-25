#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wjy.DogTraining;

[InitializeOnLoad]
public static class DogMotionRecoveryValidation
{
    private const string Session="DogMotionRecoveryValidation";
    private static bool running;
    private static double deadline;
    private static string Output=>Environment.GetEnvironmentVariable("CITYDOG_MOTION_OUTPUT")??Path.GetFullPath("Logs/MotionRecovery");
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly List<string> report=new List<string>();
    static DogMotionRecoveryValidation(){EditorApplication.update+=Tick;}
    public static void Run()
    {
        Directory.CreateDirectory(Output);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool(Session,true);EditorApplication.isPlaying=true;
    }
    private static void Tick()
    {
        if(!SessionState.GetBool(Session,false)||!EditorApplication.isPlaying)return;
        if(deadline==0)deadline=EditorApplication.timeSinceStartup+180;
        if(EditorApplication.timeSinceStartup>deadline){Finish(new TimeoutException("Motion recovery validation timed out"));return;}
        if(running)return;running=true;Time.captureFramerate=60;
        var camera=new GameObject("Test camera",typeof(Camera)).GetComponent<Camera>();camera.tag="MainCamera";
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.20f,.26f,.23f);
        camera.nearClipPlane=.05f;camera.farClipPlane=60;
        camera.orthographic=true;camera.orthographicSize=.85f;
        var light=new GameObject("Key light",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(45,-35,0);
        RenderSettings.ambientLight=new Color(.6f,.6f,.6f);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Clear test yard";
        floor.GetComponent<Renderer>().sharedMaterial=new Material(Shader.Find("Standard")){color=new Color(.48f,.57f,.49f)};
        var dog=new GameObject("Restored German Shepherd").AddComponent<DogCompanion>();dog.StartCoroutine(Guard(Check(dog,camera)));
    }
    private static IEnumerator Guard(IEnumerator root)
    {
        var pending=new Stack<IEnumerator>();pending.Push(root);
        while(pending.Count>0)
        {
            object current=null;bool more=false;Exception error=null;
            try{more=pending.Peek().MoveNext();if(more)current=pending.Peek().Current;}catch(Exception e){error=e;}
            if(error!=null){Finish(error);yield break;}
            if(!more){pending.Pop();continue;}
            if(current is IEnumerator child)pending.Push(child);else yield return current;
        }
        Finish(null);
    }
    private static Transform Bone(DogCompanion dog,string role)=>(Transform)typeof(DogCompanion).GetMethod("Bone",Private).Invoke(dog,new object[]{role});
    private static IEnumerator Check(DogCompanion dog,Camera camera)
    {
        Require(dog.Ready,"original skeleton loaded");dog.SetWanderingEnabled(false);
        var owner=new GameObject("Owner eye target").transform;owner.position=new Vector3(1.1f,1.65f,3.1f);dog.user=owner;
        dog.HasPlayClearance=(point,radius)=>true;
        var scales=new Dictionary<Transform,Vector3>();var positions=new Dictionary<Transform,Vector3>();
        foreach(var joint in dog.VisualRoot.GetComponentsInChildren<Transform>())if(joint.name.StartsWith("DEF-")){scales.Add(joint,joint.localScale);positions.Add(joint,joint.localPosition);}
        var paws=new[]{Bone(dog,"DEF-front_toes.L"),Bone(dog,"DEF-front_toes.R"),Bone(dog,"DEF-hind_paw.L"),Bone(dog,"DEF-hind_paw.R")};
        string[] actions={"sit","crouch","spin","paw","bow","invite","sniff","pounce","burst","pet","rest","refuse","come","fetch_toy","fetch_lollipop"};
        foreach(string action in actions)
        {
            dog.SetPracticeArea(Vector3.zero,new Vector3(1.5f,.11f,1.4f),new Vector3(-1.5f,.11f,1.6f));dog.transform.rotation=Quaternion.identity;dog.ResetCompanion();
            yield return new WaitForSeconds(.7f);
            var runner=dog.StartCoroutine(dog.PerformAction(action,.15f));
            int frame=0;bool sampled=false;float start=Time.time;
            while(dog.IsPerforming&&Time.time-start<22f)
            {
                yield return null;frame++;
                foreach(var joint in scales.Keys)
                {
                    Require((joint.localScale-scales[joint]).sqrMagnitude<1e-8f,"no bone scaling: "+action,false);
                    Require((joint.localPosition-positions[joint]).sqrMagnitude<1e-8f,"fixed joint length: "+action,false);
                    Require(!float.IsNaN(joint.rotation.x),"finite pose: "+action,false);
                }
                if(frame==48)
                {
                    sampled=true;
                    if(action=="sit")Require(paws[0].position.y<.09f&&paws[1].position.y<.09f,"sit forepaws supported");
                    if(action=="paw")Require(paws[1].position.y-paws[0].position.y>.085f,"right paw raised with stable supporting paw");
                    if(action=="bow")Require(Bone(dog,"DEF-chest").position.y+.035f<Bone(dog,"DEF-pelvis").position.y,"play bow lowers front, keeps rump up");
                    if(action=="crouch")Require(Bone(dog,"DEF-chest").position.y<.34f&&Bone(dog,"DEF-pelvis").position.y<.34f,"crouch lowers torso");
                    if(action=="refuse")
                    {
                        Transform question = dog.transform.Find("Puzzled question mark");
                        Require(question != null && question.gameObject.activeInHierarchy &&
                            dog.GetComponentInChildren<ParticleSystem>(true) == null, "puzzled head tilt uses a spatial question mark");
                    }
                    Capture(camera,dog,action);
                }
            }
            Require(!dog.IsPerforming&&sampled,"action completed: "+action);
            if(action.StartsWith("fetch"))Require(dog.LastFetchSucceeded,"physical fetch succeeded: "+action);
            yield return new WaitForSeconds(1.3f);
            foreach(var paw in paws)Require(paw.position.y<.09f,"paw grounded after "+action,false);
        }
        dog.SetPracticeArea(Vector3.zero,new Vector3(1.5f,.11f,1.4f),new Vector3(-1.5f,.11f,1.6f));dog.transform.rotation=Quaternion.identity;dog.ResetCompanion();
        yield return new WaitForSeconds(1f);
        var before=Array.ConvertAll(paws,p=>p.position);var root=dog.transform.position;
        dog.AttendToUser(3f);
        yield return new WaitForSeconds(.9f);
        float tilt=(float)typeof(DogCompanion).GetField("socialTilt",Private).GetValue(dog);
        Require(Mathf.Abs(tilt)>12f&&Mathf.Abs(tilt)<17f,"attentive head tilt visibly reaches 12–17 degrees");
        for(int i=0;i<paws.Length;i++)Require(Vector3.Distance(before[i],paws[i].position)<.025f,"attention keeps paw contacts",false);
        Require(Vector3.Distance(root,dog.transform.position)<.001f,"attention does not turn or translate root");
        Capture(camera,dog,"attentive-tilt");
        dog.ShowKeypoints=true;
        var overlay=dog.GetComponent<DogKeypointOverlay>();typeof(DogKeypointOverlay).GetMethod("BuildWorldLinks",Private).Invoke(overlay,null);
        Require(dog.GetComponentsInChildren<LineRenderer>().Length>20,"stereo bone links constructed");
        foreach(var line in dog.GetComponentsInChildren<LineRenderer>())
        {
            string name=line.gameObject.name;if(!name.StartsWith("Stereo bone "))continue;
            var segments=name.Substring(12).Split(new[]{" -> "},StringSplitOptions.None);
            Transform from=null,to=null;foreach(var bone in dog.GetComponentsInChildren<Transform>()){if(bone.name==segments[0])from=bone;if(bone.name==segments[1])to=bone;}
            if(from!=null&&to!=null){line.SetPosition(0,from.position);line.SetPosition(1,to.position);}
        }
        Capture(camera,dog,"attentive-skeleton");dog.ShowKeypoints=false;
        yield return CheckMicroActions(dog, camera, owner, paws);
        yield return CheckAutonomousExploration(dog, camera, owner);
        // Verify that all selectable rigs really have the roles required by the shared solver.
        var breedType=typeof(DogCompanion).Assembly.GetType("Wjy.DogTraining.DogBreed");
        var breeds=(Array)breedType.GetField("Options",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        foreach(var breed in breeds)
        {
            var go=new GameObject("Rig audit");go.SetActive(false);var variant=go.AddComponent<DogCompanion>();
            typeof(DogCompanion).GetMethod("SetBreedBeforeActivation",Private).Invoke(variant,new[]{breed});go.SetActive(true);
            Require(variant.Ready,"breed initialized: "+breedType.GetField("Id",Private|BindingFlags.Public).GetValue(breed));
            foreach(string role in new[]{"DEF-head","DEF-pelvis","DEF-chest","DEF-spine.01","DEF-spine.02","DEF-neck.01","DEF-neck.02","DEF-front_toes.L","DEF-front_toes.R","DEF-hind_paw.L","DEF-hind_paw.R"})Require(Bone(variant,role)!=null,"mapped role "+role,false);
            UnityEngine.Object.Destroy(go);yield return null;
        }
    }
    private static IEnumerator CheckMicroActions(DogCompanion dog, Camera camera, Transform owner, Transform[] paws)
    {
        var ids = new HashSet<string>();
        var head = Bone(dog, "DEF-head");
        var chest = Bone(dog, "DEF-chest");
        var leftEar = Bone(dog, "DEF-ear.01.L");
        var rightEar = Bone(dog, "DEF-ear.01.R");
        string[] poseRoles = { "DEF-head", "DEF-chest", "DEF-ear.01.L", "DEF-ear.01.R" };
        var poseJoints = new[] { head, chest, leftEar, rightEar };
        Require(!dog.TryPlayMicroAction("unknown_micro_action"), "unknown tagged action rejected");
        foreach (var action in DogCompanion.MicroActions)
        {
            Require(ids.Add(action.Id) && action.Tags.Count > 0, "unique tagged action: " + action.Id);
            dog.ResetCompanion();
            owner.position = action.Id == "gaze_exchange" ? new Vector3(1.1f, 1.65f, 3.1f) : new Vector3(0f, 1.4f, 2.6f);
            yield return new WaitForSeconds(.45f);
            var prior = Array.ConvertAll(poseJoints, joint => joint.rotation);
            var plantedAt = Array.ConvertAll(paws, paw => paw.position);
            Vector3 root = dog.transform.position;
            Quaternion facing = dog.transform.rotation;
            var peak = new float[poseRoles.Length];
            float peakYaw = 0f, peakRoll = 0f;
            Require(dog.TryPlayMicroAction(action.Id), "play " + action.Id);
            Require(!dog.TryPlayMicroAction(action.Id), "concurrent micro-action rejected", false);
            float started = Time.time;
            bool grooming = action.Id == "groom_paw";
            float captureTime = grooming ? 1.65f :
                action.Id == "ear_flick" || action.Id == "ear_perk" ? .38f : .95f;
            bool captured = false;
            while (dog.IsPlayingMicroAction && Time.time - started < action.Duration + .08f)
            {
                yield return null;
                for (int i = 0; i < peak.Length; i++)
                    peak[i] = Mathf.Max(peak[i], Quaternion.Angle(prior[i], poseJoints[i].rotation));
                Quaternion delta = head.rotation * Quaternion.Inverse(prior[0]);
                peakYaw = Mathf.Max(peakYaw, Mathf.Abs(Vector3.SignedAngle(dog.transform.forward,
                    Vector3.ProjectOnPlane(delta * dog.transform.forward, Vector3.up), Vector3.up)));
                peakRoll = Mathf.Max(peakRoll, Mathf.Abs(Vector3.SignedAngle(Vector3.up,
                    Vector3.ProjectOnPlane(delta * Vector3.up, dog.transform.forward), dog.transform.forward)));
                for (int i = 0; i < paws.Length; i++)
                {
                    if (!grooming)
                        Require(Vector3.Distance(plantedAt[i], paws[i].position) < .032f, "micro-action keeps paws planted", false);
                    else Require(paws[i].position.y < .13f, "grooming keeps toes supported", false);
                }
                Require(Vector3.Distance(root, dog.transform.position) < .001f &&
                    Quaternion.Angle(facing, dog.transform.rotation) < .05f, "micro-action preserves root", false);
                if (!captured && Time.time - started > captureTime &&
                    (!grooming || dog.transform.Find("Grooming tongue")?.gameObject.activeSelf == true))
                {
                    Capture(camera, dog, "micro-" + action.Id);
                    captured = true;
                }
            }
            Require(captured && !dog.IsPlayingMicroAction, "micro-action finishes: " + action.Id);
            switch (action.Id)
            {
                case "head_turn": Require(peakYaw > 18f && peakRoll < 9f, "head turn has visible yaw"); break;
                case "head_tilt": Require(peakRoll > 13f && peakRoll < 22f, "head tilt has visible roll"); break;
                case "ear_perk":
                case "ear_flick": Require(Mathf.Max(peak[2], peak[3]) > 4f, "ear bones move: " + action.Id); break;
                case "chest_lean": Require(peak[1] > 2.5f, "chest follows head and neck"); break;
                default: Require(peak[0] > 3f, "head bones move: " + action.Id); break;
            }
        }
    }
    private static IEnumerator CheckAutonomousExploration(DogCompanion dog, Camera camera, Transform owner)
    {
        dog.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        dog.ResetCompanion();
        owner.position = new Vector3(1.7f, 1.65f, 1.6f);
        var pointType = typeof(DogCompanion).GetNestedType("ExplorationPoint", BindingFlags.NonPublic);
        Array points = Array.CreateInstance(pointType, 1);
        points.SetValue(Activator.CreateInstance(pointType, new object[] { "玩具", new Vector3(.35f, 0f, 2.4f) }), 0);
        typeof(DogCompanion).GetMethod("SetExplorationPoints", Private).Invoke(dog, new object[] { points });
        dog.SetWanderingEnabled(true);
        bool micro = false, exploring = false, arrived = false;
        for (float deadline = Time.time + 17f; Time.time < deadline && !arrived;)
        {
            yield return null;
            if (!micro && dog.IsPlayingMicroAction)
            {
                micro = true;
                Capture(camera, dog, "autonomous-micro");
            }
            exploring |= dog.StateLabel.StartsWith("去看看", StringComparison.Ordinal);
            arrived = dog.StateLabel.StartsWith("停下来看看", StringComparison.Ordinal);
        }
        Require(micro, "quiet posture produced a micro-action without input");
        Require(exploring && arrived && dog.transform.position.z > 1.1f, "walked toward a named landmark and stopped to inspect");
        Capture(camera, dog, "autonomous-arrival");
        dog.SetWanderingEnabled(false);
    }
    private static void Capture(Camera camera,DogCompanion dog,string name)
    {
        camera.transform.position=dog.transform.position+new Vector3(2.35f,1.3f,2.8f);camera.transform.LookAt(dog.transform.position+Vector3.up*.46f);
        var target=new RenderTexture(1200,900,24);camera.targetTexture=target;camera.Render();var old=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(1200,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1200,900),0,0);image.Apply();
        File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());RenderTexture.active=old;camera.targetTexture=null;
        UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(target);
    }
    private static void Require(bool ok,string label,bool log=true)
    {if(!ok)throw new Exception(label);if(log){report.Add(label);Debug.Log("DOG_RECOVERY_CHECK: "+label);}}
    private static void Finish(Exception error)
    {
        SessionState.SetBool(Session,false);Time.captureFramerate=0;
        if(error!=null)Debug.LogException(error);
        report.Add(error==null?"DOG_MOTION_RECOVERY_OK":error.ToString());
        File.WriteAllLines(Path.Combine(Output,"result.txt"),report);EditorApplication.Exit(error==null?0:1);
    }
}
#endif
