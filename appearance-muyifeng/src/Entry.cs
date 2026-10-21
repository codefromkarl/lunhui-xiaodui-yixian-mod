using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.ExternalBridges.Spum;
using Reincarnation.Gameplay.Combat;
using Reincarnation.Modding;
using Reincarnation.Runtime.Birth;
using Reincarnation.Runtime.Core;
using Reincarnation.UI.Nodes;
using Reincarnation.UI.ViewModels;
using Reincarnation.UI.Windows;
using UnityEngine;

namespace MuyifengAppearance
{
    public sealed class Entry : ICodeMod
    {
        internal static ICodeModContext Context;
        internal static bool Enabled;
        public void OnLoad(ICodeModContext context)
        {
            Context = context;
            var config = JObject.Parse(File.ReadAllText(Path.Combine(context.PackageDirectory, "appearance.json")));
            Enabled = (bool?)config["enabled"] ?? true;
            if (!Enabled) { context.Log("牧逸风外观已在 appearance.json 停用。"); return; }
            try
            {
                Art.Load(config, context.PackageDirectory);
                new Harmony(context.PackageId).PatchAll(typeof(Entry).Assembly);
                context.Log("牧逸风外观 0.2 已加载：仅本机玩家；完整眉眼头部、前后发和衣袍；原尺寸像素网格；不修改角色数据。");
                RuntimeVerification.Install(context);
            }
            catch
            {
                new Harmony(context.PackageId).UnpatchAll(context.PackageId);
                Art.Release();
                throw;
            }
        }
        internal static bool IsLocal(int slot)
        {
            if (!Enabled) return false;
            return GameManager.TryGetInstance(out var game) &&
                game.Sessions?.Current?.CurrentSnapshot != null &&
                game.Sessions.Current.CurrentSnapshot.LocalPlayerSlot.Value == slot;
        }
        internal static T Read<T>(object source, string name) => (T)AccessTools.Field(source.GetType(), name).GetValue(source);
        internal static bool Bind(GameObject root, bool useSkin)
        {
            if (root == null) return false;
            var state = root.GetComponent<SkinState>();
            if (state == null && !useSkin) return false;
            if (state == null) state = root.AddComponent<SkinState>();
            bool changed = state.Selected != useSkin;
            state.Selected = useSkin;
            if (useSkin) changed |= state.Apply(); else changed |= state.Restore();
            return changed;
        }
        internal static bool IsLocal(BattleCombatantView view) => view != null &&
            view.Kind == CombatantKind.Player && view.Side == CombatSide.Player &&
            view.OwnerSlot.IsValid && IsLocal(view.OwnerSlot.Value);
    }

