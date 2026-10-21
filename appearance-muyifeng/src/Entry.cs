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
                MuyifengChoice.Register();
                SkinCatalog.Load(config,context.PackageDirectory);
                new Harmony(context.PackageId).PatchAll(typeof(Entry).Assembly);
                context.Log("弈仙牌角色外观 0.7.1 已加载："+SkinCatalog.All.Count+"套角色创建可选造型，分角色独立图集与身份，造型持物不受实际装备覆盖；不修改角色数值。");
                RuntimeVerification.Install(context);
            }
            catch
            {
                new Harmony(context.PackageId).UnpatchAll(context.PackageId);
                SkinCatalog.Release();
                AppearanceChoices.Unregister(MuyifengChoice.Id);
                Art.Release();
                throw;
            }
        }
        internal static T Read<T>(object source, string name) => (T)AccessTools.Field(source.GetType(), name).GetValue(source);
        internal static bool Bind(GameObject root, bool useSkin, AppearanceArt art = null)
        {
            if (root == null) return false;
            var state = root.GetComponent<SkinState>();
            if (state == null && !useSkin) return false;
            if (state == null) state = root.AddComponent<SkinState>();
            bool changed = state.Selected != useSkin;
            if(useSkin&&state.SkinArt!=(art??MuyifengAppearance.Art.Default)){changed|=state.Restore();state.SkinArt=art??MuyifengAppearance.Art.Default;}
            state.Selected = useSkin;
            if (useSkin) changed |= state.Apply(); else changed |= state.Restore();
            return changed;
        }

    }

    // Production uses original PNG cutouts. The older palette-grid loader remains
    // available only for packages that explicitly select pixelSource.
    internal sealed partial class AppearanceArt
    {
        internal string Prefix="Muyifeng";
        internal readonly Dictionary<string, Sprite> Parts = new Dictionary<string, Sprite>();
        private readonly List<Texture2D> Textures = new List<Texture2D>();
        internal void Load(JObject config, string directory)
        {
            Prefix=(string)config["spritePrefix"]??"Muyifeng";
            if(config["atlasSource"]!=null){LoadAtlas((string)config["atlasSource"],directory);return;}
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
                var texture=new Texture2D(width,height,TextureFormat.RGBA32,false){name=Prefix+"/"+property.Name,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                texture.SetPixels32(pixels);texture.Apply();Textures.Add(texture);
                var sprite=Sprite.Create(texture,new Rect(0,0,width,height),new Vector2((float)part["pivot"][0],(float)part["pivot"][1]),(float)part["pixelsPerUnit"],0,SpriteMeshType.FullRect);
                sprite.name=Prefix+"/"+property.Name;Parts.Add(property.Name,sprite);
            }
            if(!new[]{"Body/Head","HairBack","Cloth/Body","Cloth/Left","Cloth/Right","Pant/Left","Pant/Right","Body/Foot_L","Body/Foot_R","Weapon/Sword"}.All(Parts.ContainsKey))throw new ArgumentException("Missing required native pixel part.");
        }
        internal void Release()
        {
            foreach(var sprite in Parts.Values) UnityEngine.Object.Destroy(sprite); Parts.Clear();
            foreach(var texture in Textures) UnityEngine.Object.Destroy(texture); Textures.Clear();
            Sources.Clear();Offsets.Clear();AtlasDefinition=null;
        }
    }

    [DefaultExecutionOrder(10000)]
    internal sealed partial class SkinState : MonoBehaviour
    {
        private struct Original { internal Sprite Sprite; internal Color Color; }
        private readonly Dictionary<SpriteRenderer,Original> originals=new Dictionary<SpriteRenderer,Original>();
        private readonly HashSet<SpriteRenderer> suppressed=new HashSet<SpriteRenderer>();
        internal bool NativePartsHidden=>suppressed.Count>0&&suppressed.All(r=>r==null||r.sprite==null);
        private SpriteRenderer rearHair;
        private SpriteRenderer headRenderer;
        internal bool Selected;
        internal AppearanceArt SkinArt;
        private AppearanceArt Art=>SkinArt??MuyifengAppearance.Art.Default;
        internal bool Apply()
        {
            if (!Selected) return false;
            if(RigEnabled)return ApplyRig();
            bool changed=false;
            var rows=GetComponentsInChildren<SPUM_MatchingList>(true).SelectMany(x=>x.matchingTables).Where(x=>x!=null&&x.renderer!=null&&x.UnitType=="Unit").ToArray();
            var head=rows.FirstOrDefault(x=>x.PartType=="Body"&&x.Structure=="Head");
            headRenderer=head?.renderer;
            foreach(var row in rows)
            {
                bool hide=row.PartType=="Eye"||row.PartType=="FaceHair"||row.PartType=="Hair";
                string key=row.PartType+"/"+row.Structure;
                // Cosmetic mapping only: keep the equipment identity, stats, sockets and animation.
                // Do not turn an empty hand, shield, bow or gun into a sword.
                if(row.PartType=="Weapons"&&row.renderer.sprite!=null&&
                    (row.renderer.sprite==Art.Parts["Weapon/Sword"]||row.renderer.sprite.name.StartsWith("EQ-BLD-",StringComparison.Ordinal)))
                    key="Weapon/Sword";
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
                rearHair.transform.localPosition=HairOffset;
                SyncHair();
            }
            return changed;
        }
        private Vector3 HairOffset => Art.Offsets.TryGetValue("HairBack",out var offset)?offset:Vector3.zero;
        private void LateUpdate(){if(Selected){if(RigEnabled)SyncRigPose();else SyncHair();}}
        private void SyncHair()
        {
            if(rearHair==null||headRenderer==null)return;
            rearHair.color=headRenderer.color;rearHair.sortingLayerID=headRenderer.sortingLayerID;rearHair.sortingOrder=headRenderer.sortingOrder-1;
        }
        internal bool Restore()
        {
            bool changed=DisableRig();
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
        private static void Prefix(SpumBirthAppearancePreview __instance,ref BirthAppearance appearance)
        {
            AppearanceChoices.Begin(AppearanceChoices.Root(__instance),appearance);
            var choice=AppearanceChoices.Find(appearance);if(choice!=null)appearance=choice.BaseAppearance;
        }
        private static void Postfix(SpumBirthAppearancePreview __instance)
        {
            AppearanceChoices.End(AppearanceChoices.Root(__instance));
        }
    }

    [HarmonyPatch(typeof(BattleCombatantView), nameof(BattleCombatantView.ApplyLook))]
    internal static class BattleLook
    {
        private static void Prefix(BattleCombatantView __instance,BirthAppearance appearance)=>AppearanceChoices.Begin(__instance.gameObject,appearance);
        private static void Postfix(BattleCombatantView __instance)=>AppearanceChoices.End(__instance.gameObject);
    }
    // Pool ownership changes clear the former character choice before the new look.
    // Enemy/summon views never inherit a player cosmetic just from ownership.
    [HarmonyPatch(typeof(BattleCombatantView), nameof(BattleCombatantView.Apply))]
    internal static class BattlePoolOwnership
    {
        private static void Prefix(BattleCombatantView __instance, RunCombatantSnapshot snapshot)
        {
            if(__instance.RuntimeId!=snapshot.RuntimeId||snapshot.Kind!=CombatantKind.Player||snapshot.Side!=CombatSide.Player)
                AppearanceChoices.Clear(__instance.gameObject);
        }
    }
    [HarmonyPatch(typeof(BattleCombatantView), nameof(BattleCombatantView.ResetView))]
    internal static class BattlePoolReset
    {
        private static void Prefix(BattleCombatantView __instance) => AppearanceChoices.Clear(__instance.gameObject);
    }
    [HarmonyPatch(typeof(GameHUDWindow), "RefreshPlayerPortrait")]
    internal static class BattleHudPortraitLook
    {
        private static void Prefix(GameHUDWindow __instance)=>HudPortraitFraming.Restore(__instance);
        private static void Postfix(GameHUDWindow __instance, CombatantCardViewModel local)
        {
            if(local!=null)HudPortraitFraming.Apply(__instance);
        }
    }
}
