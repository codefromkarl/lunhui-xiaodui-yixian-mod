using System;
using System.Collections.Generic;
using System.Linq;
using Reincarnation.Domain;

namespace YixianSect
{
    // UI-only selection; both learning and equipping use the same exact-count rule.
    public sealed class FiveDeckDraft
    {
        public IReadOnlyList<StableId> Candidates { get; }
        public int Required { get; }
        private readonly List<StableId> selected;
        public IReadOnlyList<StableId> Selected => selected.AsReadOnly();
        public bool Ready => selected.Count==Required;
        public FiveDeckDraft(IReadOnlyList<StableId> candidates,IReadOnlyList<StableId> initial,int required)
        {
            if(candidates==null||initial==null||candidates.Distinct().Count()!=candidates.Count||initial.Distinct().Count()!=initial.Count||initial.Any(id=>!candidates.Contains(id))||required<1||initial.Count>required||candidates.Count<required)throw new ArgumentException("选牌范围或数量无效。");
            Candidates=candidates.ToArray();selected=initial.ToList();Required=required;
        }
        public void Apply(IReadOnlyList<StableId> cards)
        {
            if(cards==null||cards.Count!=Required||cards.Distinct().Count()!=Required||cards.Any(id=>!Candidates.Contains(id)))throw new ArgumentException("推荐必须恰好包含已解锁的合法牌。");
            selected.Clear();selected.AddRange(cards);
        }
        public bool Toggle(StableId card)
        {
            if(!Candidates.Contains(card))return false;
            if(selected.Remove(card))return true;
            if(selected.Count>=Required)return false;
            selected.Add(card);return true;
        }
    }
}
