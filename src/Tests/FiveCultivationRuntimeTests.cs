using System;
using System.Linq;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.UI.Flow;

namespace YixianSect
{
    public sealed partial class RuntimeSelfTests
    {
        private StatBlock AutoPresentation(Fixture f) => RunAuthoritySnapshotProjector.Create(f.A, Slot).Players.First(p => p.Slot == Slot).DerivedStats.EffectiveAttributes;
        private JObject FiveCultivationCommands()
        {
            var f = New(3, school: FiveElementRules.Lineage, buyCurrent: false);
            var library = FiveDeckRules.Unlocked(f.P).ToArray(); var deck = FiveDeckRules.Cards(f.P).ToArray();
            var pools = f.P.EnergyPools;
            Require(f.P.SetEnergyPools(new EnergyPoolSet(new EnergyPoolState(pools.Primary.Kind, 1, pools.Primary.Maximum), pools.Secondary, pools.Style)).IsSuccess, "low-energy hub fixture");
            Require(f.Send(RunCommand.PurchaseAttributePoint(Slot, ExchangeAttribute.Energy, 60, f.A.Run.Revision)).IsSuccess, "ordinary energy purchase");
            var original = AutoPresentation(f); Require(FiveCultivation.SelectPrimary(original) == 40, "energy should become automatic primary");
            int reactionPoints = original.Energy - original.Reaction - FiveCultivation.Growth(3) + 1;
            Require(reactionPoints > 0 && f.Send(RunCommand.PurchaseAttributePoint(Slot, ExchangeAttribute.Reaction, reactionPoints, f.A.Run.Revision)).IsSuccess, "ordinary reaction purchase");
            var after = AutoPresentation(f);
            Require(after.Reaction == original.Reaction + reactionPoints + 8 && after.Energy == original.Energy - 8 && after.Constitution == original.Constitution,
                "one external point must switch all eight growth points without self-lock/copy: " + original + " -> " + after);
            Require(f.P.EnergyPools.Primary.Current == 1, "automatic switch refilled resources");
            Require(f.Send(RunCommand.PurchaseAttributePoint(Slot, ExchangeAttribute.Energy, 1, f.A.Run.Revision)).IsSuccess, "ordinary energy point to make external tie");
            var tied = AutoPresentation(f);
            Require(tied.Reaction == after.Reaction && tied.Energy == after.Energy + 1, "external tie must retain reaction instead of bouncing to energy");
            after = tied;

            long revision = f.A.Run.Revision; int points = f.P.RewardPoints;
            for (int i = 0; i < 3; i++) Require(AutoPresentation(f) == after, "repeated native preview accumulates growth");
            Require(f.A.Run.Revision == revision && f.P.RewardPoints == points && !f.P.PurchasedShelfAbilityIds.Any(id => id.Value.StartsWith(FiveCultivation.Prefix)), "preview writes authority/tokens");
            var snapshot = RunAuthoritySnapshotProjector.Create(f.A, Slot);
            var visual = VisualSelfTests.FiveCultivationControls(Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path), "cultivation"),
                snapshot.Players.First(p => p.Slot == Slot), snapshot, data, battles);
            foreach (var id in new[] { FiveCultivation.Prefix + "40.1", FiveCultivation.Prefix + "20.3", FiveCultivation.Prefix + "30.7" })
            {
                Require(!f.Send(RunCommand.PurchaseShelfAbility(Slot, new StableId(id), f.A.Run.Revision)).IsSuccess, "retired manual profile accepted");
                Require(f.A.Run.Revision == revision && f.P.RewardPoints == points && AutoPresentation(f) == after, "retired command mutated state");
            }
            Require(FiveDeckRules.Unlocked(f.P).SequenceEqual(library) && FiveDeckRules.Cards(f.P).SequenceEqual(deck), "auto primary changes library/deck");
            // A saved old manual ID is tolerated but cannot override automatic selection.
            var ids = (System.Collections.Generic.List<StableId>)AccessTools.Field(typeof(PlayerRunState), "purchasedShelfAbilityIds").GetValue(f.P);
            ids.Add(new StableId(FiveCultivation.Prefix + "40.1"));
            Require(AutoPresentation(f) == after, "old saved manual profile still controls growth");
            f.StartBattle();
            int locked = SectRegistry.Read(SchoolState(f), SectRegistry.StateKey(FiveElementRules.Lineage, f.Actor.RuntimeId, FiveCultivation.BattleKey));
            Require(locked == 20, "battle did not lock automatic reaction");
            Require(SectRegistry.Read(SchoolState(f), SectRegistry.StateKey(FiveElementRules.Lineage, f.Actor.RuntimeId, "active")) == 0, "automatic attributes activate elements");
            var hand = f.Table.Hand.Select(c => c.InstanceId).ToArray();
            var changedDeck = deck.Take(6).Concat(library.Skip(8).Take(2)).ToArray();
            Require(f.Action(CardTableRules.SetPacksActionId, parameter: FiveDeckRules.EncodeSelection(changedDeck)).IsSuccess, "battle deck editing rejected");
            Require(f.Table.Hand.Select(c => c.InstanceId).SequenceEqual(hand)
                && SectRegistry.Read(SchoolState(f), SectRegistry.StateKey(FiveElementRules.Lineage, f.Actor.RuntimeId, FiveCultivation.BattleKey)) == locked, "deck edit changed hand/primary");
            // Persistent refresh during battle must keep growth on the locked primary,
            // even if an external growth modifier now makes constitution largest.
            Require(f.P.SetDeveloperGrowthAttributes(new StatBlock(0, 0, 300, 0)).IsSuccess, "arrange external body growth");
            Require((bool)AccessTools.Method(typeof(BattleAuthorityService), "DeveloperRefreshPlayerStats").Invoke(f.B, new object[] { Slot }), "native battle persistent refresh");
            var refreshed = f.P.DerivedStats.EffectiveAttributes;
            Require(refreshed.Reaction == after.Reaction && refreshed.Energy == after.Energy, "persistent refresh moved locked growth");
            ToughEnemy(f.Enemy); var card = FiveDeckRules.Candidates(1).First(c => (int)c["element"] == 1); var cardId = FiveDeckRules.CardId(card);
            f.Prepare(cardId);
            var current = RunAuthoritySnapshotProjector.Create(f.A, Slot); var player = current.Players.First(p => p.Slot == Slot);
            var model = BattleUIPresentationMapper.BuildCardTable(current, player, data, battles);
            Require(model.Hand.First(c => c.CardId == cardId.Value).Action.AttackDamagePreviews.Any(p => p.TargetId == f.Enemy.RuntimeId), "native damage preview missing");
            int before = f.Enemy.CurrentHealth; f.Play(cardId); int damage = before - f.Enemy.CurrentHealth;
            Require(damage > 0 && SectRegistry.Read(SchoolState(f), SectRegistry.StateKey(FiveElementRules.Lineage, f.Actor.RuntimeId, FiveCultivation.BattleKey)) == locked, "real card or battle lock failed");
            Require(f.A.TryGetPlayerBattlefieldId(Slot, out var field), "battlefield for save");
            f.System(RunDeveloperCommand.WinBattle);
            Require(f.A.Ledger.ExecuteSystem(RunSystemAction.CompleteBattlefield(field)).IsSuccess, "normal test settlement");
            var mapper = new Reincarnation.Domain.Save.RunSaveDataV1Mapper(new Reincarnation.Domain.Save.RunSaveCaptureMetadataV1("five-auto-selftest", data.CatalogVersion, data.GameplayContentHash, SessionMode.Solo), data,
                new Reincarnation.Runtime.Save.CatalogRunSaveDerivedStatsResolver(data), battles);
            Require(mapper.TryCapture(f.A.Run, out var saved, out var error), "native memory capture: " + error);
            Require(mapper.TryRestore(saved, out var restored, out error), "native memory restore: " + error);
            var restoredStats = (DerivedStats)AccessTools.Method(typeof(BattleAuthorityService), "CalculatePlayerPresentationStats", new[] { typeof(RunState), typeof(BattleAuthorityCatalog), typeof(PlayerRunState) }).Invoke(null, new object[] { restored, battles, restored.Players[0] });
            Require(FiveCultivation.SelectPrimary(restoredStats.EffectiveAttributes) == 30 && FiveDeckRules.Cards(restored.Players[0]).SequenceEqual(changedDeck), "next hub/save must adopt now-highest body and preserve deck");
            var peers = NewMulti(2);
            for (int rank = 1; rank <= 3; rank++) for (byte slot = 1; slot <= 2; slot++) peers.Upgrade(slot, FiveElementRules.Lineage);
            Require(peers.Send(1, RunCommand.PurchaseAttributePoint(new PlayerSlotId(1), ExchangeAttribute.Reaction, 60, peers.A.Run.Revision)).IsSuccess, "network host reaction purchase");
            Require(peers.Send(2, RunCommand.PurchaseAttributePoint(new PlayerSlotId(2), ExchangeAttribute.Constitution, 60, peers.A.Run.Revision)).IsSuccess, "network peer body purchase");
            StartMulti(peers, 2);
            foreach (byte slot in new byte[] { 1, 2 })
            {
                var peerState = (ModStateSnapshot)AccessTools.Field(typeof(BattleAuthorityService), "modState").GetValue(peers.B(slot));
                int peerPrimary = SectRegistry.Read(peerState, SectRegistry.StateKey(FiveElementRules.Lineage, peers.Actor(slot).RuntimeId, FiveCultivation.BattleKey));
                Require(peerPrimary == (slot == 1 ? 20 : 30), "multiplayer automatic primary leaked between actors");
            }
            return new JObject { ["automatic_main_attribute"] = true, ["one_point_switch_no_self_lock"] = true, ["tie_retains_previous"] = true, ["native_multiplayer_distinct_primaries"] = true, ["growth_moved_not_duplicated"] = true,
                ["no_refill_or_preview_mutation"] = true, ["retired_manual_commands_rejected"] = true, ["old_profile_ignored"] = true,
                ["battle_primary_locked"] = locked, ["battle_growth_refresh_locked"] = true, ["native_memory_restore"] = true,
                ["next_battle_eligible_primary"] = 30, ["native_preview"] = true, ["real_card_damage"] = damage, ["visual"] = visual };
        }
    }
}
