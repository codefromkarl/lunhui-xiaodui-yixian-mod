using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;
using YixianSect;

partial class Program
{
    static ModStateSnapshot StarExpansionState(params (string, int)[] values)
        => new(values.Select(x => new ModStateEntry(SectRegistry.Package, SectRegistry.StateKey(SevenStarRules.Lineage, ActorId, x.Item1), ModValueKind.Integer, x.Item2)).ToArray());
    static JArray StarHealth(ModBattlePlan p)
    {
        string text = p.State.Entries.FirstOrDefault(e => e.PackageId == SectRegistry.Package && e.Key == OriginalHealthRules.PendingKey)?.Text;
        return string.IsNullOrEmpty(text) ? new JArray() : JArray.Parse(text);
    }
    static void StarExpansionTests()
    {
        const string school = SevenStarRules.Lineage;
        var actor = Unit(school: school, rank: 5, energy: 0);
        var cards = SectRegistry.Cards.Values.Where(c => SectRegistry.Owner(c) == school).ToArray();
        var packs = SectRegistry.Packs.Where(p => SectRegistry.Owner(p) == school).ToArray();
        JObject Source(int id) => cards.Single(c => (string)c["sourceId"] == id.ToString());
        StableId SourceId(int id) => new(SectRegistry.CardPrefix + (string)Source(id)["key"]);
        ModBattlePlan Play(int id, ModStateSnapshot state = null, ModCombatantSnapshot unit = null, int round = 1)
            => Run(SevenStarRules.Play, SourceId(id), state, round, actor: unit ?? actor);
        int Read(ModBattlePlan p, string key) => SchoolState(p, school, key);
        int Draw(ModBattlePlan p) => SectRegistry.Read(p.State, "cards." + ActorId.Value + ".drawPending");
        Check(cards.Length == 48 && packs.Length == 12, "original star inventory 48/12");
        Check(cards.Select(c => (string)c["sourceId"]).Distinct().Count() == 48, "48 distinct original sources");
        Check(packs.SelectMany(p => p["cards"].Values<string>()).Distinct().Count() == 48, "star unique complete packs");
        foreach (int rank in Enumerable.Range(1, 5)) Check(packs.Count(p => (int)p["rank"] == rank) == (rank < 4 ? 2 : 3), "star realm split " + rank);
        var internalStatus=JObject.Parse(File.ReadAllText(Mod+"/content/statuses.json"))["operations"].OfType<JObject>().Single(o=>(string)o["id"]==SevenStarRules.StatusPrefix+"internal");
        var internalTick=internalStatus["fields"]["timedEffects"][0];
        Check(internalTick["damageType"]==null && (int)internalTick["damageKind"]==40 && (bool)internalTick["isDirectDamage"],"native timed internal injury uses damageKind True plus direct shield bypass");
        foreach(string key in new[]{"weak","flaw"})
        {
            var negative=JObject.Parse(File.ReadAllText(Mod+"/content/statuses.json"))["operations"].OfType<JObject>().Single(o=>(string)o["id"]==SevenStarRules.StatusPrefix+key)["fields"];
            Check((int)negative["ending"]==30 && (int)negative["stackDecay"]==20 && (int)negative["defaultDurationRounds"]==0,"original weaken/flaw lose one stack rather than expire all "+key);
        }
        var original = JArray.Parse(File.ReadAllText("data/sources/original-correction/heptastar-pavilion-cards.json")).OfType<JObject>().ToDictionary(c => (string)c["id"]);
        var costs = JObject.Parse(File.ReadAllText("data/sources/original-correction/original-costs.json"))["cards"];
        foreach (var card in cards)
        {
            var source = original[(string)card["sourceId"]]; var r = (JObject)card["rules"];
            Check(((string)card["name"]).Replace('·', '•') == (string)source["name"], "star real original name " + card["sourceId"]);
            Check((string)card["realm"] == (string)source["realm"] && (int)card["rank"] == Array.IndexOf(new[] { "炼气", "筑基", "金丹", "元婴", "化神" }, (string)source["realm"]) + 1, "star source realm " + card["sourceId"]);
            Check((string)card["originalText"] == (string)source["original"] && (string)card["sourceUrl"] == (string)source["url"], "star source text and URL " + card["sourceId"]);
            Check((int)r["cost"] == (int)costs[(string)card["sourceId"]]["cost"], "star original card-face qi cost " + card["sourceId"]);
            Check(r["starSpend"] == null && r["chargeGain"] == null && r["orbitGain"] == null, "no invented mechanics survive " + card["sourceId"]);
            var p = Play(int.Parse((string)card["sourceId"]), StarExpansionState(("star", 5), ("omen", 5)));
            foreach (var operation in p.Operations)
                Check(AuthoredSkillRules.TryCreateEffects(new BattleEffectOperation(operation.HandlerKey, CombatTargetKind.EnemySingle, default, operation.Values), out var parsed, out var error) && parsed.Count == 1, "star native authored effect " + error);
        }
        Check(((JArray)Source(4000104)["sourceDiscrepancies"]).Count > 0, "star connect card image/text discrepancy explicit");
        Check(packs.OrderBy(p => (int)p["index"]).Select(p => (int)p["index"]).SequenceEqual(new[] {70,71}.Concat(Enumerable.Range(90,10))), "frozen shelves unchanged");
        var gather = Play(4000008);
        Check(Read(gather,"star")==1 && Effect(gather,ModBattleEffectKind.Shield)==20, "original starry stars and scaled shield");
        foreach(int i in Enumerable.Range(1,8))
            Check(gather.Effects.Any(e=>e.Kind==ModBattleEffectKind.Status&&e.Content.Value==SevenStarRules.StatusPrefix+"position"+i&&e.Amount==1)==new[]{3,6}.Contains(i),"visible initial star positions "+i);
        var star = StarExpansionState(("star",2),("plays",2));
        var clamp = Play(4000010,star);
        Check(Damage(clamp)==65 && Read(clamp,"star")==2, "star at slot3 constant per-attack power with extra attack");
        Check(Damage(Play(4000010,StarExpansionState(("star",2))))==38, "star off-position has no power bonus");
        Check(Damage(Play(4000029,star))==71, "multi-hit and separate star-position attack");
        foreach(int count in Enumerable.Range(1,24))
            Check(SevenStarRules.IsPosition(count)==new[]{3,6}.Contains((count-1)%8+1), "virtual 8-slot default positions "+count);
        var ninth=Play(4000010,StarExpansionState(("star",2),("plays",8)));
        Check(Read(ninth,"slot")==1 && Damage(ninth)==38,"ninth play not invented star position");
        var marked=Play(4000023);
        Check(Read(marked,"positions")==38 && Read(marked,"slot")==1,"Dipper marks following slot2 permanently");
        var markedAttack=Play(4000010,marked.State);
        Check(Damage(markedAttack)==53,"explicitly marked slot gives Astral Clamp bonus");
        var already=Play(4000015,StarExpansionState(("plays",1)));
        Check(already.Effects.Any(e=>e.Kind==ModBattleEffectKind.Energy&&e.Energy.Amount==4),"marking existing slot3 refunds fixed one qi");
        var foreign=Run(SevenStarRules.Resolved,CardTableCatalog.Cards.First().AbilityId,marked.State,actor:actor);
        Check(Read(foreign,"plays")==2,"native card advances virtual slot");
        var ownResolved=Run(SevenStarRules.Resolved,SourceId(4000023),marked.State,actor:actor);
        Check(Read(ownResolved,"plays")==1,"own card not counted twice");
        var draw=Run(SevenStarRules.Resolved,CardTableRules.DrawActionId,marked.State,actor:actor);
        Check(Read(draw,"plays")==1,"draw does not advance slots");
        var nextRound=Run(SevenStarRules.Round,state:clamp.State,round:2,actor:actor);
        var duplicateRound=Run(SevenStarRules.Round,state:nextRound.State,round:2,actor:actor);
        Check(Read(nextRound,"plays")==3 && Read(duplicateRound,"plays")==3 && Read(nextRound,"star")==2,"rounds preserve position and resources idempotently");

        var forced=Play(4000004,StarExpansionState(("omen",1)));
        var random=Play(4000004);
        Check(Damage(forced)==50 && Read(forced,"omen")==0 && forced.Random.CallCount==0,"original Palm Thunder max forced with one omen");
        Check(random.Random.CallCount>0 && Damage(random)==Damage(Play(4000004)),"unforced native deterministic random");
        var thunder=Play(4000063,StarExpansionState(("omen",1)));
        Check(thunder.Operations.Count==2 && thunder.Random.CallCount>0 && Read(thunder,"omen")==0,"repeated thunder independently consumes omen");
        var five=Play(4000046,StarExpansionState(("omen",5)));
        Check(five.Operations.Count==5 && Damage(five)==140 && Read(five,"omen")==0 && five.Random.CallCount==0,"five thunders five independent forced successes");
        var chance=Play(4000020,StarExpansionState(("omen",1)));
        Check(chance.Effects.Any(e=>e.Kind==ModBattleEffectKind.Status&&e.Content.Value==SevenStarRules.StatusPrefix+"weak")&&Read(chance,"omen")==0,"omen ensures probability not only damage range");
        var returnOmen=Play(4000068,StarExpansionState(("omen",1)));
        Check(Read(returnOmen,"omen")==1 && Read(returnOmen,"omenUsed")==1,"Thunder Hexagram refunds own consumed omen");
        var consumed=Play(4000062,StarExpansionState(("omen",3)));
        Check(Read(consumed,"omen")==0 && Read(consumed,"star")==3 && Read(consumed,"omenUsed")==0,"trajectory consumes omen but not random-trigger count");

        var post1=Play(4000043);var post2=Play(4000043,post1.State,round:2);
        Check(Damage(post1)==20 && Damage(post2)==41,"post action begins on second same-source use, across rounds");
        var moon1=Play(4000033);var moon2=Play(4000033,moon1.State);
        Check(Damage(moon1)==0 && Damage(moon2)==56 && Draw(moon2)==1,"Scooping Moon conditional attack and adapted chase");
        var plum=Play(4000041);var repeated=Play(4000043,plum.State);
        Check(Damage(repeated)==20+41 && Read(repeated,"plays")==2,"Plum repeats effect once, triggers second-use post, no double slot");
        var ordinary=Run(SevenStarRules.Resolved,CardTableCatalog.Cards.First().AbilityId,plum.State,actor:actor);
        Check(Read(ordinary,"repeat")==1,"native card preserves explicitly school-only repeat");
        var persistent=Play(4000014);var again=Play(4000014,persistent.State);
        Check(Read(again,"hexFormation")==3,"persistent formation is not stackable by manual replay");
        Check(SevenStarRules.CanPlay(Source(4000014),ActorId,ModStateSnapshot.Empty) && !SevenStarRules.CanPlay(Source(4000014),ActorId,persistent.State),"persistent card admission before payment");
        var hexGain=Play(4000001,persistent.State);
        Check(StarHealth(hexGain).OfType<JObject>().Any(o=>(string)o["kind"]=="nonAttack"&&(int)o["amount"]==9&&(string)o["target"]==EnemyId.Value),"hex formation non-attack primitive keeps shield interception");
        var away=Play(4000034);
        Check(StarHealth(away).OfType<JObject>().Any(o=>(string)o["kind"]=="maximum"&&(int)o["amount"]==-9&&(bool)o["current"]),"Li hexagram decreases life and maximum once");
        Check(!StarHealth(away).OfType<JObject>().Any(o=>(string)o["kind"]=="loss"),"Li maximum clamp not double charged");
        var tian=Play(4000045);var tian2=Play(4000045,tian.State);
        Check(Read(tian,"positions")==255 && Read(tian,"star")==2 && Read(tian2,"star")==2,"Cosmos first slot extra star and once-only permanent positions");
        Check(Enumerable.Range(1,8).All(i=>tian.Effects.Any(e=>e.Kind==ModBattleEffectKind.Status&&e.Content.Value==SevenStarRules.StatusPrefix+"position"+i&&e.Amount==1)),"Cosmos visibly marks all 8 positions");
        var life=Play(4000093,StarExpansionState(("plays",2),("star",2)));
        Check(Read(life,"star")==3 && StarHealth(life).OfType<JObject>().Any(o=>(string)o["kind"]=="maximum"&&(int)o["amount"]==30),"Extend position life uses newly gained star");
        var sharp=Play(4000107,StarExpansionState(("plays",1),("star",3)));
        Check(Read(sharp,"star")==1 && sharp.Effects.Count(e=>e.Kind==ModBattleEffectKind.Status&&e.Content.Value.StartsWith(SevenStarRules.StatusPrefix+"guard"))==2,"Point explicitly converts two stars into independent guard instances");
        var snake=Play(4000095,unit:Unit(school:school,rank:5,shield:15));
        Check(Draw(snake)==1,"Spirit Snake checks current total defense not only newly granted defense");
        var soul=Play(4000092);var soulHit=Run(SevenStarRules.Hits,SourceId(4000003),soul.State,actor:actor,hits:new[]{new ModBattleHit(ActorId,EnemyId,0,12,DamageType.Physical,false,false)});
        Check(Read(soulHit,"soulCharges")==2 && soulHit.Effects.Any(e=>e.Kind==ModBattleEffectKind.Status&&e.Content.Value==SevenStarRules.StatusPrefix+"internal"),"soul formation counts shield hits");
        var nonattack=Run(SevenStarRules.Hits,SourceId(4000003),soul.State,actor:actor,hits:new[]{new ModBattleHit(ActorId,EnemyId,9,0,DamageType.True,true,false)});
        Check(Read(nonattack,"soulCharges")==3,"non-attack direct life loss does not trigger soul");
        var seed=StarExpansionState(("plays",2),("star",3),("omen",2),("positions",36),("initialized",1));
        string serialized=JsonConvert.SerializeObject(seed.Entries);
        var p1=Play(4000063,seed);var p2=Play(4000063,seed);
        Check(JsonConvert.SerializeObject(seed.Entries)==serialized && Damage(p1)==Damage(p2) && p1.Random==p2.Random,"preview immutable and deterministic");
        var restored=new ModStateSnapshot(JsonConvert.DeserializeObject<ModStateEntry[]>(serialized));
        Check(Damage(Play(4000063,restored))==Damage(p1),"original star saved state roundtrip");
        var other=Unit(slot:2,school:school,rank:5);
        Check(SchoolState(Play(4000008,seed,other),school,"star",other.RuntimeId)==1,"star actor isolated");
        StarFateExpansionTests();
        Console.WriteLine("PASS original seven-star: 48 real cards / 12 packs / source realms and costs / constant position power / independent omen RNG / second-use post / virtual slots / four sourced fates");
    }
    static void StarFateExpansionTests()
    {
        const string school=SevenStarRules.Lineage;
        var ids=new[]{SevenStarFateRules.Practice,SevenStarFateRules.Observatory,SevenStarFateRules.Pivots,SevenStarFateRules.Cycle};
        foreach(string id in ids)foreach(int rank in Enumerable.Range(1,5))
        {
            Check(SevenStarFateRules.Has(Unit(school:school,rank:rank,abilities:new[]{id}),id)==(rank>=SevenStarFateRules.Required(id)),"original star fate gate "+id+rank);
            Check(!SevenStarFateRules.Has(Unit(rank:rank,abilities:new[]{id}),id),"original star fate wrong school "+id+rank);
            Check(!SevenStarFateRules.Has(Unit(school:school,rank:rank),id),"original star fate purchase required "+id+rank);
        }
        foreach(int rank in Enumerable.Range(2,4))
        {
            var actor=Unit(school:school,rank:rank,abilities:new[]{SevenStarFateRules.Practice,SevenStarFateRules.Observatory});
            var start=Run(SevenStarRules.Start,actor:actor);var repeat=Run(SevenStarRules.Start,state:start.State,actor:actor);
            Check(SchoolState(start,school,"star")==1 && SchoolState(start,school,"omen")==SevenStarFateRules.InitialOmen(rank),"original fate opening values "+rank);
            Check(SchoolState(repeat,school,"star")==1,"duplicate battle start does not grant again "+rank);
            var round=Run(SevenStarRules.Round,state:start.State,round:2,actor:actor);
            Check(SchoolState(round,school,"star")==1,"removed invented per-round fate income "+rank);
        }
        var postCard=SectRegistry.Cards.Values.Single(c=>(string)c["sourceId"]=="4000018");var postId=new StableId(SectRegistry.CardPrefix+(string)postCard["key"]);
        var postActor=Unit(school:school,rank:3,abilities:new[]{SevenStarFateRules.Pivots});
        var first=Run(SevenStarRules.Play,postId,actor:postActor);var second=Run(SevenStarRules.Play,postId,first.State,actor:postActor);
        Check(Effect(first,ModBattleEffectKind.Shield)==9 && Effect(second,ModBattleEffectKind.Shield)==0,"Post Strike only first use grants rank3 scaled defense");
        Check(StarHealth(first).OfType<JObject>().Any(o=>(string)o["kind"]=="maximum"&&(int)o["amount"]==9),"Post Strike adds actual temporary maximum");
        var healer=Unit(school:school,rank:4,abilities:new[]{SevenStarFateRules.Cycle});
        var open=Run(SevenStarRules.Start,actor:healer);
        Check(StarHealth(open).OfType<JObject>().Any(o=>(string)o["kind"]=="maximum"&&(int)o["amount"]==15),"Rejuvenation opening maximum source value");
        var mantis=SectRegistry.Cards.Values.Single(c=>(string)c["sourceId"]=="4000043");
        var healed=Run(SevenStarRules.Play,new StableId(SectRegistry.CardPrefix+(string)mantis["key"]),actor:healer);
        var health=StarHealth(healed).OfType<JObject>().ToArray();
        Check(health.Length==2 && (string)health[0]["kind"]=="maximum" && (int)health[0]["amount"]==15 && (string)health[1]["kind"]=="heal", "Rejuvenation increases maximum before school healing");
    }
}
