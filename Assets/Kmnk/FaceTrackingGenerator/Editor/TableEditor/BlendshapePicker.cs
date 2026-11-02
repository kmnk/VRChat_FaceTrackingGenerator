using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // The list of blendshapes for a part: a search field, "(None)", then the mesh's blendshapes under headings made
    // from separator names (such as "=====Mouth=====" or "-----Face-----"). Up/Down move, Enter picks,
    // Escape closes.
    public sealed class BlendshapePicker : PopupWindowContent
    {
        const float RowHeight = 18;
        static readonly Regex Separator = new Regex(@"^\s*([-=]{3,})(.*?)([-=]{3,})\s*$");

        readonly List<string> names;
        readonly string current;
        readonly Action<string> pick;   // null = (None)
        readonly Action<string> hover;  // the name under the cursor (null for (None)), for Face View
        readonly Action closed;
        readonly SearchField searchField = new SearchField();
        string search;
        Vector2 scroll;
        int selected = -1;              // index into the visible items
        List<(string text, bool header)> items;

        public BlendshapePicker(IEnumerable<string> blendshapes, string current, Action<string> pick, string search = "", Action<string> hover = null, Action closed = null)
        {
            names = blendshapes.ToList();
            this.current = current;
            this.pick = pick;
            this.hover = hover;
            this.closed = closed;
            this.search = search ?? "";
            Refresh();
        }

        public static bool IsSeparator(string name) => Separator.IsMatch(name);

        // "-- Mouth --" for "=====Mouth=====" .
        public static string Heading(string name)
        {
            var m = Separator.Match(name);
            var t = m.Success ? m.Groups[2].Value.Trim() : name;
            return "-- " + (t.Length > 0 ? t : "") + " --";
        }

        // Names that contain every space-separated term (case-insensitive).
        public static bool Matches(string name, string search)
        {
            var terms = (search ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return terms.All(t => name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // (None), then headings and names; a heading is shown only when a name under it is left.
        public static List<(string text, bool header)> Items(IList<string> names, string search)
        {
            var list = new List<(string, bool)> { ("(None)", false) };
            string heading = null;
            foreach (var n in names)
            {
                if (IsSeparator(n)) { heading = Heading(n); continue; }
                if (!Matches(n, search)) continue;
                if (heading != null) { list.Add((heading, true)); heading = null; }
                list.Add((n, false));
            }
            return list;
        }

        void Refresh()
        {
            items = Items(names, search);
            selected = items.FindIndex(i => !i.header && i.text == current);
            if (selected < 0 || !string.IsNullOrEmpty(search)) selected = items.FindIndex(i => !i.header && i.text != "(None)");
            if (selected < 0) selected = 0;
        }

        public override Vector2 GetWindowSize() => new Vector2(320, 380);

        public override void OnOpen()
        {
            searchField.SetFocus();
            editorWindow.wantsMouseMove = true;
            Hover();
        }

        public override void OnClose() => closed?.Invoke();

        int hovered = -2;
        void Hover()
        {
            if (selected == hovered || hover == null) return;
            hovered = selected;
            if (selected >= 0 && selected < items.Count && !items[selected].header)
                hover(items[selected].text == "(None)" ? null : items[selected].text);
        }

        public override void OnGUI(Rect rect)
        {
            HandleKeys();
            var top = new Rect(rect.x + 4, rect.y + 4, rect.width - 8, 18);
            EditorGUI.BeginChangeCheck();
            search = searchField.OnGUI(top, search);
            if (EditorGUI.EndChangeCheck()) { Refresh(); scroll = Vector2.zero; Hover(); }

            var hint = new Rect(rect.x + 6, rect.yMax - 18, rect.width - 12, 16);
            EditorGUI.LabelField(hint, EditorText.Get("picker.keys"), EditorStyles.miniLabel);
            var listRect = new Rect(rect.x, top.yMax + 4, rect.width, rect.height - top.height - 8 - 18);
            var view = new Rect(0, 0, listRect.width - 16, items.Count * RowHeight);
            scroll = GUI.BeginScrollView(listRect, scroll, view);
            for (int i = 0; i < items.Count; i++)
            {
                var r = new Rect(0, i * RowHeight, view.width, RowHeight);
                if (r.yMax < scroll.y || r.y > scroll.y + listRect.height) continue;
                var (text, header) = items[i];
                if (header)
                {
                    EditorGUI.LabelField(new Rect(r.x + 6, r.y, r.width, r.height), text, EditorStyles.miniLabel);
                    continue;
                }
                if (i == selected) EditorGUI.DrawRect(r, new Color(0.17f, 0.36f, 0.53f));
                var isCurrent = text == current || (text == "(None)" && string.IsNullOrEmpty(current));
                EditorGUI.LabelField(new Rect(r.x + 18, r.y, r.width - 40, r.height), text);
                if (isCurrent) EditorGUI.LabelField(new Rect(r.xMax - 22, r.y, 20, r.height), "✓");
                if (Event.current.type == EventType.MouseMove && r.Contains(Event.current.mousePosition) && selected != i)
                {
                    selected = i;
                    Hover();
                    editorWindow.Repaint();
                }
                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                {
                    Choose(i);
                    Event.current.Use();
                }
            }
            GUI.EndScrollView();
        }

        void HandleKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode == KeyCode.DownArrow) { selected = Step(selected, 1); e.Use(); ScrollTo(); Hover(); }
            else if (e.keyCode == KeyCode.UpArrow) { selected = Step(selected, -1); e.Use(); ScrollTo(); Hover(); }
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Choose(selected); e.Use(); }
            else if (e.keyCode == KeyCode.Escape) { editorWindow.Close(); e.Use(); }
        }

        int Step(int from, int d)
        {
            for (int i = from + d; i >= 0 && i < items.Count; i += d)
                if (!items[i].header) return i;
            return from;
        }

        void ScrollTo()
        {
            var y = selected * RowHeight;
            if (y < scroll.y) scroll.y = y;
            if (y > scroll.y + 300) scroll.y = y - 300;
        }

        void Choose(int i)
        {
            if (i < 0 || i >= items.Count || items[i].header) return;
            pick(items[i].text == "(None)" ? null : items[i].text);
            editorWindow.Close();
        }
    }
}
