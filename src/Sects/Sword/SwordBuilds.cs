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
            public string School { get; }
            public string Key { get; }
            public string Name { get; }
            public string Play { get; }
            public string Milestone { get; }
            public int[] Priority { get; }
            private readonly Dictionary<int,int[]> rankPriorities=new Dictionary<int,int[]>();
            public bool ReuseFoundationGift { get; private set; }
            public Route AtRank(int rank,params int[] first){rankPriorities[rank]=first;return this;}
            public Route KeepFoundationGift(){ReuseFoundationGift=true;return this;}
            public IEnumerable<int> PriorityAt(int rank)
                =>rankPriorities.TryGetValue(rank,out var first)?first.Concat(Priority).Distinct():Priority;
            public Route(string key,string name,string play,string milestone,params int[] priority)
                :this(SwordRules.Lineage,key,name,play,milestone,priority){}
            public Route(string school,string key,string name,string play,string milestone,params int[] priority)
            {School=school;Key=key;Name=name;Play=play;Milestone=milestone;Priority=priority;}
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
            new Route("spirit","灵剑周转","百鸟降费，飞灵、灵海回气；万法会耗尽全部斗气，宜留作终结。","元婴优先灵海续航；化神三包组装费360点，比旧方案增加35点。",52,51,46,44,42,40,41,43,45,48,47,49)
        };
        // Historical pack-based proposals retained for explicit old-catalogue audits, not current menus.
        public static readonly IReadOnlyList<Route> FiveRoutes=new[]{
            new Route(FiveElementRules.Lineage,"metal_fire","金火兑现","保留基础印记作为启动入口；先激活金、火，再打收益牌。","印记占用三包之一，不默认追加第四包；新包仍需购买。",60,87,84,82,80,61,81,83,85,86,88,89),
            new Route(FiveElementRules.Lineage,"water_wood","水木周转","保留木、水印记；以水势、增攻和多段攻击衔接，避免只堆持续牌。","筑基沿用赠包；化神先沿用元婴三包，暂缓购买生生流转，仍可自由换包。",60,88,85,61,83,81,82,80,84,86,87,89).AtRank(2,60,61,80).AtRank(5,60,85,61).KeepFoundationGift(),
            new Route(FiveElementRules.Lineage,"earth_cycle","土灵／混元","保留土灵印与基础攻击；土灵承接护盾，混元按实际元素覆盖兑现。","筑基沿用赠包；金丹避免两包持续牌挤占输出，相生可补足未永久激活的元素。",61,89,86,83,60,81,82,80,84,85,87,88).AtRank(2,61,60,80).KeepFoundationGift()
        };
        public static readonly IReadOnlyList<Route> StarRoutes=new[]{
            new Route(SevenStarRules.Lineage,"thunder","卦雷经营","产卦后将保障留给雷击；两仪支援会奖励保留的卦象。","元婴六爻与返卦形成引擎；功能包会降低核心密度。",98,94,93,90,70,71,91,92,95,97),
            new Route(SevenStarRules.Lineage,"positions","星位多段","功能牌推进格位，多段在星位兑现；高阶天元完成覆盖。","前期保留星罗启动；天元不是低阶构筑的前提。",97,95,71,91,93,70,90,92,94,98).AtRank(2,91,71,70).AtRank(3,93,91,71),
            new Route(SevenStarRules.Lineage,"healing","后招治疗","同源第二次结算进入后招，治疗量由枯木兑现；支援与输出兼顾。","金丹获得枯木出口；多人群疗收益按实际存活人数变化。",98,92,93,90,91,70,71,94,95,97).AtRank(2,90,70,71)
        };
        public static IReadOnlyList<Route> RoutesFor(string school)
            =>school==SwordRules.Lineage?Routes:school==SevenStarRules.Lineage?StarRoutes:System.Array.Empty<Route>();
        public static Proposal Recommend(Route route,int rank,IEnumerable<StableId> owned)
        {
            var eligible=SectRegistry.Packs.Where(p=>SectRegistry.Owner(p)==route.School&&(int)p["rank"]<=rank).ToDictionary(p=>(int)p["index"]);
            var own=new HashSet<StableId>(owned);
            var priority=route.PriorityAt(rank).ToArray();
            if(rank==2&&route.ReuseFoundationGift)
            {
                var gift=priority.Where(eligible.ContainsKey).Select(i=>eligible[i])
                    .FirstOrDefault(p=>(int)p["rank"]==2&&own.Contains(SwordRules.PackId(p)));
                if(gift!=null)priority=priority.Where(i=>eligible.ContainsKey(i)&&
                    ((int)eligible[i]["rank"]==1||eligible[i]==gift)).Concat(priority).Distinct().ToArray();
            }
            var desired=priority.Where(eligible.ContainsKey).Take(3).Select(i=>eligible[i]).ToArray();
            var selected=priority.Where(eligible.ContainsKey).Select(i=>SwordRules.PackId(eligible[i])).Where(own.Contains).Take(3).ToArray();
            var missing=desired.Where(p=>!own.Contains(SwordRules.PackId(p))).ToArray();
            return new Proposal{Route=route,Selected=selected,Missing=missing,IsTransition=missing.Length>0||route.Key=="spirit"&&rank<4};
        }
        public static string Names(IEnumerable<StableId> ids) => string.Join("\n",ids.Select(id=>SwordRules.Text(SectRegistry.FindPack(id),"name")));
        public static string PoolSummary(string school,IEnumerable<StableId> ids)
        {
            if(school==SwordRules.Lineage)return Inspect(ids).Summary;
            var packs=ids.Distinct().Select(SectRegistry.FindPack).Where(p=>p!=null&&SectRegistry.Owner(p)==school).ToArray();
            var cards=packs.SelectMany(p=>((JArray)p["cards"]).Values<string>()).Select(k=>SectRegistry.Cards[SectRegistry.CardPrefix+k]).ToArray();
            if(cards.Length==0)return "尚未选择卡包";
            if(school==SevenStarRules.Lineage)
            {
                int omen=cards.Count(c=>(int?)c["rules"]?["omenGain"]>0||(int?)c["rules"]?["rankRefundOmen"]>0);
                int post=cards.Count(c=>SectDecisionHints.Post((JObject)c["rules"]));
                int star=cards.Count(c=>(int?)c["rules"]?["starGain"]>0);
                return "卡池 "+cards.Length+"张 · 产卦牌 "+omen+" · 产星牌 "+star+" · 后招牌 "+post+"\n统计含条件收益；仙命与战斗状态另计。";
            }
            var seals=cards.Select(c=>(int?)c["rules"]["activate"]??0).Where(e=>e>=1&&e<=5).Distinct().OrderBy(e=>e).ToArray();
            var missing=cards.Select(c=>(int?)c["element"]??0).Where(e=>e>=1&&e<=5&&!seals.Contains(e)).Distinct().OrderBy(e=>e).ToArray();
            string Names(int[] elements)=>string.Join("、",elements.Select(e=>new[]{"","金","水","木","火","土"}[e]));
            return "卡池 "+cards.Length+" 张 · 永久印记："+(seals.Length==0?"无":Names(seals))+
                (missing.Length==0?"":" · 缺印："+Names(missing)+"（可相生／仙命启动）");
        }
        public static string Diagnostics(Proposal proposal,IEnumerable<StableId> previous)
        {
            var cards=proposal.Selected.Select(SectRegistry.FindPack).SelectMany(p=>((JArray)p["cards"]).Values<string>()).Select(k=>SectRegistry.Cards[SectRegistry.CardPrefix+k]).ToArray();
            int old=previous.Select(SectRegistry.FindPack).Where(p=>p!=null&&SectRegistry.Owner(p)==proposal.Route.School).Sum(p=>((JArray)p["cards"]).Count);
            string result="当前池 "+old+" → 推荐池 "+cards.Length+"张。";
            if(cards.Length==0)return result;
            if(proposal.Route.School==SwordRules.Lineage)
            {
                int paid=cards.Count(c=>SectCardRules.PrintedCost(c)>=8),qi=Inspect(proposal.Selected).Qi;
                result+="\n8斗气以上牌 "+paid+"张 · 含回气效果 "+qi+"张（不等于净回气）。";
                if(proposal.Route.Key=="spirit"&&!cards.Any(c=>(string)c["rules"]?["persist"]=="spiritDiscount"))result+="\n缺灵剑减费：百鸟灵剑诀尚未进入推荐池。";
            }
            result+="\n缺失组件先过渡；不会自动购买或解锁。";return result;
        }
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
