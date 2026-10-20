using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reincarnation.Runtime.Content;
using Reincarnation.Runtime.Core;
using Reincarnation.Runtime.Scenes;
using UnityEngine;

namespace YixianSect
{
    // Explicit self-test launches only: finish native menu startup after every
    // requested report; optionally use Unity's normal shutdown lifecycle.
    internal sealed class SelfTestShutdown : MonoBehaviour
    {
        private static readonly Dictionary<string,string> Reports=new Dictionary<string,string>();
        private string receiptPath;
        private bool menuRequested;
        private bool quitRequested;
        internal static void Complete(string suite,string path)
        {
            var args=Environment.GetCommandLineArgs();
            Reports[suite]=Path.GetFileName(path);
            if((args.Contains("-yixian-selftest")||args.Contains("-yixian-schools-selftest"))&&!Reports.ContainsKey("runtime"))return;
            if(args.Contains("-yixian-fates-selftest")&&!Reports.ContainsKey("fates"))return;
            var go=new GameObject("YixianSelfTestShutdown");
            var shutdown=go.AddComponent<SelfTestShutdown>();
            shutdown.receiptPath=Path.Combine(Path.GetDirectoryName(path),"quit-"+Path.GetFileName(path));
            shutdown.quitRequested=args.Contains("-yixian-selftest-quit");
            UnityEngine.Object.DontDestroyOnLoad(go);
        }
        private void Update()
        {
            // Tests can finish while the main menu is still loading. Let the game
            // complete its own startup guard before normal shutdown; do not clear
            // or patch the guard, or the next launch could enter Mod safe mode.
            if(receiptPath==null)return;
            if(File.Exists(Path.Combine(LocalModLoader.DefaultDirectory,".startup-pending")))
            {
                // Automated launches can remain on the local login scene. Open
                // the menu through the native navigator, without activating a
                // profile or binding a run, so the game completes its own guard.
                if(!menuRequested&&GameManager.TryGetInstance(out var game)&&game.Run?.Authority==null&&game.CurrentSceneRoot is ILoginSceneFlow)
                    menuRequested=game.Scenes.TryLoadLocalScene("scene.menu",out var _);
                return;
            }
            if(!quitRequested){UnityEngine.Object.Destroy(gameObject);return;}
            var receipt=new JObject{["status"]="quit_requested",["startup_completed"]=true,["reports"]=JObject.FromObject(Reports)};
            File.WriteAllText(receiptPath,receipt.ToString());
            enabled=false;
            Application.Quit(0);
        }
    }
}
