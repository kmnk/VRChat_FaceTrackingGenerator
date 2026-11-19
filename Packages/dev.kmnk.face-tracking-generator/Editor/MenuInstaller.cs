using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Kmnk.FaceTrackingGenerator
{
    // The state machines' submenus (Menus/<state machine>.asset in the output folder); MenuBuilder puts them in the
    // generated menu. Before 0.17.0 an "Add toggles to menu" button wrote the toggles into the avatar's own menu (R28).
    public static class MenuInstaller
    {
        public static string StateMenuPath(TableModel m, string sm) => $"{m.MenuFolder}/Menus/{sm}.asset";

        static VRCExpressionsMenu LoadOrCreateMenu(string path)
        {
            var sub = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(path);
            if (sub != null) return sub;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            sub = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            AssetDatabase.CreateAsset(sub, path);
            return sub;
        }

        // Writes the toggles of each state machine's submenu: one per menu state, setting FT/Menu/<name> to the state's
        // value. The asset keeps its GUID, so the avatar's menu keeps pointing to it. Returns the paths written.
        public static List<string> WriteStateMenus(TableModel m, List<string> log)
        {
            var written = new List<string>();
            foreach (var sm in FtBuilder.WithMenu(m))
            {
                var path = StateMenuPath(m, sm.Name);
                var sub = LoadOrCreateMenu(path);
                if (sm.MenuStates.Count() > VRCExpressionsMenu.MAX_CONTROLS)
                    log.Add($"warning: state machine {sm.Name}: {sm.MenuStates.Count()} menu states; the submenu has the first {VRCExpressionsMenu.MAX_CONTROLS}");
                sub.controls = sm.MenuStates.Take(VRCExpressionsMenu.MAX_CONTROLS).Select(st => new VRCExpressionsMenu.Control
                {
                    name = st.Name,
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = FtBuilder.MenuParam(sm.Name) },
                    value = sm.MenuValue(st.Name),
                    subParameters = new VRCExpressionsMenu.Control.Parameter[0],
                    labels = new VRCExpressionsMenu.Control.Label[0],
                }).ToList();
                EditorUtility.SetDirty(sub);
                log.Add($"menu: {path}: {sub.controls.Count} toggles on {FtBuilder.MenuParam(sm.Name)}");
                written.Add(path);
            }
            AssetDatabase.SaveAssets();
            MenuBuilder.RemoveOthers($"{m.MenuFolder}/Menus", written, log);
            return written;
        }
    }
}
