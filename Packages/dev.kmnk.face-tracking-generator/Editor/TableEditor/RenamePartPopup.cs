using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // "Rename face movement": the new name, and for a part with a left/right partner whether both sides change.
    public sealed class RenamePartPopup : PopupWindowContent
    {
        readonly string from;
        readonly string partner;
        readonly Func<string, bool, PartRename.Problem> check;
        readonly Action<string, bool> rename;
        string name;
        bool together = true;
        bool focused;

        public RenamePartPopup(string from, string partner, Func<string, bool, PartRename.Problem> check, Action<string, bool> rename)
        {
            this.from = from;
            this.partner = partner;
            this.check = check;
            this.rename = rename;
            name = from;
        }

        internal void SetNameForCapture(string n) => name = n;

        public override Vector2 GetWindowSize() => new Vector2(300, partner != null ? 150 : 124);

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.LabelField("Rename face movement", EditorStyles.boldLabel);
            var labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 60;
            GUI.SetNextControlName("ftRenamePart");
            name = EditorGUILayout.TextField("Name", name);
            EditorGUIUtility.labelWidth = labelWidth;
            if (!focused) { EditorGUI.FocusTextInControl("ftRenamePart"); focused = true; }
            if (partner != null) together = EditorGUILayout.ToggleLeft("Rename left and right together (_L, _R)", together);
            var problem = check(name, together);
            var ja = EditorText.Current == EditorText.Language.Japanese;
            if (problem == PartRename.Problem.None && name != from)
            {
                var olds = partner != null && together ? new[] { from, partner } : new[] { from };
                var news = olds.Select(o => o == from ? name : name.Substring(0, name.Length - 2) + o.Substring(o.Length - 2));
                var note = EditorText.Help("part.renameNote");
                if (note != null) EditorGUILayout.LabelField(string.Join(ja ? "・" : ", ", olds) + " → " + string.Join(ja ? "・" : ", ", news) + note, EditorStyles.wordWrappedMiniLabel);
            }
            else if (problem != PartRename.Problem.None && name.Length > 0 && name != from)
            {
                var old = GUI.contentColor;
                GUI.contentColor = new Color(1f, 0.42f, 0.42f);
                var key = problem == PartRename.Problem.Form ? "newPart.form" : problem == PartRename.Problem.Used ? "newPart.used"
                    : problem == PartRename.Problem.NeedsSide ? "part.needsSide" : "part.pairWritten";
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
                        rename(name, partner != null && together);
                        editorWindow.Close();
                        GUIUtility.ExitGUI();
                    }
                if (GUILayout.Button("Cancel", GUILayout.Width(70))) editorWindow.Close();
            }
        }
    }
}
