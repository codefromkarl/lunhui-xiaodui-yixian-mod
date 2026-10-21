using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;
using Reincarnation.UI.Flow;
using Reincarnation.UI.ViewModels;

namespace YixianSect
{
    // Flat metadata on one inert, hidden status per counter. Travels through the
    // existing authoritative snapshot/network path; never reads the local host.
    public static class SectDecisionHints
    {
        public const string ReadoutPrefix = "yixian.sect.status.readout.";
        public static IEnumerable<string> ReadoutKeys(string sect)
        {
            if(sect==SwordRules.Lineage)return new[]{"spiritDiscount","nextShield","pierce"};
            if(sect==FiveElementRules.Lineage)return new[]{"persistentCount"}.Concat(SectRegistry.Cards.Values.Where(c=>SectRegistry.Owner(c)==sect&&((int?)c["rules"]?["array"]>0||(int?)c["rules"]?["activationArray"]>0)).Select(c=>"consumed."+(string)c["key"]));
            return new[]{"healingTotal"}.Concat(SectRegistry.Cards.Values.Where(c=>SectRegistry.Owner(c)==sect&&Post((JObject)c["rules"])).Select(c=>"used"+(string)c["sourceId"]));
        }
        public static bool Post(JObject r)=>new[]{"postAttack","postHeal","postGuard"}.Any(k=>(int?)r[k]>0)||(bool?)r["postAgain"]==true;
        public static void Publish(CodeBattlePlan plan,StableId actor,string sect,Func<string,int> read)
        {
            foreach(string key in ReadoutKeys(sect))
            {
                var id=new StableId(ReadoutPrefix+SectRegistry.Prefix(sect)+"."+key);int n=read(key);
                plan.ClearStatus(actor,id);if(n>0)plan.Status(actor,actor,id,1,flat:n);
            }
        }
        public static int Counter(RunCombatantSnapshot actor,string sect,string key)
        {
            var hidden=actor.Statuses.FirstOrDefault(s=>s.StatusId.Value==ReadoutPrefix+SectRegistry.Prefix(sect)+"."+key);
            if(hidden!=null)return hidden.FlatAmount;
            string id="yixian.sect.status."+(sect==SwordRules.Lineage?"":SectRegistry.Prefix(sect)+".")+key;
            return actor.Statuses.Where(s=>s.StatusId.Value==id).Sum(s=>s.Stacks.Count);
        }
        public static string Describe(JObject card,RunCombatantSnapshot actor,RunPlayerSnapshot player)
        {
            string sect=SectRegistry.Owner(card);var r=(JObject)card["rules"];int Get(string k)=>Counter(actor,sect,k);
            var lines=new List<string>();
            if(sect==SevenStarRules.Lineage)
            {
                int slot=SevenStarRules.Slot(Get("plays")+1);bool position=actor.Statuses.Any(s=>s.StatusId.Value==SevenStarRules.StatusPrefix+"position"+slot);
                lines.Add("本次第"+slot+"格"+(position?" · 星位，每段攻击＋"+Get("star")*3:" · 非星位"));
                if(Post(r))lines.Add(Get("used"+(string)card["sourceId"])>0?"本次触发后招":"首次使用：后招尚未生效");
                if(Get("repeat")>0)lines.Add("梅开二度：本牌重施一次，不再次推进格位；第二次结算可触发后招");
                int checks=(int?)r["randomMax"]>0?Math.Max(1,(int?)r["randomRepeats"]??1):0;
                checks+=(int?)r["chanceRepeats"]??0;
                if((int?)r["randomHealMax"]>0)checks++;
                if((int?)r["weakChance"]>0)checks++;
                if(checks>0)lines.Add("当前"+Get("omen")+"卦 · 首次结算"+checks+"次随机判定，保障前"+Math.Min(checks,Get("omen"))+"次；不预知随机选敌／选人");
                if((int?)r["healingStep"]>0)lines.Add("累计治疗量"+Get("healingTotal")+"（含溢出） · 枯木增伤＋"+(Get("healingTotal")/(int)r["healingStep"]*(int)r["damagePerHealing"]));
            }
            else if(sect==FiveElementRules.Lineage)
            {
                int e=(int?)card["element"]??0;string[] names={"无","金","水","木","火","土"};
                bool active=e>0&&actor.Statuses.Any(s=>s.StatusId.Value=="yixian.sect.status.five."+new[]{"","metal","water","wood","fire","earth"}[e]);
                bool activating=e>0&&((int?)r["activate"]==e||Get("nextActivate")>0);
                bool linked=e>0&&FiveElementRules.Links(Get("last"),e);
                if(e>0)lines.Add(names[e]+"灵条件："+(active?"已永久激活":activating?"本牌先激活":linked?(Get("last")==e?"上张同系":"上张"+names[Get("last")]+"灵相生"):"未满足"));
                if((int?)r["array"]>0||(int?)r["activationArray"]>0)
                    lines.Add(Get("consumed."+(string)card["key"])>0?"本场已布置，不能再次使用":"本场阵法 "+Get("persistentCount")+"／2"+(Get("persistentCount")>=2?" · 已达上限":""));
                if((bool?)r["freeWhenElement"]==true)lines.Add(active||activating||linked?"条件满足：免除原"+SectCardRules.PrintedCost(card)+"斗气费用":"条件未满足：仍需"+SectCardRules.PrintedCost(card)+"斗气");
            }
            else if(sect==SwordRules.Lineage)
            {
                bool linked=Get("cloud")>0||player.HasPurchasedShelfAbility(new StableId(FateRules.CloudSea));
                if(SwordRules.HasTag(card,"cloud"))lines.Add(linked?"本次连云生效":"尚未连云：本牌用于起手");
                bool attack=(int?)r["damage"]>0||(linked&&(int?)r["cloudDamage"]>0)||(int?)r["damageBp"]>0;
                if(attack&&card["sourceId"]!=null&&(bool?)r["noIntent"]!=true&&Get("intent")>0)
                    lines.Add("本次整张消耗"+Get("intent")+"剑意 · 每段攻击＋"+(Get("intent")*3*((bool?)r["doubleIntent"]==true?2:1)));
                string hit=(string)r["onHit"];
                if(hit=="intent3")lines.Add("主目标直接击伤后获得3剑意；护盾命中不触发");
                if(hit=="nextShield")lines.Add("主目标直接击伤后，下回合获得护盾；当前待发"+Get("nextShield"));
                if((int?)r["spendAllQiDamage"]>0)lines.Add("终结：耗尽剩余斗气，出牌后不会保留该部分斗气");
            }
            return string.Join("\n",lines);
        }
    }
    [HarmonyPatch(typeof(BattleUIPresentationMapper),"BuildCardTable")]
    internal static class SectDecisionPresentation
    {
        private static void Postfix(RunAuthoritySnapshot snapshot,RunPlayerSnapshot player,CardTableViewModel __result)
        {
            var actor=snapshot?.Battle?.Combatants.FirstOrDefault(u=>u.Kind==CombatantKind.Player&&u.OwnerSlot==player?.Slot&&!u.IsDowned);
            if(actor==null||__result==null)return;
            foreach(var card in __result.Hand)
            {
                if(!SectRegistry.Cards.TryGetValue(card.CardId,out var rule))continue;
                string hint=SectDecisionHints.Describe(rule,actor,player);if(hint.Length==0)continue;
                card.Detail=hint+"\n\n"+card.Detail;
            }
        }
    }
}
