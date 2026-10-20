using System.Collections.Generic;
using HarmonyLib;
using Reincarnation.UI;
using Reincarnation.UI.Nodes;
using Reincarnation.UI.ViewModels;
using UnityEngine;
using UnityEngine.UI;

namespace YixianSect
{
    // Display-only sprite views: the archived full card faces remain unchanged.
    internal static class SectIllustrations
    {
        private static readonly Dictionary<long,Sprite> Crops=new Dictionary<long,Sprite>();
        internal static Sprite Get(Sprite source,float aspect)
        {
            if(source==null)return null;
            long key=((long)source.GetInstanceID()<<32)|(uint)Mathf.RoundToInt(aspect*1000);
            if(Crops.TryGetValue(key,out var cached)&&cached!=null)return cached;
            var full=source.rect;
            // All audited 308x508 faces share this illustration window. Exclude
            // the left printed name/cost and the entire old rules text footer.
            var crop=new Rect(full.x+full.width*74/308f,full.y+full.height*224/508f,full.width*208/308f,full.height*250/508f);
            if(crop.width/crop.height<aspect)
            {float height=crop.width/aspect;crop.y+=(crop.height-height)*.5f;crop.height=height;}
            else
            {float width=crop.height*aspect;crop.x+=(crop.width-width)*.5f;crop.width=width;}
            var result=Sprite.Create(source.texture,crop,new Vector2(.5f,.5f),source.pixelsPerUnit,0,SpriteMeshType.FullRect);
            result.name=source.name+".yixian-illustration";Crops[key]=result;return result;
        }
        internal static void Clear()
        {foreach(var sprite in Crops.Values)if(sprite!=null)Object.Destroy(sprite);Crops.Clear();}
    }
    [HarmonyPatch(typeof(UIContentIconLibrary),"Clear")]
    internal static class SectIllustrationLifetime
    {private static void Prefix(){SectIllustrations.Clear();}}

    [HarmonyPatch(typeof(CardTableHandCard),"Present")]
    internal static class SectHandIllustration
    {
        private static void Postfix(CardTableHandCard __instance,CardTableCardViewModel model)
        {
            bool sect=model?.Action!=null&&model.CardId!=null&&SectRegistry.Cards.ContainsKey(model.CardId);
            var view=__instance.GetComponent<SectHandArtLayout>();
            if(view==null)
            {
                if(!sect)return;
                view=__instance.gameObject.AddComponent<SectHandArtLayout>();
            }
            view.Present((Image)AccessTools.Field(typeof(CardTableHandCard),"art").GetValue(__instance),sect);
        }
    }
    internal sealed class SectHandArtLayout : MonoBehaviour
    {
        private UiRectSnapshot original;
        private Image image;
        private bool preserve;
        internal void Present(Image art,bool sect)
        {
            if(!sect)
            {
                if(original!=null){original.Restore();image.preserveAspect=preserve;original=null;}
                return;
            }
            if(original==null){image=art;original=new UiRectSnapshot(art.rectTransform);preserve=art.preserveAspect;}
            var rect=art.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);
            rect.pivot=new Vector2(.5f,1);rect.anchoredPosition=Vector2.zero;rect.sizeDelta=new Vector2(92,88);
            art.preserveAspect=true;art.sprite=SectIllustrations.Get(art.sprite,rect.rect.width/rect.rect.height);
        }
    }
}