    // The production source is an authored pixel grid, not a cropped concept image.
    // One character maps to exactly one texel. No resize/interpolation code exists.
    internal static class Art
    {
        internal static readonly Dictionary<string, Sprite> Parts = new Dictionary<string, Sprite>();
        private static readonly List<Texture2D> Textures = new List<Texture2D>();
        internal static void Load(JObject config, string directory)
        {
            string path = Path.GetFullPath(Path.Combine(directory, (string)config["pixelSource"]));
            if (!path.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("Pixel source must be inside the package.");
            var source=JObject.Parse(File.ReadAllText(path));
            if((string)source["format"]!="palette-grid-v1")throw new ArgumentException("Invalid pixel grid format.");
            var palette=new Dictionary<char,Color32>();
            foreach(var property in ((JObject)source["palette"]).Properties())
            {
                if(property.Name.Length!=1||!ColorUtility.TryParseHtmlString((string)property.Value,out var color))throw new ArgumentException("Invalid palette entry.");
                palette.Add(property.Name[0],color);
            }
            foreach(var property in ((JObject)source["parts"]).Properties())
            {
                var part=property.Value;var rows=((JArray)part["rows"]).Select(x=>(string)x).ToArray();
                int height=rows.Length,width=height>0?rows[0].Length:0;
                if(width<1||height<1||width>128||height>128||rows.Any(x=>x.Length!=width))throw new ArgumentException("Invalid sprite grid: "+property.Name);
                var pixels=new Color32[width*height];
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    if(!palette.TryGetValue(rows[y][x],out var color))throw new ArgumentException("Unknown palette cell in "+property.Name);
                    pixels[(height-1-y)*width+x]=color;
                }
                var texture=new Texture2D(width,height,TextureFormat.RGBA32,false){name="Muyifeng/"+property.Name,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                texture.SetPixels32(pixels);texture.Apply();Textures.Add(texture);
                var sprite=Sprite.Create(texture,new Rect(0,0,width,height),new Vector2((float)part["pivot"][0],(float)part["pivot"][1]),(float)part["pixelsPerUnit"],0,SpriteMeshType.FullRect);
                sprite.name="Muyifeng/"+property.Name;Parts.Add(property.Name,sprite);
            }
            if(!new[]{"Body/Head","HairBack","Cloth/Body","Cloth/Left","Cloth/Right","Pant/Left","Pant/Right","Body/Foot_L","Body/Foot_R"}.All(Parts.ContainsKey))throw new ArgumentException("Missing required native pixel part.");
        }
        internal static void Release()
        {
            foreach(var sprite in Parts.Values) UnityEngine.Object.Destroy(sprite); Parts.Clear();
            foreach(var texture in Textures) UnityEngine.Object.Destroy(texture); Textures.Clear();
        }
    }

    internal sealed class SkinState : MonoBehaviour
    {
        private struct Original { internal Sprite Sprite; internal Color Color; }
        private readonly Dictionary<SpriteRenderer,Original> originals=new Dictionary<SpriteRenderer,Original>();
        private readonly HashSet<SpriteRenderer> suppressed=new HashSet<SpriteRenderer>();
        private SpriteRenderer rearHair;
        private SpriteRenderer headRenderer;
        internal bool Selected;
        internal bool Apply()
        {
            if (!Selected) return false;
            bool changed=false;
            var rows=GetComponentsInChildren<SPUM_MatchingList>(true).SelectMany(x=>x.matchingTables).Where(x=>x!=null&&x.renderer!=null&&x.UnitType=="Unit").ToArray();
            var head=rows.FirstOrDefault(x=>x.PartType=="Body"&&x.Structure=="Head");
            headRenderer=head?.renderer;
            foreach(var row in rows)
            {
                bool hide=row.PartType=="Eye"||row.PartType=="FaceHair"||row.PartType=="Hair";
                string key=row.PartType+"/"+row.Structure;
                if(!hide&&!Art.Parts.ContainsKey(key))continue;
                if(!Art.Parts.Values.Contains(row.renderer.sprite)&&!(suppressed.Contains(row.renderer)&&row.renderer.sprite==null))originals[row.renderer]=new Original{Sprite=row.renderer.sprite,Color=row.renderer.color};
                if(hide){changed|=row.renderer.sprite!=null;row.renderer.sprite=null;suppressed.Add(row.renderer);continue;}
                var sprite=Art.Parts[key];
                Color color=row.renderer.color;
                changed |= row.renderer.sprite!=sprite || row.renderer.color!=color;
                row.renderer.sprite=sprite; row.renderer.color=color;
            }
            if(headRenderer!=null)
            {
                if(rearHair==null)
                {
                    var child=new GameObject("MuyifengRearHair");child.layer=headRenderer.gameObject.layer;child.transform.SetParent(headRenderer.transform,false);
                    rearHair=child.AddComponent<SpriteRenderer>();rearHair.sharedMaterial=headRenderer.sharedMaterial;changed=true;
                }
                changed|=!rearHair.enabled;rearHair.enabled=true;rearHair.sprite=Art.Parts["HairBack"];
                SyncHair();
            }
            return changed;
        }
        private void LateUpdate(){if(Selected)SyncHair();}
        private void SyncHair()
        {
            if(rearHair==null||headRenderer==null)return;
            rearHair.color=headRenderer.color;rearHair.sortingLayerID=headRenderer.sortingLayerID;rearHair.sortingOrder=headRenderer.sortingOrder-1;
        }
        internal bool Restore()
        {
            bool changed=false;
            foreach(var item in originals)
            {
                if(item.Key==null || (!Art.Parts.Values.Contains(item.Key.sprite)&&!(suppressed.Contains(item.Key)&&item.Key.sprite==null))) continue;
                item.Key.sprite=item.Value.Sprite; item.Key.color=item.Value.Color; changed=true;
            }
            if(rearHair!=null){changed|=rearHair.enabled;rearHair.enabled=false;}
            originals.Clear();suppressed.Clear();return changed;
        }
    }

