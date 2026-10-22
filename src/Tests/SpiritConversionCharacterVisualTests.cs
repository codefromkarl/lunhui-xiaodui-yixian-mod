using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using Reincarnation.Runtime.Managers;
using Reincarnation.Runtime.Run;
using Reincarnation.UI.Flow;
using Reincarnation.UI.ViewModels;
using Reincarnation.UI.Windows;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YixianSect
{
    internal static partial class VisualSelfTests
    {
        private sealed class CharacterChoiceGateway : IRunCommandGateway
        {
            internal Func<RunAuthoritySnapshot> Snapshot;
            internal Func<RunCommand,RunCommandDispatchResult> Dispatch;
            internal int Commands;
            public bool IsAvailable=>true;
            public PlayerSlotId LocalPlayerSlot=>CurrentSnapshot.ViewerSlot;
            public RunAuthoritySnapshot CurrentSnapshot=>Snapshot();
            public event Action<RunAuthoritySnapshot> SnapshotChanged;
            public event Action<RunCommandDispatchResult> CommandCompleted;
            public event Action<BattleActionGatewayResult> BattleActionResolved {add{}remove{}}
            public event Action<BattleCommandQueued> BattleCommandQueued {add{}remove{}}
            public bool TrySubmit(RunCommand command,out RunCommandDispatchResult result)
            {
                Commands++;result=Dispatch(command);SnapshotChanged?.Invoke(CurrentSnapshot);CommandCompleted?.Invoke(result);return true;
            }
            public void ReplayCurrentSnapshot()=>SnapshotChanged?.Invoke(CurrentSnapshot);
        }
        internal static JObject SpiritConversionCharacter(string directory,DataManager data,Func<RunAuthoritySnapshot> snapshot,Func<RunCommand,RunCommandDispatchResult> dispatch)
        {
            Directory.CreateDirectory(directory);
            var root=new GameObject("SpiritCharacterVisual",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));root.SetActive(false);
            var cameraObject=new GameObject("SpiritCharacterCamera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<30;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.03f,.04f);camera.orthographic=true;camera.orthographicSize=4.5f;
            var texture=new RenderTexture(1600,900,24);texture.Create();camera.targetTexture=texture;
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
            try
            {
                var prefab=Prefab<InventoryWindow>();Require(prefab!=null,"native character window missing");
                var window=UnityEngine.Object.Instantiate(prefab,root.transform,false).GetComponent<InventoryWindow>();window.enabled=false;
                var gateway=new CharacterChoiceGateway{Snapshot=snapshot,Dispatch=dispatch};
                gateway.SnapshotChanged+=s=>AccessTools.Method(typeof(InventoryWindow),"OnModChoiceSnapshot").Invoke(window,new object[]{s});
                gateway.CommandCompleted+=r=>AccessTools.Method(typeof(InventoryWindow),"OnModChoiceCompleted").Invoke(window,new object[]{r});
                foreach(bool enabled in new[]{false,true,false})
                {
                    var current=snapshot();var player=current.Players.First(p=>p.Slot==current.ViewerSlot);
                    window.Setup(RunUIPresentationMapper.BuildCharacterCenter(current,player,data),Array.Empty<InventorySlotViewModel>(),Array.Empty<InventorySlotViewModel>(),false,false,false);
                    AccessTools.Field(typeof(InventoryWindow),"modChoiceGateway").SetValue(window,gateway);
                    AccessTools.Method(typeof(InventoryWindow),"HandleProgressionTab").Invoke(window,null);
                    AccessTools.Method(typeof(InventoryWindow),"HandleAbilitiesTab").Invoke(window,null);
                    var icons=(List<string>)AccessTools.Field(typeof(InventoryWindow),"abilityIconKeys").GetValue(window);
                    int selected=icons.IndexOf("icon."+SpiritConversion.Ability);Require(selected>=0,"conversion missing from real ability list");
                    AccessTools.Method(typeof(InventoryWindow),"SelectAbility").Invoke(window,new object[]{selected});
                    Layer(root.transform);root.SetActive(true);Canvas.ForceUpdateCanvases();
                    var button=window.GetComponentsInChildren<Button>(true).Single(b=>b.name=="SpiritConversionToggle");
                    Require(button.gameObject.activeInHierarchy&&button.interactable,"character ability toggle unavailable");
                    Require(button.GetComponentInChildren<TMP_Text>().text==(enabled?"关闭转化":"开启转化"),"character toggle state wrong");
                    Capture(camera,texture,root,Path.Combine(directory,enabled?"character-spirit-on.png":"character-spirit-off.png"));
                    var corners=new Vector3[4];((RectTransform)button.transform).GetWorldCorners(corners);
                    Require(corners.All(p=>{var s=camera.WorldToScreenPoint(p);return s.x>=0&&s.x<=1600&&s.y>=0&&s.y<=900;}),"character toggle outside viewport: "+string.Join(";",corners.Select(p=>camera.WorldToScreenPoint(p).ToString())));
                    Capture(camera,texture,root,Path.Combine(directory,enabled?"character-spirit-on.png":"character-spirit-off.png"));
                    if(gateway.Commands<2){button.onClick.Invoke();Require(gateway.Commands==(enabled?2:1),"character click did not submit one native command");}
                }
                // Changing the selected ability must remove the conversion control.
                AccessTools.Method(typeof(InventoryWindow),"SelectAbility").Invoke(window,new object[]{0});
                var remaining=window.GetComponentsInChildren<Button>(true).Single(b=>b.name=="SpiritConversionToggle");
                var allIcons=(List<string>)AccessTools.Field(typeof(InventoryWindow),"abilityIconKeys").GetValue(window);
                if(allIcons[0]!="icon."+SpiritConversion.Ability)Require(!remaining.gameObject.activeSelf,"conversion toggle leaked onto another ability");
                return new JObject{["native_ability_list"]=true,["native_choice_commands"]=gateway.Commands,["off_on_off"]=true,["unrelated_ability_hidden"]=true};
            }
            finally{UnityEngine.Object.DestroyImmediate(root);camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(cameraObject);}
        }
    }
}
