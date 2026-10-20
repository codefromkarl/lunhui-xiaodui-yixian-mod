using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.UI.Flow;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        private JObject SwordBuildSelection()
        {
            var f=New(5,buyCurrent:false);
            var original=f.P.ActiveCardPackIds.ToArray();
            foreach(var pack in SwordRules.Packs.Where(p=>!f.P.HasPurchasedShelfAbility(PackId(p))))f.Buy(PackId(pack));
            Require(f.P.ActiveCardPackIds.SequenceEqual(original),"purchasing packs changed an established build");
            var owned=SectProgression.Owned(f.P);long rev=f.A.Run.Revision;int points=f.P.RewardPoints;
            var proposals=SwordBuilds.Routes.Select(route=>SwordBuilds.Recommend(route,5,owned)).ToArray();
            Require(proposals.All(p=>p.Selected.Count==3&&p.Missing.Count==0),"complete rank5 recommendations");
            Require(f.A.Run.Revision==rev&&f.P.RewardPoints==points&&f.P.ActiveCardPackIds.SequenceEqual(original),"recommendation mutated authoritative state");
            f.StartBattle();var hand=f.Table.Hand.Select(h=>h.InstanceId).ToArray();
            foreach(var p in proposals)
            {
                var saved=f.Action(CardTableRules.SetPacksActionId,parameter:CardTableRules.EncodePacks(p.Selected));
                Require(saved.IsSuccess&&f.P.ActiveCardPackIds.SequenceEqual(p.Selected),"formal apply failed");
                Require(f.Table.Hand.Select(h=>h.InstanceId).SequenceEqual(hand),"apply replaced hand");
            }
            var mapper=new Reincarnation.Domain.Save.RunSaveDataV1Mapper(new Reincarnation.Domain.Save.RunSaveCaptureMetadataV1("build-selftest",data.CatalogVersion,data.GameplayContentHash,SessionMode.Solo),data,new Reincarnation.Runtime.Save.CatalogRunSaveDerivedStatsResolver(data),battles);
            Require(mapper.TryCapture(f.A.Run,out var captured,out var error),"capture build: "+error);
            Require(mapper.TryRestore(captured,out var restored,out error),"restore build: "+error);
            Require(restored.Players[0].ActiveCardPackIds.SequenceEqual(proposals.Last().Selected),"saved build changed on restore");
            var snapshot=RunAuthoritySnapshotProjector.Create(f.A,Slot);var player=snapshot.Players.First(p=>p.Slot==Slot);
            var model=BattleUIPresentationMapper.BuildCardTable(snapshot,player,data,battles);
            var visual=VisualSelfTests.SwordRecommendations(Path.Combine(Path.GetDirectoryName(path),Path.GetFileNameWithoutExtension(path),"sword-builds"),model);
            return new JObject{["purchase_preserved_selection"]=true,["formal_presets"]=3,["hand_unchanged"]=true,["memory_save_restore"]=true,["visual"]=visual};
        }
        private JObject SwordRouteBenchmarks()
        {
            var rows=new JArray();
            // Comparable fully-owned rank5 fixtures. This is an effect benchmark, not a win-rate claim.
            // Resolve by explicit keys below: only cards in each proposed pool are used.
            var keys=new[]{new[]{"云剑·柔心","云剑·闪风","云剑·灵海","云剑·游龙","云剑·凌波"},
                new[]{"云舞诀","灵感剑","剑意激荡","水月剑阵","千刃剑阵"},
                new[]{"百鸟灵剑诀","巨象灵剑","飞灵闪影剑","迅影飞剑","万法归灵剑"}};
            DeterministicRngState? playRandom=null;
            for(int i=0;i<SwordBuilds.Routes.Count;i++)
            {
                var f=New(5,buyCurrent:false,seed:75101);foreach(var pack in SwordRules.Packs.Where(p=>!f.P.HasPurchasedShelfAbility(PackId(p))))f.Buy(PackId(pack));
                var route=SwordBuilds.Recommend(SwordBuilds.Routes[i],5,SectProgression.Owned(f.P));
                Require(f.P.SetActiveCardPacks(route.Selected).IsSuccess,"benchmark configure");
                f.System(RunDeveloperCommand.SetAttribute,(int)ExchangeAttribute.Energy,50);
                f.System(RunDeveloperCommand.SetAttribute,(int)ExchangeAttribute.Reaction,50);
                f.System(RunDeveloperCommand.SetAttribute,(int)ExchangeAttribute.Muscle,50);
                f.StartBattle();var target=f.Enemy;ToughEnemy(target);
                var cards=keys[i].Select(name=>new StableId(SwordRules.Cards.Single(c=>(string)c.Value["name"]==name).Key)).ToArray();
                f.Prepare(cards);f.System(RunDeveloperCommand.SetVitals,int.MinValue,int.MinValue,3);
                if(!playRandom.HasValue)playRandom=f.A.Run.CaptureRandomState();else ((DeterministicRng)AccessTools.Property(typeof(RunState),"Random").GetValue(f.A.Run)).RestoreState(playRandom.Value);
                int qi=f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting),before=f.Table.Hand.Count;
                foreach(var card in cards)f.Play(card);
                rows.Add(new JObject{["route"]=route.Route.Name,["packs"]=new JArray(route.Selected.Select(id=>id.Value)),["sequence"]=new JArray(keys[i]),["damage"]=10000-target.CurrentHealth,["shield"]=f.Actor.Shield,["qi_before"]=qi,["qi_after"]=f.Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting),["hand_before"]=before,["hand_after"]=f.Table.Hand.Count,["ap_after"]=f.Actor.ActionPoints});
            }
            return new JObject{["scope"]="same rank/effective attribute settings/target seed, same pre-play RNG restored after draw preparation, five-card sequences without fates; not win-rate or optimal-play evidence",["routes"]=rows};
        }
    }
}
