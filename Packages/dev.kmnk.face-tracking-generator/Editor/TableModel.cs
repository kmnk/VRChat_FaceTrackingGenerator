using System;
using System.Collections.Generic;
using System.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // Resolved model of the two tables (expression, avatar). TableLoader builds it.

    public sealed class InputDef
    {
        public string Name;
        public string Key;              // Which value it is (e.g. v2/EyeLidLeft), to find its built-in expression
        public string Parameter;        // Animator parameter the generated layers read: FT/In/<Name> (Forward to Animator writes it)
        public string Expression;       // Custom expression from the tracking keys
        public bool ExpressionFromTable;  // true: the table's "expression"; false: built in (NativeInputs)
        // A tracking key used by the rows of neutral and the states (name "key:<key>"). Never
        // forwarded to the Animator. Used by the settings editor and FaceEval as the key's value.
        public bool IsTrack;
        public const string TrackPrefix = "key:";
    }

    public sealed class Condition
    {
        public string Input;
        public string Op;     // ">" or "<"
        public float Value;
    }

    // The two devices a table can write values for: "vr" and "desktop" (the same names as Measurements.Vr and
    // Measurements.Desktop). A value not written per device is the same on both.
    public static class Device
    {
        public const string Vr = "vr";
        public const string Desktop = "desktop";
        public static string Of(bool desktop) => desktop ? Desktop : Vr;
        public static string Other(string device) => device == Vr ? Desktop : Vr;
        public static bool IsName(string s) => s == Vr || s == Desktop;
    }

    public sealed class TransitionDef
    {
        public string From;
        public string To;
        public List<Condition> When = new List<Condition>();  // all must hold
        public float Duration;
        // null: on both devices; Device.Vr or Device.Desktop: only on that device (VRMode on the Animator).
        public string Device;
        public bool On(bool desktop) => Device == null || Device == Kmnk.FaceTrackingGenerator.Device.Of(desktop);
    }

    // A row of a Blendshapes Set: a tracking key moves a part (its blendshape) by key value x Strength x the part's
    // scale, or by its curve at the key value x the part's scale. Part null: "Don't move" (a state's override row that
    // takes the key out of the region).
    public sealed class TrackRow
    {
        public string Key;
        public string Part;
        // The strength on VR and on desktop (the same unless the table writes {"vr", "desktop"}). With a curve: its
        // highest point.
        public float StrengthVr = 1, StrengthDesktop = 1;
        // The curve on VR and on desktop ("curve"); null: the straight line from (0, 0) to (1, strength). The loader
        // keeps only curves that are not straight (a straight one becomes the strength).
        public CurveKeys CurveVr, CurveDesktop;
        public float Strength(bool desktop) => desktop ? StrengthDesktop : StrengthVr;
        public CurveKeys Curve(bool desktop) => desktop ? CurveDesktop : CurveVr;
        public bool StrengthByDevice => StrengthVr != StrengthDesktop || !CurveKeys.Same(CurveVr, CurveDesktop);
        public void SetStrength(float both) { StrengthVr = both; StrengthDesktop = both; }
    }

    // What a state does to a region: Off (the region's neutral set is 0) or Override (Rows over the neutral rows).
    public sealed class RegionDef
    {
        public bool Off;
        public List<TrackRow> Rows = new List<TrackRow>();
    }

    public sealed class StateDef
    {
        public string Name;
        public bool Menu;  // selectable from the menu
        // While its fixed amounts show, the voice's visemes do not move the mouth: on VR, on desktop (the same unless the
        // table writes "vr" or "desktop").
        public bool StopVisemesVr, StopVisemesDesktop;
        public bool StopVisemes(bool desktop) => desktop ? StopVisemesDesktop : StopVisemesVr;
        public bool StopVisemesAny => StopVisemesVr || StopVisemesDesktop;
        public bool StopVisemesByDevice => StopVisemesVr != StopVisemesDesktop;
        public SortedDictionary<string, RegionDef> Regions = new SortedDictionary<string, RegionDef>(System.StringComparer.Ordinal);
        public SortedDictionary<string, float> Fixed = new SortedDictionary<string, float>(System.StringComparer.Ordinal);  // part -> amount x scale
    }

    public sealed class StateMachineDef
    {
        public string Name;
        public List<StateDef> States = new List<StateDef>();  // first is the default state
        public List<TransitionDef> Transitions = new List<TransitionDef>();

        public StateDef State(string name) => States.Find(s => s.Name == name);
        public IEnumerable<StateDef> MenuStates => States.Where(s => s.Menu);
        // The value of FT/Menu/<name> that selects a state: 1, 2, ... in state order; 0 for a state not in the menu.
        public int MenuValue(string state)
        {
            var i = MenuStates.Select(s => s.Name).ToList().IndexOf(state);
            return i < 0 ? 0 : i + 1;
        }
        public float MaxDuration => Transitions.Select(t => t.Duration).DefaultIfEmpty(0).Max();
    }

    public sealed class AssetRef
    {
        public string Guid;
        public string Path;
    }

    public sealed class PartTarget
    {
        public string Mesh;   // renderer path from the avatar root
        public string Shape;  // blendshape name (null for a material property part)
        public string Property;  // material Float or Range property name (null for a blendshape part)
        // The values amounts 0 and 100 give a property part: the Range's limits, or 0 and 1 for a Float
        // (PropertyParts.Resolve reads them from the renderer's materials before generating).
        public float PropertyMin = 0, PropertyMax = 1;
        public float Scale = 1;  // multiplies the expression layer's amounts for this avatar (0 to 1)
        public bool IsProperty => Property != null;
        // The name a part's target goes by in lists of the face (FaceEval, Cli.Curve): "<mesh>/<blendshape>", or
        // "<mesh>/material.<property>".
        public string Key => Mesh + "/" + (IsProperty ? "material." + Property : Shape);
    }

    public sealed class AfkGroup
    {
        public List<string> Parameters = new List<string>();
        public float Wait;
    }

    public sealed class LayerAdjust
    {
        public List<string> Remove = new List<string>();
        public SortedDictionary<string, float> Weights = new SortedDictionary<string, float>(System.StringComparer.Ordinal);
        public List<string> StopWhileFaceTrack = new List<string>();
        // Layers whose weight is 0 while FT is on (set again each time FT turns on) and back to their default
        // weight while FT is off or AFK. For layers that animate the face in their default state.
        public List<string> MuteWhileFaceTrack = new List<string>();
    }

    public sealed class AfkClips
    {
        public AssetRef ActionIntro, ActionLoop, ActionOutro, FxIntro, FxLoop, FxOutro;
    }

    public sealed class TableModel
    {
        // Avatar layer
        public string AvatarName;
        public string AvatarTablePath;
        public AssetRef Prefab;
        public string VariantPath;
        public string OutputFolder;
        // The folder of the state machines' submenus (Menus/<name>.asset): the output folder (Build/ for the build-time
        // output, so that a build does not write the asset output).
        public string MenuFolder;
        // Where the asset output keeps them. The build-time output does not add a state submenu again when the avatar's
        // own menu already has this one (the "Add toggles to menu" button of 0.16.x and earlier wrote it there).
        public string AssetMenuFolder;
        public string DefaultMesh;
        public SortedDictionary<string, PartTarget> Parts = new SortedDictionary<string, PartTarget>(System.StringComparer.Ordinal);
        public string FaceTrackParameter = "FaceTrack";
        public string AfkPlayParameter = "AFKPlay";
        public string FaceTrackLabel = "FaceTrack";   // names of the toggles in the menu
        public string AfkPlayLabel = "AFK";
        public List<AfkGroup> AfkGroups = new List<AfkGroup>();
        // Face Tracking section: replace the avatar's own values (Blendshapes Sets, blendshape Parameter Behaviors,
        // Eye Look on Face Tracking) only when true.
        public bool OverwriteFaceTracking;
        // Eye Look's Eye Gaze Mode written with tracking parts: "FaceTracking", "Both" or "BoneRotation".
        public string EyeGazeMode = "FaceTracking";
        public SortedDictionary<string, LayerAdjust> Layers = new SortedDictionary<string, LayerAdjust>(System.StringComparer.Ordinal);  // "fx", "gesture", "action"
        public AssetRef Menu;
        public string SubMenu;   // when set, the toggles go into a submenu of this name inside Menu
        public AfkClips AfkClips = new AfkClips();
        // ft-measure recordings assigned to VR and desktop ("measurements"); empty when there are none.
        public Measurements Measurements = new Measurements();

        // Input layer
        public List<InputDef> Inputs = new List<InputDef>();

        // Expression layer
        public List<string> StopWhileTalking = new List<string>();
        // region -> rows, in table order. Parts not set in the avatar layer are left out (R23).
        public SortedDictionary<string, List<TrackRow>> Neutral = new SortedDictionary<string, List<TrackRow>>(System.StringComparer.Ordinal);
        public List<StateMachineDef> StateMachines = new List<StateMachineDef>();

        // What the loader dropped and why (R23), for reports
        public List<string> Dropped = new List<string>();
        // Face movements the expression layer uses that have no blendshape or property in the avatar layer (Not set).
        public SortedSet<string> UnsetParts = new SortedSet<string>(StringComparer.Ordinal);
        // Notes for the generation log that are not problems (e.g. a table key that is no longer used)
        public List<string> Notes = new List<string>();
        // Problems found while loading that do not stop loading
        public List<string> Warnings = new List<string>();
        // Material property parts that neutral or override rows used (those rows are left out; StaticChecker reports them).
        public SortedSet<string> PropertyPartsInRows = new SortedSet<string>(System.StringComparer.Ordinal);

        public InputDef Input(string name) => Inputs.Find(i => i.Name == name);

        public IEnumerable<TrackRow> AllRows() =>
            Neutral.Values.SelectMany(r => r).Concat(StateMachines.SelectMany(sm => sm.States).SelectMany(st => st.Regions.Values.SelectMany(rg => rg.Rows)));
        // The rows with a curve that is not straight on a device (CurveKeys), with a part.
        public IEnumerable<TrackRow> CurvedRows() => AllRows().Where(r => r.Part != null && (r.CurveVr != null || r.CurveDesktop != null));
        // The blendshapes such rows move (by name, as Blendshapes Sets and Parameter Behaviors find them): every row on
        // them goes into a Parameter Behavior instead of a Blendshapes Set (design.md "どの blendshape を式で動かすか").
        public HashSet<string> CurvedShapes() => new HashSet<string>(CurvedRows()
            .Where(r => Parts.TryGetValue(r.Part, out var t) && !t.IsProperty).Select(r => Parts[r.Part].Shape), StringComparer.Ordinal);
        // Whether a row's strength or curve differs between the devices: the Blendshapes Sets are then one set of each per device.
        public bool SetsByDevice => AllRows().Any(r => r.StrengthByDevice);
        // Whether anything is written per device (transitions, strengths, stopVisemes): the FX then reads VRMode.
        public bool SplitsByDevice => SetsByDevice
            || StateMachines.Any(sm => sm.Transitions.Any(t => t.Device != null) || sm.States.Any(st => st.StopVisemesByDevice));
        public StateMachineDef StateMachine(string name) => StateMachines.Find(s => s.Name == name);

        // Every part the expression layer uses: neutral rows, override rows and fixed amounts.
        public IEnumerable<string> UsedParts() =>
            Neutral.Values.SelectMany(r => r).Select(r => r.Part)
                .Concat(StateMachines.SelectMany(sm => sm.States).SelectMany(st => st.Regions.Values.SelectMany(rg => rg.Rows).Select(r => r.Part).Concat(st.Fixed.Keys)))
                .Where(p => p != null).Distinct();
    }
}
