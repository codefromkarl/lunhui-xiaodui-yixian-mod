using System;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.UI.Flow;
using Reincarnation.UI.Nodes;
using Reincarnation.UI.ViewModels;
using Reincarnation.UI.Windows;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        private JObject BuildAdjustmentContract()
        {
            var f=New(5,school:FiveElementRules.Lineage,buyCurrent:false,seed:91999);
            foreach(var pack in SectRegistry.Packs.Where(p=>SectRegistry.Owner(p)==FiveElementRules.Lineage&&!f.P.HasPurchasedShelfAbility(PackId(p))))f.Buy(PackId(pack));
            var old=SectRegistry.Packs.Where(p=>new[]{"yixian_five_evergreen","yixian_five_forest_sea","yixian_five_cycle_seed"}.Contains((string)p["key"])).Select(PackId).ToArray();
            Require(f.P.SetActiveCardPacks(old).IsSuccess,"five arrange missing-seal pool");
            f.StartBattle();var hand=f.Table.Hand.Select(h=>h.InstanceId).ToArray();int points=f.P.RewardPoints;long revision=f.A.Run.Revision;
            var snapshot=RunAuthoritySnapshotProjector.Create(f.A,Slot);
            var model=BattleUIPresentationMapper.BuildCardTable(snapshot,snapshot.Players.First(p=>p.Slot==Slot),data,battles);
            var controls=VisualSelfTests.BuildAdjustmentControls(model);
            Require(f.A.Run.Revision==revision&&f.P.RewardPoints==points&&f.P.ActiveCardPackIds.SequenceEqual(old),"UI recommendation changed authority before confirmation");
            foreach(var route in SwordBuilds.FiveRoutes)
            {
                var selected=SwordBuilds.Recommend(route,5,SectProgression.Owned(f.P)).Selected;
                Require(f.Action(CardTableRules.SetPacksActionId,parameter:CardTableRules.EncodePacks(selected)).IsSuccess,"five formal recommendation selection");
                Require(f.P.ActiveCardPackIds.SequenceEqual(selected)&&f.P.RewardPoints==points,"five selection changed points or picked different packs");
                Require(f.Table.Hand.Select(h=>h.InstanceId).SequenceEqual(hand),"five selection replaced existing hand");
            }
            return new JObject{["actual_prefab_controls"]=controls,["formal_selections"]=3,["points_unchanged"]=true,["hand_unchanged"]=true};
        }
    }

    internal static partial class VisualSelfTests
    {
        internal static JObject BuildAdjustmentControls(CardTableViewModel model)
        {
            // Active, isolated hierarchy lets the prefab run Awake and wire its
            // native buttons. It is destroyed synchronously, before a frame paints.
            var root=new GameObject("FiveBuildControlsTest",typeof(RectTransform),typeof(Canvas));
            try
            {
                var hud=Prefab<GameHUDWindow>();Require(hud!=null,"five recommendation HUD prefab missing");
                var source=Field<CardTablePackPanel>(hud.GetComponent<GameHUDWindow>(),"cardPackPrefab");
                var panel=UnityEngine.Object.Instantiate(source,root.transform,false);
                foreach(var raycaster in root.GetComponentsInChildren<GraphicRaycaster>(true))raycaster.enabled=false;
                int submits=0;IReadOnlyList<string> submitted=null;
                panel.Show(model,Field<CardTableArtSet>(hud.GetComponent<GameHUDWindow>(),"cardTableArt"),ids=>{submits++;submitted=ids;});
                Require(Field<TMP_Text>(panel,"summary").text.Contains("永久印记：无"),"actual five panel omitted missing-seal warning");
                Require(Field<TMP_Text>(panel,"fusionTabLabel").text=="推荐构筑","five recommendation tab missing");
                Field<Button>(panel,"fusionTab").onClick.Invoke();
                var recommendation=panel.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="SwordRecommendedBuilds").gameObject;
                Require(recommendation.activeSelf,"five recommendations not shown");
                AccessTools.Field(typeof(CardTablePackPanel),"fate").SetValue(panel,true);
                AccessTools.Method(typeof(CardTablePackPanel),"Render").Invoke(panel,null);
                Require(!recommendation.activeSelf,"stale recommendations overlap the fate page");
                AccessTools.Field(typeof(CardTablePackPanel),"fate").SetValue(panel,false);
                panel.Show(model,Field<CardTableArtSet>(hud.GetComponent<GameHUDWindow>(),"cardTableArt"),ids=>{submits++;submitted=ids;});
                foreach(var route in SwordBuilds.FiveRoutes)
                {
                    Field<Button>(panel,"fusionTab").onClick.Invoke();
                    var button=panel.GetComponentsInChildren<Button>(true).Single(b=>b.name=="Use"+route.Key);
                    Require(button.interactable,"five route button disabled");button.onClick.Invoke();
                    Require(submits==0,"five route bypassed native confirmation");
                    var expected=SwordBuilds.Recommend(route,5,model.Packs.Where(p=>!p.IsLocked).Select(p=>new StableId(p.Id))).Selected;
                    Require(Field<HashSet<string>>(panel,"selected").SetEquals(expected.Select(id=>id.Value)),"five button selected the wrong pool");
                }
                Require(panel.GetComponentsInChildren<TMP_Text>(true).Count(t=>t.name=="RouteDetails")==3,"five route panels missing");
                Field<Button>(panel,"confirm").onClick.Invoke();
                Require(submits==1&&submitted.Count==3,"five native confirmation did not submit once");
                return new JObject{["routes"]=3,["staged_only"]=true,["native_confirm_submits"]=1,["fate_page_overlay_hidden"]=true,["rendering_validated"]=false};
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
