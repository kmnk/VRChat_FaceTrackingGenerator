using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // The small window of "New face movement…": a name "region.movement", and for a left/right tree whether the
    // left and right get separate shapes (written as "<name>_{s}").
    public sealed class NewPartPopup : PopupWindowContent
    {
        readonly ICollection<string> allNames;
        readonly string pairVariable;   // "s" for the template's left/right trees, or null
        readonly Action<string> add;
        string name = "";
        bool separate = true;
        bool focused;

        public NewPartPopup(ICollection<string> allNames, string pairVariable, Action<string> add)
        {
            this.allNames = allNames;
            this.pairVariable = pairVariable;
            this.add = add;
        }

        public override Vector2 GetWindowSize() => new Vector2(300, pairVariable != null ? 150 : 128);

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.LabelField("New face movement", EditorStyles.boldLabel);
            GUI.SetNextControlName("ftNewPartName");
            name = EditorGUILayout.TextField("Name", name);
            if (!focused) { EditorGUI.FocusTextInControl("ftNewPartName"); focused = true; }
            var help = EditorText.Help("help.newPart");
            if (help != null) EditorGUILayout.LabelField(help, EditorStyles.wordWrappedMiniLabel);
            if (pairVariable != null) separate = EditorGUILayout.ToggleLeft("Separate shapes for left and right (_L, _R)", separate);
            var problem = Tuning.CheckName(name, separate, allNames);
            if (problem != Tuning.NameProblem.None && name.Length > 0)
            {
                var old = GUI.contentColor;
                GUI.contentColor = new Color(1f, 0.42f, 0.42f);
                EditorGUILayout.LabelField(EditorText.Get(problem == Tuning.NameProblem.Form ? "newPart.form" : "newPart.used"), EditorStyles.wordWrappedMiniLabel);
                GUI.contentColor = old;
            }
            var e = Event.current;
            var enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(problem != Tuning.NameProblem.None))
                    if (GUILayout.Button("Add", GUILayout.Width(70)) || (enter && problem == Tuning.NameProblem.None))
                    {
                        add(pairVariable != null && separate ? name + "_{" + pairVariable + "}" : name);
                        editorWindow.Close();
                        GUIUtility.ExitGUI();
                    }
                if (GUILayout.Button("Cancel", GUILayout.Width(70))) editorWindow.Close();
            }
        }
    }
}
