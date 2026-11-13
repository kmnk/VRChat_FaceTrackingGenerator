using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // The small window of "New face movement…": a name "region[.state].movement". A prefilled name (from the state
    // machine window, "<region>.<state>.") can be kept or cut down to "region.movement".
    public sealed class NewPartPopup : PopupWindowContent
    {
        readonly ICollection<string> allNames;
        readonly Action<string> add;
        string name = "";
        bool focused;
        int caretFrames;

        readonly string region;  // the region the name must start with (a row's part), or null

        public NewPartPopup(ICollection<string> allNames, Action<string> add, string prefix = "", string region = null)
        {
            this.allNames = allNames;
            this.add = add;
            this.region = region;
            name = prefix ?? "";
        }

        internal void SetNameForCapture(string n) => name = n;

        // Taller with the help text, which takes up to four lines in Japanese.
        public override Vector2 GetWindowSize() => new Vector2(300, EditorText.Help("help.newPart") != null ? 190 : 120);

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.LabelField("New face movement", EditorStyles.boldLabel);
            GUI.SetNextControlName("ftNewPartName");
            name = EditorGUILayout.TextField("Name", name);
            if (!focused) { EditorGUI.FocusTextInControl("ftNewPartName"); focused = true; }
            else if (caretFrames < 3 && Event.current.type == EventType.Repaint)
            {
                // Focusing selects the whole text; put the caret after a typed prefix ("eye.") so typing adds to it.
                // The editor fields' text editor is EditorGUI's internal activeEditor, set once the field is being edited,
                // so this is tried on the first few repaints.
                var field = typeof(EditorGUI).GetField("activeEditor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (field?.GetValue(null) is TextEditor te && te.text == name)
                {
                    te.MoveTextEnd();
                    if (CliArgs.Get("-ftAvatarNewPartIn") != null) Debug.Log($"[FTG] capture: new part text={te.text} cursor={te.cursorIndex} select={te.selectIndex}");
                }
                caretFrames++;
                editorWindow.Repaint();
            }
            var help = EditorText.Help("help.newPart");
            if (help != null) EditorGUILayout.LabelField(help, EditorStyles.wordWrappedMiniLabel);
            var problem = PartNames.Check(name, allNames);
            var wrongRegion = problem == PartNames.Problem.None && region != null && PartNames.Region(name) != region;
            if (wrongRegion)
            {
                var old = GUI.contentColor;
                GUI.contentColor = new Color(1f, 0.42f, 0.42f);
                EditorGUILayout.LabelField(string.Format(EditorText.Get("part.regionOf"), region), EditorStyles.wordWrappedMiniLabel);
                GUI.contentColor = old;
                problem = PartNames.Problem.Form;
            }
            else if (problem != PartNames.Problem.None && name.Length > 0)
            {
                var old = GUI.contentColor;
                GUI.contentColor = new Color(1f, 0.42f, 0.42f);
                EditorGUILayout.LabelField(EditorText.Get(problem == PartNames.Problem.Form ? "newPart.form" : "newPart.used"), EditorStyles.wordWrappedMiniLabel);
                GUI.contentColor = old;
            }
            var e = Event.current;
            var enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(problem != PartNames.Problem.None))
                    if (GUILayout.Button("Add", GUILayout.Width(70)) || (enter && problem == PartNames.Problem.None))
                    {
                        add(name);
                        editorWindow.Close();
                        GUIUtility.ExitGUI();
                    }
                if (GUILayout.Button("Cancel", GUILayout.Width(70))) editorWindow.Close();
            }
        }
    }
}
