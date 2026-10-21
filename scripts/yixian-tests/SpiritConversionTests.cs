using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using YixianSect;

partial class Program
{
    static void SpiritConversionTests()
    {
        Check(SpiritConversion.Convert(new StatBlock(50,30,20,10))==new StatBlock(20,12,8,70),"actual debit of 30/18/12 produces sixty energy");
        Check(SpiritConversion.Convert(new StatBlock(7,9,11,13))==new StatBlock(3,4,5,28),"each source floors independently, actual debit is transferred");
        for(int a=1;a<=150;a++)
        {
            var before=new StatBlock(a,a+1,a+2,19);var after=SpiritConversion.Convert(before);
            Check(before.Total==after.Total,"conversion conserves total attribute points "+a);
            Check(after.Muscle>=1&&after.Reaction>=1&&after.Constitution>=1,"source remains nonzero "+a);
        }
        var original=new StatBlock(50,30,20,10);var first=SpiritConversion.Convert(original);
        foreach(int attribute in new[]{10,20,30})
        {
            var grown=new StatBlock(original.Muscle+(attribute==10?10:0),original.Reaction+(attribute==20?10:0),original.Constitution+(attribute==30?10:0),original.Energy);
            var after=SpiritConversion.Convert(grown);
            Check(after.Energy==first.Energy+6,"future ten-point source growth transfers six energy "+attribute);
            Check((attribute==10?after.Muscle-first.Muscle:attribute==20?after.Reaction-first.Reaction:after.Constitution-first.Constitution)==4,"future ten-point source growth retains only four points "+attribute);
        }
        Check(SpiritConversion.Convert(new StatBlock(50,30,20,20)).Energy==first.Energy+10,"future direct energy growth is retained, not taxed or re-converted");
        var player=Player(1);var mods=new List<StatModifier>();SpiritConversion.AppendMarker(player,mods);
        Check(mods.Count==0,"unbought conversion does not affect Sword");
        var service=new HubService(ProjectedEconomy);var hub=new HubState(HubAccessKind.Full,1,new[]{new PlayerSlotId(1)});
        int points=player.RewardPoints;
        Check(service.PurchaseShelfAbility(hub,player,new StableId(SpiritConversion.Ability)).IsSuccess&&player.RewardPoints==points-20,"native D-rank training costs twenty");
        SpiritConversion.AppendMarker(player,mods);SpiritConversion.AppendMarker(player,mods);
        Check(mods.Count==1&&SpiritConversion.IsMarker(mods[0]),"one pure marker, no repeated attribute mutations");
        Check(player.Birth.FinalBirthStats==new StatBlock(10,10,10,10)&&player.PurchasedAttributes==default,"source birth and purchased attributes unchanged");
        var extra=new StatModifier(new StableId("test.gear"),new StatBlock(10,20,30,40),maximumHealthFlat:21,maximumHealthBasisPoints:1000,maximumMagicFlat:16,physicalDefenseFlat:3,criticalDamageBasisPoints:500,attributeBasisPoints:2000);
        mods.Insert(0,extra);
        var beforeStats=DerivedStatCalculator.Calculate(player.Birth.FinalBirthStats,new[]{extra},EnergyPoolKind.Fighting);
        Check(beforeStats.EffectiveAttributes==new StatBlock(24,36,48,60),"percentage bonuses applied before source debit");
        var converted=SpiritConversion.Rebuild(beforeStats,mods,EnergyPoolKind.Fighting);
        Check(converted.EffectiveAttributes==new StatBlock(10,15,20,123),"percentage bonuses are not applied a second time");
        var expected=DerivedStatCalculator.Calculate(new StatBlock(10,15,20,123),new[]{new StatModifier(new StableId("test.expected"),default,maximumHealthFlat:21,maximumHealthBasisPoints:1000,maximumMagicFlat:16,physicalDefenseFlat:3,criticalDamageBasisPoints:500)},EnergyPoolKind.Fighting);
        Check(converted==expected,"native health/pool/defenses/crit/shield use converted attributes and retain non-attribute bonuses");
        Check(SpiritConversion.Rebuild(beforeStats,mods,EnergyPoolKind.Fighting)==converted,"repeated projections do not progressively drain stats");
        var other=SchoolPlayer(SevenStarRules.Lineage,5);var otherMods=new List<StatModifier>();SpiritConversion.AppendMarker(other,otherMods);
        Check(otherMods.Count==0,"other school and player receive no conversion");
        Check(!service.PurchaseShelfAbility(hub,player,new StableId(SpiritConversion.Ability)).IsSuccess&&player.RewardPoints==points-20,"duplicate purchase neither transfers nor charges again");
        var attrs=new StatBlock(100,20,30,5);var snapshot=Unit(attributes:attrs);
        Check(Damage(Run(SwordRules.Play,Id(Card(1)),actor:snapshot))==98,"probe cloud uses muscle, not five energy");
        Check(Damage(Run(SwordRules.Play,Id(Card(91)),actor:snapshot))==46,"cloud split multihit uses reaction");
        Check(Damage(Run(SwordRules.Play,Id(Card(43)),actor:snapshot))==20,"flying spirit four segments now scale with energy");
        Check(Damage(Run(SwordRules.Play,Id(Card(94)),actor:snapshot))==20,"rapid spirit ignores reaction and uses energy");
        Console.WriteLine("PASS sword scaling and spirit conversion: real source debits, floors/conservation, native purchase, percent-safe projection, isolated marker and asymmetric card attributes");
    }
}
