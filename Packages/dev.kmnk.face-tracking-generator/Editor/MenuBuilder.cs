using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Kmnk.FaceTrackingGenerator
{
    // The generated menu: a copy of the avatar's menus from the root down to the table's "menu", with the FT toggle,
    // the AFK toggle and a submenu per state machine with menu states added to that copy. Menus off that path stay the
    // avatar's own assets. The avatar's menu files are never changed, so the avatar's own menu uses only its own
    // parameters (the SDK's build panel checks the avatar before NDMF runs), and an avatar that rebuilds its menu keeps
    // the toggles in every build.
    public static class MenuBuilder
    {
        public static VRCExpressionsMenu Build(TableModel m, VRCExpressionsMenu root, List<string> log, List<string> written, Generator.ParameterBudget budget, bool withAfk)
        {
            if (root == null)
            {
                log.Add("notice: menu: the avatar has no Expressions Menu; the FT toggles and the state submenus are not added");
                return null;
            }
            var menuPath = AssetRefs.Path(m.Menu, "menu", log);
            var want = string.IsNullOrEmpty(menuPath) ? null : AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(menuPath);
            var chain = want == null ? null : PathTo(root, want, new HashSet<VRCExpressionsMenu>());
            if (chain == null)
            {
                if (!string.IsNullOrEmpty(menuPath))
                    log.Add($"notice: menu: {menuPath} is not in the avatar's menus; the FT toggles go into the root menu {root.name}");
                chain = new List<VRCExpressionsMenu> { root };
            }

            // What the avatar's menus already have (toggles added by hand or by older versions): not added again.
            var all = AllMenus(root, new HashSet<VRCExpressionsMenu>());
            bool HasToggle(string param) => all.Any(x => x.controls.Any(c => c.type != VRCExpressionsMenu.Control.ControlType.SubMenu && c.parameter?.name == param));
            bool HasSubMenu(VRCExpressionsMenu sub) => all.Any(x => x.controls.Any(c => c.subMenu == sub));
            // The asset output's submenu of a state machine, which an avatar's own menu can have from 0.16.x and earlier.
            bool HasAssetSubMenu(string sm) => m.AssetMenuFolder != m.MenuFolder
                && all.Any(x => x.controls.Any(c => c.subMenu != null && AssetDatabase.GetAssetPath(c.subMenu) == $"{m.AssetMenuFolder}/{FtNames.StateMenusFolder}/{sm}.asset"));

            var dir = $"{m.OutputFolder}/Menu";
            System.IO.Directory.CreateDirectory(dir);
            var copies = new List<VRCExpressionsMenu>();
            for (int i = 0; i < chain.Count; i++)
            {
                var copy = CopyOf(chain[i], $"{dir}/{i}_{Safe(chain[i].name)}.asset", written);
                if (i > 0)
                    foreach (var c in copies[i - 1].controls.Where(c => c.subMenu == chain[i])) c.subMenu = copy;
                copies.Add(copy);
            }
            var place = copies.Last();

            if (!string.IsNullOrEmpty(m.SubMenu))
            {
                var existing = place.controls.FirstOrDefault(c => c.type == VRCExpressionsMenu.Control.ControlType.SubMenu && c.name == m.SubMenu && c.subMenu != null);
                if (existing != null)
                {
                    existing.subMenu = CopyOf(existing.subMenu, $"{dir}/{copies.Count}_{Safe(m.SubMenu)}.asset", written);
                    place = existing.subMenu;
                }
                else if (place.controls.Count >= VRCExpressionsMenu.MAX_CONTROLS)
                    log.Add($"notice: menu: submenu {m.SubMenu} not added; {place.name} already has {place.controls.Count} controls (max {VRCExpressionsMenu.MAX_CONTROLS}); the FT toggles go into {place.name}");
                else
                {
                    var sub = CopyOf(null, $"{dir}/{copies.Count}_{Safe(m.SubMenu)}.asset", written);
                    place.controls.Add(SubMenuControl(m.SubMenu, sub));
                    log.Add($"menu: submenu {m.SubMenu} added to {place.name}");
                    place = sub;
                }
            }

            bool Room(string what)
            {
                if (place.controls.Count < VRCExpressionsMenu.MAX_CONTROLS) return true;
                log.Add($"notice: menu: {what} not added; {place.name} already has {place.controls.Count} controls (max {VRCExpressionsMenu.MAX_CONTROLS})");
                return false;
            }
            foreach (var (param, label, def) in new[] { (m.FaceTrackParameter, m.FaceTrackLabel, 1f), (m.AfkPlayParameter, m.AfkPlayLabel, 0f) })
            {
                if (param == m.AfkPlayParameter && !withAfk) { log.Add($"menu: toggle {label} ({param}) not added: AFK is not built"); continue; }
                if (HasToggle(param)) { budget.Take(param, VRCExpressionParameters.ValueType.Bool, def); log.Add($"menu: {param}: the avatar's menu already has it"); continue; }
                if (!Room($"toggle {label} ({param})")) continue;
                if (!budget.Take(param, VRCExpressionParameters.ValueType.Bool, def)) { log.Add($"notice: menu: toggle {label} ({param}) not added; its parameter does not fit in Expression Parameters"); continue; }
                place.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = label,
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = param },
                    value = 1,
                    subParameters = new VRCExpressionsMenu.Control.Parameter[0],
                    labels = new VRCExpressionsMenu.Control.Label[0],
                });
                log.Add($"menu: toggle {label} ({param}) added to {place.name}");
            }
            foreach (var sm in FtBuilder.WithMenu(m))
            {
                var sub = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(MenuInstaller.StateMenuPath(m, sm.Name));
                if (sub == null) continue;
                var menuParam = FtNames.MenuParam(sm.Name);
                if (HasSubMenu(sub) || HasAssetSubMenu(sm.Name)) { budget.Take(menuParam, VRCExpressionParameters.ValueType.Int, 0); log.Add($"menu: submenu {sm.Name}: the avatar's menu already has it"); continue; }
                if (!Room($"submenu {sm.Name}")) continue;
                if (!budget.Take(menuParam, VRCExpressionParameters.ValueType.Int, 0)) { log.Add($"notice: menu: submenu {sm.Name} not added; its parameter {menuParam} does not fit in Expression Parameters"); continue; }
                place.controls.Add(SubMenuControl(sm.Name, sub));
                log.Add($"menu: submenu {sm.Name} added to {place.name}");
            }
            foreach (var c in copies) EditorUtility.SetDirty(c);
            EditorUtility.SetDirty(place);
            AssetDatabase.SaveAssets();
            RemoveOthers(dir, written, log);
            return copies[0];
        }

        // The folder is the generator's own: a menu asset in it that this run did not write (a former subMenu, a former
        // path to the table's "menu", a removed state machine) is deleted, so generating again gives the same files.
        // Only menus the generator writes are removed: in Menu/, copies named "<n>_<name>"; in Menus/, state machine
        // menus whose controls are all toggles of FT/Menu/* parameters. Other menus (the user's own, when "outputFolder"
        // is not a folder for the generator only) are kept.
        public static void RemoveOthers(string dir, IEnumerable<string> keep, List<string> log)
        {
            var kept = new HashSet<string>(keep.Select(p => p.Replace('\\', '/')));
            if (!AssetDatabase.IsValidFolder(dir)) return;
            var stateMenus = System.IO.Path.GetFileName(dir) == FtNames.StateMenusFolder;
            foreach (var guid in AssetDatabase.FindAssets("t:VRCExpressionsMenu", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetDirectoryName(path).Replace('\\', '/') != dir || kept.Contains(path)) continue;
                var mine = stateMenus
                    ? AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(path)?.controls.All(c => c.type == VRCExpressionsMenu.Control.ControlType.Toggle && (c.parameter?.name ?? "").StartsWith("FT/Menu/")) == true
                    : System.Text.RegularExpressions.Regex.IsMatch(System.IO.Path.GetFileName(path), @"^\d+_.+\.asset$");
                if (!mine) { log.Add("menu: kept " + path + " (not a menu the generator writes)"); continue; }
                AssetDatabase.DeleteAsset(path);
                log.Add("menu: removed " + path + " (not written by this generation)");
            }
        }

        static VRCExpressionsMenu.Control SubMenuControl(string name, VRCExpressionsMenu sub) => new VRCExpressionsMenu.Control
        {
            name = name,
            type = VRCExpressionsMenu.Control.ControlType.SubMenu,
            subMenu = sub,
            parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
            subParameters = new VRCExpressionsMenu.Control.Parameter[0],
            labels = new VRCExpressionsMenu.Control.Label[0],
        };

        // The menu at path with the controls of src (none when src is null). The asset is reused, so it keeps its GUID.
        static VRCExpressionsMenu CopyOf(VRCExpressionsMenu src, string path, List<string> written)
        {
            var dest = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(path);
            if (dest == null)
            {
                dest = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                AssetDatabase.CreateAsset(dest, path);
            }
            if (src != null) EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(src), dest);
            else dest.controls = new List<VRCExpressionsMenu.Control>();
            dest.name = System.IO.Path.GetFileNameWithoutExtension(path);
            EditorUtility.SetDirty(dest);
            written.Add(path);
            return dest;
        }

        static string Safe(string name) => string.Concat((name ?? "Menu").Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_'));

        // The menus from root to target (both included), or null when target is not under root.
        static List<VRCExpressionsMenu> PathTo(VRCExpressionsMenu at, VRCExpressionsMenu target, HashSet<VRCExpressionsMenu> seen)
        {
            if (at == null || !seen.Add(at)) return null;
            if (at == target) return new List<VRCExpressionsMenu> { at };
            foreach (var c in at.controls.Where(c => c.type == VRCExpressionsMenu.Control.ControlType.SubMenu && c.subMenu != null))
            {
                var rest = PathTo(c.subMenu, target, seen);
                if (rest != null) { rest.Insert(0, at); return rest; }
            }
            return null;
        }

        internal static List<VRCExpressionsMenu> AllMenus(VRCExpressionsMenu at, HashSet<VRCExpressionsMenu> seen)
        {
            var r = new List<VRCExpressionsMenu>();
            if (at == null || !seen.Add(at)) return r;
            r.Add(at);
            foreach (var c in at.controls.Where(c => c.type == VRCExpressionsMenu.Control.ControlType.SubMenu && c.subMenu != null))
                r.AddRange(AllMenus(c.subMenu, seen));
            return r;
        }
    }
}
