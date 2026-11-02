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
            ["help.avatar"] = ("Choose the avatar to add FT to, the menu for the FT toggle, and the face mesh to move.\nFace view shows the face of the prefab.", "FT を付けるアバターと、FT のトグルを置くメニュー、動かす顔のメッシュを選びます。\nprefab を選ぶと、Face view にその顔が映ります。"),
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
            ["help.parts"] = ("For each face movement, choose the blendshape to use. Part names are \"region.movement\": mouth.open opens the mouth.\nMove up and down in the list to see the face with that shape at 100 in Face view. Select a row to see the face with its shape.\nNot set: nothing is chosen yet. not in mesh: the avatar has no shape with that name; choose again.", "顔の動きごとに、使うシェイプキーを選びます。部品名は「部位.動き」で、mouth.open は口を開ける動きです。\n候補の一覧で上下に動かすと、その形を 100 にした顔が Face view に映ります。行を選ぶと、割り当てた形の顔が映ります。\nNot set: まだ選んでいません。not in mesh: アバターに無い名前です。選び直してください。"),
            ["help.layers"] = ("Choose what happens to the avatar's own animations (hand gesture faces, blinking, ...) while FT is on.\nStop while FT: its face does not show while FT is on. Mute while FT: it is turned off while FT is on. Remove: it is removed.\nIf you are not sure, leave them as they are.", "アバターに元からあるアニメーション（手のジェスチャーの表情、まばたきなど）を、FT の間どうするかを選びます。\nStop while FT: FT の間は表情を出さない。Mute while FT: FT の間は丸ごと止める。Remove: 消す。\n分からなければ、変えなくて構いません。"),
            ["help.inputs"] = ("Values your face tracking device sends. Without Play, moving them changes the face of the avatar in the scene and in Face view.\nUnder each slider are the parts it moves.", "フェイストラッキングの機器から届く値です。Play しなくても、動かすとシーンのアバターと Face view の顔が変わります。\n各スライダーの下に、そのスライダーで動く部品を出します。"),
            ["help.switches"] = ("Expressions that switch by value. Auto is usually fine. Pick a name to hold that expression and look at it.", "値で切り替わる表情です。ふつうは Auto のままで構いません。名前を選ぶと、その表情に固定して見られます。"),
            ["help.weights"] = ("Blendshapes moving now and how much. Red rows over 100 can break the shape because shapes add up too much. Lower the values of the trees in From, or check the parts' blendshapes.", "いま動いているシェイプキーと量です。100 を超える赤い行は、形が重なりすぎて崩れることがあります。From の木の値を下げるか、部品の割り当てを見直してください。"),
            ["msg.noApplied"] = ("The assigned blendshapes are not on the avatar, so the face does not move.\nChoose blendshapes for the parts in the Avatar tab.", "割り当てたシェイプキーがアバターに無いので、顔は動きません。\nAvatar のタブで、部品にシェイプキーを選んでください。"),
            ["picker.keys"] = ("↑↓: show in Face view  Enter: choose", "↑↓: Face view に映す  Enter: 決める"),
            ["faceview.note"] = ("The avatar and the scene are not changed.", "アバターもシーンも書き換えません。"),
            ["faceview.noPrefab"] = ("Choose the prefab in the Avatar tab.", "Avatar のタブで prefab を選んでください。"),
            ["faceview.noTable"] = ("Choose avatar.jsonc in the table editor.", "表の編集ツールで avatar.jsonc を選んでください。"),
            ["gen.step1"] = ("1. Create the tables", "1. 表を作る"),
            ["gen.help1"] = ("Type the avatar's name and press the button to make the three tables in Settings/<name>/ from the templates. Skip this when you have the tables.", "アバターの名前を入れて押すと、テンプレートから 3 つの表を Settings/<名前>/ に作ります。表がもうあれば、この手順は要りません。"),
            ["gen.step2"] = ("2. Point the tables at your avatar", "2. 表をアバターに結ぶ"),
            ["gen.help2"] = ("Select your avatar in the Hierarchy and press the button to write its prefab, face mesh and menu to the table.", "Hierarchy でアバターを選んで押すと、表に prefab・顔のメッシュ・メニューを書きます。"),
            ["gen.step3"] = ("3. Assign blendshapes to the face movements", "3. 顔の動きにシェイプキーを割り当てる"),
            ["gen.help3"] = ("In the table editor, choose the blendshape for each face movement and check the face.", "表の編集ツールで、顔の動きごとに使うシェイプキーを選び、顔を見て確かめます。"),
            ["gen.step4"] = ("4. Check the tables", "4. 表を調べる"),
            ["gen.help4"] = ("Look for mistakes and gaps in the tables. Fix them until there are no findings.", "表の誤りや抜けを調べます。指摘が無くなるまで直します。"),
            ["gen.step5"] = ("5. Add the menu toggles", "5. メニューにトグルを足す"),
            ["gen.help5"] = ("Add the FT on/off toggle and the AFK toggle to the avatar's menu. Type a submenu name when the menu is full.", "FT のオン・オフと AFK のトグルを、アバターのメニューに足します。メニューに空きが無ければサブメニューの名前を入れます。"),
            ["gen.step6"] = ("6. AFK animation (optional)", "6. AFK のアニメーション（任意）"),
            ["gen.help6"] = ("Choose the animations played while AFK. You can skip this.", "AFK のときに流すアニメーションを選びます。選ばなくても使えます。"),
            ["gen.step7"] = ("7. Attach to the avatar", "7. アバターに付ける"),
            ["gen.help7"] = ("Select your avatar in the Hierarchy and press the button to add the settings component. FT is made on Play and upload; the avatar itself is not changed.", "Hierarchy でアバターを選んで押すと、設定のコンポーネントを付けます。Play とアップロードのときに FT が作られ、アバター自体は書き換えません。"),
            ["gen.helpAdvanced"] = ("Generate writes controllers with FT and a Prefab Variant to files for checking. Scenario check plays a scenario file in Play Mode and records it.", "Generate は、FT を付けた controller と Prefab Variant をファイルに書き出して確かめるためのものです。Scenario check は、場面のファイルを Play Mode で流して記録します。"),
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

        // Headings of the generator window's steps: Japanese or English (English when Off).
        public static string Heading(string key) => Get(key);

        // Messages and dialogs: Japanese or English (English when Off).
        public static string Get(string key)
        {
            if (!Texts.TryGetValue(key, out var t)) return key;
            return Current == Language.Japanese ? t.ja : t.en;
        }

        public static GUIContent Label(string label, string tipKey) => new GUIContent(label, Get(tipKey));
    }
}
