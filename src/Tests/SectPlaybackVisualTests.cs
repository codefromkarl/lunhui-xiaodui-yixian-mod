using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Gameplay.Combat;
using Reincarnation.Presentation.Combat;
using Reincarnation.UI;
using Reincarnation.Runtime.Core;
using TMPro;
using UnityEngine;

namespace YixianSect
{
    // Test-only sink: native presenter and recipe renderer, isolated target proxies.
    // Does not register as a live authority, touch global RNG, or replay commands.
    internal sealed class SectPlaybackRecorder : MonoBehaviour,IVfxPlayer,ICombatNumberPlayer
    {
        internal readonly JArray Effects=new JArray(),Numbers=new JArray();
        internal readonly List<GameObject> Visuals=new List<GameObject>();
        internal TMP_FontAsset Font;
        public bool IsAvailable=>true;
        public void Play(CombatVfxRequest r)
        {
            Effects.Add(new JObject{["time"]=PresentationClock.Now,["style"]=r.StyleOverride.ToString(),["cast"]=r.IsCast,["card"]=r.ContentId.Value});
            var go=new GameObject("Native "+r.StyleOverride,typeof(MeshFilter),typeof(MeshRenderer));go.layer=30;go.transform.SetParent(transform,false);go.transform.position=r.HasWorldPosition?r.WorldPosition:r.Anchor.position;go.transform.localScale=Vector3.one*Mathf.Max(.7f,r.TargetHeight*r.Scale);
            var visual=go.AddComponent<CombatVfxVisual>();visual.Configure(r.StyleOverride,new Color(.25f,.85f,1),Color.white,r.Lifetime,Effects.Count);visual.Begin(r);go.GetComponent<MeshRenderer>().sortingOrder=1500;Visuals.Add(go);
        }
        public void Play(CombatNumberRequest r)
        {
            Numbers.Add(new JObject{["time"]=PresentationClock.Now,["amount"]=r.Amount,["kind"]=r.Kind.ToString()});
            var go=new GameObject("Native number request");go.layer=30;go.transform.SetParent(transform,false);go.transform.position=r.Anchor.position+new Vector3((Numbers.Count%3-1)*.25f,.75f+Numbers.Count*.10f,-.1f);
            var text=go.AddComponent<TextMeshPro>();text.font=Font;text.text=r.Amount.ToString();text.fontSize=4;text.color=Color.white;text.alignment=TextAlignmentOptions.Center;text.rectTransform.sizeDelta=new Vector2(2,1);go.GetComponent<MeshRenderer>().sortingOrder=1600;Visuals.Add(go);
        }
        public void StopAll(){foreach(var go in Visuals)if(go!=null)UnityEngine.Object.Destroy(go);Visuals.Clear();}
    }
    public sealed partial class RuntimeSelfTests
    {
        private IEnumerator DepthPlayback(Action<string> observe,Func<bool> unchanged,JObject saves)
        {
            var row=new JObject{["name"]="native_paced_attack_playback",["status"]="running"};cases.Add(row);report["activeCase"]=(string)row["name"];Write();
            var enumerator=PlaybackFrames(row);Exception failure=null;
            while(true)
            {
                bool more=false;try{more=enumerator.MoveNext();}catch(Exception ex){failure=ex;}
                if(failure!=null||!more)break;yield return enumerator.Current;
            }
            (enumerator as IDisposable)?.Dispose();
            row["status"]=failure==null?"passed":"failed";if(failure!=null){row["error"]=failure.ToString();context.Warn("[SelfTest] playback："+failure.Message);}
            report.Remove("activeCase");CompleteDepthOrSuite(observe,unchanged(),saves);
        }
        private IEnumerator PlaybackFrames(JObject row)
        {
            var output=new JArray();row["evidence"]=output;
            foreach(var sample in new[]{(school:SwordRules.Lineage,name:"流云乱剑",hits:4),(school:FiveElementRules.Lineage,name:"木灵·芽",hits:2),(school:SevenStarRules.Lineage,name:"五雷轰顶",hits:5)})
            {
                var f=sample.school==SwordRules.Lineage?ReplacementFixture(37):New(sample.school==FiveElementRules.Lineage?1:5,school:sample.school);
                if(sample.school!=SwordRules.Lineage)f.StartBattle();
                foreach(var enemy in f.B.Battle.Combatants.Where(u=>u.Side==CombatSide.Enemy&&!u.IsDowned))ToughEnemy(enemy);
                if(sample.school==SevenStarRules.Lineage)
                {
                    var generator=new StableId(SectRegistry.Cards.Single(c=>SectRegistry.Owner(c.Value)==sample.school&&(string)c.Value["name"]=="震卦").Key);
                    for(int i=0;i<5;i++){f.Prepare(generator);f.Play(generator);}
                }
                var card=SectRegistry.Cards.Single(c=>SectRegistry.Owner(c.Value)==sample.school&&(string)c.Value["name"]==sample.name);var id=new StableId(card.Key);f.Prepare(id);
                var before=RunAuthoritySnapshotProjector.Create(f.A,Slot);var receipt=f.Play(id);
                var expected=BattlePresentationResultProjector.Project(receipt);int expectedDamage=expected.Count(e=>e.Kind==BattlePresentationResultKind.Damage);
                Require(expected.Count(e=>e.Kind==BattlePresentationResultKind.ActionResolved)==sample.hits,"isolated fixture did not produce requested real segments "+sample.name);
                var root=new GameObject("Isolated native paced replay");root.SetActive(false);
                var cameraGo=new GameObject("Native replay camera");var camera=cameraGo.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=2.5f;camera.transform.position=new Vector3(0,.5f,-10);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.04f,.055f,.075f);camera.cullingMask=(1<<30)|(1<<31);
                var texture=new RenderTexture(1100,600,24);camera.targetTexture=texture;
                var directory=Path.Combine(Path.GetDirectoryName(path),"visual","playback",card.Value["key"].ToString());Directory.CreateDirectory(directory);
                float previousCapture=Time.captureDeltaTime;Time.captureDeltaTime=1f/30;
                try
                {
                    var spawn=root.AddComponent<BattleSpawnCoordinator>();var dictionary=(IDictionary)AccessTools.Field(typeof(BattleSpawnCoordinator),"activeViews").GetValue(spawn);var record=typeof(BattleSpawnCoordinator).GetNestedType("ActiveViewRecord",System.Reflection.BindingFlags.NonPublic);
                    int side=0;
                    foreach(var snapshot in before.Battle.Combatants.Where(c=>c.RuntimeId==f.Actor.RuntimeId||c.Side==CombatSide.Enemy))
                    {
                        var anchor=new GameObject(snapshot.Side==CombatSide.Player?"Caster anchor":"Target anchor");anchor.transform.SetParent(root.transform,false);
                        anchor.transform.position=snapshot.Side==CombatSide.Player?new Vector3(-2,0,0):new Vector3(1.2f+(side++)*.6f,.6f-(side-1)*.6f,0);
                        var go=new GameObject("Native view proxy");go.layer=30;go.transform.SetParent(anchor.transform,false);var view=go.AddComponent<BattleCombatantView>();view.Apply(snapshot,0,1,0);
                        var sprite=go.AddComponent<SpriteRenderer>();sprite.sprite=UIContentIconLibrary.GetOrRequest("icon.lineage.yixian_sword",null);sprite.color=snapshot.Side==CombatSide.Player?new Color(.65f,.9f,1):new Color(1,.7f,.6f);go.transform.localScale=Vector3.one;
                        dictionary.Add(snapshot.RuntimeId,Activator.CreateInstance(record,new object[]{"isolated.test",view}));
                    }
                    var bridge=root.AddComponent<BattlePresentationResultBridge>();AccessTools.Field(typeof(BattlePresentationResultBridge),"spawnCoordinator").SetValue(bridge,spawn);
                    var sink=root.AddComponent<SectPlaybackRecorder>();sink.Font=Resources.FindObjectsOfTypeAll<TMP_FontAsset>().First();
                    var presenter=root.AddComponent<CombatFeedbackPresenter>();
                    AccessTools.Field(typeof(CombatFeedbackPresenter),"spawnCoordinator").SetValue(presenter,spawn);AccessTools.Field(typeof(CombatFeedbackPresenter),"resultBridge").SetValue(presenter,bridge);AccessTools.Field(typeof(CombatFeedbackPresenter),"vfxPlayerSource").SetValue(presenter,sink);AccessTools.Field(typeof(CombatFeedbackPresenter),"numberPlayerSource").SetValue(presenter,sink);
                    var delivered=new JArray();bridge.ResultReady+=e=>{if(e.Kind==BattlePresentationResultKind.ActionResolved||e.Kind==BattlePresentationResultKind.Damage)delivered.Add(new JObject{["time"]=PresentationClock.Now,["kind"]=e.Kind.ToString(),["ordinal"]=e.ActionOrdinal,["amount"]=e.Amount});};
                    root.SetActive(true);bridge.ApproachResolver=presenter;camera.Render();yield return null;yield return null;bridge.Project(receipt);
                    float start=PresentationClock.Now,next=0;int frame=0;
                    while(PresentationClock.Now-start<2.5f && (delivered.OfType<JObject>().Count(e=>(string)e["kind"]=="ActionResolved")<sample.hits || PresentationClock.Now-(float)delivered.OfType<JObject>().Last(e=>(string)e["kind"]=="ActionResolved")["time"]<.35f))
                    {
                        if(PresentationClock.Now-start>=next)
                        {
                            camera.Render();var active=RenderTexture.active;RenderTexture.active=texture;var image=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(directory,"frame-"+(frame++).ToString("D3")+".png"),image.EncodeToPNG());UnityEngine.Object.Destroy(image);RenderTexture.active=active;next+=.08f;
                        }
                        yield return null;
                    }
                    File.WriteAllText(Path.Combine(directory,"raw-playback.json"),new JObject{["events"]=delivered,["effects"]=sink.Effects,["numbers"]=sink.Numbers}.ToString());
                    var impacts=delivered.OfType<JObject>().Where(e=>(string)e["kind"]=="ActionResolved").ToArray();
                    Require(impacts.Length==sample.hits,"native playback lost real attack segments "+sample.name);
                    Require(impacts.Zip(impacts.Skip(1),(a,b)=>(float)b["time"]-(float)a["time"]).All(dt=>dt>.05f),"multi-hit damage was delivered on one frame");
                    Require(sink.Effects.OfType<JObject>().Count(e=>(bool)e["cast"])==1,"multi-hit must start one cast, not recast for every segment");
                    Require(sink.Numbers.Count>=sample.hits,"multi-hit numbers not delivered per impact");
                    Require(sink.Effects.OfType<JObject>().Count(e=>!(bool)e["cast"]&&(string)e["style"]==SectAttackPresentation.Style(card.Value))==expectedDamage,"native school effects missing or doubled");
                    var evidence=new JObject{["card"]=sample.name,["hits"]=sample.hits,["successful_damage_events"]=expectedDamage,["events"]=delivered,["effects"]=sink.Effects,["numbers"]=sink.Numbers,["frames"]=frame,["directory"]=directory,["scope"]="native receipt queue, presenter and effect recipe; isolated target proxies, no campaign commands replayed"};
                    File.WriteAllText(Path.Combine(directory,"playback.json"),evidence.ToString());output.Add(evidence);
                }
                finally{Time.captureDeltaTime=previousCapture;root.SetActive(false);UnityEngine.Object.Destroy(root);camera.targetTexture=null;texture.Release();UnityEngine.Object.Destroy(texture);UnityEngine.Object.Destroy(cameraGo);}
            }
        }
    }
}
