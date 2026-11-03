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
            ["note.preview"] = ("Movements that mix two values (such as mouth shapes) are approximate and may differ slightly.\nSmoothing, the mouth while talking and the avatar's own animations are not included.",
                "2 つの値を組み合わせる動き（口の形など）は近似で、実際と少し違うことがあります。\nなめらかにする処理、話している間の口、アバターに元からあるアニメーションは含みません。"),
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
            ["help.parts"] = ("For each face movement, choose the blendshape to use. Names are \"region.movement\": mouth.open opens the mouth.\nMove up and down in the list to see the face with that shape at 100 in Face view. Select a row to see the face with its shape.\nNot set: nothing is chosen yet. not in mesh: the avatar has no shape with that name; choose again.", "顔の動きごとに、使うシェイプキーを選びます。名前は「部位.動き」で、mouth.open は口を開ける動きです。\n候補の一覧で上下に動かすと、その形を 100 にした顔が Face view に映ります。行を選ぶと、選んだ形の顔が映ります。\nNot set: まだ選んでいません。not in mesh: アバターに無い名前です。選び直してください。"),
            ["help.layers"] = ("While FT is on, the avatar's own face animations do not get in the way of the FT face.\nStop while FT: it does not start a new expression (a peace sign does not switch to the gesture face). Animations that always run do not stop. For hand gesture layers.\nMute while FT: it stops entirely, including animations that move the face on their own (such as automatic blinking). For blinking and eye layers; FX only.\nIf you are not sure, check neither.", "FT を使っている間、アバターに元からある表情のアニメーションが、FT の顔の邪魔をしないようにします。\nStop while FT: 新しく表情を出し始めないようにします（手でピースをしても、ジェスチャーの表情に切り替わりません）。普段から流れているものは止まりません。手のジェスチャーのレイヤー向けです。\nMute while FT: 丸ごと止めます。何もしなくても顔を動かし続けるもの（自動のまばたきなど）も止まります。まばたきや目の動きのレイヤー向けで、FX でだけ選べます。\nどれか分からないものは、どちらも付けなくて構いません。"),
            ["help.inputs"] = ("Values the device chosen in Device sends. Without Play, moving them changes the face.\nUnder each slider are the face movements it moves.", "Device で選んだ機器が送る値です。Play しなくても、動かすと顔が変わります。\n各スライダーの下に、そのスライダーで動く顔の動きを出します。"),
            ["help.switches"] = ("Switches such as a different eye closing while smiling. Auto is usually fine. Pick a name to hold that expression whatever the sliders are.", "笑っているかどうかで目の閉じ方が変わる、といった切り替えです。普段は Auto のままで構いません。名前を選ぶと、スライダーに関係なく、その表情に固定して確かめられます。"),
            ["help.weights"] = ("Blendshapes moving now and how much. Source is the value that moves it. Red rows over 100 can break the shape because shapes add up too much.", "いま動いているシェイプキーと量です。Source は、どの値から動いているかです。100 を超える赤い行は、形が重なりすぎて崩れることがあります。"),
            ["msg.noApplied"] = ("The chosen blendshapes are not on the avatar, so the face does not move.\nChoose blendshapes for the face movements in the Avatar tab.", "選んだシェイプキーがアバターに無いので、顔は動きません。\nAvatar のタブで、顔の動きにシェイプキーを選んでください。"),
            ["picker.keys"] = ("↑↓: show in Face view  Enter: choose", "↑↓: Face view に映す  Enter: 決める"),
            ["faceview.note"] = ("The avatar and the scene are not changed.", "アバターもシーンも書き換えません。"),
            ["faceview.noPrefab"] = ("Choose the prefab in the Avatar tab.", "Avatar のタブで prefab を選んでください。"),
            ["faceview.noTable"] = ("Choose the settings file in the settings editor.", "設定エディタで設定ファイルを選んでください。"),
            ["gen.step1"] = ("1. Create the FT settings", "1. FT の設定を作る"),
            ["gen.help1"] = ("Type the avatar's name and press the button to make the FT settings in Settings/<name>/ from the templates. Skip this when you have them.", "アバターの名前を入れて押すと、テンプレートから FT の設定を Settings/<名前>/ に作ります。設定がもうあれば、この手順は要りません。"),
            ["gen.step2"] = ("2. Choose your avatar", "2. アバターを指定する"),
            ["gen.help2"] = ("Select your avatar in the Hierarchy and press the button to write its prefab, face mesh and menu to the settings.", "Hierarchy でアバターを選んで押すと、設定に prefab・顔のメッシュ・メニューを書きます。"),
            ["gen.step3"] = ("3. Assign blendshapes to the face movements", "3. 顔の動きにシェイプキーを割り当てる"),
            ["gen.help3"] = ("In the settings editor, choose the blendshape for each face movement and check the face.", "設定エディタで、顔の動きごとに使うシェイプキーを選び、顔を見て確かめます。"),
            ["gen.step4"] = ("4. Check the settings", "4. 設定を確かめる"),
            ["gen.help4"] = ("Look for mistakes and gaps in the settings. Fix them until there are no findings.", "設定の誤りや抜けを調べます。指摘が無くなるまで直します。"),
            ["gen.step5"] = ("5. Add the menu toggles", "5. メニューにトグルを足す"),
            ["gen.help5"] = ("Add the FT on/off toggle and the AFK toggle to the avatar's menu. Type a submenu name when the menu is full.", "FT のオン・オフと AFK のトグルを、アバターのメニューに足します。メニューに空きが無ければサブメニューの名前を入れます。"),
            ["gen.step6"] = ("6. AFK animation (optional)", "6. AFK のアニメーション（任意）"),
            ["gen.help6"] = ("Choose the animations played while AFK. You can skip this.", "AFK のときに流すアニメーションを選びます。選ばなくても使えます。"),
            ["gen.step7"] = ("7. Attach to the avatar", "7. アバターに付ける"),
            ["gen.help7"] = ("Select your avatar in the Hierarchy and press the button to add the settings component. FT is made on Play and upload; the avatar itself is not changed.", "Hierarchy でアバターを選んで押すと、設定のコンポーネントを付けます。Play とアップロードのときに FT が作られ、アバター自体は書き換えません。"),
            ["gen.helpAdvanced"] = ("Generate writes controllers with FT and a Prefab Variant to files for checking. Scenario check plays a scenario file in Play Mode and records it.", "Generate は、FT を付けた controller と Prefab Variant をファイルに書き出して確かめるためのものです。Scenario check は、場面のファイルを Play Mode で流して記録します。"),
            ["help.presets"] = ("Try expressions that break easily. A button sets the sliders to its values.", "崩れやすい表情をボタンで試せます。押すとスライダーがその値になります。"),
            ["switches.title"] = ("Switching expressions", "値で切り替わる表情"),
            ["switches.auto"] = ("Auto (now: {0})", "Auto（今: {0}）"),
            ["device.notSent"] = (" (this device does not send it)", "（この機器は送らない）"),
            ["device.unused"] = ("Values no expression uses yet ({0})", "まだ使っている表情が無い値 ({0})"),
            ["overlap.again"] = ("Choose again: {0}", "{0} を選び直す"),
            ["overlap.notUse"] = ("Do not use {0}", "{0} を使わない"),
            ["help.thresholds"] = ("Where the input value has to reach for each face movement. Changes show in the graph and in Face view. If blinks do not close fully, raise the closing position (to 0.5 or so) so the eyes close even when the value does not reach 0. You can also change which face movements move at each position, and how much.", "入力の値がどこまで来たら、どの顔の動きになるかの位置です。位置を変えると、グラフと Face view に映ります。瞬きで目が閉じきらないときは、閉じる位置を上げる（0.5\u00A0など）と、値が 0\u00A0まで下がりきらなくても閉じます。位置ごとに、どの顔の動きをどれだけ動かすかも変えられます。"),
            ["help.tuneTry"] = ("Moving it changes the face in Face view.", "動かすと Face view の顔が変わります。"),
            ["help.smoothing"] = ("Time for the face to catch up 90% after the value changes suddenly. Shorter follows quick blinks; longer reduces small shakes.", "値が急に変わったとき、顔が 9\u00A0割まで追いつくまでの時間です。短いほど速い瞬きにも追いつき、長いほど顔の細かい震えが減ります。"),
            ["help.syncDelay"] = ("Others also see the delay of syncing.", "他の人の見え方には、さらに同期の間隔が加わります。"),
            ["tune.position"] = ("Position {0}", "Position {0}"),
            ["tune.try"] = ("Try {0}", "Try {0}"),
            ["tune.addPosition"] = ("+ Add a Position at Try ({0})", "+ Add a Position at Try ({0})"),
            ["tune.2d"] = ("Moves with {0} and {1}. Click or drag in the graph to move Try.", "{0}・{1} で動く。図の中をクリック・ドラッグすると Try が動きます。"),
            ["tip.removePosition"] = ("Remove this position", "この位置を消す"),
            ["tip.removePart"] = ("Remove this face movement", "この顔の動きを外す"),
            ["help.newPart"] = ("Write it as \"region.movement\". Regions: eye, mouth, brow, cheek, tongue and so on.", "「部位.動き」の形で。部位は eye・mouth・brow・cheek・tongue など。"),
            ["newPart.form"] = ("Use lowercase letters, digits and _, with one dot.", "英小文字・数字・_ で、. を 1 つ含めてください。"),
            ["newPart.used"] = ("This name is already used.", "この名前はもうあります。"),
            ["help.inputsTab"] = ("Check the values from the device that FT uses. Each synced value takes 8 bits; the whole avatar can sync up to 256 bits.", "機器が送る値のうち、FT で使う値に印を付けます。同期する値は 1 つ 8\u00A0bit で、アバター全体で 256\u00A0bit までです。"),
            ["inputs.breakdown"] = ("FT {0} bits + added {1} bits + avatar parameters {2} bits", "FT {0} bits + 追加 {1} bits + アバターのパラメータ {2} bits"),
            ["inputs.breakdownNoAvatar"] = ("FT {0} bits + added {1} bits + avatar parameters not counted", "FT {0} bits + 追加 {1} bits + アバターのパラメータは数えていません"),
            ["inputs.usedBy"] = ("Used by {0}", "{0} が使う"),
            ["inputs.through"] = (" (through the average {0})", "（{0} の平均）"),
            ["inputs.added"] = ("Added. No expression uses it yet.", "追加。まだどの表情も使っていません"),
            ["inputs.addedPrefix"] = ("Added. ", "追加。"),
            ["inputs.locked"] = ("Cannot uncheck: {0}", "外せません: {0}"),
            ["inputs.notSent"] = ("This device does not send it.", "この機器は送りません"),
            ["inputs.over"] = ("Over 256 bits: this cannot be saved. Uncheck values or make them Local.", "256 bit を超えているので保存できません。印を外すか Local にしてください。"),
            ["graph.help"] = ("Select a box to show its node in the Tuning tab of the settings editor. Drag from the circle right of a position and release on empty space to split it with 1D or 2D. Right-click a box to insert a box above it or to remove it.", "箱を選ぶと、設定エディタの Tuning にその節が出ます。位置の右の丸から線を引いて空いた所で離すと、その位置を 1D・2D で分けられます。箱を右クリックすると、上に箱を挟んだり消したりできます。"),
            ["tune.selectBox"] = ("Select a box in the expression graph.", "図のウィンドウで箱を選んでください。"),
            ["part.new"] = ("No expression uses it yet.", "まだどの表情も使っていません"),
            ["part.cannotRemove"] = ("An expression uses it, so it cannot be removed.", "使っている表情があるので消せません"),
            ["tune.switchNote"] = ("Used by {0}. States are in order; the first is the state to start in.", "{0} が使う。状態は上から順に、最初が始めの状態です。"),
            ["help.switches"] = ("Switches change expressions by values. Open one to edit its states and transitions. Unused ones can be removed.", "値で表情を切り替える仕組みです。開くと状態と移り方を直せます。使っていないものは消せます。"),
            ["switch.unused"] = ("Not used", "使っていません"),
            ["switch.cannotRemove"] = ("An expression uses it, so it cannot be removed.", "使っている表情があるので消せません"),
            ["tune.switchNoEdit"] = ("Switches cannot be changed in Tuning.", "切り替えは Tuning では変えられません。"),
            ["graph.pairNote"] = ("Makes {0}Left and {0}Right.", "{0}Left・{0}Right の 2 つになります。"),
            ["graph.newNote"] = ("Choose the input of the first box in the next list.", "最初の箱の入力は、次の一覧で選びます。"),
            ["graph.removeTree"] = ("Remove the expression \"{0}\"?", "表情 \"{0}\" を消します。"),
            ["graph.noTable"] = ("Open settings in the settings editor.", "設定エディタで設定を開いてください。"),
            ["tune.no1d"] = ("No expression moves with one value.", "1 つの値で動く表情がありません。"),
            ["tune.noSmoothing"] = ("No smoothing settings.", "smoothing の設定がありません。"),
            ["tune.local"] = ("Your view", "Your view"),
            ["tune.remote"] = ("Others' view", "Others' view"),
            ["tip.table"] = ("The settings file of the avatar (avatar.jsonc). It names the input and expression settings files.", "アバターの設定ファイル（avatar.jsonc）。入力と表情の設定ファイルを指します。"),
            ["tip.prefab"] = ("The avatar prefab. Its Avatar Descriptor gives the controllers.", "アバターの prefab。controller は Avatar Descriptor から取ります。"),
            ["tip.menu"] = ("The menu that gets the FT toggle and the AFK flag.", "FT のトグルと AFK のフラグを置くメニュー。"),
            ["tip.mesh"] = ("The face mesh. Face movements without their own mesh use it.", "顔のメッシュ。メッシュを指定していない顔の動きはこれを使います。"),
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
                default: return region;
            }
        }

        public static GUIContent Label(string label, string tipKey) => new GUIContent(label, Get(tipKey));
    }
}
