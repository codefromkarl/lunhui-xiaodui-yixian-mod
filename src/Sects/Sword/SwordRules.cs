using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Modding;

namespace YixianSect
{
    // All mutable battle state lives in CodeModStatePlan, not process-local counters.
    public static class SwordRules
    {
        public const string Lineage = "yixian.sect.lineage.sword";
        public const string CardPrefix = SectIds.CardPrefix;
        public static readonly Dictionary<string, JObject> Cards = new Dictionary<string, JObject>(StringComparer.Ordinal);
        public static readonly List<JObject> Packs = new List<JObject>();
        public static int PlayBaseCost { get; private set; } = 0;
        public static string StateKey(StableId actor, string name) => "sword." + actor.Value + "." + name;
        public static int Int(JObject r, string key, int fallback = 0) => (int?)r[key] ?? fallback;
        public static bool Bool(JObject r, string key) => (bool?)r[key] ?? false;
        public static string Text(JObject r, string key) => (string)r[key] ?? "";
        public static int ReadInt(ModStateSnapshot state, StableId actor, string name)
            => state?.Entries.FirstOrDefault(x => x.PackageId == "yixian.sect" && x.Key == StateKey(actor, name))?.Number ?? 0;
        public static string ReadText(ModStateSnapshot state, StableId actor, string name)
            => state?.Entries.FirstOrDefault(x => x.PackageId == "yixian.sect" && x.Key == StateKey(actor, name))?.Text ?? "";

        public static void Configure(JObject table)
        {
            SectRegistry.Configure(table);
            var cards = ((JArray)table["cards"]).OfType<JObject>().Where(c => SectRegistry.Owner(c) == Lineage).ToList();
            var packs = ((JArray)table["packs"]).OfType<JObject>().Where(p => SectRegistry.Owner(p) == Lineage).ToList();
            if (cards.Select(c => Text(c,"key")).Distinct().Count() != cards.Count ||
                packs.Select(p => Int(p,"index")).Distinct().Count() != packs.Count ||
                packs.Select(p => Text(p,"key")).Distinct().Count() != packs.Count)
                throw new ArgumentException("卡牌／卡组身份重复。");
            foreach (var p in packs)
            {
                var keys = ((JArray)p["cards"]).Values<string>().ToList();
                if ((keys.Count != 3 && keys.Count != 4) || keys.Distinct().Count() != keys.Count || Int(p,"rank") < 1 || Int(p,"rank") > 5 || Int(p,"price") < 0)
                    throw new ArgumentException("卡组声明无效：" + p["key"]);
                foreach (var key in keys)
                {
                    var c = cards.SingleOrDefault(x => Text(x,"key") == key);
                    if (c == null || Int(c,"rank") != Int(p,"rank")) throw new ArgumentException("卡组存在跨境界或缺失卡牌：" + key);
                }
            }
            PlayBaseCost = SectRegistry.BaseCost;
            Cards.Clear(); Packs.Clear();
            foreach (var c in cards) Cards.Add(CardPrefix + Text(c,"key"), c);
            Packs.AddRange(packs);

        }

        public static bool HasTag(JObject card,string tag) => ((JArray)card["tags"])?.Values<string>().Contains(tag) ?? false;
        public static int Value(JObject rule,string key,StatBlock stats,int unit=3)
            => Int(rule,key)*unit + FixedPointMath.ScaleByBasisPointsFloor(AttributeScaling.Read((CombatScalingAttribute)Int(rule,key+"Attr"),stats),Int(rule,key+"Bp"));
        public static StableId PackId(JObject pack) => SectRegistry.PackId(pack);
        public static JObject FindPack(StableId id) => Packs.SingleOrDefault(p=>PackId(p)==id);

        public static void Register(ICodeModContext context)
        {
            foreach (var id in Cards.Keys) context.OnAction(id, Play);
            context.OnBattle("battle_start", Start);
            context.OnBattle("round_start", Round);
            context.OnBattle("action_resolved", Resolved);
            context.OnBattle("damage_dealt", Hits);
        }

