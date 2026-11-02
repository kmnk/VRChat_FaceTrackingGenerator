using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // Help texts of the table editor in English and Japanese. Labels stay in English (they match the table keys);
    // only the descriptions, notes, messages and tooltips change with the language.
    public static class EditorText
    {
        public enum Language { English, Japanese, Off }

        const string PrefKey = "Kmnk.FaceTrackingGenerator.HelpLanguage";
        static readonly string[] PrefValues = { "en", "ja", "off" };
        public static readonly string[] Choices = { "English", "日本語", "Off" };

        // Set by TableEditorChecks.Capture so that a check does not change the user's setting.
        internal static Language? Override;

        public static Language Current
        {
            get
            {
                if (Override.HasValue) return Override.Value;
                var v = EditorPrefs.GetString(PrefKey, "");
                var i = Array.IndexOf(PrefValues, v);
                if (i >= 0) return (Language)i;
                return EditorIsJapanese() ? Language.Japanese : Language.English;
            }
            set => EditorPrefs.SetString(PrefKey, PrefValues[(int)value]);
        }

        // Unity's editor language (internal LocalizationDatabase.currentEditorLanguage). English when unknown.
        static bool EditorIsJapanese()
        {
            try
            {
                var t = typeof(Editor).Assembly.GetType("UnityEditor.LocalizationDatabase");
                var p = t?.GetProperty("currentEditorLanguage", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                return p != null && (SystemLanguage)p.GetValue(null) == SystemLanguage.Japanese;
            }
            catch { return false; }
        }

        static readonly Dictionary<string, (string en, string ja)> Texts = new Dictionary<string, (string, string)>
        {
            ["help.avatar"] = ("Choose the avatar to add FT to, the menu for the FT toggle, and the face mesh to move.",
                "FT を付けるアバターと、FT のトグルを置くメニュー、動かす顔のメッシュを選びます。"),
            ["help.parts"] = ("For each face movement (closing an eye, opening the mouth, ...), choose the blendshape to use.\nCheck Missing if your avatar has no shape for it; that movement is then not used.",
                "顔の動き（目を閉じる、口を開けるなど）ごとに、使うシェイプキーを選びます。\nアバターに合う形が無い動きは Missing に印を付けると、使わなくなります。"),
            ["help.layers"] = ("Choose what happens to the avatar's own animations (hand gesture faces, blinking, ...) while FT is on.\nStop while FT: its face does not show while FT is on. Mute while FT: it is turned off while FT is on. Remove: it is removed.",
                "アバターに元からあるアニメーション（手のジェスチャーの表情、まばたきなど）を、FT の間どうするかを選びます。\nStop while FT: FT の間は表情を出さない。Mute while FT: FT の間は丸ごと止める。Remove: 消す。"),
            ["help.inputs"] = ("Values your face tracking device sends. Move a slider to see the face for that value on the avatar in the scene.",
                "フェイストラッキングの機器から届く値です。スライダーを動かすと、その値のときの顔をシーンのアバターで見られます。"),
            ["help.switches"] = ("Expressions that switch by value (such as smiling eyes). Auto switches by the slider values now. Pick a name to keep that one.",
                "値に応じて切り替わる表情（笑顔の目など）です。Auto は今のスライダーの値で切り替えます。名前を選ぶと、その表情のままにします。"),
            ["help.weights"] = ("Blendshapes moving now and how much. Over 100 is shown in red.",
                "いま動いているシェイプキーと、その量です。100 を超えるものは赤で出します。"),
            ["note.preview"] = ("Movements that mix two values (such as mouth shapes) are approximate and may differ slightly.\nSmoothing, the mouth while talking and the avatar's own animations are not included.",
                "2 つの値を組み合わせる動き（口の形など）は近似で、実際と少し違うことがあります。\nなめらかにする処理、話している間の口、アバターに元からあるアニメーションは含みません。"),
            ["msg.previewing"] = ("Previewing. The avatar is not changed; stopping restores it.",
                "プレビュー中です。アバターは書き換えません。止めると元に戻ります。"),
            ["msg.noTable"] = ("Choose avatar.jsonc.", "avatar.jsonc を選んでください。"),
            ["msg.chooseAvatar"] = ("Choose an avatar in the scene.", "シーンのアバターを選んでください。"),
            ["msg.animBusy"] = ("Close the Animation window preview first.", "Animation ウィンドウのプレビューを止めてください。"),
            ["msg.playMode"] = ("The preview does not run in Play Mode.", "Play Mode の間はプレビューできません。"),
            ["msg.noController"] = ("No controller", "controller がありません"),
            ["msg.noPrefab"] = ("The prefab is not found. Choose it above.", "prefab が見つかりません。上で選んでください。"),
            ["msg.notOnAvatar"] = ("(not on avatar)", "(not on avatar)"),
            ["dialog.discard"] = ("The tables have unsaved changes. Discard them?", "表に保存していない変更があります。破棄しますか？"),
            ["dialog.save"] = ("The tables have unsaved changes. Save them?", "表に保存していない変更があります。保存しますか？"),
            ["tip.table"] = ("The avatar table. It names the input and expression tables.", "アバターの表。入力の表と表情の表を指します。"),
            ["tip.prefab"] = ("The avatar prefab. Its Avatar Descriptor gives the controllers.", "アバターの prefab。controller は Avatar Descriptor から取ります。"),
            ["tip.menu"] = ("The menu that gets the FT toggle and the AFK flag.", "FT のトグルと AFK のフラグを置くメニュー。"),
            ["tip.mesh"] = ("The face mesh. Parts without their own mesh use it.", "顔のメッシュ。メッシュを指定していない部品はこれを使います。"),
            ["tip.missing"] = ("The avatar has no shape for this movement.", "アバターにこの動きに合う形がありません。"),
            ["tip.stopAutoEyes"] = ("Turn off VRChat's automatic blinking and eye movement while FT is on.", "FT がオンの間、VRChat の自動のまばたきと目線を止めます。"),
        };

        // The text in the current language, or null when the language is Off (help texts are not shown).
        public static string Help(string key)
        {
            if (Current == Language.Off) return null;
            return Get(key);
        }

        // Messages and dialogs: Japanese or English (English when Off).
        public static string Get(string key)
        {
            if (!Texts.TryGetValue(key, out var t)) return key;
            return Current == Language.Japanese ? t.ja : t.en;
        }

        public static GUIContent Label(string label, string tipKey) => new GUIContent(label, Get(tipKey));
    }
}
