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

        // Raised when the language changes, for windows that keep texts (the expression graph).
        public static event Action Changed;

        // For TableEditorChecks.Capture: changes the language as a user would, without the user's setting.
        internal static void SetForCapture(Language language) { Override = language; Changed?.Invoke(); }

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
            set { EditorPrefs.SetString(PrefKey, PrefValues[(int)value]); Changed?.Invoke(); }
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
            ["note.preview"] = ("The mouth while talking and the avatar's own animations are not included.", "話している間の口、アバターに元からあるアニメーションは含みません。"),
            ["msg.previewing"] = ("Previewing. The avatar is not changed; stopping restores it.",
                "プレビュー中です。アバターは書き換えません。止めると元に戻ります。"),
            ["msg.noTable"] = ("Choose the settings file.", "設定ファイルを選んでください。"),
            ["msg.chooseAvatar"] = ("Choose an avatar in the scene.", "シーンのアバターを選んでください。"),
            ["msg.animBusy"] = ("Close the Animation window preview first.", "Animation ウィンドウのプレビューを止めてください。"),
            ["msg.playMode"] = ("The preview does not run in Play Mode.", "Play Mode の間はプレビューできません。"),
            ["msg.noController"] = ("No controller", "controller がありません"),
            ["msg.noPrefab"] = ("The prefab is not found. Choose it above.", "prefab が見つかりません。上で選んでください。"),
            ["msg.notOnAvatar"] = ("(not on avatar)", "(not on avatar)"),
            ["dialog.discard"] = ("The settings have unsaved changes. Discard them?", "設定に保存していない変更があります。破棄しますか？"),
            ["dialog.save"] = ("The settings have unsaved changes. Save them?", "設定に保存していない変更があります。保存しますか？"),
            ["help.parts"] = ("For each face movement, choose the blendshape to use.\nNames are \"region.movement\" or \"region.state.movement\". A state's movements are used only while in that state.\nMove up and down in the list to see the face with that shape at 100 in Face view. Select a row to see the face with its shape.\nNot set: nothing is chosen yet. not in mesh: the avatar has no shape with that name; choose again.\nScale corrects the strength of this avatar's shapekey. Change the strength per expression in State Machine.", "顔の動きごとに、使うシェイプキーを選びます。\n名前は「部位.動き」か「部位.状態.動き」です。状態の動きは、その状態の間だけ使います。\n候補の一覧で上下に動かすと、その形を 100 にした顔が Face view に映ります。行を選ぶと、選んだ形の顔が映ります。\nNot set: まだ選んでいません。not in mesh: アバターに無い名前です。選び直してください。\nScale は、このアバターの shapekey の強さの補正です。表情ごとの強さは State Machine で直します。"),
            ["help.layers"] = ("While FT is on, the avatar's own face animations do not get in the way of the FT face.\nStop while FT: it does not start a new expression (a peace sign does not switch to the gesture face). Animations that always run do not stop. For hand gesture layers.\nMute while FT: it stops entirely, including animations that move the face on their own (such as automatic blinking). For blinking and eye layers; FX only.\nIf you are not sure, check neither.", "FT を使っている間、アバターに元からある表情のアニメーションが、FT の顔の邪魔をしないようにします。\nStop while FT: 新しく表情を出し始めないようにします（手でピースをしても、ジェスチャーの表情に切り替わりません）。普段から流れているものは止まりません。手のジェスチャーのレイヤー向けです。\nMute while FT: 丸ごと止めます。何もしなくても顔を動かし続けるもの（自動のまばたきなど）も止まります。まばたきや目の動きのレイヤー向けで、FX でだけ選べます。\nどれか分からないものは、どちらも付けなくて構いません。"),
            ["help.inputs"] = ("Values the device chosen in Device sends. Without Play, moving them changes the face.\nUnder each slider are the face movements it moves.", "Device で選んだ機器が送る値です。Play しなくても、動かすと顔が変わります。\n各スライダーの下に、そのスライダーで動く顔の動きを出します。"),
            ["help.switches"] = ("The state of each state machine now, and whether the values or the menu chose it.", "状態遷移機ごとの今の状態と、値とメニューのどちらで選ばれているかです。"),
            ["help.weights"] = ("Blendshapes moving now and how much. Source is the value that moves it. Red rows over 100 can break the shape because shapes add up too much.", "いま動いているシェイプキーと量です。Source は、どの値から動いているかです。100 を超える赤い行は、形が重なりすぎて崩れることがあります。"),
            ["msg.noApplied"] = ("The chosen blendshapes are not on the avatar, so the face does not move.\nChoose blendshapes for the face movements in the Avatar tab.", "選んだシェイプキーがアバターに無いので、顔は動きません。\nAvatar のタブで、顔の動きにシェイプキーを選んでください。"),
            ["picker.keys"] = ("↑↓: show in Face view  Enter: choose", "↑↓: Face view に映す  Enter: 決める"),
            ["faceview.note"] = ("The avatar and the scene are not changed.", "アバターもシーンも書き換えません。"),
            ["faceview.noPrefab"] = ("Choose the prefab in the Avatar tab.", "Avatar のタブで prefab を選んでください。"),
            ["faceview.noTable"] = ("Choose the settings file in the settings editor.", "設定エディタで設定ファイルを選んでください。"),
            ["gen.step1"] = ("1. Create the FT settings", "1. FT の設定を作る"),
            ["gen.help1"] = ("Type the avatar's name and press a button to make the FT settings in Settings/<name>/: from the templates, or from a folder exported with Share in the settings editor of another project. Skip this when you have them.",
                "アバターの名前を入れてボタンを押すと、FT の設定を Settings/<名前>/ に作ります。テンプレートからか、別のプロジェクトの設定エディタの Share で書き出したフォルダから作れます。設定がもうあれば、この手順は要りません。"),
            ["share.overwrite"] = ("The folder already has {0}. Overwrite?", "フォルダに {0} が既にあります。上書きしますか？"),
            ["share.exported"] = ("Exported to {0}", "{0} に書き出しました"),
            ["share.noChanges"] = ("The folder's input and expression tables are the same as these settings. Nothing changes.", "フォルダの入力の表と表情の表は、この設定と同じです。変わる表はありません。"),
            ["share.confirmImport"] = ("These tables will be overwritten with the folder's: {0}\nThe avatar table does not change. Import?", "次の表をフォルダのもので上書きします: {0}\nアバターの表は変わりません。取り込みますか？"),
            ["gen.step2"] = ("2. Choose your avatar", "2. アバターを指定する"),
            ["gen.help2"] = ("Choose your avatar in the scene (selecting it in the Hierarchy also chooses it) and press the button to write its prefab, face mesh, menu and AFK clips (only empty ones) to the settings.", "シーンのアバターを選んで（Hierarchy で選んでも入ります）押すと、設定に prefab・顔のメッシュ・メニューと、空いている AFK の clip を書きます。"),
            ["gen.step3"] = ("3. Assign blendshapes to the face movements", "3. 顔の動きにシェイプキーを割り当てる"),
            ["gen.help3"] = ("In the settings editor, choose the blendshape for each face movement and check the face.", "設定エディタで、顔の動きごとに使うシェイプキーを選び、顔を見て確かめます。"),
            ["gen.step4"] = ("4. Check the settings", "4. 設定を確かめる"),
            ["gen.help4"] = ("Look for mistakes and gaps in the settings. Fix them until there are no findings.", "設定の誤りや抜けを調べます。指摘が無くなるまで直します。"),
            ["gen.step5"] = ("5. Add the menu toggles", "5. メニューにトグルを足す"),
            ["gen.help5"] = ("Add the FT on/off toggle and the AFK toggle to the avatar's menu. Type a submenu name when the menu is full.", "FT のオン・オフと AFK のトグルを、アバターのメニューに足します。メニューに空きが無ければサブメニューの名前を入れます。"),
            ["gen.step6"] = ("6. AFK animation (optional)", "6. AFK のアニメーション（任意）"),
            ["gen.help6"] = ("Choose the animations played while AFK. You can skip this.", "AFK のときに流すアニメーションを選びます。選ばなくても使えます。"),
            ["gen.step7"] = ("7. Attach to the avatar", "7. アバターに付ける"),
            ["gen.help7"] = ("Select your avatar in the Hierarchy and press the button to add the settings component. FT is made on Play and upload; the avatar itself is not changed. FT uses the avatar's Face Tracking section, so it moves with the desktop Selfie Expression and with face tracking devices through VRCFaceTracking (2026-11-02 or later).", "Hierarchy でアバターを選んで押すと、設定のコンポーネントを付けます。Play とアップロードのときに FT が作られ、アバター自体は書き換えません。FT はアバターの Face Tracking の節を使うので、デスクトップの Selfie Expression でも、VRCFaceTracking（2026-11-02 以降）を通した VR の機器でも動きます。"),
            ["gen.helpAdvanced"] = ("Generate writes controllers with FT and a Prefab Variant to files for checking. Scenario check plays a scenario file in Play Mode and records it.", "Generate は、FT を付けた controller と Prefab Variant をファイルに書き出して確かめるためのものです。Scenario check は、場面のファイルを Play Mode で流して記録します。"),
            ["help.presets"] = ("Try expressions that break easily. A button sets the sliders to its values.", "崩れやすい表情をボタンで試せます。押すとスライダーがその値になります。"),
            ["switches.title"] = ("States", "値で切り替わる表情"),
            ["switches.byValue"] = ("(by the values)", "（値で切り替え中）"),
            ["switches.byMenu"] = ("(chosen in the menu)", "（メニューで選択中）"),
            ["help.previewMenu"] = ("See the face of a state chosen in the menu. None: the values change the state.", "メニューで状態を選んだときの顔を見ます。None なら値で切り替わります。"),
            ["part.region"] = ("It moves rows of its region, so the region (before the first dot) cannot change.", "部位の行で使われているので、部位（最初の . の前）は変えられません。"),
            ["part.addIn"] = ("Add a face movement to {0}", "{0} に顔の動きを足す"),
            ["part.regionOf"] = ("A movement of this row must start with \"{0}.\".", "この行の動きは「{0}.」で始めてください。"),
            ["sm.help"] = ("Select a state to show its settings on the right. Right-click a state to make a transition. Wheel: zoom.", "状態を選ぶと右に設定が出ます。状態を右クリックすると遷移を作れます。ホイールで拡大・縮小します。"),
            ["sm.regions"] = ("Override: while in this state, the region moves with the rows below. Off: the region's neutral stops.", "Override: この状態の間、部位を下の行で動かします。Off: 部位の neutral を止めます。"),
            ["sm.others"] = ("The other {0} keys move with neutral. × puts a key back to neutral.", "ほかの {0} キーは neutral で動きます。× で neutral に戻せます。"),
            ["sm.neutral"] = ("The rows of neutral. Other states use them for the keys they do not write.", "neutral の行です。ほかの状態は、書いていないキーをここから使います。"),
            ["sm.noFixed"] = ("No fixed movements.", "固定の動きはありません。"),
            ["sm.conditions"] = ("Moves when all the conditions hold. While a state is chosen in the menu, this transition does not run.", "すべての条件が成り立つと移ります。メニューで状態を選んでいる間は、この遷移は動きません。"),
            ["sm.noTable"] = ("Open settings in the settings editor.", "設定エディタで表を開いてください。"),
            ["sm.noInputs"] = ("No inputs. Add one in the Inputs tab.", "入力がありません。Inputs タブで足してください。"),
            ["sm.pickTarget"] = ("Click the state to go to (Esc to cancel).", "行き先の状態を押してください（Esc でやめます）。"),
            ["sm.removeState"] = ("Remove the state \"{0}\" and its transitions?", "状態 \"{0}\" と、その遷移を消します。"),
            ["device.notSent"] = (" (this device does not send it)", "（この機器は送らない）"),
            ["device.range"] = (" (this device: {0} to {1})", "（この機器では {0}〜{1}）"),
            ["device.unused"] = ("Values no expression uses yet ({0})", "まだ使っている表情が無い値 ({0})"),
            ["overlap.again"] = ("Choose again: {0}", "{0} を選び直す"),
            ["overlap.notUse"] = ("Do not use {0}", "{0} を使わない"),
            ["tip.removePosition"] = ("Remove this position", "この位置を消す"),
            ["help.newPart"] = ("Write it as \"region.movement\" or \"region.state.movement\". Regions: gaze, eye, brow, nose, cheek, jaw, lip, tongue, throat.", "「部位.動き」か「部位.状態.動き」の形で。部位は gaze・eye・brow・nose・cheek・jaw・lip・tongue・throat。"),
            ["newPart.form"] = ("Write it like jaw.jawOpen or eye.joy.tear: a region, an optional state and a movement joined by dots, using letters, digits and _.", "jaw.jawOpen や eye.joy.tear のように、部位・（省ける）状態・動きを . でつないでください。使える文字は英字・数字・_ です。"),
            ["newPart.used"] = ("This name is already used.", "この名前はもうあります。"),
            ["help.inputsTab"] = ("Check the values from the device that FT uses. Each value is made from the face tracking keys by its expression in the avatar's Face Tracking section; values are not synced, as every client makes them. Over 10 values, the avatar is no longer Excellent in Forwarded Parameters.", "機器が送る値のうち、FT で使う値に印を付けます。値は、アバターの Face Tracking の節で、トラッキングのキーから式で作ります。どのクライアントでも同じように作るので、同期しません。10 個を超えると、Forwarded Parameters の評価が Excellent から外れます。"),
            ["inputs.usedBy"] = ("Used by {0}", "{0} が使う"),
            ["inputs.through"] = (" (through the average {0})", "（{0} の平均）"),
            ["inputs.added"] = ("Added. No expression uses it yet.", "追加。まだどの表情も使っていません"),
            ["inputs.addedPrefix"] = ("Added. ", "追加。"),
            ["inputs.locked"] = ("Cannot uncheck: {0}", "外せません: {0}"),
            ["inputs.notSent"] = ("This device does not send it.", "この機器は送りません"),
            ["inputs.expression"] = ("Expression: {0}", "式: {0}"),
            ["inputs.expressionTable"] = ("Expression (from the settings): {0}", "式（設定に書いたもの）: {0}"),
            ["inputs.noExpression"] = ("No expression: not used. Write \"expression\" in input.jsonc.", "式が無いので使いません。input.jsonc に \"expression\" を書いてください"),
            ["build.paused"] = ("Paused while the VRChat SDK builds the avatar. It resumes when the build is done; press Resume if it does not.", "VRChat SDK がアバターをビルドしている間は止めています。ビルドが終わると戻ります。戻らないときは Resume を押してください。"),
            ["part.renameNote"] = (". Uses in the expression table change too.", "。表情の表で使っている所もまとめて変えます。"),
            ["part.new"] = ("No expression uses it yet.", "まだどの表情も使っていません"),
            ["part.cannotRemove"] = ("An expression uses it, so it cannot be removed.", "使っている表情があるので消せません"),
            ["tip.table"] = ("The settings file of the avatar (avatar.jsonc). It names the input and expression settings files.", "アバターの設定ファイル（avatar.jsonc）。入力と表情の設定ファイルを指します。"),
            ["tip.prefab"] = ("The avatar prefab. Its Avatar Descriptor gives the controllers.", "アバターの prefab。controller は Avatar Descriptor から取ります。"),
            ["tip.menu"] = ("The menu that gets the FT toggle and the AFK flag.", "FT のトグルと AFK のフラグを置くメニュー。"),
            ["tip.mesh"] = ("The face mesh. Face movements without their own mesh use it.", "顔のメッシュ。メッシュを指定していない顔の動きはこれを使います。"),
            ["tip.missing"] = ("The avatar has no shape for this movement.", "アバターにこの動きに合う形がありません。"),
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

        // Short descriptions of input names for Source ("まぶた（EyeLidLeft）"). Null when there is none.
        public static string InputDescription(string input)
        {
            (string en, string ja)? d =
                input.StartsWith("EyeLid") ? ("eyelid", "まぶた") :
                input == "SmileFrown" || input == "SmileSad" ? ("smiling mouth", "笑った口") :
                input == "JawOpen" ? ("open jaw", "口を開ける") :
                input == "MouthOpen" ? ("mouth open", "口の開き") :
                input == "LipPucker" ? ("pucker", "口をすぼめる") :
                input.StartsWith("MouthCornerPull") ? ("mouth corner", "口角") :
                input.StartsWith("CheekPuffSuck") ? ("cheek", "頬") :
                input.StartsWith("Tongue") ? ("tongue", "舌") :
                (input.StartsWith("Eye") && (input.EndsWith("X") || input.EndsWith("Y"))) ? ("gaze", "目線") :
                ((string, string)?)null;
            if (d == null) return null;
            return Current == Language.Japanese ? d.Value.ja : d.Value.en;
        }

        // Names of regions (the part of a face movement's name before the dot).
        public static string Region(string region)
        {
            var ja = Current == Language.Japanese;
            switch (region)
            {
                case "eye": return ja ? "目" : "eye";
                case "mouth": return ja ? "口" : "mouth";
                case "brow": return ja ? "眉" : "brow";
                case "tongue": return ja ? "舌" : "tongue";
                case "cheek": return ja ? "頬" : "cheek";
                case "gaze": return ja ? "目線" : "gaze";
                case "nose": return ja ? "鼻" : "nose";
                case "jaw": return ja ? "顎" : "jaw";
                case "lip": return ja ? "唇" : "lip";
                case "throat": return ja ? "喉・首" : "throat";
                default: return region;
            }
        }

        public static GUIContent Label(string label, string tipKey) => new GUIContent(label, Get(tipKey));
    }
}
