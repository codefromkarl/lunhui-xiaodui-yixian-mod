using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Config;
using Reincarnation.ExternalBridges.Spum;
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
    internal sealed partial class RuntimeVerification : MonoBehaviour
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
        private BirthAppearance showcaseAppearance=MuyifengChoice.Token;
        private bool ownsSoloSession;
        internal static void Install(ICodeModContext context)
        {
            if(!Environment.GetCommandLineArgs().Contains("-muyifeng-verify")) return;
            var go=new GameObject("MuyifengRuntimeVerification");
            go.AddComponent<RuntimeVerification>().context=context;
            Application.runInBackground=true;
            Application.targetFrameRate=60;
            QualitySettings.vSyncCount=0;
            DontDestroyOnLoad(go);
        }
        private IEnumerator Start()
        {
            while(!GameManager.TryGetInstance(out var game)||!game.IsInitialized) yield return null;
            yield return null;
            output=Path.Combine(context.PackageDirectory,"TestResults",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(output);
            report=new JObject{
                ["modVersion"]=typeof(Entry).Assembly.GetName().Version.ToString(),["unity"]=Application.unityVersion,["graphics"]=SystemInfo.graphicsDeviceName,
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
                report["static_only"]=Environment.GetCommandLineArgs().Contains("-muyifeng-static");
                Require(GameManager.TryGetInstance(out var sessionGame)&&sessionGame.Sessions.Current==null,"verification requires an idle copy with no user session");
                Require(sessionGame.Sessions.TryActivateSolo(out var sessionError),"native solo session: "+sessionError);ownsSoloSession=true;
                Case("registered_identity_only",()=>{Require(MuyifengChoice.Token.IsValid&&BirthAppearance.Default.IsValid,"registered or native identity invalid");Require(!new BirthAppearance(255,77,89,70,2,0,0).IsValid,"unregistered appearance accepted");});
                Setup(prefab);
                Case("only_selected_character",()=>{Require(HasSkin(hero)&&!HasSkin(native),"target selection leaked or missed");});
                Case("folded_arm_keeps_elbow",()=>{foreach(float side in new[]{-1f,1f}){var elbow=SkinState.SolveElbow(Vector3.zero,new Vector3(.0001f,0,0),.14f,.13f,side);Require(elbow.magnitude>.13f&&elbow.magnitude<.14f,"folded upper arm collapsed");Require(!float.IsNaN(elbow.x),"folded arm NaN");}});
                Case("extended_arm_reaches_palm",()=>{var palm=new Vector3(.4f,.3f,0);var elbow=SkinState.SolveElbow(Vector3.zero,palm,.14f,.13f,1);Require(Mathf.Abs(elbow.magnitude+Vector3.Distance(elbow,palm)-palm.magnitude)<.0001f,"extended arm failed to reach");});
                Case("rig_parts_bound",()=>{Require(Art.Parts.Count==18&&new[]{"Body/Head","HairBack","Cloth/Body","Cloth/Left","Cloth/Right","Cloth/LeftLower","Cloth/RightLower","Pant/Left","Pant/Right","Body/Foot_L","Body/Foot_R","Weapon/Sword"}.All(key=>VisiblePart(hero,key)),"bound costume part missing");Require(VisiblePart(hero,"Hand/Grip_L")&&VisiblePart(hero,"Hand/Open_R"),"independent hands missing");});
                Case("full_face_replaced",()=>{Require(Rows(hero).Where(x=>x.PartType=="Body"&&x.Structure=="Head").All(x=>x.renderer.sprite==null)&&VisiblePart(hero,"Body/Head"),"vanilla face retained or bound head missing");});
                Case("vanilla_eyes_and_beard_suppressed",()=>{Require(Rows(hero).Where(x=>x.PartType=="Eye"||x.PartType=="Hair"||x.PartType=="FaceHair").All(x=>x.renderer.sprite==null),"vanilla eyes/hair/beard overlay custom face");});
                Case("native_texel_dimensions",()=>{foreach(var pair in Art.Parts){Require(pair.Value.texture.filterMode==FilterMode.Point,"blur filter");Require(pair.Value.rect.width==pair.Value.texture.width&&pair.Value.rect.height==pair.Value.texture.height,"sprite resampled");File.WriteAllBytes(Path.Combine(output,"source-"+pair.Key.Replace('/','-')+".png"),pair.Value.texture.EncodeToPNG());}});
                Case("native_reapply_retains_skin",()=>{hero.ApplyLook(MuyifengChoice.Token,"EQ-BLD-01");Require(HasSkin(hero),"native reapply removed custom skin");});
                Case("hair_color_preserves_authored_palette",()=>{Require(hero.GetComponentsInChildren<SpriteRenderer>(true).Where(x=>x.sprite==Art.Parts["HairBack"]||x.sprite==Art.Parts["Body/Head"]).All(x=>x.color==Color.white),"native blue dye changed authored head/hair");});
                Case("portrait_character_choice",()=>{Require(HasSkin(Entry.Read<GameObject>(localPortrait,"rig"))&&!HasSkin(Entry.Read<GameObject>(remotePortrait,"rig")),"portrait targeting failed");});
                Case("portrait_cache_invalidated",()=>{Require(Entry.Read<bool>(localPortrait,"dirty"),"new skin did not dirty portrait cache");localPortrait.RenderPortrait();});
                Case("pooled_player_becomes_remote",()=>{hero.Apply(Snapshot(2,"pooled-remote"),0,1,2.5f);Require(Rows(hero).Any(x=>x.PartType=="Eye"&&x.renderer.sprite!=null),"pool restore left remote eyes hidden");hero.ApplyLook(BirthAppearance.Default,"EQ-BLD-01");Require(!HasSkin(hero),"pool reuse ignored remote native choice");hero.Apply(Snapshot(1,"local"),0,1,2.5f);hero.ApplyLook(MuyifengChoice.Token,"EQ-BLD-01");Place(hero,.2f,-1.8f);Require(HasSkin(hero),"pool reuse failed to reapply local skin");});
                Case("summon_not_replaced",()=>{hero.Apply(Snapshot(1,"summon",CombatantKind.Summon),0,1,2.5f);Require(!HasSkin(hero),"owner summon inherited local skin");hero.Apply(Snapshot(1,"local"),0,1,2.5f);hero.ApplyLook(MuyifengChoice.Token,"EQ-BLD-01");Place(hero,.2f,-1.8f);});
                Case("portrait_reuse_restores_remote",()=>{localPortrait.Set(PortraitModel(2,false),null);Require(!HasSkin(Entry.Read<GameObject>(localPortrait,"rig")),"portrait reuse leaked skin");localPortrait.Set(PortraitModel(1,true),null);Require(HasSkin(Entry.Read<GameObject>(localPortrait,"rig")),"portrait reuse failed to reapply");});
                Case("straight_sword_selected_only",()=>{Require(HasSword(hero)&&!HasSword(native),"straight sword missing or leaked to control");Require(Rows(native).Any(x=>x.PartType=="Weapons"&&x.renderer.sprite?.name=="EQ-BLD-01"),"native weapon changed");});
                Case("source_face_pixels_unchanged",()=>{
                    Require(Art.AtlasDefinition!=null,"source atlas not active");
                    var row=Art.AtlasDefinition["parts"]["Body/Head"];var crop=row["rect"];var region=Art.AtlasDefinition["faceIdentityRegion"];
                    var original=Art.Sources["reference"];var actual=Art.Parts["Body/Head"].texture;int compared=0;
                    for(int yy=(int)region[1];yy<(int)region[1]+(int)region[3];yy++)for(int xx=(int)region[0];xx<(int)region[0]+(int)region[2];xx++)
                    {
                        Color32 expected=original.GetPixel(xx,original.height-1-yy);
                        Color32 received=actual.GetPixel(xx-(int)crop[0],actual.height-1-(yy-(int)crop[1]));
                        Require(expected.Equals(received),"reference face texel changed: "+xx+","+yy);compared++;
                    }
                    Require(compared>=20000,"face identity sample too small");report["face_pixels_compared"]=compared;
                });
                Case("source_clothing_pixels_unchanged",()=>{
                    var unique=new HashSet<int>();
                    foreach(var key in new[]{"Cloth/Body","Cloth/Left","Cloth/Right","Cloth/LeftLower","Cloth/RightLower"}){
                        var row=Art.AtlasDefinition["parts"][key];var rect=row["rect"];var src=Art.Sources[(string)row["sheet"]];var dst=Art.Parts[key].texture;int threshold=(int?)row["alphaThreshold"]??0;
                        var before=src.GetPixels32();var after=dst.GetPixels32();
                        for(int y=0;y<dst.height;y++)for(int x=0;x<dst.width;x++){
                            var expected=before[(src.height-(int)rect[1]-dst.height+y)*src.width+(int)rect[0]+x];
                            if(expected.a<Math.Max(1,threshold))continue;
                            if(after[y*dst.width+x].a==0)continue;
                            Require(expected.Equals(after[y*dst.width+x]),"retained reference clothing changed: "+key);unique.Add((src.height-(int)rect[1]-dst.height+y)*src.width+(int)rect[0]+x);
                        }
                    }
                    Require(unique.Count>10000,"clothing sample too small");report["clothing_unique_pixels_compared"]=unique.Count;
                });
                Case("sword_mapping_preserves_bone_transform",()=>{
                    var state=hero.GetComponent<SkinState>();var weapon=Rows(hero).First(x=>x.PartType=="Weapons"&&x.renderer.gameObject.activeInHierarchy&&state.NativeSprite(x.renderer)?.name=="EQ-BLD-01").renderer;
                    var p=weapon.transform.localPosition;var q=weapon.transform.localRotation;var scale=weapon.transform.localScale;
                    Entry.Bind(hero.gameObject,false);Entry.Bind(hero.gameObject,true);
                    Require(weapon.transform.localPosition==p&&weapon.transform.localRotation==q&&weapon.transform.localScale==scale,"weapon bone modified by appearance mapping");
                });
                Case("binding_preserves_native_skeleton",()=>{
                    var bones=hero.PresentationMount.GetComponentsInChildren<Transform>(true).Where(t=>!t.name.StartsWith("MuyifengBinding/",StringComparison.Ordinal)).ToArray();
                    var pose=bones.Select(t=>(t.localPosition,t.localRotation,t.localScale)).ToArray();
                    Entry.Bind(hero.gameObject,false);Entry.Bind(hero.gameObject,true);
                    for(int i=0;i<bones.Length;i++)Require(bones[i].localPosition==pose[i].localPosition&&bones[i].localRotation==pose[i].localRotation&&bones[i].localScale==pose[i].localScale,"binding changed native skeleton: "+bones[i].name);
                });
                Case("sword_excluded_from_body_normalization",()=>{
                    var preview=hero.GetComponentInChildren<SpumBirthAppearancePreview>(true);
                    var sword=hero.GetComponentsInChildren<SpriteRenderer>(true).First(x=>x.enabled&&x.gameObject.activeInHierarchy&&x.sprite==Art.Parts["Weapon/Sword"]);
                    Require(preview.IsEquipmentRenderer(sword),"sword included in body sizing");
                });
                Case("standalone_weapon_refresh",()=>{
                    var preview=hero.GetComponentInChildren<SpumBirthAppearancePreview>(true);
                    preview.ApplyWeapon(SpumWeaponCatalog.GetVisual(""));Require(HasSword(hero),"standalone unarmed refresh removed costume sword");
                    preview.ApplyWeapon(SpumWeaponCatalog.GetVisual("EQ-BLD-01"));Require(HasSword(hero),"standalone blade refresh missing");
                });
                Case("unarmed_keeps_costume_sword",()=>{hero.ApplyLook(MuyifengChoice.Token,"");Require(HasSword(hero),"unarmed rig lost costume sword");hero.ApplyLook(MuyifengChoice.Token,"EQ-BLD-01");Require(HasSword(hero),"blade refresh lost costume sword");});
                Case("pooled_weapon_restores_native",()=>{Entry.Bind(hero.gameObject,false);Require(!HasSword(hero)&&Rows(hero).Any(x=>x.PartType=="Weapons"&&x.renderer.sprite?.name=="EQ-BLD-01"),"weapon restore failed");Entry.Bind(hero.gameObject,true);Require(HasSword(hero),"sword rebind failed");});
                var weaponSource=Rows(native).First(x=>x.PartType=="Weapons"&&x.renderer.sprite!=null).renderer.sprite;
                var weaponTarget=RenderTexture.GetTemporary(weaponSource.texture.width,weaponSource.texture.height,0,RenderTextureFormat.ARGB32);
                var previousTarget=RenderTexture.active;
                Graphics.Blit(weaponSource.texture,weaponTarget);RenderTexture.active=weaponTarget;
                var weaponPixels=new Texture2D((int)weaponSource.rect.width,(int)weaponSource.rect.height,TextureFormat.RGBA32,false);
                weaponPixels.SetPixels32(new Color32[weaponPixels.width*weaponPixels.height]);weaponPixels.Apply();
                weaponPixels.ReadPixels(weaponSource.textureRect,(int)weaponSource.textureRectOffset.x,(int)weaponSource.textureRectOffset.y);weaponPixels.Apply();
                File.WriteAllBytes(Path.Combine(output,"native-weapon.png"),weaponPixels.EncodeToPNG());
                Destroy(weaponPixels);RenderTexture.active=previousTarget;RenderTexture.ReleaseTemporary(weaponTarget);
                report["cloth_geometry"]=JArray.FromObject(Rows(hero).Where(x=>x.PartType=="Cloth").GroupBy(x=>x.renderer).Select(g=>g.First()).Select(x=>new{part=x.Structure,active=x.renderer.gameObject.activeInHierarchy,enabled=x.renderer.enabled,position=x.renderer.transform.position.ToString("F4"),angle=x.renderer.transform.eulerAngles.z,scale=x.renderer.transform.lossyScale.ToString("F4"),width=x.renderer.sprite?.rect.width,height=x.renderer.sprite?.rect.height}));
                report["native_weapon_geometry"]=JArray.FromObject(Rows(native).Where(x=>x.PartType=="Weapons"&&x.renderer.sprite!=null).Select(x=>new{sprite=x.renderer.sprite.name,width=x.renderer.sprite.rect.width,height=x.renderer.sprite.rect.height,pivotX=x.renderer.sprite.pivot.x,pivotY=x.renderer.sprite.pivot.y,trimX=x.renderer.sprite.textureRectOffset.x,trimY=x.renderer.sprite.textureRectOffset.y,trimWidth=x.renderer.sprite.textureRect.width,trimHeight=x.renderer.sprite.textureRect.height,ppu=x.renderer.sprite.pixelsPerUnit,angle=x.renderer.transform.eulerAngles.z}).Take(1));
                report["matching_rows"]=JArray.FromObject(Rows(hero).Select(x=>new{type=x.PartType,structure=x.Structure,sprite=x.renderer.sprite?.name,order=x.renderer.sortingOrder}));
                report["animations"]=JArray.FromObject(hero.GetComponentsInChildren<Animator>(true).SelectMany(x=>x.runtimeAnimatorController?.animationClips??Array.Empty<AnimationClip>()).Select(x=>x.name).Distinct());
                VerifyHudPortrait();
                VerifyCharacterChoices();
                Case("setup_complete",()=>{});
            }
            catch(Exception ex) { cases.Add(new JObject{["name"]="setup",["status"]="failed",["error"]=ex.ToString()}); }
            if(hero!=null && camera!=null)
            {
                var creation=VerifyCreationWindow();bool creationFailed=false;
                while(true){bool next=false;object wait=null;try{next=creation.MoveNext();if(next)wait=creation.Current;}catch(Exception ex){Case("creation_window",()=>throw new InvalidOperationException("Creation verification failed",ex));creationFailed=true;}
                    if(creationFailed||!next)break;yield return wait;}
                if(!creationFailed)Case("creation_window",()=>{});
                display=true;
                foreach(var step in VerifyFixedSwordEquipment())yield return step;
                foreach(var step in VerifyExpandedCharacters())yield return step;
                foreach(var step in VerifyCostumeMotion())yield return step;
                Time.captureFramerate=24;
                int frame=0;
                foreach(var phase in Environment.GetCommandLineArgs().Contains("-muyifeng-static")?new[]{"IDLE"}:Environment.GetCommandLineArgs().Contains("-muyifeng-keyposes")?new[]{"IDLE","ATTACK"}:new[]{"IDLE","MOVE","ATTACK","DAMAGED","DEATH","MIRROR","RESTORE"})
                {
                    phaseLabel.text="动作验证  /  "+phase;
                    try{BeginPhase(phase);}catch(Exception ex){cases.Add(new JObject{["name"]="phase_"+phase,["status"]="failed",["error"]=ex.ToString()});}
                    var hashes=new HashSet<string>();
                    for(int i=0;i<(Environment.GetCommandLineArgs().Any(x=>x=="-muyifeng-static"||x=="-muyifeng-keyposes")?16:48);i++)
                    {
                        yield return new WaitForEndOfFrame();
                        try
                        {
                            if(i==0) Directory.CreateDirectory(Path.Combine(output,"frames"));
                            foreach(var actor in Actors)foreach(var animator in actor.GetComponentsInChildren<Animator>(true))
                                activeClip.SampleAnimation(animator.gameObject,Mathf.Min((i/24f)%activeClip.length,activeClip.length-.001f));
                            PreviewCostumePhase(phase,((i/24f)%.34f)/.34f);
                            foreach(var actor in Actors)actor.GetComponent<SkinState>()?.SyncRigPose();
                            foreach(var actor in Actors)PrepareDisplay(actor);
                            foreach(var actor in Actors)actor.GetComponent<SkinState>()?.SyncRigPose();
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
                            if(i==12) {ReportBinding(phase);ReportAnatomy(native,phase);Capture(Path.Combine(output,phase.ToLowerInvariant()+".png"));CapturePortrait(localPortrait,Path.Combine(output,phase.ToLowerInvariant()+"-portrait-local.png"));CapturePortrait(remotePortrait,Path.Combine(output,phase.ToLowerInvariant()+"-portrait-control.png"));if(phase=="IDLE"||phase=="ATTACK")CaptureGuides(phase);}
                            hashes.Add(string.Join(";",hero.PresentationMount.GetComponentsInChildren<Transform>(true).Where(x=>x!=hero.PresentationMount).Select(x=>x.localPosition.ToString("F4")+x.localRotation.ToString("F4"))));
                            Require(HasSkin(hero)&&HasSword(hero),"custom face/clothes/sword overwritten during "+phase);
                            Require(hero.GetComponent<SkinState>().NativePartsHidden,"native body/clothes/equipment overlay during "+phase);
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
            report["scope"]="Original-art cutouts and supplementary support, source face/clothing pixel comparisons, native size normalization, SPUM clips and NodePartyPortrait; isolated Unity graphics verification; no campaign/save host.";
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
        private static int SkinCount(Component root)=>root.GetComponentsInChildren<SpriteRenderer>(true).Count(x=>x.enabled&&x.gameObject.activeInHierarchy&&x.sprite!=null&&x.sprite.name.StartsWith("Muyifeng/"));
        private static bool HasSkin(Component root)=>SkinCount(root)>=8;
        private static bool HasSword(Component root)=>VisiblePart(root,"Weapon/Sword");
        private static bool VisiblePart(Component root,string key)=>root.GetComponentsInChildren<SpriteRenderer>(true).Any(x=>x.enabled&&x.gameObject.activeInHierarchy&&x.sprite==Art.Parts[key]);
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
            Text("弈仙牌三门派  /  可选角色外观 0.7.2",70,820,1100,55,36,new Color(.87f,.94f,1));
            Text("按角色选择显示 · 原图脸部 · 独立身份随存档与联机传递",72,777,1300,40,18,new Color(.50f,.66f,.78f));
            Text("游戏标准尺寸 · 原生缩放规则",80,728,1000,40,24,new Color(.87f,.94f,1));
            Text("原生外观 · 队友对照",100,665,420,40,24,new Color(.58f,.68f,.77f));
            Text("所选角色 · 本机玩家",540,665,420,40,24,new Color(.46f,.86f,.81f));
            Text("2.4 倍细节检查 · 同一套实际部件",80,455,1000,40,22,new Color(.50f,.66f,.78f));
            Text("原生队伍头像组件",1160,665,390,40,23,new Color(.87f,.94f,1));
            Text("本机玩家",1160,600,350,35,18,new Color(.46f,.86f,.81f));
            Text("其他玩家 · 独立选择",1160,370,350,35,18,new Color(.58f,.68f,.77f));
            phaseLabel=Text("动作验证",72,70,1050,40,24,new Color(.87f,.94f,1));
            Text("游戏内隔离验证 · 上排为标准尺寸，下排仅放大检查 · 不接入用户存档",72,28,1450,30,17,new Color(.50f,.66f,.78f));
            native=Instantiate(prefab);native.name="NativeControl";owned.Add(native.gameObject);native.Apply(Snapshot(2,"remote"),0,1,2.5f);native.ApplyLook(BirthAppearance.Default,"EQ-BLD-01");Place(native,-4.7f,-1.8f);
            hero=Instantiate(prefab);hero.name="MuyifengLocalPlayer";owned.Add(hero.gameObject);hero.Apply(Snapshot(1,"local"),0,1,2.5f);hero.ApplyLook(MuyifengChoice.Token,"EQ-BLD-01");Place(hero,.2f,-1.8f);
            detailNative=Instantiate(prefab);detailNative.name="NativeDetail";owned.Add(detailNative.gameObject);detailNative.Apply(Snapshot(2,"detail-remote"),0,1,2.5f);detailNative.ApplyLook(BirthAppearance.Default,"EQ-BLD-01");Place(detailNative,-4.7f,-1.8f);
            detailHero=Instantiate(prefab);detailHero.name="MuyifengDetail";owned.Add(detailHero.gameObject);detailHero.Apply(Snapshot(1,"detail-local"),0,1,2.5f);detailHero.ApplyLook(MuyifengChoice.Token,"EQ-BLD-01");Place(detailHero,.2f,-1.8f);
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
        private static NodePartyMemberViewModel PortraitModel(int slot,bool local,BirthAppearance? appearance=null)
        {
            var selected=appearance??(local?MuyifengChoice.Token:BirthAppearance.Default);
            string name=AppearanceChoices.Find(selected)?.Name??"原生";
            var character=new CharacterCenterViewModel(name+(local?" · 本机":" · 队友"),selected,"","","","","","","","","","","",0);
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
            PreviewCostumePhase(phase,0);
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
            actor.ApplyLook(actor==hero||actor==detailHero?showcaseAppearance:BirthAppearance.Default,"EQ-BLD-01");
        }
        private void ReportBinding(string phase)
        {
            Case("joint_alignment_"+phase,()=>{
                var state=hero.GetComponent<SkinState>();state.SyncRigPose();var links=state.Joints;
                Require(links.Count>=6,"missing anatomical links");float max=0;
                var rows=new JArray();foreach(var link in links){var a=camera.WorldToScreenPoint(link.Actual);var b=camera.WorldToScreenPoint(link.Target);float error=Vector2.Distance(a,b);max=Mathf.Max(max,error);rows.Add(new JObject{["name"]=link.Name,["actual"]=JArray.FromObject(new[]{a.x,a.y}),["target"]=JArray.FromObject(new[]{b.x,b.y}),["errorPixels"]=error});}
                Require(max<1,"joint separation exceeds one screen pixel: "+max);report["binding_"+phase]=rows;
            });
        }
        private void CaptureGuides(string phase)
        {
            var state=detailHero.GetComponent<SkinState>();var guides=new List<GameObject>();
            foreach(var link in state.Joints){
                var point=camera.WorldToScreenPoint(link.Actual);
                var marker=new GameObject("BindingGuide",typeof(RectTransform),typeof(Image));marker.transform.SetParent(canvas.transform,false);marker.layer=30;
                marker.GetComponent<Image>().color=new Color(.2f,1f,.65f,1);Rect(marker.GetComponent<RectTransform>(),point.x-2,point.y-2,4,4);guides.Add(marker);
                var label=Text(link.Name,point.x+5,point.y-6,180,22,12,new Color(.3f,1f,.7f));label.gameObject.layer=30;guides.Add(label.gameObject);
            }
            Canvas.ForceUpdateCanvases();camera.Render();Capture(Path.Combine(output,phase.ToLowerInvariant()+"-fit-guides.png"));
            foreach(var guide in guides)Destroy(guide);
        }
        private void ReportAnatomy(BattleCombatantView actor,string phase)
        {
            string PathOf(Transform t){var names=new List<string>();while(t!=null&&t!=actor.transform){names.Add(t.name);t=t.parent;}names.Reverse();return string.Join("/",names);}
            report["anatomy_"+phase]=JArray.FromObject(Rows(actor).Where(x=>x.renderer.gameObject.activeInHierarchy&&x.renderer.sprite!=null&&(x.PartType=="Body"||x.PartType=="Cloth"||x.PartType=="Pant"||x.PartType=="Weapons")).GroupBy(x=>x.renderer).Select(g=>g.First()).Select(x=>{
                var r=x.renderer;var sp=r.sprite;var point=camera.WorldToScreenPoint(r.transform.position);
                return new{key=x.PartType+"/"+x.Structure,path=PathOf(r.transform),local=r.transform.localPosition.ToString("F5"),world=r.transform.position.ToString("F5"),screen=new[]{point.x,point.y},angle=r.transform.eulerAngles.z,scale=r.transform.lossyScale.ToString("F5"),width=sp.rect.width,height=sp.rect.height,pivotX=sp.pivot.x,pivotY=sp.pivot.y,ppu=sp.pixelsPerUnit,min=sp.bounds.min.ToString("F5"),max=sp.bounds.max.ToString("F5")};
            }));
            if(phase!="IDLE")return;
            foreach(var key in new[]{"Head","Body","Arm_L","Arm_R"}){
                var row=Rows(actor).First(x=>x.PartType=="Body"&&x.Structure==key&&x.renderer.gameObject.activeInHierarchy&&x.renderer.sprite!=null);var sp=row.renderer.sprite;
                var rt=RenderTexture.GetTemporary(sp.texture.width,sp.texture.height,0,RenderTextureFormat.ARGB32);var old=RenderTexture.active;
                Graphics.Blit(sp.texture,rt);RenderTexture.active=rt;
                var pixels=new Texture2D((int)sp.rect.width,(int)sp.rect.height,TextureFormat.RGBA32,false);pixels.SetPixels32(new Color32[pixels.width*pixels.height]);pixels.Apply();
                pixels.ReadPixels(sp.textureRect,(int)sp.textureRectOffset.x,(int)sp.textureRectOffset.y);pixels.Apply();
                File.WriteAllBytes(System.IO.Path.Combine(output,"native-body-"+key+".png"),pixels.EncodeToPNG());
                Destroy(pixels);RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);
            }
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
