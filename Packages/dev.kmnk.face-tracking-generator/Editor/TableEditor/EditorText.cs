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
            ["msg.noTable"] = ("Choose the settings file.", "設定ファイルを選んでください。"),
            ["msg.chooseAvatar"] = ("Choose an avatar in the scene.", "シーンのアバターを選んでください。"),
            ["msg.animBusy"] = ("Close the Animation window preview first.", "Animation ウィンドウのプレビューを止めてください。"),
            ["msg.playMode"] = ("The preview does not run in Play Mode.", "Play Mode の間はプレビューできません。"),
            ["msg.noController"] = ("No controller", "controller がありません"),
            ["msg.noPrefab"] = ("The prefab is not found. Choose it above.", "prefab が見つかりません。上で選んでください。"),
            ["msg.notOnAvatar"] = ("(not on avatar)", "（アバターに無い）"),
            ["dialog.discard"] = ("The settings have unsaved changes. Discard them?", "設定に保存していない変更があります。破棄しますか？"),
            ["dialog.save"] = ("The settings have unsaved changes. Save them?", "設定に保存していない変更があります。保存しますか？"),
            ["msg.noApplied"] = ("The chosen blendshapes are not on the avatar, so the face does not move.\nChoose blendshapes for the face movements in the Avatar tab.", "選んだシェイプキーがアバターに無いので、顔は動きません。\nAvatar のタブで、顔の動きにシェイプキーを選んでください。"),
            ["picker.keys"] = ("↑↓: show in Face view  Enter: choose", "↑↓: Face view に映す  Enter: 決める"),
            ["faceview.noPrefab"] = ("Choose the prefab in the Avatar tab.", "Avatar のタブで prefab を選んでください。"),
            ["faceview.noTable"] = ("Choose the settings file in the settings editor.", "設定エディタで設定ファイルを選んでください。"),
            ["gen.step1"] = ("1. Create the FT settings", "1. FT の設定を作る"),
            ["share.overwrite"] = ("The folder already has {0}. Overwrite?", "フォルダに {0} が既にあります。上書きしますか？"),
            ["share.exported"] = ("Exported to {0}", "{0} に書き出しました"),
            ["share.noChanges"] = ("The folder's expression settings are the same as these settings. Nothing changes.", "フォルダの表情の設定は、この設定と同じです。変わるファイルはありません。"),
            ["share.confirmImport"] = ("These files will be overwritten with the folder's: {0}\nThe avatar settings do not change. Import?", "次のファイルをフォルダのもので上書きします: {0}\nアバターの設定は変わりません。取り込みますか？"),
            ["gen.step2"] = ("2. Edit the settings", "2. 設定を編集する"),
            ["gen.step3"] = ("3. Check the settings", "3. 設定を確かめる"),
            ["gen.help3"] = ("When Check finds something, read it and fix the settings in the settings editor.", "指摘が出たら、内容を見て設定エディタで直してください。"),
            ["gen.step4"] = ("4. Add the settings component to the avatar", "4. アバターに設定コンポーネントを付ける"),
            ["help.liveInputs"] = ("Live input is moving the sliders. Stop it to move them by hand.", "Live input がスライダーを動かしています。手で動かすには止めてください。"),
            ["live.stopped"] = ("Stopped.", "止まっています。"),
            ["live.waiting"] = ("Waiting on port {0}. Nothing has arrived for a second.", "ポート {0} で待っています。1 秒以上届いていません。"),
            ["live.receiving"] = ("Receiving on port {0}: {1} messages in the last second.", "ポート {0} で受信中: 直近 1 秒に {1} メッセージ。"),
            ["live.error"] = ("Could not open port {0}: {1}", "ポート {0} を開けませんでした: {1}"),
            ["live.ignored"] = (" Ignored: {0}.", " 読めずに捨てたもの: {0}。"),
            ["switches.title"] = ("States", "状態"),
            ["switches.byValue"] = ("(by the values)", "（値で切り替え中）"),
            ["switches.byMenu"] = ("(chosen in the menu)", "（メニューで選択中）"),
            ["part.region"] = ("It moves rows of its region, so the region (before the first dot) cannot change.", "部位の行で使われているので、部位（最初の . の前）は変えられません。"),
            ["part.addIn"] = ("Add a face movement to {0}", "{0} に顔の動きを足す"),
            ["part.regionOf"] = ("A movement of this row must start with \"{0}.\".", "この行の動きは「{0}.」で始めてください。"),
            ["sm.others"] = ("The other {0} keys move with neutral.", "ほかの {0} キーは neutral で動きます。"),
            ["sm.curveTip"] = ("How much the movement moves (up) for the key's value (right). Click to edit. Its highest point is the strength.", "キーの値（横）に対する動く量（縦）。クリックで編集します。一番高い所が強さです。"),
            ["sm.curveNote"] = ("The curve of {0} can't be matched closely by an expression (largest difference {1}).", "{0} の曲線は式で近づけきれません（最大の差 {1}）。"),
            ["tip.removeRow"] = ("Put this key back to neutral.", "このキーを neutral に戻します。"),
            ["sm.noFixed"] = ("No fixed movements.", "固定の動きはありません。"),
            ["sm.noTable"] = ("Open a settings file in the settings editor.", "設定エディタで設定ファイルを開いてください。"),
            ["sm.brokenTransitions"] = ("{0} transitions go to or from a state this state machine does not have (transitions[{1}]). They are not drawn and generation leaves them out.", "{0} 個の遷移が、この State Machine に無い状態を指しています（transitions[{1}]）。図には描かれず、生成でも使われません。"),
            ["sm.removeBroken"] = ("Remove them", "消す"),
            ["sm.pickTarget"] = ("Click the state to go to (Esc to cancel).", "行き先の状態を押してください（Esc でやめます）。"),
            ["sm.removeMachine"] = ("Remove the state machine \"{0}\" with its states and transitions?", "State Machine \"{0}\" を、その状態と遷移ごと消します。"),
            ["sm.machineForm"] = ("Use letters, digits and _.", "英字・数字・_ で書いてください。"),
            ["sm.machineUsed"] = ("A state machine has this name (capitals are not told apart).", "この名前の State Machine があります（大文字小文字は区別しません）。"),
            ["sm.sameName"] = ("Type a new name.", "新しい名前を入れてください。"),
            ["sm.removeState"] = ("Remove the state \"{0}\" and its transitions?", "状態 \"{0}\" と、その遷移を消します。"),
            ["devices.noRecords"] = ("No recordings. Add recordings made with ft-measure to see the range of values measured on that device.", "記録がありません。ft-measure で測った記録を入れると、その機器で測定した値の範囲を確認できます。"),
            ["range.neutral"] = ("neutral", "真顔"),
            ["range.talk"] = ("talking", "真顔で喋る"),
            ["range.smile"] = ("talking with a smile", "笑顔で喋る"),
            ["sm.inputBand"] = ("Editing {0}", "{0} を編集中"),
            ["preview.side"] = ("Runs as {0}: the transitions and strengths of {0}", "{0} として動く: {0} の遷移と強さ"),
            ["input.range"] = ("{0}: {1} to {2}", "{0}: {1}〜{2}"),
            ["input.over"] = ("⚠ {0} reaches only {1}", "⚠ {0} では {1} までしか届きません"),
            ["input.under"] = ("⚠ {0} does not go below {1}", "⚠ {0} では {1} より下がりません"),
            ["input.notSent"] = ("{0} does not send it (always 0)", "{0} では送られない（常に 0）"),
            ["device.unused"] = ("Values no expression uses yet ({0})", "まだ使っている表情が無い値 ({0})"),
            ["overlap.again"] = ("Choose again: {0}", "{0} を選び直す"),
            ["overlap.notUse"] = ("Do not use {0}", "{0} を使わない"),
            ["tip.removePosition"] = ("Remove this position", "この位置を消す"),
            ["newPart.form"] = ("Write it like jaw.jawOpen or eye.joy.tear: a region, an optional state and a movement joined by dots, using letters, digits and _.", "jaw.jawOpen や eye.joy.tear のように、部位・（省ける）状態・動きを . でつないでください。使える文字は英字・数字・_ です。"),
            ["newPart.used"] = ("This name is already used.", "この名前はもうあります。"),
            ["inputs.expression"] = ("Expression: {0}", "式: {0}"),
            ["inputs.expressionTable"] = ("Expression (from the settings): {0}", "式（設定に書いたもの）: {0}"),
            ["build.paused"] = ("Paused while the VRChat SDK builds the avatar. It resumes when the build is done; press Resume if it does not.", "VRChat SDK がアバターをビルドしている間は止めています。ビルドが終わると戻ります。戻らないときは Resume を押してください。"),
            ["part.renameNote"] = (". Uses in the expression settings change too.", "。表情の設定で使っている所もまとめて変えます。"),
            ["part.new"] = ("No expression uses it yet.", "まだどの表情も使っていません"),
            ["part.cannotRemove"] = ("An expression uses it, so it cannot be removed.", "使っている表情があるので消せません"),
            ["tip.table"] = ("The settings file of the avatar (avatar.jsonc). It names the expression settings file.", "アバターの設定ファイル（avatar.jsonc）。表情の設定ファイルを指します。"),
            ["tip.prefab"] = ("The avatar prefab. Its Avatar Descriptor gives the controllers.", "アバターの prefab。controller は Avatar Descriptor から取ります。"),
            ["tip.menu"] = ("The menu that gets the FT and AFK toggles and the state submenus. On upload and in Play Mode the avatar gets a copy of it with them added; this menu asset is not edited.", "FT と AFK のトグルと状態のサブメニューを足すメニュー。アップロードと Play Mode のときに、それらを足した写しがアバターに付きます。このメニューのファイルは書き換えません。"),
            ["tip.mesh"] = ("The face mesh. Face movements without their own mesh use it.", "顔のメッシュ。メッシュを指定していない顔の動きはこれを使います。"),
            ["placeholder.subMenu"] = ("Empty: put directly in Menu", "空なら Menu に直接入れます"),
            ["tip.subMenu"] = ("With a name, a submenu of that name is made in Menu and the FT and AFK toggles and the state submenus go into it. A submenu of the same name already in Menu is used. When Menu is full, they go directly into Menu and the log says so.", "名前を入れると、Menu の中にその名前のサブメニューを作り、FT と AFK のトグルと状態のサブメニューをそこに入れます。同じ名前のサブメニューが既にあれば、それを使います。Menu が満杯でサブメニューを足せないときは、Menu に直接入れ、ログにそう出します。"),
            ["tip.faceTrackLabel"] = ("The name of the toggle that turns FT on and off.", "FT のオン・オフを切り替えるトグルの名前。"),
            ["tip.afkPlayLabel"] = ("The name of the toggle that plays the AFK animation.", "AFK のアニメーションを流すトグルの名前。"),
            ["tip.fill"] = ("Fills the face mesh, the menu, the face movements whose blendshape has the same name, and the empty AFK animations from the prefab. Undo puts them back.", "prefab から、顔のメッシュ・メニュー・同じ名前のシェイプキー・空いている AFK のアニメーションを埋めます。Undo で戻せます。"),
            ["tip.movement"] = ("Names are \"region.movement\" or \"region.state.movement\". A state's movements are used only while in that state.", "名前は「部位.動き」か「部位.状態.動き」です。状態の動きは、その状態の間だけ使います。"),
            ["tip.blendshape"] = ("Not set: nothing is chosen yet. not in mesh: the avatar has no shape with that name; choose again. Material values (Float, Range) can be chosen too: they move only in a state's fixed amounts, and amount 100 gives the property's largest value.", "Not set: まだ選んでいません。not in mesh: アバターに無い名前です。選び直してください。マテリアルの値（Float・Range）も選べます。状態の固定の量でだけ動き、量 100 でプロパティの最大の値になります。"),
            ["tip.scale"] = ("Corrects the strength of this avatar's blendshape. Change the strength per expression in State Machine.", "このアバターのシェイプキーの強さの補正です。表情ごとの強さは State Machine で直します。"),
            ["sm.empty"] = ("Select a state or a transition to edit it here.", "状態か遷移を選ぶと、ここに設定が出ます。"),
            ["overlap.none"] = ("Overlaps: none", "重なり: なし"),
            ["overlap.count"] = ("Overlaps: {0}. See below the sliders.", "重なり: {0} 件。詳しくはスライダーの下にあります。"),
            ["gen.noTable"] = ("Choose the settings file, or create one above.", "設定ファイルを選ぶか、上で作ってください。"),
            ["gen.tableError"] = ("The settings file cannot be read: {0}", "設定ファイルを読めません: {0}"),
            ["gen.created"] = ("Created {0}.", "{0} を作りました。"),
            ["gen.checkNone"] = ("Check: no findings.", "Check: 指摘はありません。"),
            ["gen.checkFindings"] = ("Check: {0} findings.\n{1}", "Check: 指摘が {0} 件あります。\n{1}"),
            ["gen.attached"] = ("Added the settings component to {0}. FT is made on Play and upload.", "{0} に設定のコンポーネントを付けました。Play とアップロードのときに FT が作られます。"),
            ["gen.generated"] = ("Generated.\n{0}", "生成しました。\n{0}"),
            ["gen.running"] = ("Running {0}. The record goes to {1}.", "{0} を流しています。記録は {1} に書きます。"),
            ["gen.failed"] = ("Failed: {0}", "失敗しました: {0}"),
            ["gen.scenarioTxt"] = ("Choose a scenario .txt file.", "場面の .txt ファイルを選んでください。"),
            ["inspector.noFile"] = ("No settings file. Make one in the Face Tracking Generator window or choose it here.", "設定ファイルがありません。Face Tracking Generator の窓で作るか、ここで選んでください。"),
            ["inspector.info"] = ("Makes FT from these settings when you enter Play Mode and when you upload.", "Play Mode に入るときとアップロードのときに、この設定で FT を作ります。"),
            ["gen.exists"] = ("Settings/{0}/ already exists, so nothing was made. To use it, choose its avatar.jsonc in Settings file; to make new settings, type another name.", "Settings/{0}/ はもうあるので、作っていません。使うなら Settings file でその avatar.jsonc を選び、新しく作るなら別の名前を入れてください。"),
            ["tip.stopWhileFT"] = ("While FT is on, the layer does not start a new expression (a hand sign does not switch to its face); what always runs keeps running. For hand gesture layers.", "FT の間、新しく表情を出し始めないようにします（手でピースをしても表情は切り替わりません）。普段から流れているものは止まりません。手のジェスチャーのレイヤー向けです。"),
            ["tip.muteWhileFT"] = ("While FT is on, the layer stops entirely, also what moves the face by itself (such as automatic blinking). For blinking and eye layers; FX only.", "FT の間、丸ごと止めます。何もしなくても顔を動かし続けるもの（自動のまばたきなど）も止まります。まばたきや目の動きのレイヤー向けで、FX でだけ選べます。"),
            ["tip.live"] = ("Moves the inputs with the values VRCFaceTracking sends (/tracking/face/v1). Set VRCFaceTracking's Send Port to this port and do not run VRChat.", "VRCFaceTracking が送る値（/tracking/face/v1）で入力を動かします。VRCFaceTracking の Send Port をこのポートにし、VRChat は起動しないでください。"),
            ["tip.amount"] = ("Over 100 (red): shapes add up so much that the face can break.", "100 を超える赤い行は、形が重なりすぎて崩れることがあります。"),
            ["tip.device"] = ("Runs the preview as the chosen device: the sliders show the values it sends.", "選んだ機器として動かします。スライダーには、その機器が送る値の範囲が出ます。"),
            ["tip.previewMenu"] = ("A state chosen as the menu does. None: the values choose the state.", "メニューで選んだ状態。None なら値で切り替わります。"),
            ["tip.regionMode"] = ("Neutral: the region moves as in neutral. Override: while in this state, the region moves with the rows below. Off: the region's neutral stops.", "Neutral: neutral のとおりに動きます。Override: この状態の間、下の行で動かします。Off: その部位の neutral を止めます。"),
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
