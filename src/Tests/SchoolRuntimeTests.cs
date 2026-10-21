using System;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;
using Reincarnation.UI.Flow;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        private static StableId SchoolCard(string key) => new StableId(SectRegistry.CardPrefix + key);
        private static StableId SchoolSource(string school,int source)=>new StableId(SectRegistry.Cards.Single(p=>SectRegistry.Owner(p.Value)==school&&(string)p.Value["sourceId"]==source.ToString()).Key);
        private static ModStateSnapshot SchoolState(Fixture fixture) => (ModStateSnapshot)AccessTools.Field(typeof(BattleAuthorityService), "modState").GetValue(fixture.B);
        private static int SchoolValue(Fixture fixture, string school, string key) => SectRegistry.Read(SchoolState(fixture), SectRegistry.StateKey(school, fixture.Actor.RuntimeId, key));
        private static void RequireFiveStatuses(Fixture fixture, int mask)
        {
            var keys = new[] { "metal", "water", "wood", "fire", "earth" };
            for (int i = 0; i < keys.Length; i++)
                Require(fixture.Actor.Statuses.Any(s => s.StatusId.Value == "yixian.sect.status.five." + keys[i] && s.StackCount == 1) == ((mask & (1 << i)) != 0), "five status mismatch: " + keys[i]);
            Require(!fixture.Actor.Statuses.Any(s => s.StatusId.Value == "yixian.sect.status.five.elements"), "legacy count-only badge remains");
        }
        private Fixture SchoolFixture(string school, bool visual = false,int rank=1,StableId required=default)
        {
            var fixture = New(rank,school: school, buyCurrent: true);
            Require(fixture.P.ActiveCardPackIds.Count >= (school==FiveElementRules.Lineage?1:2) && SectProgression.Owned(fixture.P).All(id => (id==FiveDeckRules.PackId?FiveElementRules.Lineage:SectRegistry.Owner(SectRegistry.FindPack(id))) == school), "wrong starter pool");
            Require(fixture.P.SetActiveCardPacks(fixture.P.ActiveCardPackIds.Reverse().ToArray()).IsSuccess, "school active selection failed");
            if(!required.IsEmpty)Require(fixture.P.SetActiveCardPacks(TestCardPool.ForCards(fixture.P,new[]{required})).IsSuccess,"source card pack enabled before battle");
            fixture.StartBattle(); Require(fixture.Table.Hand.Count == 3, "school opening hand");
            Require(fixture.Table.Hand.All(h => SectRegistry.Cards.TryGetValue(h.CardId.Value, out var card) && SectRegistry.Owner(card) == school), "foreign card drawn");
            var snapshot = RunAuthoritySnapshotProjector.Create(fixture.A,Slot); var player = snapshot.Players.First(p => p.Slot == Slot);
            var model = BattleUIPresentationMapper.BuildCardTable(snapshot, player, data, battles);
            Require(model.Packs.Count == (school==FiveElementRules.Lineage?1:school==SevenStarRules.Lineage?10:12) && model.Packs.All(p => p.Cards.Count == (school==FiveElementRules.Lineage?8:4) && p.CardDetails.Contains("消耗")), "school actual pack projection/details");
            if (visual) VisualSelfTests.Run(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), System.IO.Path.GetFileNameWithoutExtension(path), school), model, player, snapshot, data, battles, model.Packs[0].Id, false);
            ToughEnemy(fixture.Enemy);
            return fixture;
        }
        private JObject FiveElementLoop()
        {
            var school=FiveElementRules.Lineage;var f=SchoolFixture(school,true);
            var seal=SchoolSource(school,7000001);var needle=SchoolSource(school,7000002);
            f.Prepare(seal,needle);f.Play(seal);Require(SchoolValue(f,school,"active")==1,"metal seal did not activate metal");
            f.Play(needle);Require(SchoolValue(f,school,"active")==1&&SchoolValue(f,school,"sharp")==2,"original metal needle effect or permanent activation incorrect");
            RequireFiveStatuses(f,1);MemoryRoundTrip(f,school,"active",1);
            var ordinary=SchoolFixture(school);var sprout=SchoolSource(school,7000004);ordinary.Prepare(sprout);ordinary.Play(sprout);
            Require(SchoolValue(ordinary,school,"active")==0,"ordinary elemental card incorrectly permanently activates element");
            return new JObject{["real_seal_activation"]=true,["sharp_gain"]=2,["ordinary_card_not_a_seal"]=true,["memory_save_restore"]=true};
        }
        private JObject SevenStarLoop()
        {
            var school=SevenStarRules.Lineage;var f=SchoolFixture(school,true);
            var shock=SchoolSource(school,4000003);var map=SchoolSource(school,4000008);var clamp=SchoolSource(school,4000010);var thunder=SchoolSource(school,4000004);
            f.Prepare(shock,map,clamp,thunder);f.Play(shock);f.Play(map);f.Play(clamp);
            Require(SchoolValue(f,school,"star")==1&&SchoolValue(f,school,"slot")==3,"original permanent star/slot3 effect");
            Require(SchoolValue(f,school,"omen")==1,"unrelated star attack consumed omen");
            f.Play(thunder);Require(SchoolValue(f,school,"omen")==0&&SchoolValue(f,school,"star")==1,"random attack consumes one omen but not star power");
            f.NextRound();Require(SchoolValue(f,school,"plays")==4,"virtual eight-slot sequence incorrectly reset each host round");
            ToughEnemy(f.Enemy);
            var incessant=SchoolSource(school,4000013);var sparrow=SchoolSource(school,4000061);
            f.Prepare(incessant);f.System(RunDeveloperCommand.SetVitals,f.Actor.MaximumHealth-20,int.MinValue,int.MinValue);
            int beforeHealth=f.Actor.CurrentHealth;int beforeEnemy=f.Enemy.CurrentHealth;
            f.Play(incessant);
            Require(f.Actor.CurrentHealth==beforeHealth&&f.Enemy.CurrentHealth<beforeEnemy,"Incessant first use must attack without healing or omen");
            f.Prepare(incessant);f.Play(incessant);
            Require(f.Actor.CurrentHealth>beforeHealth&&SchoolValue(f,school,"used4000013")==2,"Incessant second use must heal through native health bridge");
            f.Prepare(sparrow);f.System(RunDeveloperCommand.SetVitals,int.MinValue,4,0);CostHandView(f,sparrow,4);
            beforeEnemy=f.Enemy.CurrentHealth;f.Play(sparrow);
            Require(f.Actor.CurrentMagic==0&&f.Actor.ActionPoints==0&&f.Enemy.CurrentHealth<beforeEnemy,"Sparrow must deal damage without omen and spend exactly four energy, zero AP");
            MemoryRoundTrip(f,school,"star",1);
            return new JObject{["star_not_spent"]=true,["original_slots_3_6"]=true,["omen_consumed_by_random"]=true,["sequence_survives_host_round"]=true,["starter_attack_and_post_heal"]=true,["sparrow_zero_omen_four_energy"]=true,["memory_save_restore"]=true,["team_healing"]=SevenStarTeamHealing()};
        }
        private void MemoryRoundTrip(Fixture f, string school, string key, int expected)
        {
            var actorId = f.Actor.RuntimeId;
            Require(f.A.TryGetPlayerBattlefieldId(Slot, out var field), "save roundtrip battlefield missing");
            f.System(RunDeveloperCommand.WinBattle);
            // TickBattlefield advances turns only. CompleteBattlefield is the native settlement
            // command that merges terminal vitals and ModState into the run before saving.
            Require(f.A.Ledger.ExecuteSystem(RunSystemAction.CompleteBattlefield(field)).IsSuccess, "native battlefield settlement failed");
            Require(SectRegistry.Read(f.A.Run.ModState, SectRegistry.StateKey(school, actorId, key)) == expected, "school state not merged by settlement");
            var mapper = new Reincarnation.Domain.Save.RunSaveDataV1Mapper(new Reincarnation.Domain.Save.RunSaveCaptureMetadataV1("school-selftest", data.CatalogVersion, data.GameplayContentHash, SessionMode.Solo), data, new Reincarnation.Runtime.Save.CatalogRunSaveDerivedStatsResolver(data), battles);
            Require(mapper.TryCapture(f.A.Run, out var saved, out var error), "memory save capture: " + error);
            Require(mapper.TryRestore(saved, out var restored, out error), "memory save restore: " + error);
            Require(restored.Players[0].Lineages.Any(l => l.LineageId.Value == school), "restored school missing");
            Require(SectRegistry.Read(restored.ModState, SectRegistry.StateKey(school, actorId, key)) == expected, "restored school state missing");
        }
        private JObject SingleSchoolGate()
        {
            foreach (var school in SectRegistry.Sects.Keys)
            {
                var f = New(school: school, buyCurrent: false);
                foreach (var other in SectRegistry.Sects.Keys.Where(id => id != school))
                {
                    int points = f.P.RewardPoints; long revision = f.A.Run.Revision;
                    var result = f.Send(RunCommand.PurchaseOrUpgradeLineage(Slot, new StableId(other), revision));
                    Require(!result.IsSuccess && f.P.RewardPoints == points && f.A.Run.Revision == revision, "second school accepted or charged");
                }
                f.Upgrade(CardTableCatalog.LineageId);
                Require(f.P.Lineages.Count == 2, "native lineage combination lost");
            }
            return new JObject { ["schools"] = SectRegistry.Sects.Count, ["rejections_atomic"] = true, ["native_combination"] = true };
        }
    }
}