    [HarmonyPatch(typeof(SpumBirthAppearancePreview), nameof(SpumBirthAppearancePreview.ApplyAppearance))]
    internal static class PreserveSkinAfterNativeLook
    {
        private static void Postfix(SpumBirthAppearancePreview __instance)
        {
            var binding=__instance.GetComponentInParent<SkinState>();
            if(binding!=null && binding.Selected) binding.Apply();
        }
    }

    [HarmonyPatch(typeof(BattleCombatantView), nameof(BattleCombatantView.ApplyLook))]
    internal static class BattleLook
    {
        private static void Prefix(BattleCombatantView __instance) => Entry.Bind(__instance.gameObject,Entry.IsLocal(__instance));
        private static void Postfix(BattleCombatantView __instance) => Entry.Bind(__instance.gameObject,Entry.IsLocal(__instance));
    }
    // Apply runs on pooled objects before ApplyLook; restore a former local player's
    // skin even when the new owner is an enemy/summon that never calls ApplyLook.
    [HarmonyPatch(typeof(BattleCombatantView), nameof(BattleCombatantView.Apply))]
    internal static class BattlePoolOwnership
    {
        private static void Prefix(BattleCombatantView __instance, RunCombatantSnapshot snapshot)
        {
            if(snapshot.Kind!=CombatantKind.Player || snapshot.Side!=CombatSide.Player || !Entry.IsLocal(snapshot.OwnerSlot.Value))
                Entry.Bind(__instance.gameObject,false);
        }
    }
    [HarmonyPatch(typeof(BattleCombatantView), nameof(BattleCombatantView.ResetView))]
    internal static class BattlePoolReset
    {
        private static void Prefix(BattleCombatantView __instance) => Entry.Bind(__instance.gameObject,false);
    }
    [HarmonyPatch(typeof(NodePartyPortrait), nameof(NodePartyPortrait.Set))]
    internal static class PartyPortraitLook
    {
        private static void Postfix(NodePartyPortrait __instance) => Update(__instance);
        internal static void Update(NodePartyPortrait portrait)
        {
            var model=Entry.Read<NodePartyMemberViewModel>(portrait,"model");
            if(Entry.Bind(Entry.Read<GameObject>(portrait,"rig"),model!=null && model.IsLocalPlayer))
                AccessTools.Field(typeof(NodePartyPortrait),"dirty").SetValue(portrait,true);
        }
    }
    [HarmonyPatch(typeof(NodePartyPortrait), "CreateRig")]
    internal static class PartyPortraitRecreation
    {
        private static void Postfix(NodePartyPortrait __instance) => PartyPortraitLook.Update(__instance);
    }
    [HarmonyPatch(typeof(GameHUDWindow), "RefreshPlayerPortrait")]
    internal static class BattleHudPortraitLook
    {
        private static void Postfix(GameHUDWindow __instance, CombatantCardViewModel local)
        {
            bool selected=local!=null && !local.IsEnemy && local.Kind==CombatantKind.Player &&
                BattleCombatantView.TryFindActive(local.RuntimeId,out var view) && Entry.IsLocal(view);
            if(Entry.Bind(Entry.Read<GameObject>(__instance,"playerPortraitRig"),selected))
                Entry.Read<GameObject>(__instance,"playerPortraitRig").GetComponentInChildren<Camera>(true)?.Render();
        }
    }
}
