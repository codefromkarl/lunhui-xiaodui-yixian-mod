using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Config;
using Reincarnation.Domain;
using Reincarnation.Gameplay.Combat;
using Reincarnation.Gameplay.SceneRoots;
using Reincarnation.Modding;
using Reincarnation.Runtime.Core;
using Reincarnation.Runtime.Scenes;
using Reincarnation.Runtime.Content;
using Reincarnation.UI.Nodes;
using Reincarnation.UI.ViewModels;
using Reincarnation.Runtime.Birth;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MuyifengAppearance
{
    internal sealed class RuntimeVerification : MonoBehaviour
    {
        private ICodeModContext context;
        private string output;
        private JObject report;
        private readonly JArray cases=new JArray();
        private readonly List<GameObject> owned=new List<GameObject>();
        private readonly Dictionary<BattleCombatantView,Vector3> standardScales=new Dictionary<BattleCombatantView,Vector3>();
        private readonly Dictionary<BattleCombatantView,float> nativeFacing=new Dictionary<BattleCombatantView,float>();
        private readonly Dictionary<BattleCombatantView,bool> nativeFacingConfig=new Dictionary<BattleCombatantView,bool>();
        private RenderTexture recording;
        private Camera camera;
        private BattleCombatantView native,hero,detailNative,detailHero;
        private BattleCombatantView[] Actors=>new[]{native,hero,detailNative,detailHero}.Where(x=>x!=null).ToArray();
        private NodePartyPortrait localPortrait,remotePortrait;
        private TMP_FontAsset font;
        private Canvas canvas;
        private TMP_Text phaseLabel;
        private bool display;
        private AnimationClip activeClip;
        private bool ownsSoloSession;
        internal static void Install(ICodeModContext context)
        {
            if(!Environment.GetCommandLineArgs().Contains("-muyifeng-verify")) return;
            var go=new GameObject("MuyifengRuntimeVerification");
            go.AddComponent<RuntimeVerification>().context=context;
            DontDestroyOnLoad(go);
        }
        private IEnumerator Start()
        {
            while(!GameManager.TryGetInstance(out var game)||!game.IsInitialized) yield return null;
            yield return null;
            output=Path.Combine(context.PackageDirectory,"TestResults",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(output);
            report=new JObject{
                ["unity"]=Application.unityVersion,["graphics"]=SystemInfo.graphicsDeviceName,
                ["runtime"]=Type.GetType("Mono.Runtime")!=null?"Mono":"other",["status"]="running",["cases"]=cases,
                ["combatantViews"]=JArray.FromObject(Resources.FindObjectsOfTypeAll<BattleCombatantView>().Select(x=>x.name)),
                ["spawnCoordinators"]=JArray.FromObject(Resources.FindObjectsOfTypeAll<BattleSpawnCoordinator>().Select(x=>x.name)),
                ["portraits"]=JArray.FromObject(Resources.FindObjectsOfTypeAll<NodePartyPortrait>().Select(x=>x.name)),
                ["spum"]=JArray.FromObject(Resources.FindObjectsOfTypeAll<SPUM_Prefabs>().Select(x=>x.name)),
                ["windows"]=JArray.FromObject(Resources.FindObjectsOfTypeAll<ProjectContentManifest>().Where(x=>x.UIWindowRegistry!=null).SelectMany(x=>x.UIWindowRegistry.Registrations).Select(x=>x.WindowPrefab?.name))
            };
            File.WriteAllText(Path.Combine(output,"inventory.json"),report.ToString());
            // Additively load the shipped game scene only to obtain its original
            // player prefab. The existing login/menu root keeps registration;
            // immediately deactivate scene roots before their next Update.
            var beforeAuthority=GameManager.TryGetInstance(out var managerBefore)?managerBefore.Run?.Authority:null;
            var saves=SaveInventory();
            var loading=SceneManager.LoadSceneAsync("Game",LoadSceneMode.Additive);
            yield return loading;
            var scene=SceneManager.GetSceneByName("Game");
            foreach(var root in scene.GetRootGameObjects()) root.SetActive(false);
            var coordinator=Resources.FindObjectsOfTypeAll<BattleSpawnCoordinator>().FirstOrDefault(x=>x.gameObject.scene==scene);
            try
            {
                Require(coordinator!=null,"shipped Game scene has no BattleSpawnCoordinator");
                var prefab=Entry.Read<BattleCombatantView>(coordinator,"playerCombatantViewPrefab");
                Require(prefab!=null,"shipped battle player prefab missing");
                report["player_prefab"]=prefab.name;
                Require(GameManager.TryGetInstance(out var sessionGame)&&sessionGame.Sessions.Current==null,"verification requires an idle copy with no user session");
                Require(sessionGame.Sessions.TryActivateSolo(out var sessionError),"native solo session: "+sessionError);ownsSoloSession=true;
                Case("native_local_session_identity",()=>{Require(Entry.IsLocal(1)&&!Entry.IsLocal(2),"local slot did not resolve from native SessionSnapshot");});
                Setup(prefab);
                Case("local_player_only",()=>{Require(HasSkin(hero)&&!HasSkin(native),"target selection leaked or missed");});
                Case("nine_parts_bound",()=>{Require(SkinCount(hero)>=8&&hero.GetComponentsInChildren<SpriteRenderer>(true).Any(x=>x.enabled&&x.sprite==Art.Parts["HairBack"]),"complete head, clothes, legs, boots or rear hair missing");});
                Case("full_face_replaced",()=>{Require(Rows(hero).Where(x=>x.PartType=="Body"&&x.Structure=="Head").All(x=>x.renderer.sprite==Art.Parts["Body/Head"]),"vanilla face retained");});
                Case("vanilla_eyes_and_beard_suppressed",()=>{Require(Rows(hero).Where(x=>x.PartType=="Eye"||x.PartType=="Hair"||x.PartType=="FaceHair").All(x=>x.renderer.sprite==null),"vanilla eyes/hair/beard overlay custom face");});
                Case("native_texel_dimensions",()=>{foreach(var pair in Art.Parts){Require(pair.Value.texture.filterMode==FilterMode.Point,"blur filter");Require(pair.Value.rect.width==pair.Value.texture.width&&pair.Value.rect.height==pair.Value.texture.height,"sprite resampled");File.WriteAllBytes(Path.Combine(output,"source-"+pair.Key.Replace('/','-')+".png"),pair.Value.texture.EncodeToPNG());}});
                Case("native_reapply_retains_skin",()=>{hero.ApplyLook(BirthAppearance.Default.WithIndex(BirthAppearancePart.HairColor,3),"EQ-BLD-01");Require(HasSkin(hero),"native reapply removed custom skin");});
                Case("hair_color_preserves_authored_palette",()=>{Require(hero.GetComponentsInChildren<SpriteRenderer>(true).Where(x=>x.sprite==Art.Parts["HairBack"]||x.sprite==Art.Parts["Body/Head"]).All(x=>x.color==Color.white),"native blue dye changed authored head/hair");});
                Case("portrait_local_only",()=>{Require(HasSkin(Entry.Read<GameObject>(localPortrait,"rig"))&&!HasSkin(Entry.Read<GameObject>(remotePortrait,"rig")),"portrait targeting failed");});
                Case("portrait_cache_invalidated",()=>{Require(Entry.Read<bool>(localPortrait,"dirty"),"new skin did not dirty portrait cache");localPortrait.RenderPortrait();});
                Case("pooled_player_becomes_remote",()=>{hero.Apply(Snapshot(2,"pooled-remote"),0,1,2.5f);Require(Rows(hero).Any(x=>x.PartType=="Eye"&&x.renderer.sprite!=null),"pool restore left remote eyes hidden");hero.ApplyLook(BirthAppearance.Default,"EQ-BLD-01");Require(!HasSkin(hero),"pool reuse retained skin on remote");hero.Apply(Snapshot(1,"local"),0,1,2.5f);hero.ApplyLook(BirthAppearance.Default,"EQ-BLD-01");Place(hero,.2f,-1.8f);Require(HasSkin(hero),"pool reuse failed to reapply local skin");});
                Case("summon_not_replaced",()=>{hero.Apply(Snapshot(1,"summon",CombatantKind.Summon),0,1,2.5f);Require(!HasSkin(hero),"owner summon inherited local skin");hero.Apply(Snapshot(1,"local"),0,1,2.5f);hero.ApplyLook(BirthAppearance.Default,"EQ-BLD-01");Place(hero,.2f,-1.8f);});
                Case("portrait_reuse_restores_remote",()=>{localPortrait.Set(PortraitModel(2,false),null);Require(!HasSkin(Entry.Read<GameObject>(localPortrait,"rig")),"portrait reuse leaked skin");localPortrait.Set(PortraitModel(1,true),null);Require(HasSkin(Entry.Read<GameObject>(localPortrait,"rig")),"portrait reuse failed to reapply");});
                Case("weapons_unchanged",()=>{Require(!Rows(hero).Where(x=>x.PartType=="Weapons").Any(x=>x.renderer.sprite!=null&&x.renderer.sprite.name.StartsWith("Muyifeng/")),"weapon sprites changed");});
                report["matching_rows"]=JArray.FromObject(Rows(hero).Select(x=>new{type=x.PartType,structure=x.Structure,sprite=x.renderer.sprite?.name,order=x.renderer.sortingOrder}));
                report["animations"]=JArray.FromObject(hero.GetComponentsInChildren<Animator>(true).SelectMany(x=>x.runtimeAnimatorController?.animationClips??Array.Empty<AnimationClip>()).Select(x=>x.name).Distinct());
                Case("setup_complete",()=>{});
            }
            catch(Exception ex) { cases.Add(new JObject{["name"]="setup",["status"]="failed",["error"]=ex.ToString()}); }
            if(hero!=null && camera!=null)
            {
                display=true;Time.captureFramerate=24;
                int frame=0;
                foreach(var phase in new[]{"IDLE","MOVE","ATTACK","DAMAGED","DEATH","MIRROR","RESTORE"})
                {
                    phaseLabel.text="动作验证  /  "+phase;
                    try{BeginPhase(phase);}catch(Exception ex){cases.Add(new JObject{["name"]="phase_"+phase,["status"]="failed",["error"]=ex.ToString()});}
                    var hashes=new HashSet<string>();
                    for(int i=0;i<48;i++)
                    {
                        yield return new WaitForEndOfFrame();
                        try
                        {
                            if(i==0) Directory.CreateDirectory(Path.Combine(output,"frames"));
                            foreach(var actor in Actors)foreach(var animator in actor.GetComponentsInChildren<Animator>(true))
                                activeClip.SampleAnimation(animator.gameObject,Mathf.Min((i/24f)%activeClip.length,activeClip.length-.001f));
                            foreach(var actor in Actors)PrepareDisplay(actor);
                            if(i==12&&phase=="IDLE")
                            {
                                var measurements=new JObject();
                                foreach(var actor in Actors)if(actor.TryGetBodyWorldBounds(out var bounds))
                                {
                                    float height=Mathf.Abs(camera.WorldToScreenPoint(bounds.max).y-camera.WorldToScreenPoint(bounds.min).y);
                                    measurements[actor.name]=height;
                                    if(actor==hero||actor==native)Require(height>=80&&height<=170,"standard game-size renderer unexpectedly enlarged: "+height);
                                }
                                report["body_height_pixels_at_900p"]=measurements;
                                report["size_policy"]="native NormalizePerceivedHeight(camera), 1x scale for upper comparison; lower comparison is explicit 2.4x inspection only";
                            }
                            camera.Render();
                            string path=Path.Combine(output,"frames","frame-"+frame.ToString("D5")+".png");
                            Capture(path); frame++;
                            if(i==12) {Capture(Path.Combine(output,phase.ToLowerInvariant()+".png"));CapturePortrait(localPortrait,Path.Combine(output,phase.ToLowerInvariant()+"-portrait-local.png"));CapturePortrait(remotePortrait,Path.Combine(output,phase.ToLowerInvariant()+"-portrait-control.png"));}
                            hashes.Add(string.Join(";",hero.PresentationMount.GetComponentsInChildren<Transform>(true).Where(x=>x!=hero.PresentationMount).Select(x=>x.localPosition.ToString("F4")+x.localRotation.ToString("F4"))));
                            Require(HasSkin(hero),"custom sprites overwritten during "+phase);
                            Require(Rows(hero).Where(x=>x.PartType=="Eye"||x.PartType=="FaceHair").All(x=>x.renderer.sprite==null),"vanilla facial sprites reappeared during "+phase);
                            if(phase=="MIRROR"||phase=="RESTORE")Require(Actors.All(x=>Mathf.Sign(x.PresentationMount.localScale.x)==nativeFacing[x]*(phase=="MIRROR"?-1:1)),"native motion update reset facing during "+phase);
                        }
                        catch(Exception ex){cases.Add(new JObject{["name"]="frame_"+phase+"_"+i,["status"]="failed",["error"]=ex.Message});break;}
                    }
                    cases.Add(new JObject{["name"]="animation_"+phase,["status"]=hashes.Count>1||phase=="RESTORE"?"passed":"failed",["distinct_transform_poses"]=hashes.Count});
                }
                report["frames"]=frame;report["fps"]=24;
                Time.captureFramerate=0;display=false;
            }
            foreach(var item in owned) if(item!=null) Destroy(item);
            if(ownsSoloSession&&GameManager.TryGetInstance(out var sessionManager))sessionManager.Sessions.LeaveCurrentSession();
            Case("user_state_isolation",()=>{Require(GameManager.TryGetInstance(out var current)&&ReferenceEquals(beforeAuthority,current.Run?.Authority),"live authority changed");Require(JToken.DeepEquals(saves,SaveInventory()),"save inventory changed");});
            report["passed"]=cases.OfType<JObject>().Count(x=>(string)x["status"]=="passed");report["failed"]=cases.OfType<JObject>().Count(x=>(string)x["status"]=="failed");
            report["status"]=(int)report["failed"]==0?"passed":"failed";
            report["scope"]="Shipped player prefab, authored native pixel grids, native size normalization, native SPUM clips and NodePartyPortrait; isolated additive Unity graphics verification; no campaign/save host.";
            File.WriteAllText(Path.Combine(output,"results.json"),report.ToString());
            context.Log("[AppearanceTest] "+report["status"]+"; "+output);
            if(Environment.GetCommandLineArgs().Contains("-muyifeng-quit"))
            {
                while(File.Exists(Path.Combine(LocalModLoader.DefaultDirectory,".startup-pending")))
                {
                    if(GameManager.TryGetInstance(out var manager)&&manager.Run?.Authority==null&&manager.CurrentSceneRoot is ILoginSceneFlow)
                        manager.Scenes.TryLoadLocalScene("scene.menu",out _);
                    yield return null;
                }
                File.WriteAllText(Path.Combine(output,"quit.json"),new JObject{["normal_quit"]=true,["startup_completed"]=true}.ToString());
                Application.Quit((int)report["failed"]==0?0:1);
            }
        }
        private static JObject SaveInventory()
        {
            string root=Path.Combine(Application.persistentDataPath,"Reincarnation","Saves","Runs");var r=new JObject();
            if(Directory.Exists(root))foreach(string p in Directory.GetFiles(root,"*",SearchOption.AllDirectories)){var f=new FileInfo(p);r[p.Substring(root.Length)]=f.Length+":"+f.LastWriteTimeUtc.Ticks;}
            return r;
        }
        private void OnGUI(){if(display&&recording!=null) GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),recording,ScaleMode.ScaleToFit,false);}
        private static void Require(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
        private void Case(string name,Action action)
        {
            try{action();cases.Add(new JObject{["name"]=name,["status"]="passed"});}
            catch(Exception ex){cases.Add(new JObject{["name"]=name,["status"]="failed",["error"]=ex.ToString()});}
        }
        private static MatchingElement[] Rows(Component root)=>root.GetComponentsInChildren<SPUM_MatchingList>(true).SelectMany(x=>x.matchingTables).Where(x=>x!=null&&x.renderer!=null&&x.UnitType=="Unit").ToArray();
        private static int SkinCount(Component root)=>Rows(root).Count(x=>x.renderer.sprite!=null&&x.renderer.sprite.name.StartsWith("Muyifeng/"));
        private static bool HasSkin(Component root)=>SkinCount(root)>=8;
        private static bool HasSkin(GameObject root)=>root!=null&&SkinCount(root.transform)>=8;
        private static RunCombatantSnapshot Snapshot(int slot,string id,CombatantKind kind=CombatantKind.Player,int health=100)=>new RunCombatantSnapshot(new StableId("appearance.test."+id),new StableId("player.test"),kind,CombatSide.Player,FormationRow.Front,new PlayerSlotId((byte)slot),100,health,0,20,20,3,0,0,0,0,0,Array.Empty<RunCombatStatusSnapshot>(),Array.Empty<RunAbilityCooldownSnapshot>());
        private void Setup(BattleCombatantView prefab)
        {
            var camObject=new GameObject("AppearanceCaptureCamera");owned.Add(camObject);camera=camObject.AddComponent<Camera>();camera.enabled=false;
            camera.transform.position=new Vector3(10000,0,-20);camera.orthographic=true;camera.orthographicSize=4.5f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.045f,.075f,.12f);camera.cullingMask=1<<30;
            recording=new RenderTexture(1600,900,24){filterMode=FilterMode.Point};camera.targetTexture=recording;
            var canvasObject=new GameObject("AppearanceVerificationCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));owned.Add(canvasObject);canvas=canvasObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
            var scaler=canvasObject.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);
            var portraitPrefab=Resources.FindObjectsOfTypeAll<NodePartyPortrait>().FirstOrDefault(x=>x.gameObject.scene.name==null||!x.gameObject.scene.IsValid());
            if(portraitPrefab==null)portraitPrefab=Resources.FindObjectsOfTypeAll<NodePartyPortrait>().First();
            font=Entry.Read<TMP_Text>(portraitPrefab,"nameLabel").font;
            Text("牧逸风  /  完整像素外观 0.2",70,820,1100,55,36,new Color(.87f,.94f,1));
            Text("完整脸形与眉眼 · 原尺寸像素源 · 原生骨架与动作",72,777,1300,40,18,new Color(.50f,.66f,.78f));
            Text("游戏标准尺寸 · 原生缩放规则",80,728,1000,40,24,new Color(.87f,.94f,1));
            Text("原生外观 · 队友对照",100,665,420,40,24,new Color(.58f,.68f,.77f));
            Text("牧逸风 · 本机玩家",540,665,420,40,24,new Color(.46f,.86f,.81f));
            Text("2.4 倍细节检查 · 同一套实际部件",80,455,1000,40,22,new Color(.50f,.66f,.78f));
            Text("原生队伍头像组件",1160,665,390,40,23,new Color(.87f,.94f,1));
            Text("本机玩家",1160,600,350,35,18,new Color(.46f,.86f,.81f));
            Text("其他玩家 · 不换装",1160,370,350,35,18,new Color(.58f,.68f,.77f));
            phaseLabel=Text("动作验证",72,70,1050,40,24,new Color(.87f,.94f,1));
            Text("游戏内隔离验证 · 上排为标准尺寸，下排仅放大检查 · 不接入用户存档",72,28,1450,30,17,new Color(.50f,.66f,.78f));
            native=Instantiate(prefab);native.name="NativeControl";owned.Add(native.gameObject);native.Apply(Snapshot(2,"remote"),0,1,2.5f);native.ApplyLook(BirthAppearance.Default,"EQ-BLD-01");Place(native,-4.7f,-1.8f);
            hero=Instantiate(prefab);hero.name="MuyifengLocalPlayer";owned.Add(hero.gameObject);hero.Apply(Snapshot(1,"local"),0,1,2.5f);hero.ApplyLook(BirthAppearance.Default,"EQ-BLD-01");Place(hero,.2f,-1.8f);
            detailNative=Instantiate(prefab);detailNative.name="NativeDetail";owned.Add(detailNative.gameObject);detailNative.Apply(Snapshot(2,"detail-remote"),0,1,2.5f);detailNative.ApplyLook(BirthAppearance.Default,"EQ-BLD-01");Place(detailNative,-4.7f,-1.8f);
            detailHero=Instantiate(prefab);detailHero.name="MuyifengDetail";owned.Add(detailHero.gameObject);detailHero.Apply(Snapshot(1,"detail-local"),0,1,2.5f);detailHero.ApplyLook(BirthAppearance.Default,"EQ-BLD-01");Place(detailHero,.2f,-1.8f);
            localPortrait=Instantiate(portraitPrefab,canvas.transform,false);localPortrait.name="LocalPlayerPortrait";Rect(localPortrait.GetComponent<RectTransform>(),1160,425,350,165);localPortrait.Set(PortraitModel(1,true),null);localPortrait.gameObject.SetActive(true);
            remotePortrait=Instantiate(portraitPrefab,canvas.transform,false);remotePortrait.name="OtherPlayerPortrait";Rect(remotePortrait.GetComponent<RectTransform>(),1160,195,350,165);remotePortrait.Set(PortraitModel(2,false),null);remotePortrait.gameObject.SetActive(true);
            Layer(canvasObject.transform);
            foreach(var actor in Actors)
            {
                Layer(actor.transform);
                nativeFacing[actor]=Mathf.Sign(actor.PresentationMount.localScale.x);
                nativeFacingConfig[actor]=Entry.Read<bool>(actor,"artFacesRight");
                foreach(var spum in actor.GetComponentsInChildren<SPUM_Prefabs>(true))
                {if(spum._anim==null)spum._anim=spum.GetComponentInChildren<Animator>(true);if(spum.StateAnimationPairs.Count==0){spum.PopulateAnimationLists();spum.OverrideControllerInit();}}
                foreach(var animator in actor.GetComponentsInChildren<Animator>(true)) animator.enabled=false;
            }
        }
        private static NodePartyMemberViewModel PortraitModel(int slot,bool local)
        {
            var character=new CharacterCenterViewModel(local?"牧逸风 · 本机":"队友 · 原生",BirthAppearance.Default,"","","","","","","","","","","",0);
            return new NodePartyMemberViewModel(slot,local,100,100,character);
        }
        private static void Rect(RectTransform rect,float x,float y,float w,float h){rect.anchorMin=rect.anchorMax=Vector2.zero;rect.pivot=Vector2.zero;rect.anchoredPosition=new Vector2(x,y);rect.sizeDelta=new Vector2(w,h);}
        private TMP_Text Text(string value,float x,float y,float w,float h,int size,Color color)
        {
            var go=new GameObject("Label",typeof(RectTransform));go.transform.SetParent(canvas.transform,false);var text=go.AddComponent<TextMeshProUGUI>();text.font=font;text.text=value;text.fontSize=size;text.color=color;text.enableWordWrapping=false;Rect(text.rectTransform,x,y,w,h);return text;
        }
        private static void Layer(Transform root){root.gameObject.layer=30;foreach(Transform child in root)Layer(child);}
        private void Place(BattleCombatantView view,float x,float y)
        {
            Transform mount=view.transform.parent;
            if(mount==null||mount.name!="VerificationDisplayScale")
            {mount=new GameObject("VerificationDisplayScale").transform;view.transform.SetParent(mount,false);}
            mount.localScale=Vector3.one;mount.position=new Vector3(10000+x,-2.5f,0);
            view.transform.localPosition=Vector3.zero;view.transform.localScale=Vector3.one;
        }
        private void PrepareDisplay(BattleCombatantView actor)
        {
            if(!standardScales.TryGetValue(actor,out var standard))
            {
                actor.NormalizePerceivedHeight(camera);standard=actor.transform.localScale;standardScales.Add(actor,standard);
            }
            actor.transform.localScale=standard;
            bool detail=actor==detailNative||actor==detailHero;
            if(detail){var scale=actor.transform.localScale;actor.transform.localScale=new Vector3(scale.x*2.4f,scale.y*2.4f,scale.z);}
            if(actor.TryGetBodyWorldBounds(out var bounds))
            {
                float x=(actor==native||actor==detailNative)?285:730;
                float y=detail?140:535;
                var target=camera.ScreenToWorldPoint(new Vector3(x,y,20));
                actor.transform.position+=new Vector3(target.x-bounds.center.x,target.y-bounds.min.y,0);
            }
        }
        private void BeginPhase(string phase)
        {
            foreach(var actor in Actors)actor.SetStagedDowned(false);
            string path=phase=="MOVE"?"01_Move/0_move":phase=="ATTACK"?"02_Attack/00_MeleeAttack/0_Attack_Normal":phase=="DAMAGED"?"03_Damaged/0_Damaged":phase=="DEATH"?"04_Death/0_Die":"00_Idle/0_idle";
            activeClip=Resources.Load<AnimationClip>("Addons/Legacy/0_Unit/1_Animation/"+path);
            Require(activeClip!=null&&activeClip.length>0,"native SPUM clip missing: "+path);
            report["clip_"+phase]=activeClip.name;
            foreach(var actor in Actors)
            foreach(var spum in actor.GetComponentsInChildren<SPUM_Prefabs>(true))
            {
                PlayerState state=PlayerState.IDLE;
                if(Enum.TryParse<PlayerState>(phase,out var requested))state=requested;
                if(spum.StateAnimationPairs.TryGetValue(state.ToString(),out var clips)&&clips.Count>0)spum.PlayAnimation(state,0);
            }
            if(phase=="ATTACK")foreach(var actor in Actors)actor.PlayAttackMotion();
            if(phase=="DAMAGED")foreach(var actor in Actors)actor.PlayHitMotion(1,0,true);
            if(phase=="DEATH")foreach(var actor in Actors)actor.SetStagedDowned(true);
            if(phase=="MIRROR")
            {
                foreach(var actor in Actors)SetNativeFacing(actor,true);
                Case("actual_mirror_changes_facing",()=>{Require(Actors.All(x=>Mathf.Sign(x.PresentationMount.localScale.x)==-nativeFacing[x]),"mirror phase did not reverse the native facing");});
            }
            if(phase=="RESTORE")
            {
                foreach(var actor in Actors)SetNativeFacing(actor,false);
                Case("actual_restore_original_facing",()=>{Require(Actors.All(x=>Mathf.Sign(x.PresentationMount.localScale.x)==nativeFacing[x]),"restore phase left a reversed actor");});
            }
        }
        private void SetNativeFacing(BattleCombatantView actor,bool mirror)
        {
            AccessTools.Field(typeof(BattleCombatantView),"artFacesRight").SetValue(actor,mirror?!nativeFacingConfig[actor]:nativeFacingConfig[actor]);
            // Native ApplyFacing captures the motion base; changing scale alone
            // would be undone by the next native LateUpdate.
            AccessTools.Method(typeof(BattleCombatantView),"ApplyFacing").Invoke(actor,null);
            actor.ApplyLook(BirthAppearance.Default,"EQ-BLD-01");
        }
        private void Capture(string path)
        {
            Canvas.ForceUpdateCanvases();var old=RenderTexture.active;RenderTexture.active=recording;var image=new Texture2D(recording.width,recording.height,TextureFormat.RGB24,false);
            try{image.ReadPixels(new Rect(0,0,recording.width,recording.height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}finally{Destroy(image);RenderTexture.active=old;}
        }
        private void CapturePortrait(NodePartyPortrait portrait,string path)
        {
            portrait.RenderPortrait();var texture=Entry.Read<RenderTexture>(portrait,"texture");var old=RenderTexture.active;RenderTexture.active=texture;var image=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);
            try{image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());Require(image.GetPixels32().Count(x=>x.a>64&&x.r+x.g+x.b>60)>100,"portrait camera produced empty pixels");}finally{Destroy(image);RenderTexture.active=old;}
        }
    }
}