        public static int ResourceCost(JObject card,int shield,int cloud,bool discount)
        {
            var r=(JObject)card["rules"];
            int surcharge=SectCardRules.PrintedCost(card);
            if(Bool(r,"shieldFree")&&shield>0 || Bool(r,"cloudFree")&&cloud>0)surcharge=0;
            if(HasTag(card,"spirit")&&discount)surcharge-=4;
            return Math.Max(0,surcharge);
        }
        public static int EnergyCost(JObject card, CombatantState actor, ModStateSnapshot state)
            =>ResourceCost(card,actor.Shield,ReadInt(state,actor.RuntimeId,"cloud"),ReadInt(state,actor.RuntimeId,"spiritDiscount")>0);

        private sealed class Turn
        {
            public readonly ModBattleContext C; public readonly CodeBattlePlan P; public readonly ModCombatantSnapshot Actor;
            public int PlannedEnergy; public int PlannedShield;
            public Turn(ModBattleContext c, CodeBattlePlan p, ModCombatantSnapshot actor) { C=c;P=p;Actor=actor;PlannedEnergy=actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting);PlannedShield=actor.Shield; }
            public int Get(string n) => P.GetInt(StateKey(Actor.RuntimeId,n));
            public string Str(string n) => P.GetText(StateKey(Actor.RuntimeId,n));
            public void Set(string n,int v) => P.SetState(StateKey(Actor.RuntimeId,n),v);
            public void Set(string n,string v) => P.SetState(StateKey(Actor.RuntimeId,n),v);
            public void Add(string n,int v) => Set(n,Math.Max(0,Get(n)+v));
            public EnergyPoolSlot Slot => Actor.EnergyPools.Primary.Kind == EnergyPoolKind.Fighting ? EnergyPoolSlot.Primary :
                Actor.EnergyPools.Secondary.Kind == EnergyPoolKind.Fighting ? EnergyPoolSlot.Secondary : EnergyPoolSlot.None;
            public int Energy => Actor.EnergyPools.GetCurrent(EnergyPoolKind.Fighting);
            public void Qi(int amount)
            {
                int gain=Math.Min(amount,Math.Max(0,Actor.EnergyPools.GetMaximum(EnergyPoolKind.Fighting)-PlannedEnergy));
                if(Slot==EnergyPoolSlot.None||gain<=0)return;
                PlannedEnergy+=gain;P.EnergyRestore(Actor.RuntimeId,Slot,gain);
                if(Get("energyGuard")>0)Shield(gain);
            }
            public void Pay(int amount) { if (amount>0 && amount<=PlannedEnergy && Slot != EnergyPoolSlot.None) { PlannedEnergy-=amount;P.EnergyPay(Actor.RuntimeId,Slot==EnergyPoolSlot.Primary?amount:0,Slot==EnergyPoolSlot.Secondary?amount:0);if(Get("energyGuard")>0)Shield(amount); } }
            public void Shield(int amount) { if(amount>0) {PlannedShield+=amount;P.Shield(Actor.RuntimeId,amount);} }
            public void Heal(int amount) { if(amount>0) P.Heal(Actor.RuntimeId,amount); }
            // Requests are committed with ModState, then drained by the authoritative native draw bridge.
            public void Again() => Add("drawPending",1);
            public void Damage(IReadOnlyList<StableId> targets,int amount,int hits,bool pierce)
            {
                if(amount<=0 || targets.Count==0) return;
                var values = new Dictionary<string,object> { ["effects"]=1,["e0.primitive"]=10,["e0.target"]=10,["e0.flat"]=amount,
                    ["e0.damage"]=10,["e0.shape"]=10,["pierceBp"]=pierce?10000:0 };
                for(int i=0;i<Math.Min(hits,8);i++) P.Use("effect.skill",targets,values,caster:Actor.RuntimeId);
            }
        }

