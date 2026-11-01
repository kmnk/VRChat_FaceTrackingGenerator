using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Kmnk.FaceTrackingGenerator
{
    // Adds the FT toggle and the AFK flag to the avatar's Expression Parameters and to the menu the avatar
    // layer points to (spec R28, design.md "メニューの調整"). Changes the original assets, not copies.
    // Does nothing for a name that is already there. Does not add to a full menu (8 controls).
    public static class MenuInstaller
    {
        public static List<string> Install(TableModel m)
        {
            var log = new List<string>();
            var prefabPath = !string.IsNullOrEmpty(m.Prefab?.Guid) ? AssetDatabase.GUIDToAssetPath(m.Prefab.Guid) : m.Prefab?.Path;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) ?? throw new TableLoader.TableException("prefab not found: " + prefabPath);
            var expr = prefab.GetComponent<VRCAvatarDescriptor>()?.expressionParameters ?? throw new TableLoader.TableException("the avatar has no Expression Parameters");
            var menuPath = !string.IsNullOrEmpty(m.Menu?.Guid) ? AssetDatabase.GUIDToAssetPath(m.Menu.Guid) : m.Menu?.Path;
            var menu = string.IsNullOrEmpty(menuPath) ? null : AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(menuPath);
            if (menu == null) throw new TableLoader.TableException("menu not found: " + (menuPath ?? "(not set in the avatar layer)"));
            if (m.SubMenu != null)
            {
                menu = SubMenu(m, menu, log);
                if (menu == null) { AssetDatabase.SaveAssets(); return log; }
            }

            foreach (var (param, def, label) in new[] { (m.FaceTrackParameter, 1f, m.FaceTrackLabel), (m.AfkPlayParameter, 0f, m.AfkPlayLabel) })
            {
                var hasParam = expr.parameters.Any(p => p.name == param);
                var hasControl = menu.controls.Any(c => c.parameter?.name == param);
                if (hasParam && hasControl) { log.Add($"{param}: already there"); continue; }
                if (!hasControl && menu.controls.Count >= VRCExpressionsMenu.MAX_CONTROLS)
                {
                    log.Add($"{param}: not added; menu {menu.name} already has {menu.controls.Count} controls (max {VRCExpressionsMenu.MAX_CONTROLS})");
                    continue;
                }
                if (!hasParam)
                {
                    var cost = expr.CalcTotalCost() + VRCExpressionParameters.TypeCost(VRCExpressionParameters.ValueType.Bool);
                    if (cost > VRCExpressionParameters.MAX_PARAMETER_COST)
                    {
                        log.Add($"{param}: not added; synced parameters would use {cost} bits (max {VRCExpressionParameters.MAX_PARAMETER_COST})");
                        continue;
                    }
                    var list = expr.parameters.ToList();
                    list.Add(new VRCExpressionParameters.Parameter { name = param, valueType = VRCExpressionParameters.ValueType.Bool, defaultValue = def, saved = false, networkSynced = true });
                    expr.parameters = list.ToArray();
                    EditorUtility.SetDirty(expr);
                    log.Add($"{param}: added to {expr.name}");
                }
                if (!hasControl)
                {
                    menu.controls.Add(new VRCExpressionsMenu.Control
                    {
                        name = label,
                        type = VRCExpressionsMenu.Control.ControlType.Toggle,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = param },
                        value = 1,
                        subParameters = new VRCExpressionsMenu.Control.Parameter[0],
                        labels = new VRCExpressionsMenu.Control.Label[0],
                    });
                    EditorUtility.SetDirty(menu);
                    log.Add($"{param}: toggle '{label}' added to {menu.name}");
                }
            }
            AssetDatabase.SaveAssets();
            return log;
        }

        // Finds the submenu control named m.SubMenu in the root menu, or adds one with a new menu asset in the
        // output folder. Returns null when the root menu is full.
        static VRCExpressionsMenu SubMenu(TableModel m, VRCExpressionsMenu root, List<string> log)
        {
            var existing = root.controls.FirstOrDefault(c => c.type == VRCExpressionsMenu.Control.ControlType.SubMenu && c.name == m.SubMenu && c.subMenu != null);
            if (existing != null) return existing.subMenu;
            if (root.controls.Count >= VRCExpressionsMenu.MAX_CONTROLS)
            {
                log.Add($"submenu {m.SubMenu}: not added; menu {root.name} already has {root.controls.Count} controls (max {VRCExpressionsMenu.MAX_CONTROLS})");
                return null;
            }
            System.IO.Directory.CreateDirectory(m.OutputFolder);
            var path = $"{m.OutputFolder}/Menu_{m.SubMenu}.asset";
            var sub = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(path);
            if (sub == null)
            {
                sub = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                AssetDatabase.CreateAsset(sub, path);
            }
            root.controls.Add(new VRCExpressionsMenu.Control
            {
                name = m.SubMenu,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = sub,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
                subParameters = new VRCExpressionsMenu.Control.Parameter[0],
                labels = new VRCExpressionsMenu.Control.Label[0],
            });
            EditorUtility.SetDirty(root);
            log.Add($"submenu {m.SubMenu}: added to {root.name} ({path})");
            return sub;
        }
    }
}
