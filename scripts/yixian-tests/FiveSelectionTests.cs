using Newtonsoft.Json.Linq;
using System.Reflection;
using Reincarnation.Domain;
using YixianSect;
partial class Program
{
 static PlayerRunState SchoolPlayer(string school,int rank)
 {
  var p=Player(sect:false);Check(p.AddLineage(new StableId(school)).IsSuccess,"create selection school player");
  ((List<LineageRankState>)typeof(PlayerRunState).GetField("lineages",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(p))[0]=new LineageRankState(new StableId(school),rank);return p;
 }
 static void FiveSelectionTests()
 {
  Check(FiveCardSelection.Entries.Count==48,"selection represents all five cards without new card effects");
  var operations=JObject.Parse(File.ReadAllText(Mod+"/content/abilities.json"))["operations"].OfType<JObject>().ToDictionary(o=>(string)o["id"]);
  var total=new List<int>();
  for(int rank=1;rank<=5;rank++)
  {
   var p=SchoolPlayer(FiveElementRules.Lineage,rank);SectProgression.Refresh(p);
   var purchased=(List<StableId>)typeof(PlayerRunState).GetField("purchasedShelfAbilityIds",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(p);
   var water=FiveCardSelection.Entries.Where(e=>(int)e["rank"]<=rank&&(int?)SectRegistry.Cards[SectRegistry.CardPrefix+(string)e["cardKey"]]["element"]==2).ToArray();
   foreach(var e in water.Where(e=>!FiveCardSelection.Owns(p,e)))purchased.Add(FiveCardSelection.Id(e));
   Check(water.Length==new[]{2,3,5,7,10}[rank-1],"pure water original realm counts");
   Check(FiveCardSelection.Water(p).Count==water.Length,"water recommendation uses true owned cards");
   var ids=water.Select(FiveCardSelection.Id).ToArray();int points=p.RewardPoints;
   Check(SectProgression.SetActive(p,ids).IsSuccess,"pure water selectable at every realm");
   Check(p.RewardPoints==points,"selection does not purchase");
   SectProgression.Refresh(p);Check(p.ActiveCardPackIds.SequenceEqual(ids),"refresh preserves singleton selection including two-card entry");
   var before=p.ActiveCardPackIds.ToArray();long revision=p.Revision;
   foreach(var bad in new[]{ids.Concat(ids.Take(1)).ToArray(),ids.Take(FiveCardSelection.Minimum(rank)-1).ToArray(),ids.Concat(SectProgression.Owned(p).Take(1)).ToArray(),new[]{new StableId(FiveCardSelection.Prefix+"forged")}})
   {Check(!SectProgression.SetActive(p,bad).IsSuccess,"duplicate, undersized, mixed representation and forged selection rejected");Check(p.Revision==revision&&p.ActiveCardPackIds.SequenceEqual(before),"invalid selection atomic");}
   foreach(var e in water)
   {var op=operations[FiveCardSelection.Id(e).Value];Check((int)op["fields"]["purchasePrice"]==(int)e["price"],"authored purchase price agrees with card catalogue");}
   total.Add(water.Sum(e=>(int)e["price"]));
  }
  Check(total.SequenceEqual(new[]{0,15,45,95,170}),"pure-water cumulative price envelope");
  var p5=SchoolPlayer(FiveElementRules.Lineage,5);SectProgression.Refresh(p5);
  var raw=(List<StableId>)typeof(PlayerRunState).GetField("purchasedShelfAbilityIds",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(p5);
  foreach(var pack in SectRegistry.Packs.Where(p=>SectRegistry.Owner(p)==FiveElementRules.Lineage))raw.Add(SectRegistry.PackId(pack));
  Check(FiveCardSelection.Owned(p5).Count==48,"all original mixed packs imply individual card ownership");
  var mix=FiveCardSelection.Owned(p5).Take(12).ToArray();Check(SectProgression.SetActive(p5,mix).IsSuccess,"mixed elements can use free selection");
  Check(SectProgression.SetActive(p5,SectProgression.Owned(p5).Take(3).ToArray()).IsSuccess,"legacy pack selection remains available");
  Check(!FiveCardSelection.Valid(Player(5),mix),"other schools cannot select five cards");
  ModStateSnapshot WaterState(int mask,int mastery)=>new(new[]{("waterForce",2),("deckElements",mask),("waterMastery",mastery)}.Select(v=>new ModStateEntry(SectRegistry.Package,SectRegistry.StateKey(FiveElementRules.Lineage,ActorId,v.Item1),ModValueKind.Integer,v.Item2)).ToArray());
  var baseTick=Run(FiveElementRules.End,state:WaterState(2,0),actor:Unit(school:FiveElementRules.Lineage,rank:2));
  var pureTick=Run(FiveElementRules.End,state:WaterState(2,1),actor:Unit(school:FiveElementRules.Lineage,rank:2));
  var earlyTick=Run(FiveElementRules.End,state:WaterState(2,1),actor:Unit(school:FiveElementRules.Lineage,rank:1));
  Check(Effect(baseTick,ModBattleEffectKind.Damage)==6&&Effect(pureTick,ModBattleEffectKind.Damage)==12&&Effect(earlyTick,ModBattleEffectKind.Damage)==6,"mastery doubles pure water tick only from foundation");
  var metal=SectRegistry.Cards.Values.Single(c=>(string)c["name"]=="金灵印");
  var broken=Run(FiveElementRules.Play,NewId((string)metal["key"]),WaterState(2,1),actor:Unit(school:FiveElementRules.Lineage,rank:2));
  Check(SectRegistry.Read(broken.State,SectRegistry.StateKey(FiveElementRules.Lineage,ActorId,"waterMastery"))==0,"playing nonwater cancels mastery");
  Console.WriteLine("PASS water pilot: canonical selections, gifts, all realm prices, mixed ownership, atomic guards and representation switching");
 }
}
