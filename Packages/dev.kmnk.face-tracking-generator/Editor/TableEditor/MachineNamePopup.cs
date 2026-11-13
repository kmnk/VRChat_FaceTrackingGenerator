using System;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // The name of a new or renamed state machine (the State Machine window's dropdown).
    public sealed class MachineNamePopup : PopupWindowContent
    {
        readonly string title;
        readonly Func<string, string> problem;  // null when the name can be used, else the message
        readonly Action<string> done;
        string name;
        bool focused;
        int caretFrames;

        public MachineNamePopup(string title, string name, Func<string, string> problem, Action<string> done)
        {
            this.title = title;
            this.name = name;
            this.problem = problem;
            this.done = done;
        }

        public override Vector2 GetWindowSize() => new Vector2(300, 100);

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            GUI.SetNextControlName("ftMachineName");
            name = EditorGUILayout.TextField("Name", name);
            if (!focused) { EditorGUI.FocusTextInControl("ftMachineName"); focused = true; }
            else if (caretFrames < 3 && Event.current.type == EventType.Repaint)
            {
                // Focusing selects the whole text; put the caret at the end (as NewPartPopup does).
                var field = typeof(EditorGUI).GetField("activeEditor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (field?.GetValue(null) is TextEditor te && te.text == name) te.MoveTextEnd();
                caretFrames++;
                editorWindow.Repaint();
            }
            var why = problem(name);
            if (why != null && name.Length > 0)
            {
                var old = GUI.contentColor;
                GUI.contentColor = new Color(1f, 0.42f, 0.42f);
                EditorGUILayout.LabelField(why, EditorStyles.wordWrappedMiniLabel);
                GUI.contentColor = old;
            }
            var e = Event.current;
            var enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(why != null))
                    if (GUILayout.Button("OK", GUILayout.Width(70)) || (enter && why == null))
                    {
                        done(name);
                        editorWindow.Close();
                        GUIUtility.ExitGUI();
                    }
                if (GUILayout.Button("Cancel", GUILayout.Width(70))) editorWindow.Close();
            }
        }
    }
}