        private static void SyncView(Turn t)
        {
            SectDecisionHints.Publish(t.P,t.Actor.RuntimeId,Lineage,t.Get);
            foreach(var name in new[]{"intent","cloud","power"})
            {
                var id=new StableId("yixian.sect.status."+name);
                t.P.ClearStatus(t.Actor.RuntimeId,id);
                if(t.Get(name)>0)t.P.Status(t.Actor.RuntimeId,t.Actor.RuntimeId,id,t.Get(name));
            }
        }
        private static ModCombatantSnapshot Find(ModBattleContext c, StableId id) => c.Units.FirstOrDefault(x=>x.RuntimeId==id);
        private static bool IsSect(ModCombatantSnapshot u) => u != null && !u.IsDowned && u.Holds(new StableId(Lineage));
        public static void Start(ModBattleContext c,CodeBattlePlan p)
        {
            foreach(var u in c.Units.Where(IsSect))
            {
                var prefix=StateKey(u.RuntimeId,"");
                foreach(var e in c.State.Entries.Where(x=>x.PackageId=="yixian.sect" && x.Key.StartsWith(prefix,StringComparison.Ordinal))) p.RemoveState(e.Key);
                var t=new Turn(c,p,u);
                if (u.Loadout.Abilities.Any(id=>Packs.Any(pack=>((JArray)pack["cards"]).Values<string>().Any(k=>
                    Cards.TryGetValue(CardPrefix+k,out var card) && Bool((JObject)card["rules"],"openingQi")) &&
                    id==PackId(pack))) && t.Energy==0) t.Qi(4);
                SyncView(t);
            }
        }
        public static void Round(ModBattleContext c,CodeBattlePlan p)
        {
            foreach(var u in c.Units.Where(IsSect))
            {
                var t=new Turn(c,p,u);
                if(t.Get("nextShield")>0) { t.Shield(t.Get("nextShield"));t.Set("nextShield",0); }
                if(t.Get("waterMoon")>0) { t.Shield(t.Get("waterMoonAmount"));t.Add("waterMoon",-1); }
                if(t.Get("gatherQi")>0 && c.Round>t.Get("gatherAt") && (c.Round-t.Get("gatherAt"))%2==0) t.Qi(t.Get("gatherQiAmount"));
            }
        }
        public static void Play(ModBattleContext c,CodeBattlePlan p)
        {
            if(!Cards.TryGetValue(c.ActionId.Value,out var card)) return;
            var u=Find(c,c.ActorId);
            if(!IsSect(u)) throw new InvalidOperationException("剑宗卡牌需要云灵剑宗血统。");
            var t=new Turn(c,p,u);
            var targets=c.Action?.Targets ?? Array.Empty<StableId>();
            Apply(t,card,targets,false);
            FateRules.Played(c,p,u,card);
            SyncView(t);
        }

