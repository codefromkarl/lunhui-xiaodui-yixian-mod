using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Gameplay.Combat;
using Reincarnation.UI.Flow;
using Reincarnation.Presentation.Combat;
using UnityEngine;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        private JObject SectDepth()
        {
            var bindings=Resources.FindObjectsOfTypeAll<Reincarnation.Config.ProjectContentManifest>().Where(m=>m.VendorAssetMap!=null).SelectMany(m=>m.VendorAssetMap.Bindings).Select(b=>b.StableKey).ToArray();
            var effectKeys=SectRegistry.Cards.Values.Select(c=>SectAttackPresentation.Key(SectAttackPresentation.Style(c))).Distinct().ToArray();
            Require(effectKeys.All(bindings.Contains),"native VFX carrier not registered: "+string.Join(",",effectKeys.Where(k=>!bindings.Contains(k))));
            var f=ReplacementFixture(37);var before=RunAuthoritySnapshotProjector.Create(f.A,Slot);var player=before.Players.First(p=>p.Slot==Slot);
            long revision=f.A.Run.Revision;
            var rng=(DeterministicRng)HarmonyLib.AccessTools.Property(typeof(RunState),"Random").GetValue(f.A.Run);var random=rng.CaptureState();
            var model=BattleUIPresentationMapper.BuildCardTable(before,player,data,battles);
            Require(revision==f.A.Run.Revision&&random.Equals(rng.CaptureState()),"decision preview changed authoritative revision or RNG");
            var receipt=f.Play(Card(37));var events=BattlePresentationResultProjector.Project(receipt);
            var rows=new JArray(receipt.PresentationSteps.Select(s=>new JObject{["actor"]=s.SourceActorRuntimeId.Value,["action"]=s.ActionId.Value,["targets"]=s.Targets.Count}));
            Require(events.Count(e=>e.Kind==BattlePresentationResultKind.ActionResolved)==4,"four native attack starts missing");
            Require(events.Where(e=>e.HasDamageResolution).All(e=>e.ContentId==Card(37)),"multi-hit lost authored card cosmetics");
            foreach(var route in FiveBuildGuidance.Routes)
            {
                var unlocked=FiveDeckRules.Candidates(1).Select(FiveDeckRules.CardId).ToList();
                for(int rank=2;rank<=5;rank++)
                {
                    var candidates=FiveDeckRules.Candidates(rank).Select(FiveDeckRules.CardId).ToArray();
                    var learned=FiveBuildGuidance.Suggest(route,candidates,2,rank);
                    Require(learned.Count==2&&learned.All(candidates.Contains),"recommendation crosses learning rank");unlocked.AddRange(learned);
                    var selected=FiveBuildGuidance.Suggest(route,unlocked,8,rank);
                    Require(selected.Count==8&&selected.Distinct().Count()==8&&selected.All(unlocked.Contains),"recommended eight grants unavailable card");
                }
            }
            Require(SwordBuilds.RoutesFor(SevenStarRules.Lineage).Count==3,"star recommendations missing");
            var st=New(5,school:SevenStarRules.Lineage);st.StartBattle();ToughEnemy(st.Enemy);
            var starId=SectRegistry.Cards.Single(c=>SectRegistry.Owner(c.Value)==SevenStarRules.Lineage&&(string)c.Value["name"]=="震卦").Key;
            st.Prepare(new StableId(starId));st.Play(new StableId(starId));st.Prepare(new StableId(starId));st.Play(new StableId(starId));
            var ss=RunAuthoritySnapshotProjector.Create(st.A,Slot);var sm=BattleUIPresentationMapper.BuildCardTable(ss,ss.Players.First(p=>p.Slot==Slot),data,battles);
            Require(sm.Hand.All(c=>c.Detail.Contains("本次第3格")),"actual star HUD lost next-slot hints");
            var post= new StableId(SectRegistry.Cards.Single(c=>SectRegistry.Owner(c.Value)==SevenStarRules.Lineage&&(string)c.Value["name"]=="藕断丝连").Key);
            st.Prepare(post);st.Play(post);st.Prepare(post);st.Play(post);st.Prepare(post);
            var afterPost=RunAuthoritySnapshotProjector.Create(st.A,Slot);var postActor=afterPost.Battle.Combatants.First(c=>c.RuntimeId==st.Actor.RuntimeId);
            Require(SectDecisionHints.Counter(postActor,SevenStarRules.Lineage,"healingTotal")>0,"native hidden FlatAmount readout lost actual generated healing");
            var postModel=BattleUIPresentationMapper.BuildCardTable(afterPost,afterPost.Players.First(p=>p.Slot==Slot),data,battles);
            Require(postModel.Hand.First(c=>c.CardId==post.Value).Detail.Contains("本次触发后招"),"native source-use readout lost post condition");

            var visual=VisualSelfTests.DecisionControls(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path),"visual","decisions"),model,sm);

            return new JObject{["native_vfx_keys"]=JArray.FromObject(effectKeys),["visual"]=visual,["steps"]=rows,["events"]=new JArray(events.Select(e=>new JObject{["kind"]=e.Kind.ToString(),["card"]=e.ContentId.Value,["ordinal"]=e.ActionOrdinal,["amount"]=e.Amount})),["five_routes_reachable"]=true,["star_routes"]=3,["presenters"]=new JArray(Resources.FindObjectsOfTypeAll<CombatFeedbackPresenter>().Select(x=>x.name)),["spawns"]=new JArray(Resources.FindObjectsOfTypeAll<BattleSpawnCoordinator>().Select(x=>x.name)),["views"]=new JArray(Resources.FindObjectsOfTypeAll<BattleCombatantView>().Select(x=>x.name))};
        }
    }
}
