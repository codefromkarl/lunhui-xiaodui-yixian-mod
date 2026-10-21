using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MuyifengAppearance
{
    internal sealed partial class AppearanceArt
    {
        internal JObject AtlasDefinition;
        internal readonly Dictionary<string,Texture2D> Sources=new Dictionary<string,Texture2D>();
        internal readonly Dictionary<string,Vector3> Offsets=new Dictionary<string,Vector3>();
        internal Vector3 SourcePoint(string key,JToken point)
        {
            var part=AtlasDefinition["parts"][key];var rect=part["rect"];var pivot=part["pivot"];float ppu=(float)part["pixelsPerUnit"];
            float x=((float)point[0]-(float)pivot[0]*(float)rect[2])/ppu;
            float y=((float)rect[3]-(float)point[1]-(float)pivot[1]*(float)rect[3])/ppu;
            float angle=((float?)part["rotation"]??0)*Mathf.Deg2Rad;
            var result=new Vector3(Mathf.Cos(angle)*x-Mathf.Sin(angle)*y,Mathf.Sin(angle)*x+Mathf.Cos(angle)*y,0);
            if((bool?)part["mirrorX"]==true)result.x=-result.x;
            return result;
        }
        private static string Inside(string directory,string relative)
        {
            var path=Path.GetFullPath(Path.Combine(directory,relative));
            if(!path.StartsWith(Path.GetFullPath(directory)+Path.DirectorySeparatorChar,StringComparison.Ordinal))
                throw new ArgumentException("Art source must remain inside the package.");
            return path;
        }
        private void LoadAtlas(string relative,string directory)
        {
            AtlasDefinition=JObject.Parse(File.ReadAllText(Inside(directory,relative)));
            if((string)AtlasDefinition["format"]!="source-atlas-v1")throw new ArgumentException("Unsupported source atlas.");
            foreach(var sheet in ((JObject)AtlasDefinition["sheets"]).Properties())
            {
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false){name=Prefix+"Source/"+sheet.Name,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                Textures.Add(texture);
                if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(Inside(directory,(string)sheet.Value))))throw new ArgumentException("Invalid atlas PNG: "+sheet.Name);
                Sources.Add(sheet.Name,texture);
            }
            foreach(var part in ((JObject)AtlasDefinition["parts"]).Properties())
            {
                var row=(JObject)part.Value;var source=Sources[(string)row["sheet"]];
                var rect=((JArray)row["rect"]).Values<int>().ToArray();
                int x=rect[0],top=rect[1],width=rect[2],height=rect[3];
                if(x<0||top<0||width<1||height<1||x+width>source.width||top+height>source.height)throw new ArgumentException("Part outside source: "+part.Name);
                var pivot=((JArray)row["pivot"]).Values<float>().ToArray();
                float ppu=(float)row["pixelsPerUnit"];if(ppu<=0)throw new ArgumentException("Invalid art scale.");
                var original=source.GetPixels32();var pixels=new Color32[width*height];
                var polygon=(row["mask"] as JArray)?.Select(v=>new Vector2((float)v[0],(float)v[1])).ToArray();
                var exclusions=(row["exclude"] as JArray)?.Select(p=>((JArray)p).Select(v=>new Vector2((float)v[0],(float)v[1])).ToArray()).ToArray();
                int threshold=(int?)row["alphaThreshold"]??0;
                var backing=row["backing"] as JObject;
                var overlap=(backing?["polygon"] as JArray)?.Select(v=>new Vector2((float)v[0],(float)v[1])).ToArray();
                Color32 overlapColor=new Color32();
                if(backing!=null){if(!ColorUtility.TryParseHtmlString((string)backing["color"],out var color))throw new ArgumentException("Invalid overlap color.");overlapColor=color;}
                var tint=(row["colorMultiply"] as JArray)?.Values<float>().ToArray();
                for(int yy=0;yy<height;yy++)for(int xx=0;xx<width;xx++)
                {
                    var pixel=original[(source.height-top-height+yy)*source.width+x+xx];
                    if(pixel.a<threshold||(polygon!=null&&!InPolygon(polygon,xx+.5f,height-yy-.5f))||
                        exclusions!=null&&exclusions.Any(p=>InPolygon(p,xx+.5f,height-yy-.5f)))pixel=new Color32();
                    if(pixel.a>0&&tint!=null)pixel=new Color32((byte)Mathf.RoundToInt(pixel.r*tint[0]),(byte)Mathf.RoundToInt(pixel.g*tint[1]),(byte)Mathf.RoundToInt(pixel.b*tint[2]),pixel.a);
                    // Hidden joint overlap only. Never paint over any opaque source-art texel.
                    if(pixel.a==0&&overlap!=null&&InPolygon(overlap,xx+.5f,height-yy-.5f))pixel=overlapColor;
                    pixels[yy*width+xx]=pixel;
                }
                if(row["componentSeed"] is JArray seed)KeepComponent(pixels,width,height,Mathf.FloorToInt((float)seed[0]),Mathf.FloorToInt((float)seed[1]));
                var pixelPivot=new Vector2(pivot[0]*width,pivot[1]*height);
                float rotation=(float?)row["rotation"]??0;
                // Only the supplementary sword is rotated into the native weapon axis.
                // Reference face/clothes are copied at original resolution, without resampling.
                if(rotation!=0)Rotate(ref pixels,ref width,ref height,ref pixelPivot,rotation);
                var texture=new Texture2D(width,height,TextureFormat.RGBA32,false){name=Prefix+"/"+part.Name,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                texture.SetPixels32(pixels);texture.Apply();Textures.Add(texture);
                var sprite=Sprite.Create(texture,new Rect(0,0,width,height),new Vector2(pixelPivot.x/width,pixelPivot.y/height),ppu,0,SpriteMeshType.FullRect);
                sprite.name=Prefix+"/"+part.Name;Parts.Add(part.Name,sprite);
                if(row["offset"] is JArray offset)Offsets.Add(part.Name,new Vector3((float)offset[0],(float)offset[1],0));
            }
            if(!new[]{"Body/Head","HairBack","Cloth/Body","Cloth/Left","Cloth/Right","Pant/Left","Pant/Right","Body/Foot_L","Body/Foot_R","Weapon/Sword"}.All(Parts.ContainsKey))
                throw new ArgumentException("Missing source-art part.");
            if(AtlasDefinition["rig"] is JObject&&!new[]{"Cloth/LeftLower","Cloth/RightLower","Hand/Grip_L","Hand/Grip_R","Hand/Open_L","Hand/Open_R"}.All(Parts.ContainsKey))
                throw new ArgumentException("Missing joint-bound source-art part.");
        }
        private static void KeepComponent(Color32[] pixels,int width,int height,int x,int top)
        {
            if(x<0||x>=width||top<0||top>=height||pixels[(height-1-top)*width+x].a==0)
                throw new ArgumentException("Component seed must be inside the extracted anatomy.");
            var keep=new bool[pixels.Length];var queue=new Queue<int>();int start=(height-1-top)*width+x;keep[start]=true;queue.Enqueue(start);
            while(queue.Count>0){int current=queue.Dequeue(),cx=current%width,cy=current/width;
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){
                    int nx=cx+dx,ny=cy+dy;if(nx<0||nx>=width||ny<0||ny>=height)continue;
                    int next=ny*width+nx;if(!keep[next]&&pixels[next].a>0){keep[next]=true;queue.Enqueue(next);}
                }
            }
            for(int i=0;i<pixels.Length;i++)if(!keep[i])pixels[i]=new Color32();
        }
        private static bool InPolygon(Vector2[] points,float x,float y)
        {
            bool inside=false;
            for(int i=0,j=points.Length-1;i<points.Length;j=i++)
                if((points[i].y>y)!=(points[j].y>y)&&x<(points[j].x-points[i].x)*(y-points[i].y)/(points[j].y-points[i].y)+points[i].x)inside=!inside;
            return inside;
        }
        private static void Rotate(ref Color32[] pixels,ref int width,ref int height,ref Vector2 pivot,float degrees)
        {
            float c=Mathf.Cos(degrees*Mathf.Deg2Rad),s=Mathf.Sin(degrees*Mathf.Deg2Rad);
            var origin=pivot;
            var corners=new[]{new Vector2(0,0),new Vector2(width,0),new Vector2(0,height),new Vector2(width,height)}
                .Select(p=>p-origin).Select(p=>new Vector2(c*p.x-s*p.y,s*p.x+c*p.y)).ToArray();
            int left=Mathf.FloorToInt(corners.Min(p=>p.x)),bottom=Mathf.FloorToInt(corners.Min(p=>p.y));
            int w=Mathf.CeilToInt(corners.Max(p=>p.x))-left,h=Mathf.CeilToInt(corners.Max(p=>p.y))-bottom;
            var result=new Color32[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                float dx=left+x+.5f,dy=bottom+y+.5f;
                int sx=Mathf.FloorToInt(pivot.x+c*dx+s*dy),sy=Mathf.FloorToInt(pivot.y-s*dx+c*dy);
                if(sx>=0&&sx<width&&sy>=0&&sy<height)result[y*w+x]=pixels[sy*width+sx];
            }
            pixels=result;width=w;height=h;pivot=new Vector2(-left,-bottom);
        }
    }
}