        private static void Apply(Turn t,JObject card,IReadOnlyList<StableId> targets,bool repeated)
        {
            var r=(JObject)card["rules"]; var name=Text(card,"name");
            int chain=t.Get("cloud"), intent=t.Get("intent"), power=t.Get("power");
            bool cloud=HasTag(card,"cloud"), linked=FateRules.Linked(t.Actor,chain);
            bool formation=HasTag(card,"formation");
            var stats=t.Actor.Attributes.GetValueOrDefault();
            // Snapshot is after the engine charged this action's cost.
            int qiGain=Value(r,"qi",stats,4), remaining=t.PlannedEnergy, max=t.Actor.EnergyPools.GetMaximum(EnergyPoolKind.Fighting);
            int gained=Math.Min(qiGain,Math.Max(0,max-remaining));
            t.Qi(qiGain); int qi=(remaining+gained)/4;
            if(linked && Int(r,"cloudQiPerChain")>0) t.Qi(chain*4);
            int shield=Value(r,"shield",stats) + (linked?Value(r,"cloudShield",stats):0) + Int(r,"shieldPerAttack")*t.Get("attacks")*3;
            int fromQi=Int(r,"shieldPerQi")*qi;
            if(Int(r,"shieldPerQiCap")>0) fromQi=Math.Min(fromQi,Int(r,"shieldPerQiCap"));
            shield+=fromQi*3;
            if(linked) shield+=Int(r,"cloudShieldPerChain")*chain*3;
            if(Int(r,"shieldPerQiIntent")>0) shield+=Math.Min(qi+intent+Int(r,"intent"),Int(r,"shieldPerQiIntentCap"))*3;
            int baseDamage=Value(r,"damage",stats) + (linked?Value(r,"cloudDamage",stats):0) + Int(r,"damagePerQi")*qi*3;
            if(Int(r,"damagePerShield")>0) baseDamage+=Int(r,"damagePerShield")*((t.PlannedShield+shield)/3)*3;
            if(Int(r,"damagePerShieldDivisor")>0) baseDamage+=t.PlannedShield/(Int(r,"damagePerShieldDivisor")*3)*3;
            if(Int(r,"qiThreshold")>0) baseDamage=t.PlannedEnergy>Int(r,"qiThreshold")*4?Value(r,"conditionalDamage",stats):0;
            if(Int(r,"spendAllQiDamage")>0) {baseDamage+=qi*Int(r,"spendAllQiDamage")*3;t.Pay(remaining+gained);}
            int hits=Int(r,"hits",1);
            if(Bool(r,"extraQiHit") && remaining>=4) {t.Pay(4);hits++;}
            bool attacks=baseDamage>0;
            bool sword=card["sourceId"]!=null;
            int usedIntent=attacks && sword && !Bool(r,"noIntent")?intent:0;
            int multiplier=Bool(r,"doubleIntent")?2:1;
            int amount=attacks?baseDamage+(usedIntent+(sword?power:0))*multiplier*3:0;
            bool pierce=sword && t.Get("pierce")>0;
            if(attacks)
            {
                t.Set("intent",intent-usedIntent);if(sword)t.Set("pierce",0);
                t.Add("attacks",1);
                t.Set("pending",Text(card,"key"));t.Set("spentIntent",usedIntent);
                var selected=t.C.Action?.SelectedTarget??default;
                var primary=targets.Contains(selected)?selected:targets.FirstOrDefault();
                t.Set("primaryTarget",primary.Value);
                t.Set("hitHandled",0);
                t.Damage(targets,amount,hits,pierce);
                var area=card["area"] as JObject;
                var secondary=SwordTargeting.Select(SwordTargeting.For(t.C),t.Actor.RuntimeId,primary,card,linked)
                    .Where(id=>!targets.Contains(id)).ToArray();
                t.Set("areaTargets","");
                if(area!=null && Text(area,"shape")=="splash")
                    t.Set("areaTargets",string.Join("\n",secondary.Select(id=>id.Value)));
                else foreach(var id in secondary)
                    t.Damage(new[]{id},FixedPointMath.ScaleByBasisPointsFloor(amount,Int(area,"secondaryBp")),hits,pierce);
            }
            else if(!repeated) t.Set("pending","");
            t.Shield(shield);
            int heal=Value(r,"heal",stats)+(linked?Value(r,"cloudHeal",stats):0);
            if(Int(r,"healPerIntentPower")>0) heal=Math.Min(Int(r,"healCap",100)*3,heal+(intent+power)*Int(r,"healPerIntentPower")*3);
            t.Heal(heal);
            int addedIntent=Int(r,"intent")+(linked?Int(r,"cloudIntent"):0);
            addedIntent+=Math.Min(qi*Int(r,"intentPerQi"),Int(r,"intentPerQiCap"));
            t.Add("intent",addedIntent);
            if(Int(r,"intentAmplify")>0) t.Add("intent",(t.Get("intent")*Int(r,"intentAmplify")+99)/100);
            if(Bool(r,"intentToQi")) {t.Qi(t.Get("intent")*4);t.Set("intent",0);}
            if(Int(r,"pierceNext")>0 || linked&&Int(r,"cloudPierce")>0) t.Set("pierce",1);
            t.Add("power",Int(r,"power")+(linked?Int(r,"cloudPower"):0));
            if(t.Get("cloudHeal")>0 && cloud) t.Heal(t.Get("cloudHealAmount"));
            if(t.Get("energyGuard")>0 && !repeated)
            {
                var paid=ResourceCost(card,t.Actor.Shield,FateRules.Linked(t.Actor,chain)?Math.Max(1,chain):chain,t.Get("spiritDiscount")>0);
                t.Shield(paid);
            }
            var persistent=Text(r,"persist");
            if(persistent.Length>0)
            {
                t.Set(persistent,persistent=="waterMoon"?3:1);
                if(persistent=="cloudHeal")t.Set("cloudHealAmount",Value(r,"persistHeal",stats));
                if(persistent=="waterMoon")t.Set("waterMoonAmount",Value(r,"tickShield",stats,1));
                if(persistent=="gatherQi"){t.Set("gatherAt",t.C.Round);t.Set("gatherQiAmount",Value(r,"tickQi",stats,4));}
            }
            if(!repeated)
            {
                if(Bool(r,"reAct") || linked&&Bool(r,"cloudReAct") || qi>0&&Bool(r,"qiReAct") || t.Get("intent")>0&&Bool(r,"intentReAct")) t.Again();
                if(Bool(r,"repeatFormation"))
                {
                    var previous=t.Str("formation");
                    if(Cards.TryGetValue(previous,out var prev)) Apply(t,prev,targets,true);
                }
                else if(formation) t.Set("formation",CardPrefix+Text(card,"key"));
                t.Set("cloud",cloud?chain+Int(r,"cloudCount",1):(FateRules.Has(t.Actor,FateRules.CloudSea)?chain:0));
            }
        }

