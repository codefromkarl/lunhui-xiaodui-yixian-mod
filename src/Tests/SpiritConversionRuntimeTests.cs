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
        private static StatBlock ExpectedSpiritAttributes(StatBlock source)
        {
            int m=source.Muscle*6/10,r=source.Reaction*6/10,c=source.Constitution*6/10;
            return new StatBlock(source.Muscle-m,source.Reaction-r,source.Constitution-c,source.Energy+m+r+c);
        }
        private JObject SpiritConversionCommands()
        {
            // Exercise the actual patched native narrator in Mono, including pack
            // acquisition/flavour and fates, which previously appeared twice.
            var ids=SectRegistry.Cards.Keys.Concat(SectRegistry.Fates.Keys).Concat(SectRegistry.Packs.Select(p=>SectRegistry.PackId(p).Value));
            foreach(string abilityId in ids)
            {
                Require(data.TryGetAbility(new StableId(abilityId),out var ability),"description ability missing: "+abilityId);
                string actual=Reincarnation.UI.Flow.RunUIPresentationMapper.BuildAbilityOperationDetail(ability,ability.Ranks[0].Operation,data);
                string descriptionExpected=SectRegistry.Cards.TryGetValue(abilityId,out var card)?SectDescriptions.CardDetail(card):SectRegistry.Fates.TryGetValue(abilityId,out var fate)?(string)fate["description"]+"\n独立被动，本局限购一次，不占手牌。":SectDescriptions.PackDetail(SectRegistry.FindPack(new StableId(abilityId)));
                Require(actual==descriptionExpected,"native ability detail duplicated: "+abilityId);
            }
            var id=new StableId(SpiritConversion.Ability);var f=New(5,buyCurrent:false);
            void Toggle(Fixture fixture,bool enabled)
                => Require(fixture.Send(RunCommand.PurchaseShelfAbility(Slot,new StableId(enabled?SpiritConversion.ToggleOn:SpiritConversion.ToggleOff),fixture.A.Run.Revision)).IsSuccess,"native shop toggle rejected");
            long unownedRevision=f.A.Run.Revision;var unownedStats=AutoPresentation(f);
            Require(!f.Send(RunCommand.PurchaseShelfAbility(Slot,new StableId(SpiritConversion.ToggleOn),unownedRevision)).IsSuccess
                && f.A.Run.Revision==unownedRevision && AutoPresentation(f)==unownedStats,"unowned toggle changed state");
            foreach(var pair in new[]{Tuple.Create(ExchangeAttribute.Muscle,50),Tuple.Create(ExchangeAttribute.Reaction,30),Tuple.Create(ExchangeAttribute.Constitution,20)})
                Require(f.Send(RunCommand.PurchaseAttributePoint(Slot,pair.Item1,pair.Item2,f.A.Run.Revision)).IsSuccess,"prepare ordinary source attribute purchases");
            var beforeSnapshot=RunAuthoritySnapshotProjector.Create(f.A,Slot).Players.First(p=>p.Slot==Slot);
            var before=beforeSnapshot.DerivedStats.EffectiveAttributes;var expected=ExpectedSpiritAttributes(before);
            var sourceBirth=f.P.Birth.FinalBirthStats;var purchased=f.P.PurchasedAttributes;
            var pools=f.P.EnergyPools;
            Require(f.P.SetEnergyPools(new EnergyPoolSet(new EnergyPoolState(pools.Primary.Kind,1,pools.Primary.Maximum),pools.Secondary,pools.Style)).IsSuccess,"low pool before conversion");
            int points=f.P.RewardPoints;f.Buy(id);
            Require(points-f.P.RewardPoints==20,"training must cost exactly twenty");
            Require(AutoPresentation(f)==before&&!SpiritConversion.Enabled(f.A.Run.ModState,f.P),"purchase must remain disabled");
            Toggle(f,true);
            int toggleWallet=f.P.RewardPoints;long toggleRevision=f.A.Run.Revision;
            Toggle(f,true);
            Require(f.A.Run.Revision==toggleRevision&&f.P.RewardPoints==toggleWallet,"repeated enable is not idempotent/free");
            Require(AutoPresentation(f)==expected&&f.P.DerivedStats.EffectiveAttributes==expected,"source attributes not actually debited / energy not actually transferred: "+before+" -> "+AutoPresentation(f)+" expected "+expected);
            Require(f.P.DerivedStats.MaximumHealth<beforeSnapshot.DerivedStats.MaximumHealth&&f.P.DerivedStats.CriticalChanceBasisPoints<beforeSnapshot.DerivedStats.CriticalChanceBasisPoints,"source debits did not reduce native health/critical stats");
            Require(f.P.Birth.FinalBirthStats==sourceBirth&&f.P.PurchasedAttributes==purchased,"conversion permanently rewrote birth/purchased stat sources");
            Require(f.P.EnergyPools.Primary.Current==1,"conversion filled newly expanded qi capacity");
            long revision=f.A.Run.Revision;int wallet=f.P.RewardPoints;var random=f.A.Run.CaptureRandomState();
            for(int i=0;i<4;i++)Require(AutoPresentation(f)==expected,"repeated preview drains source again");
            Require(f.A.Run.Revision==revision&&f.P.RewardPoints==wallet&&f.A.Run.CaptureRandomState().Equals(random),"preview changes authority/wallet/RNG");
            Require(!f.Send(RunCommand.PurchaseShelfAbility(Slot,id,revision)).IsSuccess&&f.P.RewardPoints==wallet&&f.A.Run.Revision==revision,"duplicate training charged or mutated authority");
            var poor=New(1,buyCurrent:false);Require(poor.P.SpendRewardPoints(poor.P.RewardPoints-19).IsSuccess,"prepare nineteen-point boundary");
            var poorStats=AutoPresentation(poor);long poorRevision=poor.A.Run.Revision;
            Require(!poor.Send(RunCommand.PurchaseShelfAbility(Slot,id,poorRevision)).IsSuccess&&poor.P.RewardPoints==19&&poor.A.Run.Revision==poorRevision&&AutoPresentation(poor)==poorStats&&!poor.P.HasPurchasedShelfAbility(id),"nineteen-point failure changed attributes or money");
            poor.System(RunDeveloperCommand.AddRewardPoints,1);poor.Buy(id);Require(AutoPresentation(poor)==poorStats,"purchase auto-enabled poor player");Toggle(poor,true);Require(poor.P.RewardPoints==0&&AutoPresentation(poor)==ExpectedSpiritAttributes(poorStats),"twenty-point D-rank purchase did not debit correctly");
            // Purchase FIRST, then spend ordinary reward points on each attribute.
            // Rebuild from full original sources, never from already converted stats.
            var convertedOnPurchase=expected;var persistentSource=before;var continuous=new JArray();
            foreach(var attribute in new[]{ExchangeAttribute.Muscle,ExchangeAttribute.Reaction,ExchangeAttribute.Constitution,ExchangeAttribute.Energy})
            {
                int beforePoints=f.P.RewardPoints;
                int normalPrice=AttributeExchangeRules.CalculatePrice(f.P.PurchasedAttributes,attribute,5);
                Require(f.Send(RunCommand.PurchaseAttributePoint(Slot,attribute,5,f.A.Run.Revision)).IsSuccess,"ordinary attribute purchase after conversion: "+attribute);
                persistentSource=new StatBlock(persistentSource.Muscle+(attribute==ExchangeAttribute.Muscle?5:0),
                    persistentSource.Reaction+(attribute==ExchangeAttribute.Reaction?5:0),
                    persistentSource.Constitution+(attribute==ExchangeAttribute.Constitution?5:0),
                    persistentSource.Energy+(attribute==ExchangeAttribute.Energy?5:0));
                expected=ExpectedSpiritAttributes(persistentSource);
                var actual=AutoPresentation(f);
                Require(actual==expected,"newly purchased attribute escaped ongoing conversion: "+attribute+" got "+actual+" expected "+expected);
                Require(f.P.DerivedStats.EffectiveAttributes==expected,"stored player attributes stale after ordinary purchase: "+attribute);
                Require(beforePoints-f.P.RewardPoints==normalPrice&&f.P.PurchasedShelfAbilityIds.Count(a=>a==id)==1,"ongoing conversion charged another training fee or duplicated purchase");
                Require(f.P.EnergyPools.Primary.Current==1,"ongoing conversion refilled resource when adding attributes");
                continuous.Add(new JObject{["purchased_attribute"]=attribute.ToString(),["purchased_points"]=5,["normal_attribute_fee"]=normalPrice,
                    ["unconverted_source"]=persistentSource.ToString(),["converted"]=actual.ToString()});
            }
            for(int i=0;i<3;i++)Require(AutoPresentation(f)==expected,"post-growth previews re-deduct source stats");
            var mapper=new Reincarnation.Domain.Save.RunSaveDataV1Mapper(new Reincarnation.Domain.Save.RunSaveCaptureMetadataV1("spirit-conversion-selftest",data.CatalogVersion,data.GameplayContentHash,SessionMode.Solo),data,new Reincarnation.Runtime.Save.CatalogRunSaveDerivedStatsResolver(data),battles);
            Require(mapper.TryCapture(f.A.Run,out var saved,out var error),"conversion memory capture: "+error);
            Require(mapper.TryRestore(saved,out var restored,out error),"conversion memory restore: "+error);
            var restoredStats=(DerivedStats)AccessTools.Method(typeof(BattleAuthorityService),"CalculatePlayerPresentationStats",new[]{typeof(RunState),typeof(BattleAuthorityCatalog),typeof(PlayerRunState)}).Invoke(null,new object[]{restored,battles,restored.Players[0]});
            Require(restoredStats.EffectiveAttributes==expected&&restored.Players[0].HasPurchasedShelfAbility(id),"restored conversion missing or source debited twice");
            var offhandRows=new JArray();
            foreach(string primary in new[]{"lineage.BL06","lineage.BL09"})
            {
                var off=New(0,buyCurrent:false);off.Upgrade(new StableId(primary));off.Upgrade(new StableId(SwordRules.Lineage));
                var original=AutoPresentation(off);off.Buy(id);Toggle(off,true);
                Require(AutoPresentation(off)==ExpectedSpiritAttributes(original),"offhand conversion differs for "+primary);
                offhandRows.Add(new JObject{["primary"]=primary,["before"]=original.ToString(),["after"]=AutoPresentation(off).ToString()});
            }
            var peers=NewMulti(2);peers.Upgrade(1,SwordRules.Lineage);peers.Upgrade(2,SwordRules.Lineage);
            StatBlock Peer(byte slot)=>RunAuthoritySnapshotProjector.Create(peers.A,new PlayerSlotId(slot)).Players.First(p=>p.Slot.Value==slot).DerivedStats.EffectiveAttributes;
            var hostBefore=Peer(1);var clientBefore=Peer(2);int clientPoints=peers.P(2).RewardPoints;
            Require(peers.Send(2,RunCommand.PurchaseShelfAbility(new PlayerSlotId(2),id,peers.A.Run.Revision)).IsSuccess,"network client training purchase rejected");
            Require(Peer(1)==hostBefore&&Peer(2)==clientBefore,"client purchase auto-enabled conversion");
            Require(peers.Send(2,RunCommand.PurchaseShelfAbility(new PlayerSlotId(2),new StableId(SpiritConversion.ToggleOn),peers.A.Run.Revision)).IsSuccess,"network toggle rejected");
            Require(Peer(1)==hostBefore&&Peer(2)==ExpectedSpiritAttributes(clientBefore)&&peers.P(2).RewardPoints==clientPoints-20,"network training leaked to host or failed on client");
            Require(peers.Send(2,RunCommand.PurchaseAttributePoint(new PlayerSlotId(2),ExchangeAttribute.Constitution,5,peers.A.Run.Revision)).IsSuccess,"client attribute purchase after training rejected");
            var clientGrown=new StatBlock(clientBefore.Muscle,clientBefore.Reaction,clientBefore.Constitution+5,clientBefore.Energy);
            Require(Peer(1)==hostBefore&&Peer(2)==ExpectedSpiritAttributes(clientGrown)&&peers.P(2).DerivedStats.EffectiveAttributes==ExpectedSpiritAttributes(clientGrown),"ongoing client attribute conversion stale or leaked to host");
            // A later native lineage upgrade must use the same persistent projection.
            poor.System(RunDeveloperCommand.AddRewardPoints,1000);poor.Upgrade(new StableId(SwordRules.Lineage));
            var rankControl=New(2,buyCurrent:false);
            Require(AutoPresentation(poor)==ExpectedSpiritAttributes(AutoPresentation(rankControl)),"later lineage growth escaped ongoing conversion");
            // Real native spell execution after transfer. Newly expanded capacity
            // stays empty until normal refill; EnterWorld follows native refill rules.
            var spirit=Card(43);var pack=CardTableCatalog.Packs.Single(p=>p.CardIds.Contains(spirit));
            if(!f.P.HasPurchasedShelfAbility(pack.UnlockAbilityId))f.Buy(pack.UnlockAbilityId);
            Require(f.P.SetActiveCardPacks(f.P.ActiveCardPackIds.Concat(new[]{pack.UnlockAbilityId}).Distinct().ToArray()).IsSuccess,"activate late spirit pack");
            // Disable, save and restore: original full sources are recovered, with
            // no fee, health/qi gain or mutation of the underlying attribute points.
            int toggleHealth=f.P.CurrentHealth,toggleQi=f.P.CurrentMagic;
            Toggle(f,false);
            Require(AutoPresentation(f)==persistentSource&&f.P.DerivedStats.EffectiveAttributes==persistentSource,"off failed to restore source attributes");
            Require(f.P.CurrentHealth<=toggleHealth&&f.P.CurrentMagic<=toggleQi,"off refilled health/qi");
            Require(mapper.TryCapture(f.A.Run,out var disabledSave,out error),"disabled save capture: "+error);
            Require(mapper.TryRestore(disabledSave,out var disabledRun,out error),"disabled save restore: "+error);
            Require(!SpiritConversion.Enabled(disabledRun.ModState,disabledRun.Players[0]),"saved off was lost");
            Require(f.Send(RunCommand.PurchaseAttributePoint(Slot,ExchangeAttribute.Muscle,5,f.A.Run.Revision)).IsSuccess,"off-mode growth failed");
            persistentSource=new StatBlock(persistentSource.Muscle+5,persistentSource.Reaction,persistentSource.Constitution,persistentSource.Energy);
            Require(AutoPresentation(f)==persistentSource&&f.P.DerivedStats.EffectiveAttributes==persistentSource,"off-mode growth was converted");
            expected=ExpectedSpiritAttributes(persistentSource);toggleWallet=f.P.RewardPoints;
            for(int i=0;i<3;i++){Toggle(f,true);Require(AutoPresentation(f)==expected,"re-enable accumulated source debits");Toggle(f,false);}
            Toggle(f,true);
            Require(f.P.RewardPoints==toggleWallet&&f.P.PurchasedShelfAbilityIds.Count(a=>a==id)==1,"switching charged or duplicated ability");
            var visualFixture=New(1,buyCurrent:false);
            var snapshot=RunAuthoritySnapshotProjector.Create(visualFixture.A,Slot);
            var visual=VisualSelfTests.SpiritConversionShelf(Path.Combine(Path.GetDirectoryName(path),Path.GetFileNameWithoutExtension(path),"spirit-conversion"),snapshot.Players.First(p=>p.Slot==Slot),snapshot,data,battles,enabled=>{
                if(!visualFixture.P.HasPurchasedShelfAbility(id))visualFixture.Buy(id);
                Toggle(visualFixture,enabled);
                return RunAuthoritySnapshotProjector.Create(visualFixture.A,Slot);
            });
            var characterVisual=VisualSelfTests.SpiritConversionCharacter(Path.Combine(Path.GetDirectoryName(path),Path.GetFileNameWithoutExtension(path),"spirit-character"),data,
                ()=>RunAuthoritySnapshotProjector.Create(visualFixture.A,Slot),command=>visualFixture.Send(command));
            f.StartBattle();ToughEnemy(f.Enemy);
            var battleRevision=f.A.Run.Revision;int battleWallet=f.P.RewardPoints;
            Require(!f.Send(RunCommand.PurchaseShelfAbility(Slot,new StableId(SpiritConversion.ToggleOff),battleRevision)).IsSuccess
                && f.A.Run.Revision==battleRevision&&f.P.RewardPoints==battleWallet&&SpiritConversion.Enabled(f.A.Run.ModState,f.P),"battle toggle was not atomically rejected");
            Require(f.P.DerivedStats.EffectiveAttributes==expected,"battle starts with different/double-transferred stats");
            f.Prepare(spirit);var current=RunAuthoritySnapshotProjector.Create(f.A,Slot);var model=BattleUIPresentationMapper.BuildCardTable(current,current.Players.First(p=>p.Slot==Slot),data,battles);
            Require(model.Hand.First(c=>c.CardId==spirit.Value).Detail.Contains("40%×能量"),"actual spirit hand detail still claims reaction");
            int hp=f.Enemy.CurrentHealth,qi=f.Actor.CurrentMagic,ap=f.Actor.ActionPoints;var receipt=f.Play(spirit);
            Require(f.Enemy.CurrentHealth<hp&&receipt.PresentationSteps.Count==4&&f.Actor.ActionPoints==ap,"converted spirit failed real four-hit execution or charged AP");
            Require(f.P.DerivedStats.EffectiveAttributes==expected,"spell execution re-converted source stats");
            return new JObject{["manual_free_toggle"]=true,["default_disabled"]=true,["off_save_restore"]=true,["battle_toggle_rejected"]=true,["before"]=before.ToString(),["after"]=convertedOnPurchase.ToString(),["price"]=20,["post_purchase_attribute_growth"]=continuous,["after_later_growth"]=expected.ToString(),["actual_source_debit"]=true,["nineteen_twenty_atomic"]=true,["preview_no_accumulation_or_refill"]=true,["native_memory_restore"]=true,["offhand"]=offhandRows,["network_client_isolation"]=true,["network_client_later_growth"]=true,["later_lineage_growth"]=true,["spirit_native_segments"]=receipt.PresentationSteps.Count,["health_damage"]=hp-f.Enemy.CurrentHealth,["character_visual"]=characterVisual,["visual"]=visual};
        }
    }
}
