using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // "Rename face movement": the new name.
    public sealed class RenamePartPopup : PopupWindowContent
    {
        readonly string from;
        readonly Func<string, bool, PartRename.Problem> check;
        readonly Action<string, bool> rename;
        string name;
        bool focused;

        public RenamePartPopup(string from, Func<string, bool, PartRename.Problem> check, Action<string, bool> rename)
        {
            this.from = from;
            this.check = check;
            this.rename = rename;
            name = from;
        }

        internal void SetNameForCapture(string n) => name = n;

        public override Vector2 GetWindowSize() => new Vector2(300, 138);

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.LabelField("Rename face movement", EditorStyles.boldLabel);
            var labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 60;
            GUI.SetNextControlName("ftRenamePart");
            name = EditorGUILayout.TextField("Name", name);
            EditorGUIUtility.labelWidth = labelWidth;
            if (!focused) { EditorGUI.FocusTextInControl("ftRenamePart"); focused = true; }
            var problem = check(name, false);
            var ja = EditorText.Current == EditorText.Language.Japanese;
            if (problem == PartRename.Problem.None && name != from)
            {
                var note = EditorText.Help("part.renameNote");
                if (note != null) EditorGUILayout.LabelField(from + " → " + name + note, EditorStyles.wordWrappedMiniLabel);
            }
            else if (problem != PartRename.Problem.None && name.Length > 0 && name != from)
            {
                var old = GUI.contentColor;
                GUI.contentColor = new Color(1f, 0.42f, 0.42f);
                var key = problem == PartRename.Problem.Form ? "newPart.form" : problem == PartRename.Problem.Region ? "part.region" : "newPart.used";
                EditorGUILayout.LabelField(EditorText.Get(key), EditorStyles.wordWrappedMiniLabel);
                GUI.contentColor = old;
            }
            var e = Event.current;
            var enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            var ok = problem == PartRename.Problem.None && name != from;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!ok))
                    if (GUILayout.Button("Rename", GUILayout.Width(70)) || (enter && ok))
                    {
                        rename(name, false);
                        editorWindow.Close();
                        GUIUtility.ExitGUI();
                    }
                if (GUILayout.Button("Cancel", GUILayout.Width(70))) editorWindow.Close();
            }
        }
    }
}