        public static void Hits(ModBattleContext c,CodeBattlePlan p)
        {
            foreach(var u in c.Units.Where(IsSect))
            {
                var t=new Turn(c,p,u);var key=t.Str("pending");
                if(key.Length==0 || t.Get("hitHandled")!=0 || !Cards.TryGetValue(CardPrefix+key,out var card)) continue;
                var primary=new StableId(t.Str("primaryTarget"));
                var hits=c.Hits.Where(h=>h.Source==u.RuntimeId && h.Target==primary && h.Direct && h.HealthDamage>0 && Find(c,h.Target)?.Side!=u.Side).ToList();
                if(hits.Count==0) continue;
                // Commit the guard before supplemental damage: it must not retrigger itself.
                t.Set("hitHandled",1);
                int health=hits.Sum(h=>h.HealthDamage); var r=(JObject)card["rules"];
                switch(Text(r,"onHit"))
                {
                    // A health-hit payoff is damage, not a second attack that can miss.
                    // The SDK damage effect applies physical mitigation in the same plan transaction.
                    case "thunder":
                        int payoff=Value(r,"hitDamage",u.Attributes.GetValueOrDefault());
                        p.Damage(u.RuntimeId,primary,payoff,DamageType.Physical);
                        var area=card["area"] as JObject;
                        int splash=FixedPointMath.ScaleByBasisPointsFloor(payoff,area==null?0:Int(area,"secondaryBp"));
                        foreach(var id in t.Str("areaTargets").Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries).Select(s=>new StableId(s)))
                            if(splash>0 && Find(c,id) is ModCombatantSnapshot areaTarget && !areaTarget.IsDowned && areaTarget.Side!=u.Side && areaTarget.Kind!=CombatantKind.AuxiliaryTarget)
                                p.Damage(u.RuntimeId,id,splash,DamageType.Physical);
                        break;
                    case "returnIntent":t.Add("intent",t.Get("spentIntent"));break;
                    case "intent3":t.Add("intent",Int(r,"hitIntent",3));break;
                    case "shieldDamage":t.Shield(health);break;
                    case "nextShield":t.Add("nextShield",r["nextShield"] == null ? 21 : Value(r,"nextShield",u.Attributes.GetValueOrDefault()));break;
                    case "drainQi":
                        var target=Find(c,hits[0].Target);
                        if(target!=null){int pay=Math.Min(4,target.EnergyPools.Primary.Current);if(pay>0)p.EnergyPay(target.RuntimeId,pay);}break;
                    case "reAct":t.Again();break;
                    case "qiPerHit":t.Qi(hits.Count*4);break;
                }
                SyncView(t);
            }
        }
        public static void Resolved(ModBattleContext c,CodeBattlePlan p)
        {
            var u=Find(c,c.ActorId);if(!IsSect(u)) return;
            var t=new Turn(c,p,u);t.Set("pending","");t.Set("primaryTarget","");t.Set("areaTargets","");
            if(!FateRules.Has(u,FateRules.CloudSea) && !Cards.ContainsKey(c.ActionId.Value) && CardTableCatalog.TryGetCard(c.ActionId,out var _)){t.Set("cloud",0);SyncView(t);}
        }
    }
}
