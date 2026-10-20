using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;

namespace YixianSect
{
    // Read-only proposals. Applying a proposal is still the native SetPacks transaction.
    public static class SwordBuilds
    {
        public sealed class Route
        {
            public string Key { get; }
            public string Name { get; }
            public string Play { get; }
            public string Milestone { get; }
            public int[] Priority { get; }
            public Route(string key,string name,string play,string milestone,params int[] priority)
            {Key=key;Name=name;Play=play;Milestone=milestone;Priority=priority;}
        }
        public sealed class Proposal
        {
            public Route Route;
            public IReadOnlyList<StableId> Selected;
            public IReadOnlyList<JObject> Missing;
            public bool IsTransition;
        }
        public sealed class Pool
        {
            public int Packs, Cards, Cloud, Qi, Draw;
            public int CloudPercent => Cards==0?0:Cloud*100/Cards;
            public string Summary => Cards==0?"尚未选择卡包":"云剑 "+Cloud+"/"+Cards+"（"+CloudPercent+"%） · 回气 "+Qi+" · 摸牌 "+Draw;
        }
        public static readonly IReadOnlyList<Route> Routes=new[]{
            new Route("cloud","云剑连招","先接云剑，再选插牌；闪风、追风和游龙续牌，灵海回气。","筑基开始衔接，元婴形成续牌循环。",48,46,44,42,40,41,43,45,51,52,47,49),
            new Route("intent","剑意／剑阵","剑挡、凝意蓄势；多段剑招兑现剑意，护盾支持剑阵攻守。","炼气蓄意，金丹多段兑现，化神剑阵收束。",49,47,45,43,41,40,42,44,46,48,51,52),
            new Route("spirit","灵剑周转","先回气再爆发；百鸟降费、飞灵回气，万法决定何时耗尽气池。","金丹巨鹏过渡，元婴解锁灵剑主题包后成型。",52,51,44,42,40,41,43,45,46,48,47,49)
        };
        public static Proposal Recommend(Route route,int rank,IEnumerable<StableId> owned)
        {
            var eligible=SwordRules.Packs.Where(p=>(int)p["rank"]<=rank).ToDictionary(p=>(int)p["index"]);
            var own=new HashSet<StableId>(owned);
            var desired=route.Priority.Where(eligible.ContainsKey).Take(3).Select(i=>eligible[i]).ToArray();
            var selected=route.Priority.Where(eligible.ContainsKey).Select(i=>SwordRules.PackId(eligible[i])).Where(own.Contains).Take(3).ToArray();
            var missing=desired.Where(p=>!own.Contains(SwordRules.PackId(p))).ToArray();
            return new Proposal{Route=route,Selected=selected,Missing=missing,IsTransition=missing.Length>0||route.Key=="spirit"&&rank<4};
        }
        public static string Names(IEnumerable<StableId> ids) => string.Join("\n",ids.Select(id=>SwordRules.Text(SectRegistry.FindPack(id),"name")));
        public static Pool Inspect(IEnumerable<StableId> ids)
        {
            var packs=ids.Distinct().Select(SectRegistry.FindPack).Where(p=>p!=null&&SectRegistry.Owner(p)==SwordRules.Lineage).ToArray();
            var cards=packs.SelectMany(p=>((JArray)p["cards"]).Values<string>()).Select(k=>SwordRules.Cards[SwordRules.CardPrefix+k]).ToArray();
            return new Pool{Packs=packs.Length,Cards=cards.Length,Cloud=cards.Count(c=>SwordRules.HasTag(c,"cloud")),
                Qi=cards.Count(c=>{var r=(JObject)c["rules"];return SwordRules.Int(r,"qi")>0||SwordRules.Int(r,"qiBp")>0||SwordRules.Int(r,"cloudQiPerChain")>0||SwordRules.Bool(r,"intentToQi")||SwordRules.Text(r,"persist")=="gatherQi"||SwordRules.Text(r,"onHit")=="qiPerHit";}),
                Draw=cards.Count(c=>{var r=(JObject)c["rules"];return new[]{"reAct","cloudReAct","qiReAct","intentReAct"}.Any(k=>SwordRules.Bool(r,k))||SwordRules.Text(r,"onHit")=="reAct";})};
        }
    }
}
