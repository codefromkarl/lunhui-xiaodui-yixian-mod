using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mirror;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Domain.Save;
using Reincarnation.ExternalBridges.Spum;
using Reincarnation.Network;
using Reincarnation.Runtime.Birth;
using Reincarnation.Runtime.Core;
using Reincarnation.Runtime.Content;
using Reincarnation.Runtime.Save;
using Reincarnation.Runtime.Scenes;
using Reincarnation.UI.Windows;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MuyifengAppearance
{
    internal sealed partial class RuntimeVerification
    {
        private void VerifyCharacterChoices()
        {
            Case("unselected_local_stays_native",()=>{
                hero.ApplyLook(BirthAppearance.Default,"EQ-BLD-01");Require(!HasSkin(hero),"unselected local player replaced");hero.ApplyLook(MuyifengChoice.Token,"EQ-BLD-01");
            });
            Case("remote_selected_skin_and_choice_swap",()=>{
                native.ApplyLook(MuyifengChoice.Token,"EQ-BLD-01");hero.ApplyLook(BirthAppearance.Default,"EQ-BLD-01");
                Require(HasSkin(native)&&!HasSkin(hero),"slot or local ownership overrides character choice");
                native.ApplyLook(BirthAppearance.Default,"EQ-BLD-01");hero.ApplyLook(MuyifengChoice.Token,"EQ-BLD-01");
                Require(!HasSkin(native)&&HasSkin(hero),"choice swap leaked");
            });
            Case("remote_portrait_uses_character_choice",()=>{
                remotePortrait.Set(PortraitModel(2,false,MuyifengChoice.Token),null);Require(HasSkin(Entry.Read<GameObject>(remotePortrait,"rig")),"remote selected portrait stayed native");
                remotePortrait.Set(PortraitModel(2,false),null);Require(!HasSkin(Entry.Read<GameObject>(remotePortrait,"rig")),"remote native portrait retained skin");
            });
            Case("second_registered_choice_is_not_muyifeng",()=>{
                var other=new AppearanceChoice("test.appearance.other","测试外观",new BirthAppearance(255,84,69,83,1,0,0),BirthAppearance.Default.WithIndex(BirthAppearancePart.Cloth,4));
                try{AppearanceChoices.Register(other);Require(other.Token.IsValid,"second registered choice invalid");native.ApplyLook(other.Token,"EQ-BLD-01");Require(!HasSkin(native)&&HasSkin(hero),"another full skin was overwritten");}
                finally{AppearanceChoices.Unregister(other.Id);native.ApplyLook(BirthAppearance.Default,"EQ-BLD-01");}
            });
            Case("network_birth_wire_roundtrip",()=>{
                Require(GameManager.TryGetInstance(out var game)&&BirthDraftCatalog.TryCreate(game.Data,out var catalog,out var error),"birth catalog unavailable");
                BirthDraftCatalog.TryCreate(game.Data,out var content,out _);var draft=new BirthDraftModel(content,new PlayerSlotId(1),"AppearanceTest");
                Require(draft.TrySetAppearance(MuyifengChoice.Token,out var setError),setError);Require(draft.Validate().IsSuccess,"native birth validation rejected selected skin");
                var sent=NetworkBirthPayload.FromSelection(draft.CreateSelection());
                using(var writer=NetworkWriterPool.Get()){
                    writer.Write(sent);var bytes=writer.ToArray();
                    using(var reader=NetworkReaderPool.Get(bytes)){
                        var received=reader.Read<NetworkBirthPayload>();
                        Require(received.TryCreateSelection(2,"RemotePlayer",out var selected,out var code),"native host payload rejected: "+code);
                        Require(selected.PlayerSlot.Value==2&&selected.Appearance==MuyifengChoice.Token,"wire transfer lost identity or authoritative slot");
                    }
                    report["birth_payload_wire_bytes"]=bytes.Length;
                }
                var malformed=sent;malformed.AppearanceCloth=2;Require(!malformed.TryCreateSelection(2,"RemotePlayer",out _,out _),"malformed identity accepted by host");
                Require(!sent.TryCreateSelection(0,"RemotePlayer",out _,out _),"invalid authoritative slot accepted");
            });
            Case("two_player_native_birth_authority_and_run_save",()=>{
                Require(GameManager.TryGetInstance(out var game),"game missing");var data=game.Data;
                int tokenSequence=0;var creation=NetworkSessionAuthority.Create("appearance-isolation-test",new SessionPlayerIdentity("appearance-host","Host"),1,1000,
                    selection=>{var validation=BirthValidator.Validate(selection,data);return validation.IsSuccess?RuleResult.Success(0):RuleResult.Failure(validation.ErrorCode,0);},2,()=>"appearance-test-token-"+(++tokenSequence));
                var authority=creation.Authority;var host=creation.HostCredentials;
                Require(authority.TryJoin(new SessionPlayerIdentity("appearance-guest","Guest"),2,1001).TryGetValue(out var guest),"guest admission");
                var nativeAppearance=BirthAppearance.Default.WithIndex(BirthAppearancePart.Cloth,6);
                var births=new[]{new BirthSelection(new PlayerSlotId(1),"Host",MuyifengChoice.Token,data.Origins[0].StableId,data.Experiences[0].StableId,data.EntryReasons[0].StableId),
                    new BirthSelection(new PlayerSlotId(2),"Guest",nativeAppearance,data.Origins[0].StableId,data.Experiences[0].StableId,data.EntryReasons[0].StableId)};
                Require(authority.SubmitBirth(new SessionCommandContext(1,authority.SessionEpoch,host.NextClientSequence),births[0],1002).IsSuccess,"host selected birth rejected");
                Require(authority.SubmitBirth(new SessionCommandContext(2,authority.SessionEpoch,guest.NextClientSequence),births[1],1003).IsSuccess,"guest native birth rejected");
                var run=new RunState(new StableId("appearance.test.run"),91007,Difficulty.Standard,0,data.CreateWorldRoute(91007));
                foreach(var birth in births){Require(BirthValidator.Validate(birth,data).TryGetValue(out var validated),"validate birth");Require(run.AddPlayer(new PlayerRunState(validated)).IsSuccess,"add selected player");}
                Require(BattleAuthorityCatalogFactory.TryCreate(data,out var battles,out var battleError),battleError);
                var mapper=new RunSaveDataV1Mapper(new RunSaveCaptureMetadataV1("appearance-selftest",data.CatalogVersion,data.GameplayContentHash,SessionMode.Host),data,new CatalogRunSaveDerivedStatsResolver(data),battles);
                Require(mapper.TryCapture(run,out var saved,out var captureError),captureError);
                var copied=JsonConvert.DeserializeObject<RunSaveDataV1>(JsonConvert.SerializeObject(saved));
                Require(mapper.TryRestore(copied,out var restored,out var restoreError),restoreError);
                Require(restored.Players.Count==2&&restored.Players[0].BirthSelection.Appearance==MuyifengChoice.Token&&restored.Players[1].BirthSelection.Appearance==nativeAppearance,"run save restore merged per-player appearances");
                var snapshotConstructor=typeof(RunPlayerSnapshot).GetConstructors(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).First(c=>c.GetParameters()[0].ParameterType==typeof(PlayerRunState));
                var snapshots=restored.Players.Select(p=>(RunPlayerSnapshot)snapshotConstructor.Invoke(new object[]{p,null,null})).ToArray();
                Require(snapshots[0].Appearance==MuyifengChoice.Token&&snapshots[1].Appearance==nativeAppearance,"run snapshots lost appearance identity");
                report["two_player_saved_appearances"]=JArray.FromObject(copied.Players.Select(p=>new{slot=p.SlotId,birth=p.Birth}));
            });
            Case("native_save_fields_and_repair_roundtrip",()=>{
                var saved=new BirthSelectionSaveDataV1();AppearanceChoices.WriteSave(saved,MuyifengChoice.Token);
                var json=JsonConvert.SerializeObject(saved);var restored=JsonConvert.DeserializeObject<BirthSelectionSaveDataV1>(json);
                var repair=AccessTools.Method(AccessTools.TypeByName("Reincarnation.Domain.Save.RunSaveRepair"),"RepairAppearance");repair.Invoke(null,new object[]{restored});
                Require(AppearanceChoices.MatchesSave(restored,AppearanceChoices.Find(MuyifengChoice.Token)),"native repair erased selected identity");
                var appearance=new BirthAppearance(checked((byte)restored.AppearanceBody),checked((byte)restored.AppearanceEye),checked((byte)restored.AppearanceHair),checked((byte)restored.AppearanceFacialHair),checked((byte)restored.AppearanceCloth),checked((byte)restored.AppearancePant),checked((byte)restored.AppearanceHairColor));
                Require(appearance.IsValid&&appearance==MuyifengChoice.Token,"save reader lost identity");
                var broken=new BirthSelectionSaveDataV1{AppearanceBody=999,AppearanceEye=-1,AppearanceHair=999};repair.Invoke(null,new object[]{broken});
                Require(broken.AppearanceBody==4&&broken.AppearanceEye==0&&broken.AppearanceHair==9,"native repair behavior changed");
                report["selected_birth_save_fields"]=JObject.Parse(json);
            });
            Case("all_character_wire_and_save_roundtrips",()=>{
                Require(GameManager.TryGetInstance(out var game)&&BirthDraftCatalog.TryCreate(game.Data,out _,out _),"birth catalog unavailable");
                BirthDraftCatalog.TryCreate(game.Data,out var catalog,out _);
                var verified=new JArray();
                foreach(var choice in AppearanceChoices.All.Where(c=>SkinCatalog.All.ContainsKey(c.Id)))
                {
                    var draft=new BirthDraftModel(catalog,new PlayerSlotId(1),choice.Name);Require(draft.TrySetAppearance(choice.Token,out var error),error);
                    var sent=NetworkBirthPayload.FromSelection(draft.CreateSelection());
                    using(var writer=NetworkWriterPool.Get()){
                        writer.Write(sent);using(var reader=NetworkReaderPool.Get(writer.ToArray())){
                            var received=reader.Read<NetworkBirthPayload>();Require(received.TryCreateSelection(2,choice.Name,out var birth,out var code),code.ToString());
                            Require(birth.Appearance==choice.Token&&birth.PlayerSlot.Value==2,"wire identity or authoritative slot changed: "+choice.Id);
                        }
                    }
                    var saved=new BirthSelectionSaveDataV1();AppearanceChoices.WriteSave(saved,choice.Token);
                    var copy=JsonConvert.DeserializeObject<BirthSelectionSaveDataV1>(JsonConvert.SerializeObject(saved));
                    AccessTools.Method(AccessTools.TypeByName("Reincarnation.Domain.Save.RunSaveRepair"),"RepairAppearance").Invoke(null,new object[]{copy});
                    Require(AppearanceChoices.MatchesSave(copy,choice),"native save repair erased character: "+choice.Id);verified.Add(choice.Id);
                }
                report["all_character_wire_and_save_ids"]=verified;
            });
        }
        private sealed class CreationFlow : IBirthSceneFlow
        {
            public BirthDraftModel Draft{get;set;}
            public bool IsReady=>true;public string InitializationError=>"";public string SceneId=>"appearance.test";public string SceneName=>"AppearanceCreationTest";
            public bool TryConfirm(out string error){error="Test flow does not create a run.";return false;}
            public bool TryReturnToMenu(out string error){error="";return true;}
        }
        private IEnumerator VerifyCreationWindow()
        {
            Require(GameManager.TryGetInstance(out var game),"game unavailable");
            Require(BirthDraftCatalog.TryCreate(game.Data,out var catalog,out var error),error);
            var flow=new CreationFlow{Draft=new BirthDraftModel(catalog,new PlayerSlotId(1),"牧逸风")};
            var previous=game.CurrentSceneRoot;var scaler=canvas.GetComponent<CanvasScaler>();var reference=scaler.referenceResolution;scaler.referenceResolution=new Vector2(1920,1080);
            if(canvas.GetComponent<GraphicRaycaster>()==null)canvas.gameObject.AddComponent<GraphicRaycaster>();var stage=new GameObject("CreationVerificationStage",typeof(RectTransform));stage.transform.SetParent(canvas.transform,false);
            var stageRect=(RectTransform)stage.transform;stageRect.anchorMin=Vector2.zero;stageRect.anchorMax=Vector2.one;stageRect.offsetMin=stageRect.offsetMax=Vector2.zero;stage.SetActive(false);
            GameObject preview=null;RenderTexture texture=null;
            var siblings=canvas.transform.Cast<Transform>().Where(t=>t!=stage.transform&&t.gameObject.activeSelf).ToArray();
            var layers=Actors.SelectMany(a=>a.GetComponentsInChildren<Transform>(true)).ToDictionary(t=>t,t=>t.gameObject.layer);
            foreach(var t in siblings)t.gameObject.SetActive(false);foreach(var t in layers.Keys)t.gameObject.layer=29;
            try
            {
                game.UnregisterSceneRoot(previous);Require(game.TryRegisterSceneRoot(flow),"test creation flow registration failed");
                var template=Resources.FindObjectsOfTypeAll<BirthWindow>().First(x=>!x.gameObject.scene.IsValid());
                var window=Instantiate(template,stage.transform,false);window.name="NativeBirthWindow";
                var portraitRig=Entry.Read<GameObject>(localPortrait,"appearanceRigPrefab");preview=Instantiate(portraitRig);preview.transform.position=new Vector3(20000,0,0);
                AccessTools.Field(typeof(BirthWindow),"appearancePreviewRoot").SetValue(window,preview.transform);
                AccessTools.Field(typeof(BirthWindow),"appearanceRestored").SetValue(window,true);
                AccessTools.Field(typeof(BirthWindow),"difficultyChosenFor").SetValue(window,flow.Draft);
                window.SetFlow(flow);AccessTools.Field(typeof(BirthWindow),"appearanceRestored").SetValue(window,true);
                var previewCamera=preview.GetComponentInChildren<Camera>(true);previewCamera.enabled=false;texture=new RenderTexture(512,512,16);texture.Create();previewCamera.targetTexture=texture;previewCamera.orthographicSize=2.3f;previewCamera.transform.localPosition=new Vector3(0,1.3f,-10);
                var raw=window.GetComponentsInChildren<RawImage>(true).FirstOrDefault();if(raw!=null)raw.texture=texture;
                stage.SetActive(true);window.OnOpen();Entry.Read<Reincarnation.UI.Nodes.PartyDifficultyPicker>(window,"difficultyPicker")?.Close();Layer(stage.transform);Layer(preview.transform);previewCamera.cullingMask=1<<30;
                AccessTools.Method(typeof(BirthWindow),"RefreshView").Invoke(window,null);
                yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();Canvas.ForceUpdateCanvases();
                if(raw!=null){previewCamera.targetTexture=null;Destroy(texture);texture=new RenderTexture(768,Mathf.RoundToInt(768*raw.rectTransform.rect.height/raw.rectTransform.rect.width),16);texture.Create();previewCamera.targetTexture=texture;raw.texture=texture;}
                var nativeChoice=BirthAppearance.Default.WithIndex(BirthAppearancePart.Hair,4).WithIndex(BirthAppearancePart.Cloth,6);Require(flow.Draft.TrySetAppearance(nativeChoice,out error),error);
                Button Find(string name)=>window.GetComponentsInChildren<Button>(true).First(b=>b.name==name&&b.gameObject.activeInHierarchy);
                void Click(Button b){
                    Require(b.interactable&&b.gameObject.activeInHierarchy,"inactive creation control: "+b.name);Canvas.ForceUpdateCanvases();
                    var r=(RectTransform)b.transform;var point=RectTransformUtility.WorldToScreenPoint(camera,r.TransformPoint(r.rect.center));
                    var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=point};
                    var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
                    var hit=hits.FirstOrDefault(h=>h.gameObject.transform.IsChildOf(window.transform));
                    Require(hit.gameObject!=null&&hit.gameObject.GetComponentInParent<Button>()==b,"creation control is covered or unhittable: "+b.name+"; top hit: "+hit.gameObject?.name);
                    ExecuteEvents.Execute(b.gameObject,pointer,ExecuteEvents.pointerClickHandler);
                }
                Click(Find("AppearanceChoiceOpen"));yield return new WaitForEndOfFrame();Click(Find("AppearanceChoice."+MuyifengChoice.Id));yield return new WaitForEndOfFrame();
                Require(flow.Draft.Appearance==MuyifengChoice.Token&&HasSkin(preview),"creation selection or live preview failed");
                Require(flow.Draft.CreateSelection().Appearance==MuyifengChoice.Token,"creation submit lost selected skin");
                Require(Entry.Read<Button[]>(window,"appearanceNextButtons").Where(b=>b!=null).All(b=>!b.interactable),"native parts editable inside full skin");
                Canvas.ForceUpdateCanvases();previewCamera.Render();camera.Render();Capture(System.IO.Path.Combine(output,"birth-selected.png"));
                var selectedCharacters=new JArray();
                foreach(var choice in AppearanceChoices.All.Where(c=>SkinCatalog.All.ContainsKey(c.Id)).ToArray())
                {
                    Click(Find("AppearanceChoiceOpen"));yield return new WaitForEndOfFrame();
                    Click(Find("AppearanceGroup."+choice.Group));yield return new WaitForEndOfFrame();
                    Click(Find("AppearanceChoice."+choice.Id));yield return new WaitForEndOfFrame();
                    Require(flow.Draft.Appearance==choice.Token&&ReferenceEquals(preview.GetComponentInChildren<SkinState>(true).SkinArt,SkinCatalog.All[choice.Id]),"creation character choice failed: "+choice.Id);
                    Require(flow.Draft.CreateSelection().Appearance==choice.Token,"creation submit changed identity: "+choice.Id);
                    previewCamera.Render();camera.Render();Capture(System.IO.Path.Combine(output,"birth-character-"+choice.Id+".png"));selectedCharacters.Add(choice.Id);
                }
                report["creation_character_choices"]=selectedCharacters;
                Click(Find("AppearanceChoiceOpen"));yield return new WaitForEndOfFrame();Canvas.ForceUpdateCanvases();camera.Render();Capture(System.IO.Path.Combine(output,"birth-choice-menu.png"));Click(Find("AppearanceChoice.native"));yield return new WaitForEndOfFrame();
                Require(flow.Draft.Appearance==nativeChoice&&!HasSkin(preview),"return to native lost customization or retained full skin");
                Canvas.ForceUpdateCanvases();previewCamera.Render();camera.Render();Capture(System.IO.Path.Combine(output,"birth-native.png"));
                Click(Find("AppearanceChoiceOpen"));yield return new WaitForEndOfFrame();Click(Find("AppearanceChoice."+MuyifengChoice.Id));yield return new WaitForEndOfFrame();
                Click(Entry.Read<Button>(window,"randomAppearanceButton"));yield return new WaitForEndOfFrame();Require(AppearanceChoices.NativeValid(flow.Draft.Appearance)&&!HasSkin(preview),"native random state: "+flow.Draft.Appearance.Body+","+flow.Draft.Appearance.Eye+","+flow.Draft.Appearance.Hair+","+flow.Draft.Appearance.Cloth+"; previewSkin="+HasSkin(preview));
                Require(flow.Draft.TrySetTab(BirthDraftTab.Origin,out error),error);yield return new WaitForEndOfFrame();Require(!window.GetComponentsInChildren<Button>(true).First(b=>b.name=="AppearanceChoiceOpen").gameObject.activeInHierarchy,"choice control leaked out of info tab");
                Require(flow.Draft.TrySetTab(BirthDraftTab.Info,out error),error);yield return new WaitForEndOfFrame();
                report["creation_raycast_clicks"]=true;
                report["creation_layout"]=JArray.FromObject(Entry.Read<GameObject>(window,"infoPage").GetComponentsInChildren<RectTransform>(true).Select(t=>new{name=t.name,parent=t.parent?.name,rect=t.rect.ToString(),pos=t.anchoredPosition.ToString(),components=t.GetComponents<Component>().Select(c=>c.GetType().Name).ToArray()}));
                report["creation_controls"]=JArray.FromObject(window.GetComponentsInChildren<Button>(true).Where(b=>b.gameObject.activeInHierarchy).Select(b=>new{name=b.name,interactable=b.interactable,rect=((RectTransform)b.transform).rect.ToString(),world=b.transform.position.ToString()}));
            }
            finally
            {
                scaler.referenceResolution=reference;stage.SetActive(false);Destroy(stage);if(preview!=null)Destroy(preview);if(texture!=null)Destroy(texture);
                foreach(var t in layers)if(t.Key!=null)t.Key.gameObject.layer=t.Value;foreach(var t in siblings)if(t!=null)t.gameObject.SetActive(true);
                game.UnregisterSceneRoot(flow);if(previous!=null)Require(game.TryRegisterSceneRoot(previous),"original scene root restore failed");
            }
        }
    }
}
