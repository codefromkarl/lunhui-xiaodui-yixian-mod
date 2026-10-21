using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Reincarnation.Domain;
using UnityEngine;

namespace MuyifengAppearance
{
    // Stable default facade keeps the original package and verification entry
    // points usable. Each rendered character holds its own immutable art set.
    internal static class Art
    {
        internal static readonly AppearanceArt Default=new AppearanceArt();
        internal static Dictionary<string,Sprite> Parts=>Default.Parts;
        internal static Dictionary<string,Texture2D> Sources=>Default.Sources;
        internal static Dictionary<string,Vector3> Offsets=>Default.Offsets;
        internal static JObject AtlasDefinition=>Default.AtlasDefinition;
        internal static Vector3 SourcePoint(string key,JToken point)=>Default.SourcePoint(key,point);
        internal static void Load(JObject config,string directory)=>Default.Load(config,directory);
        internal static void Release()=>Default.Release();
    }
    internal static class SkinCatalog
    {
        private static readonly Dictionary<string,AppearanceArt> arts=new Dictionary<string,AppearanceArt>();
        private static readonly Dictionary<string,int> order=new Dictionary<string,int>();
        internal static IReadOnlyDictionary<string,AppearanceArt> All=>arts;
        internal static int SourceOrder(string id)=>order.TryGetValue(id,out var value)?value:int.MaxValue;
        internal static void Register(string id,string name,BirthAppearance token,string group,AppearanceArt art,int ordinal=int.MaxValue)
        {
            AppearanceChoices.Register(new AppearanceChoice(id,name,token,BirthAppearance.Default,
                (root,selected)=>{
                    var state=root.GetComponent<SkinState>();
                    if(selected){Entry.Bind(root,true,art);root.GetComponent<SkinState>().BeforeNativeLook();}
                    else if(state!=null&&state.Selected&&ReferenceEquals(state.SkinArt,art))Entry.Bind(root,false);
                },
                root=>{var state=root.GetComponent<SkinState>();if(state!=null&&state.Selected&&ReferenceEquals(state.SkinArt,art))state.AfterNativeLook();},group));
            arts.Add(id,art);
            order.Add(id,ordinal);
        }
        internal static void Load(JObject config,string directory)
        {
            if(!(config["characters"] is JArray characters))return;
            foreach(var row in characters)
            {
                var bytes=row["token"].ToObject<byte[]>();
                if(bytes.Length!=7)throw new ArgumentException("Character identity needs exactly seven bytes.");
                var art=new AppearanceArt();
                try{
                    art.Load((JObject)row,directory);
                    Register((string)row["id"],(string)row["name"],new BirthAppearance(bytes[0],bytes[1],bytes[2],bytes[3],bytes[4],bytes[5],bytes[6]),(string)row["group"],art,
                        int.TryParse((string)row["sourceId"],out var ordinal)?ordinal:int.MaxValue);
                }
                catch{art.Release();throw;}
            }
        }
        internal static void Release()
        {
            foreach(var pair in arts){AppearanceChoices.Unregister(pair.Key);if(!ReferenceEquals(pair.Value,Art.Default))pair.Value.Release();}
            arts.Clear();order.Clear();
        }
    }
}
