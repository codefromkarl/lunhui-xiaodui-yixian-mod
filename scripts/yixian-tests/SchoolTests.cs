using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reincarnation.Config;
using Reincarnation.Domain;
using Reincarnation.Runtime.Content;
using YixianSect;

partial class Program
{
    static int SchoolState(ModBattlePlan plan,string school,string name,StableId actor=default)
        =>SectRegistry.Read(plan.State,SectRegistry.StateKey(school,actor.IsEmpty?ActorId:actor,name));
    static StableId NewId(string key)=>new(SectRegistry.CardPrefix+key);
    static void SchoolTests()
    {
        var nativeCards=CardTableCatalog.Cards;var nativePacks=CardTableCatalog.Packs;
        {
            CardTableBridge.BuildCatalog(out var catalogCards,out var catalogPacks);
            Check(catalogCards.Count==nativeCards.Count+SectRegistry.Cards.Count,"combined card catalog");
            Check(catalogPacks.Count==nativePacks.Count+SectRegistry.Packs.Count+1,"combined pack catalog");
            foreach(int index in Enumerable.Range(40,10))Check(catalogPacks[12+index-40].ShelfIndex==index,"legacy cp ordinal "+index);
            Check(catalogPacks[22].ShelfIndex==51&&catalogPacks[23].ShelfIndex==52,"v032 new ordinals preserved");
            Check(ReferenceEquals(CardTableCatalog.Cards,nativeCards)&&ReferenceEquals(CardTableCatalog.Packs,nativePacks),"catalog planning does not mutate native statics");
            var manifest=JObject.Parse(File.ReadAllText(Mod+"/mod.json"));var diagnostics=new List<string>();
            var economy=ModEconomyManifestReader.Read(SectRegistry.Package,manifest["economy"],diagnostics.Add);
            Check(diagnostics.Count==0&&economy.Shop.Count==SectRegistry.Sects.Count,"native economy parser accepts exclusivity rules");
            foreach(var school in SectRegistry.Sects.Keys)
            {
                var player=Player(sect:false);Check(player.AddLineage(new StableId(school)).IsSuccess,"school acquire");
                int points=player.RewardPoints;long revision=player.Revision;
                Check(SectRegistry.CanAcquire(player,school),"can retain/upgrade own school");
                foreach(var other in SectRegistry.Sects.Keys.Where(s=>s!=school))
                {
                    Check(!SectRegistry.CanAcquire(player,other),"one school only");
                    var guard=typeof(Entry).Assembly.GetType("YixianSect.SectPurchaseExclusivity");
                    object[] args={player,new StableId(other),default(RuleResult)};
                    Check(!(bool)Method(guard,"Prefix").Invoke(null,args)&&!((RuleResult)args[2]).IsSuccess,"purchase guard rejects before mutation");
                }
                Check(player.RewardPoints==points&&player.Revision==revision,"rejection atomic");
                Check(SectRegistry.CanAcquire(player,CardTableCatalog.LineageId.Value),"native lineage still allowed");
                Check(!SectProgression.Refresh(player),"school handles progression");
                Check(SectProgression.Owned(player).Count==(school==FiveElementRules.Lineage?1:2)&&player.ActiveCardPackIds.Count==(school==FiveElementRules.Lineage?1:2),"only own two starter packs active");
                Check(SectProgression.Owned(player).All(id=>(id==FiveDeckRules.PackId?FiveElementRules.Lineage:SectRegistry.Owner(SectRegistry.FindPack(id)))==school),"no cross-school gift");
                SectProgression.Refresh(player);Check(player.PurchasedShelfAbilityIds.Count==(school==FiveElementRules.Lineage?17:2),"starter idempotent");
                Check(!player.TryGetLineageRank(CardTableCatalog.LineageId,out var _),"school does not impersonate owned BL08");
                var packs=SectProgression.Owned(player);long beforeActive=player.Revision;
                Check(SectProgression.SetActive(player,packs).IsSuccess&&player.Revision==beforeActive,"unchanged active selection is a no-op success");
                Check(SectProgression.SetActive(player,packs.AsEnumerable().Reverse().ToArray()).IsSuccess&&player.Revision==beforeActive+(school==FiveElementRules.Lineage?0:1),"active selection commits once");
                beforeActive=player.Revision;
                Check(!SectProgression.SetActive(player,new[]{packs[0],packs[0]}).IsSuccess&&player.Revision==beforeActive,"duplicate active pack rejected atomically");
                var foreign= SectRegistry.Packs.FirstOrDefault(p=>SectRegistry.Owner(p)!=school);
                if(foreign!=null)Check(!SectProgression.SetActive(player,new[]{packs[0],SectRegistry.PackId(foreign)}).IsSuccess&&player.Revision==beforeActive,"foreign active pack rejected atomically");
                var rules=economy.Shop.Single(s=>s.ContentId.Value==school);
                Check(rules.Conditions.Forbids.Count==SectRegistry.Sects.Count-1&&!rules.Conditions.Forbids.Any(id=>id.Value==school),"shop excludes only other schools");
            }
            var table=JObject.Parse(File.ReadAllText(Mod+"/content/cardtable.json"));
            var invalid=(JObject)table.DeepClone();((JObject)invalid["packs"][0])["sect"]="lineage.unknown";
            try{SectRegistry.Configure(invalid);throw new Exception("invalid ownership accepted");}catch(ArgumentException){}
            Check(SectRegistry.Cards.Count==((JArray)table["cards"]).Count,"invalid config leaves registry unchanged");
            foreach(var op in JObject.Parse(File.ReadAllText(Mod+"/content/abilities.json"))["operations"].OfType<JObject>().Where(o=>(string)o["fields"]["sourceContentId"]!=SwordRules.Lineage))
                Check(!ContentVocabulary.Check("abilities",(JObject)op["fields"]).Any(),"new ability vocabulary "+op["id"]);
            foreach(var op in JObject.Parse(File.ReadAllText(Mod+"/content/statuses.json"))["operations"].OfType<JObject>())
                Check(!ContentVocabulary.Check("statuses",(JObject)op["fields"]).Any(),"new status vocabulary "+op["id"]);
            ProjectSchoolLineages();
        }
        Console.WriteLine("PASS school core: ownership/gifts/exclusivity/native compatibility, catalog identity/ordinals, native shared card-engine projection and content");
    }
    static void ProjectSchoolLineages()
    {
        var lineages=new List<RuntimeLineageDefinition>();
        foreach(var op in JObject.Parse(File.ReadAllText(Mod+"/content/lineages.json"))["operations"].OfType<JObject>().Where(o=>SectRegistry.IsSect((string)o["id"])))
        {
            var fields=(JObject)op["fields"];
            var steps=fields["steps"].OfType<JObject>().Select(step=>new ContentProgressionStepDefinition((string)step["rank"],(int)step["price"],Operation((JObject)step["passive"]),Operation((JObject)step["active"]))).ToArray();
            var definition=new ContentLineageDefinition((string)op["id"],1,(string)fields["displayName"],(string)fields["lineageCode"],(int)fields["energyPoolKind"],steps,(string)fields["flavour"],fields["rankTitles"].Values<string>().ToArray());
            var runtime=(RuntimeLineageDefinition)PrivateCtor(typeof(RuntimeLineageDefinition),definition);lineages.Add(runtime);
            var expectations=JObject.Parse(File.ReadAllText("tests/fixtures/lineage-stats.json"));
            var expected=expectations["schools"][((string)op["id"]).Split('.').Last()];
            foreach(var rank in Enumerable.Range(1,steps.Length))
            {
                Check(M6ProgressionContentResolver.TryCreateLineagePlan(runtime,rank,ProgressionEffectMode.Passive,out var plan,out var error),"school native lineage plan "+error);
                Check(ProgressionPassiveBattleRules.TryCreateProfile(new[]{ProgressionPassivePlanSlot.Primary(plan,EnergyPoolKind.Fighting)},out var profile,out error),"native stat profile "+error);
                var modifier=ProgressionPassiveBattleRules.CreateStatModifier(profile,new StableId("test.aligned.profile"));
                var attrs=expected["attributes"]?[rank-1]?.Values<int>().ToArray()??new[]{0,0,0,0};
                Check(modifier.Attributes==new StatBlock(attrs[0],attrs[1],attrs[2],attrs[3]),"native exact non-cumulative lineage attributes "+op["id"]+"/"+rank);
                Check(modifier.MaximumMagicFlat==(int)expected["poolBase"][rank-1]&&modifier.MaximumHealthBasisPoints==(int)expected["healthBp"][rank-1],"native exact capacity/health projection");
            }
            if((string)op["id"]!=SwordRules.Lineage)Check(steps.Length==5,"school exposes all five realms");
        }
        var projection=typeof(RuntimeLineageDefinition).Assembly.GetType("Reincarnation.Runtime.Content.RuntimeDomainCatalogProjection");
        var progressions=new List<ProgressionDefinition>();
        Method(projection,"ProjectProgressions").MakeGenericMethod(typeof(RuntimeLineageDefinition)).Invoke(null,new object[]{lineages,Reincarnation.Domain.CatalogContentKind.Lineage,progressions});
        Check(progressions.Count==SectRegistry.Sects.Count,"native projection includes all schools");
        foreach(var p in progressions)Check(p.PoolBases[0]==(p.ContentId.Value==SwordRules.Lineage?12:p.ContentId.Value==FiveElementRules.Lineage?8:9),"school-specific native opening qi base");
    }
}
