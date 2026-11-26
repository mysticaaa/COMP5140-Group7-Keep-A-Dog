#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Wjy.DogTraining;

[InitializeOnLoad]
public static class Dog1HeadsetValidation
{
    private const string Key="Dog1HeadsetValidation";
    private static bool running;
    private static double timeout;
    private static string Output=>Environment.GetEnvironmentVariable("CITYDOG_QA_OUTPUT") ??
        Path.GetFullPath("Logs/Dog1HeadsetValidation");
    static Dog1HeadsetValidation(){EditorApplication.update+=Tick;}
    public static void Run()
    {
        Directory.CreateDirectory(Output);
        Environment.SetEnvironmentVariable("KEEP_A_DOG_DISABLE_XR","1");
        Environment.SetEnvironmentVariable("KEEP_A_DOG_TEST_SAVES",Path.Combine(Output,"saves"));
        SessionState.SetBool(Key,true);timeout=EditorApplication.timeSinceStartup+180;
        EditorSceneManager.OpenScene("Assets/Scenes/Dog1.unity");
        EditorApplication.isPlaying=true;
    }
    private static void Tick()
    {
        if(!SessionState.GetBool(Key,false))return;
        if(timeout==0)timeout=EditorApplication.timeSinceStartup+180;
        if(EditorApplication.timeSinceStartup>timeout&&timeout>0){Finish(new Exception("Headset test timed out"));return;}
        if(!EditorApplication.isPlaying||running)return;
        var trainer=UnityEngine.Object.FindObjectOfType<Dog1Training>();
        if(trainer==null||trainer.dog==null||!trainer.dog.Ready)return;
        running=true;trainer.StartCoroutine(Guard(Test(trainer)));
    }
    private static IEnumerator Guard(IEnumerator test)
    {
        while(true){object current;try{if(!test.MoveNext())break;current=test.Current;}catch(Exception e){Finish(e);yield break;}yield return current;}
        Finish(null);
    }
    private static object Get(object target,string name)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
    private static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
    private static object Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);
    private static void Require(bool ok,string message){if(!ok)throw new Exception(message);Debug.Log("DOG1_XR_CHECK: "+message);}
    private static IEnumerator Test(Dog1Training trainer)
    {
        var rig=trainer.HeadsetRig;
        Require(rig!=null&&rig.SeparateHeadCamera,"separate headset camera exists");
        var desktop=Camera.main;
        Require(desktop!=rig.HeadCamera&&desktop.enabled&&!rig.HeadCamera.enabled,"desktop camera remains active without headset");
        Require(trainer.GetComponents<Dog2VrTraining>().Length==0,"one Dog1 simulation, no duplicate Dog2 trainer");
        Call(trainer,"BeginPlay");Set(trainer,"nextAutonomy",float.MaxValue);
        Set(trainer, "sessionKey", "");
        Set(trainer, "apiKey", "");
        Require(trainer.dog.VisualRoot.name == "border-collie - rigged", "fresh Dog1 uses the existing Border Collie model");
        trainer.HeadsetCanvas.gameObject.SetActive(true);
        rig.HeadCamera.enabled=true; desktop.enabled=false;
        Call(trainer,"BringHeadsetMenu");
        foreach(var pointer in trainer.GetComponentsInChildren<Dog2ControllerRay>())pointer.ManualInputForValidation=true;
        var ray=trainer.GetComponentInChildren<Dog2ControllerRay>();
        yield return null;
        rig.HeadCamera.Render();
        Click(trainer,ray,"故事");yield return null;
        Call(trainer,"RefreshHeadsetText");
        Require(trainer.HeadsetCanvas.GetComponentsInChildren<Text>().Any(label => label.text.Contains("到家的第一晚")),
            "headset story shows the current chapter and objectives");
        Call(trainer,"OpenTrickReward");Set(trainer,"pendingInvitation",true);
        Call(trainer,"RefreshHeadsetText");yield return null;
        Click(trainer,ray,"等一会儿再玩");
        Require(!(bool)Get(trainer,"pendingInvitation"),"story choice responds to the dog's invitation");
        Call(trainer,"RefreshHeadsetText");
        Require(trainer.HeadsetCanvas.GetComponentsInChildren<Text>().Any(label => label.text.Contains("先自己待一会儿")),
            "story choice shows its response immediately even while a notice is active");
        Set(trainer,"nextAutonomy",float.MaxValue);
        Click(trainer,ray,"训练");
        yield return null;
        Require((int)Get(trainer,"xrTab")==2,"controller ray navigates training tab");
        Click(trainer,ray,"坐下");
        Require((bool)Get(trainer,"busy"),"controller ray starts actual Agent training");
        float deadline=Time.realtimeSinceStartup+20;
        while((bool)Get(trainer,"busy")&&Time.realtimeSinceStartup<deadline)yield return null;
        Require(!(bool)Get(trainer,"busy"),"Agent action completed");
        Click(trainer,ray,"照料");yield return null;
        Require(trainer.HeadsetCanvas.GetComponentsInChildren<Button>().Length>=15,"care controls rendered in world space");
        Click(trainer,ray,"出门");yield return null;
        Require((int)Get(trainer,"xrTab")==3,"scene menu available");
        Click(trainer,ray,"犬种");yield return null;
        Require(trainer.HeadsetCanvas.GetComponentsInChildren<Text>().Length>10,"breed menu rendered");
        Click(trainer,ray,"故事");yield return null;
        Click(trainer,ray,"暂停");yield return null;
        Require(Time.timeScale==0,"headset pause applies");
        Click(trainer,ray,"继续陪伴");yield return null;
        Require(Time.timeScale==1,"headset resume works while paused");
        Click(trainer,ray,"动作");yield return null;
        Click(trainer,ray,"歪头倾听");yield return null;
        float microDeadline=Time.realtimeSinceStartup+3f;
        while(!trainer.dog.IsPlayingMicroAction&&Time.realtimeSinceStartup<microDeadline)yield return null;
        Require(trainer.dog.IsPlayingMicroAction,"headset action page plays head tilt");
        while((bool)Get(trainer,"busy"))yield return null;
        Require(trainer.HeadsetCanvas.GetComponentsInChildren<Button>().Any(button => button.gameObject.name == "连播全部动作"),
            "developer action browser offers sequential playback");
        DogPerceptionValidation.Validate(trainer.dog);
        Require(ray.ClickCount>=8,"geometric controller press and release dispatches clicks");
        // A real cabin movement must add its delta without cancelling physical head bob.
        var lift=UnityEngine.Object.FindObjectOfType<DogApartmentLift>();
        if(lift!=null)
        {
            var cabin=(Transform)Get(lift,"cabin");
            var pose=new GameObject("Tracked lift test pose").transform;pose.position=rig.HeadCamera.transform.position;
            Call(lift,"StartRide",false,true);
            float before=cabin.position.y;float eye=pose.position.y;
            Call(lift,"Tick",pose,false,false,.3f,true);
            Require(Mathf.Abs(pose.position.y-eye-(cabin.position.y-before))<.001f,"lift carries XR origin by cabin delta");
            pose.position+=Vector3.up*.08f;eye=pose.position.y;before=cabin.position.y;
            Call(lift,"Tick",pose,false,false,.3f,true);
            Require(Mathf.Abs(pose.position.y-eye-(cabin.position.y-before))<.001f,"physical head movement preserved in moving lift");
            Call(lift,"Tick",pose,false,false,10f,true);Call(lift,"StartRide",true,false);Call(lift,"Tick",pose,false,false,10f,true);
            UnityEngine.Object.Destroy(pose.gameObject);
        }
        var speech=trainer.GetComponent<Dog2SpeechInput>();
        Require(!speech.PreferHeadsetMicrophone,"desktop does not force stale Quest microphone");
        float voiceDeadline=Time.realtimeSinceStartup+45;
        while(!(bool)Get(speech,"serviceReady")&&Time.realtimeSinceStartup<voiceDeadline)yield return null;
        Require((bool)Get(speech,"serviceReady"),"local speech service prewarmed and verified");
        byte[] wav=File.ReadAllBytes(Path.Combine(Application.dataPath,"wjy_workspace/Dog2/SpeechService/Tests~/sit.wav"));
        var transcribe=(IEnumerator)Call(speech,"Transcribe",wav,(int)Get(speech,"session"));
        yield return trainer.StartCoroutine(transcribe);
        Require(!string.IsNullOrEmpty(speech.LiveText)&&speech.LiveText.Contains("坐"),"real Chinese WAV transcribes through Unity HTTP client");
        Require(((string)Get(trainer,"caption")).Contains("坐"),"speech transcript reaches same Dog1 Agent");
        while((bool)Get(trainer,"busy"))yield return null;
        Click(trainer,ray,"故事");yield return null;
        Call(trainer,"BuildHeadsetPage");Call(trainer,"RefreshHeadsetText");yield return null;
        var render=new RenderTexture(1440,1100,24);rig.HeadCamera.targetTexture=render;rig.HeadCamera.Render();
        var previous=RenderTexture.active;RenderTexture.active=render;
        var shot=new Texture2D(1440,1100,TextureFormat.RGB24,false);shot.ReadPixels(new Rect(0,0,1440,1100),0,0);shot.Apply();
        File.WriteAllBytes(Path.Combine(Output,"dog1-headset-menu.png"),shot.EncodeToPNG());
        RenderTexture.active=previous;rig.HeadCamera.targetTexture=null;
        UnityEngine.Object.Destroy(shot);UnityEngine.Object.Destroy(render);
        Require(desktop.GetComponent<Dog1PlayerView>()!=null,"desktop controls retained");

        string simulatedModel = Environment.GetEnvironmentVariable("CITYDOG_QA_MODEL_URL");
        bool useFixture = Uri.TryCreate(simulatedModel, UriKind.Absolute, out var fixture) && fixture.IsLoopback;
        if (useFixture)
        {
            trainer.endpoint = simulatedModel;
            trainer.model = "citydog-validation";
        }
        trainer.StartCoroutine((IEnumerator)Call(trainer, "Train", "看看我"));
        float choiceDeadline = Time.realtimeSinceStartup + 23f;
        bool played = false;
        while ((bool)Get(trainer,"busy") && Time.realtimeSinceStartup < choiceDeadline)
        {
            played |= trainer.dog.IsPlayingMicroAction && ((string)Get(trainer,"reply")).StartsWith(
                useFixture ? "场景选择 ·" : "麦粒 ·", StringComparison.Ordinal);
            yield return null;
        }
        Require(played && !(bool)Get(trainer,"busy"), useFixture ? "loopback model choice reached actual motion playback" :
            "local scene choice reached actual motion playback");
        rig.HeadCamera.enabled=false;desktop.enabled=true;
    }
    private static void Click(Dog1Training trainer,Dog2ControllerRay ray,string label)
    {
        Canvas.ForceUpdateCanvases();Button button=null;
        foreach(var candidate in trainer.HeadsetCanvas.GetComponentsInChildren<Button>())
            if(candidate.gameObject.name==label){button=candidate;break;}
        if(button==null)throw new Exception("Missing button: "+label);
        var rect=button.GetComponent<RectTransform>();Vector3 center=rect.TransformPoint(rect.rect.center);
        Vector3 head=trainer.HeadsetRig.HeadCamera.transform.position;var aim=new Ray(head,(center-head).normalized);
        int before=ray.ClickCount;ray.ProcessRay(aim,false,true);ray.ProcessRay(aim,true,true);ray.ProcessRay(aim,false,true);
        if(ray.ClickCount==before)
        {
            var camera=trainer.HeadsetRig.HeadCamera;
            var pointer=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
            pointer.position=RectTransformUtility.WorldToScreenPoint(camera,center);
            var hits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            trainer.HeadsetCanvas.GetComponent<GraphicRaycaster>().Raycast(pointer,hits);
            var graphic=button.GetComponent<Image>();
            Debug.Log("XR_RAY_DEBUG center="+center+" head="+head+" screen="+pointer.position+" camera="+camera.pixelRect+" canvas="+trainer.HeadsetCanvas.GetComponent<RectTransform>().rect+" hits="+string.Join(",",hits.ConvertAll(h=>h.gameObject.name))+" depth="+graphic.depth+" active="+graphic.isActiveAndEnabled+" contains="+RectTransformUtility.RectangleContainsScreenPoint(rect,pointer.position,camera)+" cameraForward="+camera.transform.forward+" graphicForward="+rect.forward);
        }
        Require(ray.ClickCount==before+1,"ray click: "+label);
    }
    private static void Finish(Exception error)
    {
        SessionState.SetBool(Key,false);Time.timeScale=1;
        if(error!=null)Debug.LogException(error);
        File.WriteAllText(Path.Combine(Output,"unity-result.txt"),error==null?"PASS: Dog1 XR world menu, controller rays, action browser, scene choice playback, pause/resume, real local Chinese ASR -> Agent.":error.ToString());
        EditorApplication.Exit(error==null?0:1);
    }
}
#endif
