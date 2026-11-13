using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // Tools/Kmnk/Face Tracking State Machine: the expression layer's state machines as a graph (states, transitions on
    // the inputs, the menu), and on the right the settings of the selected state or transition (spec "試作: State
    // Machine の窓"). It edits the settings editor's open tables (TableEditorWindow.Current) through StateMachineEdit;
    // Save and Undo are the settings editor's.
    public sealed class StateMachineWindow : EditorWindow
    {
        const float NodeW = 150, NodeH = 36, ToolbarH = 22;
        static readonly Color Background = new Color(0.125f, 0.125f, 0.125f);
        static readonly Color Grid = new Color(0.15f, 0.15f, 0.15f);
        static readonly Color DefaultFill = new Color(0.72f, 0.41f, 0.16f);
        static readonly Color StateFill = new Color(0.29f, 0.29f, 0.29f);
        static readonly Color MenuFill = new Color(0.12f, 0.42f, 0.42f);
        static readonly Color MenuLine = new Color(0.25f, 0.65f, 0.65f);
        static readonly Color Line = new Color(0.82f, 0.82f, 0.82f);
        static readonly Color Selected = new Color(0.23f, 0.47f, 0.73f);

        string machine;          // the state machine shown
        string selState;         // the selected state, or null
        int selTransition = -1;  // the selected transition's index, or -1
        string linkFrom;         // "Make Transition": the state the new transition starts from
        string dragState;
        Vector2 dragStart, dragOffset;
        Vector2 pan = new Vector2(220, 260);  // where graph space (0, 0) is in the graph area
        // The inspector's width: the line between the graph and the inspector can be dragged. Kept for the user.
        const string InspectorWidthKey = "Kmnk.FaceTrackingGenerator.StateMachine.InspectorWidth";
        float inspectorWidth = 360;
        bool resizing;
        float InspectorWidth => Mathf.Clamp(inspectorWidth, 260, Math.Max(260, position.width - 200));
        float zoom = 1f;                      // graph space to the screen; the mouse wheel changes it
        const float MinZoom = 0.3f, MaxZoom = 2f;
        Vector2 inspectorScroll;
        readonly HashSet<string> openRegions = new HashSet<string>(StringComparer.Ordinal);

        [MenuItem("Tools/Kmnk/Face Tracking State Machine")]
        public static StateMachineWindow Open()
        {
            var w = GetWindow<StateMachineWindow>("FT State Machine");
            w.minSize = new Vector2(700, 400);
            return w;
        }

        static TableEditorWindow Editor => TableEditorWindow.Current;
        static JObject Expr => Editor?.ExpressionJson;

        void OnEnable()
        {
            inspectorWidth = EditorPrefs.GetFloat(InspectorWidthKey, 360);
            TableEditorWindow.Changed += Repaint;
            EditorText.Changed += Repaint;
        }

        void OnDisable()
        {
            TableEditorWindow.Changed -= Repaint;
            EditorText.Changed -= Repaint;
        }

        void OnGUI()
        {
            if (BuildGuard.Building) { BuildGuard.DrawPaused(); return; }
            var e = Expr;
            if (e == null) { EditorGUILayout.HelpBox(EditorText.Get("sm.noTable"), MessageType.Info); return; }
            var names = StateMachineEdit.Machines(e)?.Properties().Select(p => p.Name).ToList() ?? new List<string>();
            if (machine == null || !names.Contains(machine)) { machine = names.FirstOrDefault(); selState = null; selTransition = -1; }
            DrawToolbar(e, names);
            if (machine == null)
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("+ State Machine", GUILayout.Width(160))) Edit("Add FT state machine", x => StateMachineEdit.AddStateMachine(x, "Emotion"));
                    GUILayout.FlexibleSpace();
                }
                GUILayout.FlexibleSpace();
                return;
            }
            var top = ToolbarH + (EditorText.Help("sm.help") != null ? 18 : 0);
            var graph = new Rect(0, top, Math.Max(200, position.width - InspectorWidth), position.height - top);
            var inspector = new Rect(graph.xMax, top, position.width - graph.xMax, position.height - top);
            Splitter(new Rect(graph.xMax - 3, top, 6, graph.height));  // before the graph, which takes clicks in its area
            DrawGraph(e, graph);
            EditorGUI.DrawRect(new Rect(inspector.x, inspector.y, 1, inspector.height), new Color(0.1f, 0.1f, 0.1f));
            GUILayout.BeginArea(new Rect(inspector.x + 6, inspector.y + 4, inspector.width - 10, inspector.height - 8));
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            if (selState != null && StateMachineEdit.StateNames(e, machine).Contains(selState)) DrawState(e, selState);
            else if (selTransition >= 0 && selTransition < (StateMachineEdit.Transitions(e, machine)?.Count ?? 0)) DrawTransition(e, selTransition);
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void Splitter(Rect r)
        {
            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeHorizontal);
            var ev = Event.current;
            switch (ev.type)
            {
                case EventType.MouseDown when ev.button == 0 && r.Contains(ev.mousePosition):
                    resizing = true; ev.Use();
                    break;
                case EventType.MouseDrag when resizing:
                    inspectorWidth = Mathf.Clamp(position.width - ev.mousePosition.x, 260, Math.Max(260, position.width - 200));
                    ev.Use(); Repaint();
                    break;
                case EventType.MouseUp when resizing:
                    resizing = false;
                    EditorPrefs.SetFloat(InspectorWidthKey, inspectorWidth);
                    ev.Use();
                    break;
            }
        }

        void Edit(string undo, Action<JObject> edit) => Editor?.EditExpression(undo, x => { edit(x); return 0; });

        // ---- toolbar

        void DrawToolbar(JObject e, List<string> names)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (names.Count > 0)
                {
                    // The state machines, and adding, renaming and deleting one.
                    if (EditorGUILayout.DropdownButton(new GUIContent(machine ?? ""), FocusType.Passive, EditorStyles.toolbarDropDown, GUILayout.Width(140)))
                        MachineMenu(e, names, GUILayoutUtility.GetLastRect()).DropDown(GUILayoutUtility.GetLastRect());
                }
                using (new EditorGUI.DisabledScope(machine == null))
                    if (GUILayout.Button("+ State", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    {
                        var existing = StateMachineEdit.StateNames(e, machine);
                        var n = 1;
                        while (existing.Contains("state" + n)) n++;
                        var name = "state" + n;
                        var at = (new Vector2(position.width - InspectorWidth, position.height) / 2 - pan) / zoom;
                        Edit("Add FT state", x => StateMachineEdit.AddState(x, machine, name, at.x, at.y));
                        Select(name, -1);
                    }
                using (new EditorGUI.DisabledScope(machine == null))
                    if (GUILayout.Button("Frame All", EditorStyles.toolbarButton, GUILayout.Width(70))) FrameAll(e);
                GUILayout.FlexibleSpace();
            }
            var help = EditorText.Help("sm.help");
            if (help != null) EditorGUILayout.LabelField(linkFrom != null ? EditorText.Get("sm.pickTarget") : help, EditorStyles.miniLabel);
        }

        GenericMenu MachineMenu(JObject e, List<string> names, Rect at)
        {
            var menu = new GenericMenu();
            foreach (var n in names)
            {
                var name = n;
                menu.AddItem(new GUIContent(name), name == machine, () => { machine = name; selState = null; selTransition = -1; FrameAll(Expr); });
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("New State Machine…"), false, () =>
            {
                var k = 1;
                while (StateMachineEdit.MachineNameUsed(Expr, "StateMachine" + k)) k++;
                PopupWindow.Show(at, new MachineNamePopup("New state machine", "StateMachine" + k, n => MachineNameProblem(n, null), n =>
                {
                    Edit("Add FT state machine", x => StateMachineEdit.AddStateMachine(x, n));
                    machine = n; selState = null; selTransition = -1;
                    FrameAll(Expr);
                }));
            });
            var current = machine;
            menu.AddItem(new GUIContent($"Rename \"{current}\"…"), false, () =>
                PopupWindow.Show(at, new MachineNamePopup("Rename state machine", current, n => n == current ? EditorText.Get("sm.sameName") : MachineNameProblem(n, current), n =>
                {
                    Edit("Rename FT state machine", x => StateMachineEdit.RenameStateMachine(x, current, n));
                    machine = n;
                })));
            menu.AddItem(new GUIContent($"Delete \"{current}\"…"), false, () =>
            {
                if (!EditorUtility.DisplayDialog("FT State Machine", string.Format(EditorText.Get("sm.removeMachine"), current), "Delete", "Cancel")) return;
                Edit("Remove FT state machine", x => StateMachineEdit.RemoveStateMachine(x, current));
                machine = null; selState = null; selTransition = -1;
            });
            return menu;
        }

        // Why a state machine name cannot be used, or null.
        static string MachineNameProblem(string name, string except)
        {
            if (!StateMachineEdit.IsMachineName(name)) return EditorText.Get("sm.machineForm");
            if (StateMachineEdit.MachineNameUsed(Expr, name, except)) return EditorText.Get("sm.machineUsed");
            return null;
        }

        void Select(string state, int transition)
        {
            selState = state;
            selTransition = transition;
            if (state != null) Editor?.ShowState(machine, state);
            Repaint();
        }

        // ---- graph

        // Node positions in graph space: written ones, and a column on the right for states without a position.
        Dictionary<string, Vector2> Positions(JObject e)
        {
            var names = StateMachineEdit.StateNames(e, machine);
            var r = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            var y = -100f;
            foreach (var n in names)
            {
                var p = StateMachineEdit.Position(e, machine, n);
                if (p != null) r[n] = new Vector2(p.Value.x, p.Value.y);
                else if (n == names[0]) r[n] = Vector2.zero;
                else { r[n] = new Vector2(320, y); y += 90; }
            }
            if (dragState != null && r.ContainsKey(dragState)) r[dragState] += dragOffset;
            return r;
        }

        Rect NodeRect(Vector2 p, Rect graph) =>
            new Rect(graph.x + pan.x + (p.x - NodeW / 2) * zoom, graph.y + pan.y + (p.y - NodeH / 2) * zoom, NodeW * zoom, NodeH * zoom);

        Vector2 MenuPos(Dictionary<string, Vector2> pos) =>
            new Vector2((pos.Count > 0 ? pos.Values.Min(v => v.x) : 0) - 260, pos.Count > 0 ? pos.Values.Average(v => v.y) : 0);

        void FrameAll(JObject e)
        {
            var pos = Positions(e);
            if (pos.Count == 0) return;
            var all = pos.Values.Append(MenuPos(pos)).ToList();
            // The nodes' halves and a little room (above and below also for the menu lines bending around).
            var room = new Vector2(NodeW / 2, NodeH / 2 + 40);
            var min = new Vector2(all.Min(v => v.x), all.Min(v => v.y)) - room;
            var max = new Vector2(all.Max(v => v.x), all.Max(v => v.y)) + room;
            var size = new Vector2(position.width - InspectorWidth, position.height - ToolbarH - 18);
            // Fit everything, but not larger than 1 (a small machine stays at its size). When it has to be made smaller,
            // leave a margin so that the outer nodes do not touch the edges.
            var fit = Math.Min(size.x / (max.x - min.x), size.y / (max.y - min.y));
            if (fit < 1) fit = Math.Min((size.x - 48) / (max.x - min.x), (size.y - 48) / (max.y - min.y));
            zoom = Mathf.Clamp(fit, MinZoom, 1f);
            pan = size / 2 - (min + max) / 2 * zoom;
            Repaint();
        }

        void DrawGraph(JObject e, Rect graph)
        {
            EditorGUI.DrawRect(graph, Background);
            GUI.BeginClip(graph);
            var local = new Rect(0, 0, graph.width, graph.height);
            var cell = 20 * zoom;
            for (var x = Mathf.Repeat(pan.x, cell); x < local.width; x += cell) EditorGUI.DrawRect(new Rect(x, 0, 1, local.height), Grid);
            for (var y = Mathf.Repeat(pan.y, cell); y < local.height; y += cell) EditorGUI.DrawRect(new Rect(0, y, local.width, 1), Grid);

            var states = StateMachineEdit.StateNames(e, machine);
            var pos = Positions(e);
            var rects = states.ToDictionary(n => n, n => NodeRect(pos[n], local), StringComparer.Ordinal);
            var menuStates = states.Where(n => (StateMachineEdit.States(e, machine)[n] as JObject)?["menu"]?.Value<bool>() == true).ToList();
            var menuRect = NodeRect(MenuPos(pos), local);

            // transitions: a pair in both directions is drawn as two lines side by side; more transitions in the same
            // direction (either of two conditions, such as the left or the right side) go further to the side.
            var trs = StateMachineEdit.Transitions(e, machine)?.OfType<JObject>().ToList() ?? new List<JObject>();
            var segments = new List<(Vector2 a, Vector2 b, int index)>();
            var sameWay = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < trs.Count; i++)
            {
                var f = (string)trs[i]["from"]; var t = (string)trs[i]["to"];
                if (!rects.ContainsKey(f) || !rects.ContainsKey(t) || f == t) continue;
                var a = rects[f].center; var b = rects[t].center;
                var n = (b - a).normalized;
                var k = sameWay.TryGetValue(f + "\n" + t, out var c0) ? c0 : 0;
                sameWay[f + "\n" + t] = k + 1;
                var side = new Vector2(-n.y, n.x) * (5 + 8 * k) * zoom;
                segments.Add((a + side, b + side, i));
            }
            Handles.BeginGUI();
            // Menu lines bend around the states between (above for states above the menu, below for those under it).
            var centerY = rects.Count > 0 ? rects.Values.Average(r => r.center.y) : menuRect.center.y;
            foreach (var st in menuStates)
            {
                var a = menuRect.center; var b = rects[st].center;
                var bend = b.y < centerY - 1 ? -1f : b.y > centerY + 1 ? 1f : -1f;
                var c = (a + b) / 2 + new Vector2(0, bend * (90 * zoom + Math.Abs(b.y - a.y) * 0.5f));
                var pts = new List<Vector3>();
                const int n = 40;
                for (int i = 0; i <= n; i++)
                {
                    var t = i / (float)n;
                    var p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * c + t * t * b;
                    pts.Add(p);
                }
                Handles.color = MenuLine;
                for (int i = 0; i + 1 < pts.Count; i += 2) Handles.DrawAAPolyLine(1.5f, pts[i], pts[i + 1]);
                var k = n / 2;
                Arrow(pts[k], ((Vector2)(pts[k + 1] - pts[k])).normalized, MenuLine);
            }
            foreach (var (a, b, index) in segments)
            {
                var c = index == selTransition ? Selected : Line;
                Handles.color = c;
                Handles.DrawAAPolyLine(index == selTransition ? 3.5f : 2f, a, b);
                Arrow((a + b) / 2, (b - a).normalized, c);
            }
            if (linkFrom != null && rects.ContainsKey(linkFrom))
            {
                Handles.color = Line;
                Handles.DrawAAPolyLine(2f, rects[linkFrom].center, Event.current.mousePosition);
            }
            Handles.EndGUI();

            if (menuStates.Count > 0) Node(menuRect, "Menu", MenuFill, false, zoom);
            for (int i = 0; i < states.Count; i++) Node(rects[states[i]], states[i], i == 0 ? DefaultFill : StateFill, states[i] == selState, zoom);

            HandleGraphInput(e, local, rects, segments);
            GUI.EndClip();
        }

        void Arrow(Vector2 at, Vector2 dir, Color c)
        {
            dir *= Mathf.Clamp(zoom, 0.5f, 1.5f);
            var side = new Vector2(-dir.y, dir.x);
            Handles.color = c;
            Handles.DrawAAConvexPolygon(at + dir * 6, at - dir * 4 + side * 5, at - dir * 4 - side * 5);
        }

        static GUIStyle nodeLabel;

        static void Node(Rect r, string label, Color fill, bool selected, float zoom)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (selected) EditorGUI.DrawRect(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), Selected);
            EditorGUI.DrawRect(r, fill);
            if (nodeLabel == null) nodeLabel = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            nodeLabel.fontSize = Math.Max(6, Mathf.RoundToInt(EditorStyles.label.fontSize * zoom));
            GUI.Label(r, label, nodeLabel);
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Math.Max(1e-4f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        void HandleGraphInput(JObject e, Rect local, Dictionary<string, Rect> rects, List<(Vector2 a, Vector2 b, int index)> segments)
        {
            var ev = Event.current;
            var hit = rects.FirstOrDefault(kv => kv.Value.Contains(ev.mousePosition)).Key;
            switch (ev.type)
            {
                case EventType.MouseDown when ev.button == 0 && local.Contains(ev.mousePosition):
                    if (linkFrom != null)
                    {
                        if (hit != null && hit != linkFrom) MakeTransition(e, linkFrom, hit);
                        linkFrom = null;
                    }
                    else if (hit != null)
                    {
                        Select(hit, -1);
                        dragState = hit; dragStart = ev.mousePosition; dragOffset = Vector2.zero;
                    }
                    else
                    {
                        var seg = segments.Where(s => DistanceToSegment(ev.mousePosition, s.a, s.b) < 6).Select(s => s.index).DefaultIfEmpty(-1).First();
                        Select(null, seg);
                    }
                    ev.Use();
                    break;
                case EventType.MouseDrag when dragState != null:
                    dragOffset = (ev.mousePosition - dragStart) / zoom;
                    ev.Use();
                    Repaint();
                    break;
                case EventType.MouseDrag when ev.button == 2 || ev.button == 0 && ev.alt:
                    pan += ev.delta;
                    ev.Use();
                    Repaint();
                    break;
                case EventType.ScrollWheel when local.Contains(ev.mousePosition):
                    ZoomAt(ev.mousePosition, zoom * Mathf.Exp(-ev.delta.y * 0.03f));
                    ev.Use();
                    break;
                case EventType.MouseUp when dragState != null:
                    if (dragOffset.sqrMagnitude > 1)
                    {
                        var p = StateMachineEdit.Position(e, machine, dragState) is (float, float) q ? new Vector2(q.x, q.y) : Positions(e)[dragState] - dragOffset;
                        var to = p + dragOffset; var st = dragState;
                        Edit("Move FT state", x => StateMachineEdit.SetPosition(x, machine, st, to.x, to.y));
                    }
                    dragState = null; dragOffset = Vector2.zero;
                    ev.Use();
                    break;
                case EventType.ContextClick when hit != null:
                {
                    var st = hit;
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Make Transition"), false, () => { linkFrom = st; Repaint(); });
                    menu.AddItem(new GUIContent("Set as Default"), false, () => Edit("Set FT default state", x => StateMachineEdit.SetDefault(x, machine, st)));
                    menu.AddItem(new GUIContent("Delete State"), false, () => DeleteState(st));
                    menu.ShowAsContext();
                    ev.Use();
                    break;
                }
                case EventType.KeyDown when ev.keyCode == KeyCode.Escape && linkFrom != null:
                    linkFrom = null; ev.Use(); Repaint();
                    break;
                case EventType.KeyDown when ev.keyCode == KeyCode.Delete || ev.keyCode == KeyCode.Backspace && ev.command:
                    if (selState != null) DeleteState(selState);
                    else if (selTransition >= 0) { var i = selTransition; Edit("Remove FT transition", x => StateMachineEdit.RemoveTransition(x, machine, i)); selTransition = -1; }
                    ev.Use();
                    break;
                case EventType.KeyDown when ev.keyCode == KeyCode.F:
                    if (selState != null) { var size = new Vector2(position.width - InspectorWidth, position.height) / 2; pan = size - Positions(e)[selState] * zoom; Repaint(); }
                    else FrameAll(e);
                    ev.Use();
                    break;
            }
        }

        // Zoom keeping the graph point under 'at' (graph-area coordinates) in place.
        void ZoomAt(Vector2 at, float to)
        {
            to = Mathf.Clamp(to, MinZoom, MaxZoom);
            var g = (at - pan) / zoom;
            zoom = to;
            pan = at - g * zoom;
            Repaint();
        }

        void DeleteState(string st)
        {
            if (!EditorUtility.DisplayDialog("FT State Machine", string.Format(EditorText.Get("sm.removeState"), st), "Delete", "Cancel")) return;
            Edit("Remove FT state", x => StateMachineEdit.RemoveState(x, machine, st));
            if (selState == st) selState = null;
        }

        // The key a new condition starts with: one the transitions already compare, else one the rows use.
        string DefaultKey()
        {
            var m = Editor?.Model;
            return m?.Inputs.FirstOrDefault(i => !i.IsTrack)?.Key ?? m?.Inputs.FirstOrDefault(i => i.IsTrack)?.Key ?? NativeKeys.Names.First();
        }

        // The tracking keys for the condition popup, grouped by their first word ("Mouth/MouthCornerPullLeft").
        static string[] keyNames, keyLabels;
        static void LoadKeys()
        {
            if (keyNames != null) return;
            keyNames = NativeKeys.Names.ToArray();
            keyLabels = keyNames.Select(k => System.Text.RegularExpressions.Regex.Match(k, "^[A-Z][a-z]+").Value + "/" + k).ToArray();
        }

        void MakeTransition(JObject e, string from, string to)
        {
            var input = DefaultKey();
            var index = Editor.EditExpression("Add FT transition", x => StateMachineEdit.AddTransition(x, machine, from, to, input));
            Select(null, index);
        }

        // ---- inspector: a state

        static GUIStyle boxStyle;
        static GUIStyle Box => boxStyle ?? (boxStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(8, 6, 4, 6) });

        // The parts of a region in the tables (for the Movement lists).
        List<string> PartsOf(string region) =>
            Editor.AllPartNames().Where(p => p.Contains('.') && PartNames.Region(p) == region).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();

        void DrawState(JObject e, string st)
        {
            var names = StateMachineEdit.StateNames(e, machine);
            var isDefault = names[0] == st;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("State", EditorStyles.boldLabel, GUILayout.Width(60));
                var n = EditorGUILayout.DelayedTextField(st);
                if (n != st && !string.IsNullOrEmpty(n) && !names.Contains(n))
                {
                    Edit("Rename FT state", x => StateMachineEdit.RenameState(x, machine, st, n));
                    selState = n;
                    GUIUtility.ExitGUI();
                }
                if (isDefault) GUILayout.Label("(default)", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
            }
            if (!isDefault)
            {
                var menu = (StateMachineEdit.States(e, machine)[st] as JObject)?["menu"]?.Value<bool>() == true;
                EditorGUI.BeginChangeCheck();
                menu = EditorGUILayout.ToggleLeft("Selectable from menu", menu);
                if (EditorGUI.EndChangeCheck()) Edit("Set FT menu state", x => StateMachineEdit.SetMenu(x, machine, st, menu));
            }
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Regions", EditorStyles.boldLabel);
            var neutralRegions = StateMachineEdit.NeutralRegions(e);
            if (isDefault) DrawNeutral(e, neutralRegions);
            else DrawRegions(e, st, neutralRegions);
            EditorGUILayout.Space(6);
            DrawFixed(e, st);
        }

        void DrawRegions(JObject e, string st, List<string> neutralRegions)
        {
            var help = EditorText.Help("sm.regions");
            if (help != null) EditorGUILayout.LabelField(help, EditorStyles.wordWrappedMiniLabel);
            var written = ((StateMachineEdit.States(e, machine)[st] as JObject)?["regions"] as JObject)?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>();
            var regions = neutralRegions.Concat(written).Distinct().ToList();
            var modes = new[] { StateMachineEdit.Neutral, StateMachineEdit.Override, StateMachineEdit.Off };
            var labels = new[] { "Neutral", "Override", "Off" };
            foreach (var region in regions)
            {
                var mode = StateMachineEdit.RegionMode(e, machine, st, region);
                EditorGUI.BeginChangeCheck();
                var m = EditorGUILayout.Popup(region, Array.IndexOf(modes, mode), labels);
                if (EditorGUI.EndChangeCheck() && modes[m] != mode) { var r = region; Edit("Set FT region", x => StateMachineEdit.SetRegion(x, machine, st, r, modes[m])); }
                if (mode == StateMachineEdit.Override) DrawOverride(e, st, region);
            }
        }

        void DrawOverride(JObject e, string st, string region)
        {
            using (new EditorGUILayout.VerticalScope(Box))
            {
                var others = StateMachineEdit.StateNames(e, machine).Where(x => x != st && StateMachineEdit.RegionMode(e, machine, x, region) == StateMachineEdit.Override).ToList();
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Copy from", GUILayout.Width(80));
                    var key = "copy/" + region;
                    copyFrom.TryGetValue(key, out var ci);
                    using (new EditorGUI.DisabledScope(others.Count == 0))
                    {
                        ci = EditorGUILayout.Popup(Math.Min(ci, Math.Max(0, others.Count - 1)), others.ToArray());
                        copyFrom[key] = ci;
                        if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(50)) && others.Count > 0)
                        { var from = others[ci]; Edit("Copy FT region", x => StateMachineEdit.CopyRegion(x, machine, st, region, from)); }
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Key", EditorStyles.miniLabel, GUILayout.Width(120));
                    GUILayout.Label("Movement", EditorStyles.miniLabel);
                    GUILayout.Label("Strength", EditorStyles.miniLabel, GUILayout.Width(52));
                    GUILayout.Space(22);
                }
                var rows = StateMachineEdit.OverrideRows(e, machine, st, region);
                var parts = PartsOf(region);
                foreach (var (key, part, strength) in rows)
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(key, GUILayout.Width(120));
                        var options = new List<string> { "Don't move" };
                        options.AddRange(parts);
                        options.Add("New face movement…");
                        var index = part == null ? 0 : Math.Max(0, options.IndexOf(part));
                        if (part != null && options.IndexOf(part) < 0) { options.Insert(1, part); index = 1; }
                        EditorGUI.BeginChangeCheck();
                        var ni = EditorGUILayout.Popup(index, options.ToArray());
                        var popupRect = GUILayoutUtility.GetLastRect();
                        if (EditorGUI.EndChangeCheck() && ni != index)
                        {
                            var k = key; var s0 = strength;
                            if (ni == options.Count - 1) NewPart(popupRect, $"{region}.{st}.", name => Edit("Add FT face movement", x => StateMachineEdit.SetRow(x, machine, st, region, k, name, s0)), region);
                            else { var np = ni == 0 ? null : options[ni]; Edit("Set FT row", x => StateMachineEdit.SetRow(x, machine, st, region, k, np, s0)); }
                        }
                        using (new EditorGUI.DisabledScope(part == null))
                        {
                            EditorGUI.BeginChangeCheck();
                            var ns = EditorGUILayout.DelayedFloatField(part == null ? 0 : strength, GUILayout.Width(52));
                            if (EditorGUI.EndChangeCheck() && part != null) { var k = key; var p = part; ns = Mathf.Clamp01(ns); Edit("Set FT strength", x => StateMachineEdit.SetRow(x, machine, st, region, k, p, ns)); }
                        }
                        if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20))) { var k = key; Edit("Remove FT row", x => StateMachineEdit.RemoveRow(x, machine, st, region, k)); GUIUtility.ExitGUI(); }
                    }
                var neutralKeys = StateMachineEdit.NeutralRows(e, region).Select(r => r.key).ToList();
                var rest = neutralKeys.Count(k => rows.All(r => r.key != k));
                EditorGUILayout.LabelField(string.Format(EditorText.Get("sm.others"), rest), EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("+ Add Row", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                {
                    var menu = new GenericMenu();
                    foreach (var nr in StateMachineEdit.NeutralRows(e, region).Where(r => rows.All(x => x.key != r.key)))
                    {
                        var r = nr;
                        menu.AddItem(new GUIContent(r.key), false, () => Edit("Add FT row", x => StateMachineEdit.SetRow(x, machine, st, region, r.key, r.part, r.strength)));
                    }
                    if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent("(every key is written)"));
                    menu.ShowAsContext();
                }
            }
        }

        readonly Dictionary<string, int> copyFrom = new Dictionary<string, int>();

        // region: the region a row's part must be in (null for a fixed movement, which can be in any region).
        void NewPart(Rect at, string prefix, Action<string> done, string region = null)
        {
            PopupWindow.Show(at, new NewPartPopup(Editor.AllPartNames(), done, prefix, region));
        }

        void DrawNeutral(JObject e, List<string> regions)
        {
            var help = EditorText.Help("sm.neutral");
            if (help != null) EditorGUILayout.LabelField(help, EditorStyles.wordWrappedMiniLabel);
            foreach (var region in regions)
            {
                var rows = StateMachineEdit.NeutralRows(e, region);
                bool open;
                using (new EditorGUILayout.HorizontalScope())
                {
                    open = EditorGUILayout.Foldout(openRegions.Contains(region), region, true, EditorStyles.foldoutHeader);
                    GUILayout.Label(rows.Count == 1 ? "1 key" : $"{rows.Count} keys", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                }
                var header = GUILayoutUtility.GetLastRect();
                if (open) openRegions.Add(region); else openRegions.Remove(region);
                if (Event.current.type == EventType.ContextClick && header.Contains(Event.current.mousePosition))
                {
                    var r = region;
                    var menu = new GenericMenu();
                    foreach (var k in NativeKeys.Names.Where(k => rows.All(x => x.key != k)))
                    {
                        var key = k;
                        menu.AddItem(new GUIContent("Add Key/" + key), false, () =>
                        {
                            var part = PartsOf(r).FirstOrDefault() ?? $"{r}.{char.ToLowerInvariant(key[0])}{key.Substring(1)}";
                            Edit("Add FT neutral key", x => StateMachineEdit.SetNeutralRow(x, r, key, part));
                        });
                    }
                    foreach (var row in rows) { var key = row.key; menu.AddItem(new GUIContent("Remove Key/" + key), false, () => Edit("Remove FT neutral key", x => StateMachineEdit.RemoveNeutralRow(x, r, key))); }
                    menu.ShowAsContext();
                    Event.current.Use();
                }
                if (!open) continue;
                using (new EditorGUILayout.VerticalScope(Box))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label("Key", EditorStyles.miniLabel, GUILayout.Width(120));
                        GUILayout.Label("Movement", EditorStyles.miniLabel);
                        GUILayout.Label("Strength", EditorStyles.miniLabel, GUILayout.Width(52));
                    }
                    var parts = PartsOf(region);
                    foreach (var (key, part, strength) in rows)
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label(key, GUILayout.Width(120));
                            var options = parts.Contains(part) ? parts.ToList() : parts.Prepend(part).ToList();
                            options.Add("New face movement…");
                            var index = options.IndexOf(part);
                            EditorGUI.BeginChangeCheck();
                            var ni = EditorGUILayout.Popup(index, options.ToArray());
                            var popupRect = GUILayoutUtility.GetLastRect();
                            if (EditorGUI.EndChangeCheck() && ni != index)
                            {
                                var k = key; var s0 = strength; var r = region;
                                if (ni == options.Count - 1) NewPart(popupRect, $"{region}.", name => Edit("Add FT face movement", x => StateMachineEdit.SetNeutralRow(x, r, k, name, s0)), region);
                                else { var np = options[ni]; Edit("Set FT neutral row", x => StateMachineEdit.SetNeutralRow(x, r, k, np, s0)); }
                            }
                            EditorGUI.BeginChangeCheck();
                            var ns = EditorGUILayout.DelayedFloatField(strength, GUILayout.Width(52));
                            if (EditorGUI.EndChangeCheck()) { var k = key; var p = part; var r = region; ns = Mathf.Clamp01(ns); Edit("Set FT strength", x => StateMachineEdit.SetNeutralRow(x, r, k, p, ns)); }
                        }
                }
            }
        }

        void DrawFixed(JObject e, string st)
        {
            EditorGUILayout.LabelField("Fixed blendshapes", EditorStyles.boldLabel);
            var fixedParts = ((StateMachineEdit.States(e, machine)[st] as JObject)?["fixed"] as JObject)?.Properties().ToList() ?? new List<JProperty>();
            if (fixedParts.Count == 0) EditorGUILayout.LabelField(EditorText.Get("sm.noFixed"), EditorStyles.miniLabel);
            foreach (var p in fixedParts)
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(p.Name, GUILayout.Width(130));
                    var v = p.Value.Value<float>();
                    EditorGUI.BeginChangeCheck();
                    var nv = EditorGUILayout.IntSlider((int)Math.Round(v), 0, 100);
                    if (EditorGUI.EndChangeCheck()) { var part = p.Name; Edit("Set FT fixed amount", x => StateMachineEdit.SetFixed(x, machine, st, part, nv)); }
                    if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20))) { var part = p.Name; Edit("Remove FT fixed amount", x => StateMachineEdit.RemoveFixed(x, machine, st, part)); GUIUtility.ExitGUI(); }
                }
            var addRect = GUILayoutUtility.GetRect(new GUIContent("+ Add Blendshape"), EditorStyles.miniButton, GUILayout.ExpandWidth(false));
            if (GUI.Button(addRect, "+ Add Blendshape", EditorStyles.miniButton))
            {
                var menu = new GenericMenu();
                foreach (var part in Editor.AllPartNames().Where(x => x.Contains('.') && fixedParts.All(f => f.Name != x)).Distinct().OrderBy(x => x, StringComparer.Ordinal))
                {
                    var pt = part;
                    menu.AddItem(new GUIContent(PartNames.Region(pt) + "/" + pt), false, () => Edit("Add FT fixed amount", x => StateMachineEdit.SetFixed(x, machine, st, pt, 100)));
                }
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("New face movement…"), false, () => NewPart(addRect, $"eye.{st}.", name => Edit("Add FT fixed amount", x => StateMachineEdit.SetFixed(x, machine, st, name, 100))));
                menu.DropDown(addRect);
            }
        }

        // ---- inspector: a transition

        void DrawTransition(JObject e, int index)
        {
            var t = (JObject)StateMachineEdit.Transitions(e, machine)[index];
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Transition", EditorStyles.boldLabel, GUILayout.Width(90));
                GUILayout.Label($"{(string)t["from"]} → {(string)t["to"]}");
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Duration", GUILayout.Width(90));
                EditorGUI.BeginChangeCheck();
                var d = EditorGUILayout.DelayedFloatField(t["duration"]?.Value<float>() ?? 0, GUILayout.Width(70));
                GUILayout.Label("s");
                if (EditorGUI.EndChangeCheck()) Edit("Set FT duration", x => StateMachineEdit.SetDuration(x, machine, index, d));
            }
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Conditions", EditorStyles.boldLabel);
            var help = EditorText.Help("sm.conditions");
            if (help != null) EditorGUILayout.LabelField(help, EditorStyles.wordWrappedMiniLabel);
            LoadKeys();
            var when = t["when"] as JArray ?? new JArray();
            for (int c = 0; c < when.Count; c++)
            {
                var cond = when[c] as JArray;
                if (cond == null || cond.Count != 3) continue;
                var input = (string)cond[0]; var op = (string)cond[1]; var value = cond[2].Value<float>();
                using (new EditorGUILayout.HorizontalScope())
                {
                    // A tracking key ("v2/" is shown without it); an unknown name stays first so that it is not lost.
                    var names = keyNames.ToList(); var labels = keyLabels.ToList();
                    var shown = NativeInputs.NameOf(input);
                    if (!names.Contains(shown)) { names.Insert(0, input); labels.Insert(0, input + " (not a key)"); shown = input; }
                    EditorGUI.BeginChangeCheck();
                    var ii = EditorGUILayout.Popup(Math.Max(0, names.IndexOf(shown)), labels.ToArray());
                    var oi = EditorGUILayout.Popup(op == "<" ? 1 : 0, new[] { ">", "<" }, GUILayout.Width(40));
                    var nv = EditorGUILayout.DelayedFloatField(value, GUILayout.Width(60));
                    if (EditorGUI.EndChangeCheck()) { var ci = c; var ni = names[ii]; var no = oi == 1 ? "<" : ">"; Edit("Set FT condition", x => StateMachineEdit.SetCondition(x, machine, index, ci, ni, no, nv)); }
                    using (new EditorGUI.DisabledScope(when.Count <= 1))
                        if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20))) { var ci = c; Edit("Remove FT condition", x => StateMachineEdit.RemoveCondition(x, machine, index, ci)); GUIUtility.ExitGUI(); }
                }
            }
            if (GUILayout.Button("+ Add Condition", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
            {
                var k = DefaultKey();
                Edit("Add FT condition", x => StateMachineEdit.AddCondition(x, machine, index, k));
            }
            EditorGUILayout.Space(8);
            if (GUILayout.Button("Remove Transition", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
            {
                Edit("Remove FT transition", x => StateMachineEdit.RemoveTransition(x, machine, index));
                selTransition = -1;
                GUIUtility.ExitGUI();
            }
        }

        // ---- TableEditorChecks.Capture

        // -ftSmMachine <name>, -ftSmEdits <JSON Lines of StateMachineEdit operations>, -ftSmSelect <state>|<from>-><to>,
        // -ftSmOpen <region>;... (neutral's folds).
        internal void ApplyCaptureArgs()
        {
            machine = CliArgs.Get("-ftSmMachine") ?? machine;
            var edits = CliArgs.Get("-ftSmEdits");
            if (edits != null)
                foreach (var line in System.IO.File.ReadAllLines(edits).Where(l => !string.IsNullOrWhiteSpace(l)))
                {
                    var op = JObject.Parse(line);
                    Edit("Capture", x => StateMachineEdit.Apply(x, op));
                }
            var e = Expr;
            if (e != null && machine == null) machine = StateMachineEdit.Machines(e)?.Properties().Select(p => p.Name).FirstOrDefault();
            foreach (var r in (CliArgs.Get("-ftSmOpen") ?? "").Split(';').Where(x => x != "")) openRegions.Add(r);
            var sel = CliArgs.Get("-ftSmSelect");
            if (sel != null && e != null && machine != null)
            {
                var arrow = sel.IndexOf("->", StringComparison.Ordinal);
                if (arrow < 0) Select(sel, -1);
                else
                {
                    var from = sel.Substring(0, arrow); var to = sel.Substring(arrow + 2);
                    var i = StateMachineEdit.Transitions(e, machine)?.OfType<JObject>().ToList().FindIndex(t => (string)t["from"] == from && (string)t["to"] == to) ?? -1;
                    Select(null, i);
                }
            }
            position = new Rect(80, 80, 1100, 700);
            // -ftSmInspector <width>: as dragging the line between the graph and the inspector (not kept).
            if (float.TryParse(CliArgs.Get("-ftSmInspector"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var iw)) inspectorWidth = iw;
            if (e != null && machine != null) FrameAll(e);
            // -ftSmZoom <factor>: the mouse wheel at the middle of the graph area.
            if (float.TryParse(CliArgs.Get("-ftSmZoom"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z))
                ZoomAt(new Vector2(position.width - InspectorWidth, position.height - ToolbarH - 18) / 2, zoom * z);
        }

        internal string CaptureState()
        {
            var e = Expr;
            if (e == null || machine == null) return "no state machine";
            var states = StateMachineEdit.StateNames(e, machine);
            var trs = StateMachineEdit.Transitions(e, machine)?.OfType<JObject>().Select(t => $"{t["from"]}->{t["to"]}") ?? Enumerable.Empty<string>();
            var menu = states.Where(n => (StateMachineEdit.States(e, machine)[n] as JObject)?["menu"]?.Value<bool>() == true);
            return $"machine={machine} states={string.Join(",", states)} transitions={string.Join(",", trs)} menu={string.Join(",", menu)} selected={(selState ?? (selTransition >= 0 ? "transition " + selTransition : "-"))} inspector={InspectorWidth:0} zoom={zoom.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}";
        }
    }
}
