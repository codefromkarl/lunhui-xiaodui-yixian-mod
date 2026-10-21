using System;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.UI.Flow;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        private JObject FiveFixedDeckContract()
        {
            var f=New(1,school:FiveElementRules.Lineage,buyCurrent:false);var original=FiveDeckRules.Cards(f.P).ToArray();var rows=new JArray();
            var opening=RunAuthoritySnapshotProjector.Create(f.A,Slot);int startPoints=f.P.RewardPoints;long startRevision=f.A.Run.Revision;
            var controls=VisualSelfTests.FiveUpgradeControls(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path),"five-learn"),opening.Players.First(p=>p.Slot==Slot),opening,data,battles);
            Require(f.A.Run.Revision==startRevision&&f.P.RewardPoints==startPoints,"learning UI changed authority");
            for(int rank=2;rank<=5;rank++)
            {
                var oldLibrary=FiveDeckRules.Unlocked(f.P).ToArray();int points=f.P.RewardPoints;long revision=f.A.Run.Revision;
                var candidates=FiveDeckRules.Candidates(rank).ToArray();
                var array=rank==3?candidates.First(c=>(int?)c["rules"]["array"]==2):null;
                var added=(array==null?candidates.Take(2):new[]{array}.Concat(candidates.Where(c=>c!=array).Take(1))).Select(FiveDeckRules.CardId).ToArray();
                var answer=FiveDeckRules.EncodeUnlock(rank,added);
                foreach(var bad in new[]{default(StableId),new StableId("five-learn~"+rank+"~bad")})
                {
                    Require(!f.Send(RunCommand.PurchaseOrUpgradeLineage(Slot,new StableId(FiveElementRules.Lineage),revision,bad)).IsSuccess,"invalid learning accepted");
                    Require(f.A.Run.Revision==revision&&f.P.RewardPoints==points&&FiveDeckRules.Unlocked(f.P).SequenceEqual(oldLibrary)&&FiveDeckRules.Cards(f.P).SequenceEqual(original),"invalid learning changed state");
                }
                Require(f.Send(RunCommand.PurchaseOrUpgradeLineage(Slot,new StableId(FiveElementRules.Lineage),revision,answer)).IsSuccess,"formal learning purchase");
                Require(FiveDeckRules.Unlocked(f.P).Count==8+2*(rank-1)&&oldLibrary.All(FiveDeckRules.Unlocked(f.P).Contains)&&added.All(FiveDeckRules.Unlocked(f.P).Contains),"permanent library growth");
                Require(FiveDeckRules.Cards(f.P).SequenceEqual(original),"breakthrough automatically changed deck");
                data.TryGetLineage(new StableId(FiveElementRules.Lineage),out var lineage);Require(points-f.P.RewardPoints==lineage.Steps[rank-1].Price,"extra card fee");
                Require(!f.Send(RunCommand.PurchaseOrUpgradeLineage(Slot,new StableId(FiveElementRules.Lineage),revision,answer)).IsSuccess,"stale learning answer accepted");
                rows.Add(new JObject{["rank"]=rank,["unlocked"]=FiveDeckRules.Unlocked(f.P).Count,["active"]=8});
            }
            f.StartBattle();foreach(var enemy in f.B.Battle.Combatants.Where(u=>u.Side==CombatSide.Enemy))ToughEnemy(enemy);var library=FiveDeckRules.Unlocked(f.P).ToArray();var next=library.Skip(8).ToArray();
            var hand=f.Table.Hand.Select(h=>h.InstanceId).ToArray();int qi=f.Actor.CurrentMagic,ap=f.Actor.ActionPoints,before=f.P.RewardPoints;long rev=f.A.Run.Revision;
            var rng=f.A.Run.CaptureRandomState();var state=JToken.FromObject(SchoolState(f).Entries);
            Require(f.Action(CardTableRules.SetPacksActionId,parameter:FiveDeckRules.EncodeSelection(next)).IsSuccess,"battle cannot save eight unlocked cards");
            Require(FiveDeckRules.Cards(f.P).SequenceEqual(next)&&FiveDeckRules.Unlocked(f.P).SequenceEqual(library),"save lost selection/library");
            Require(f.Table.Hand.Select(h=>h.InstanceId).SequenceEqual(hand)&&f.Actor.CurrentMagic==qi&&f.Actor.ActionPoints==ap&&f.P.RewardPoints==before&&f.A.Run.CaptureRandomState().Equals(rng)&&JToken.DeepEquals(state,JToken.FromObject(SchoolState(f).Entries)),"save changed hand/resources/RNG/battle state");
            Require(!f.Send(RunCommand.SubmitBattleAction(Slot,CombatActionKind.UseAbility,CardTableRules.SetPacksActionId,default,FormationRow.None,-1,rev,FiveDeckRules.EncodeSelection(original))).IsSuccess&&FiveDeckRules.Cards(f.P).SequenceEqual(next),"stale save changed deck");
            var unlearned=FiveDeckRules.Candidates(5).Select(FiveDeckRules.CardId).First(id=>!library.Contains(id));rev=f.A.Run.Revision;
            foreach(var bad in new[]{next.Take(7).ToArray(),library.Take(9).ToArray(),next.Take(7).Append(unlearned).ToArray(),next.Take(7).Append(next[0]).ToArray()})
                Require(!f.Action(CardTableRules.SetPacksActionId,parameter:FiveDeckRules.EncodeSelection(bad)).IsSuccess&&f.A.Run.Revision==rev&&FiveDeckRules.Cards(f.P).SequenceEqual(next),"invalid battle selection committed");
            // Draw and consume a real array, then remove/re-add it through the same
            // selection command. Editing must not reset consumed/persistent state.
            var retained=f.Table.Hand.First(h=>!next.Contains(h.CardId)).CardId;
            f.System(RunDeveloperCommand.SetVitals,int.MinValue,f.Actor.MaximumMagic,1000);f.Play(retained);
            var waterArray=next.First(id=>(int?)SectRegistry.Cards[id.Value]["rules"]["array"]==2);
            f.System(RunDeveloperCommand.SetVitals,int.MinValue,f.Actor.MaximumMagic,1000);
            for(int i=0;i<160&&!f.Table.Hand.Any(h=>h.CardId==waterArray);i++)
            {
                if(f.Table.Hand.Count>=10)Require(f.Action(CardTableRules.DiscardActionId,parameter:CardTableRules.EncodePlay(f.Table.Hand[0].InstanceId)).IsSuccess,"fixture discard");
                Require(f.Action(CardTableRules.DrawActionId).IsSuccess,"fixture draw array");
            }
            Require(f.Table.Hand.Any(h=>h.CardId==waterArray),"array not drawn within fixed budget");f.Play(waterArray);
            state=JToken.FromObject(SchoolState(f).Entries);hand=f.Table.Hand.Select(h=>h.InstanceId).ToArray();
            var without=next.Where(id=>id!=waterArray).Append(original[0]).ToArray();
            Require(f.Action(CardTableRules.SetPacksActionId,parameter:FiveDeckRules.EncodeSelection(without)).IsSuccess&&f.Action(CardTableRules.SetPacksActionId,parameter:FiveDeckRules.EncodeSelection(next)).IsSuccess,"array swap roundtrip");
            Require(JToken.DeepEquals(state,JToken.FromObject(SchoolState(f).Entries))&&f.Table.Hand.Select(h=>h.InstanceId).SequenceEqual(hand),"editing reset consumed array or replaced hand");
            var players=(System.Collections.IDictionary)AccessTools.Field(typeof(BattleAuthorityService),"players").GetValue(f.B);
            var pool=(System.Collections.Generic.List<StableId>)AccessTools.Method(typeof(BattleAuthorityService),"CardDrawPool").Invoke(f.B,new[]{players[Slot]});
            Require(!pool.Contains(waterArray),"consumed array re-entered draw pool");
            var mapper=new Reincarnation.Domain.Save.RunSaveDataV1Mapper(new Reincarnation.Domain.Save.RunSaveCaptureMetadataV1("five-library",data.CatalogVersion,data.GameplayContentHash,SessionMode.Solo),data,new Reincarnation.Runtime.Save.CatalogRunSaveDerivedStatsResolver(data),battles);
            Require(mapper.TryCapture(f.A.Run,out var saved,out var error),"library capture: "+error);Require(mapper.TryRestore(saved,out var restored,out error),"library restore: "+error);
            Require(FiveDeckRules.Cards(restored.Players[0]).SequenceEqual(next)&&FiveDeckRules.Unlocked(restored.Players[0]).SequenceEqual(library),"saved library/selection lost");
            var snap=RunAuthoritySnapshotProjector.Create(f.A,Slot);var player=snap.Players.First(p=>p.Slot==Slot);var model=BattleUIPresentationMapper.BuildCardTable(snap,player,data,battles);
            Require(model.Packs.Single().Cards.Select(c=>new StableId(c.CardId)).SequenceEqual(next),"HUD not showing actual eight");
            var editor=VisualSelfTests.FiveEditorControls(model);
            return new JObject{["learning_controls"]=controls,["editor_controls"]=editor,["progression"]=rows,["free_battle_selection"]=true,["hand_resources_rng_unchanged"]=true,["unequipped_hand_card_still_playable"]=true,["persistent_consumption_preserved"]=true,["native_save_restore"]=true};
        }
    }
}
