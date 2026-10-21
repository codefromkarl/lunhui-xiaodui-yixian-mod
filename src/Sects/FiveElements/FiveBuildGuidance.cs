using System;
using System.Collections.Generic;
using System.Linq;
using Reincarnation.Domain;

namespace YixianSect
{
    // Suggestions own no unlocked cards, currency, RNG or authority. A different
    // learning history is respected; missing pieces are explained, never granted.
    public static class FiveBuildGuidance
    {
        public sealed class Route
        {
            public string Name,Play;
            public string[][] Learn;
            public string[] Priority;
        }
        public static readonly Route[] Routes={
            new Route{Name="水势经营",Play="先激活水灵，再经营水势；前期保留木灵回气，不强求纯水。",Learn=new[]{
                new[]{"水灵·波澜","木灵·复苏"},new[]{"水灵阵","水灵·泉涌"},new[]{"水灵·腾浪","水灵·潜遁"},new[]{"水灵·乘风浪","水灵·雨纷纷"}},
                Priority=new[]{"水灵·雨纷纷","水灵·乘风浪","水灵·腾浪","水灵·波澜","水灵·涛","水灵印","水灵·泉涌","水灵阵","木灵·复苏","木灵印","水灵·潜遁","土灵印","金灵印","金灵·针","木灵·芽","火灵印"}},
            new Route{Name="金木多段",Play="灵印启动，锋锐与多段配合；保留回气位置，不只堆攻击。",Learn=new[]{
                new[]{"金灵阵","木灵·疏影"},new[]{"金灵·锋芒","木灵·巡林"},new[]{"木灵·玫刺","金灵·铁骨"},new[]{"金灵·回锋刃","木灵·柳纷飞"}},
                Priority=new[]{"金灵·回锋刃","木灵·玫刺","金灵·锋芒","金灵阵","金灵印","木灵印","水灵印","木灵·疏影","木灵·柳纷飞","木灵·巡林","金灵·针","木灵·芽","土灵印","火灵印","水灵·涛","金灵·铁骨"}},
            new Route{Name="土防混元",Play="土灵承压，混元补进攻；土系无法凑纯八张，主动保留异系工具。",Learn=new[]{
                new[]{"土灵阵","镇印心法"},new[]{"金灵·蓄锐","火灵·轰爆"},new[]{"土灵·流沙","混元碎击"},new[]{"土灵·合八荒","火灵·燃烬灭"}},
                Priority=new[]{"混元碎击","土灵·合八荒","土灵·流沙","土灵阵","金灵印","木灵印","水灵印","火灵印","金灵·蓄锐","火灵·燃烬灭","火灵·轰爆","镇印心法","土灵印","金灵·针","水灵·涛","木灵·芽"}}
        };
        public static StableId Card(string name)=>FiveDeckRules.CardId(SectRegistry.Cards.Values.Single(c=>SectRegistry.Owner(c)==FiveElementRules.Lineage&&(string)c["name"]==name));
        public static IReadOnlyList<StableId> Suggest(Route route,IReadOnlyList<StableId> candidates,int required,int rank)
        {
            var names=required==2?route.Learn[rank-2]:route.Priority;
            return names.Select(Card).Concat(candidates).Where(candidates.Contains).Distinct().Take(required).ToArray();
        }
        public static string Explain(Route route,IReadOnlyList<StableId> candidates,IReadOnlyList<StableId> selected,int required,int rank)
        {
            string result=route.Play;
            if(required==2)return result+"\n本次仅暂选两张，确认后才解锁。";
            var desired=route.Priority.Select(Card).Where(id=>(int)SectRegistry.Cards[id.Value]["rank"]<=rank).Take(8).ToArray();
            var missing=desired.Where(id=>!candidates.Contains(id)).Select(id=>(string)SectRegistry.Cards[id.Value]["name"]).ToArray();
            int arrays=selected.Count(id=>(int?)SectRegistry.Cards[id.Value]["rules"]?["array"]>0||(int?)SectRegistry.Cards[id.Value]["rules"]?["activationArray"]>0);
            return result+(missing.Length==0?"":"\n未解锁组件："+string.Join("、",missing)+"；已使用现有牌过渡。")+
                "\n持续阵法"+arrays+"张，本场最多使用2张。"+(arrays>2?"存在互斥选择，不能全部布置。":"")+"\n只改变待选，保存后下次抽牌生效，不替换手牌。";
        }
    }
}
