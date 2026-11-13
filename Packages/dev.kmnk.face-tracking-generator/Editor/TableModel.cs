using System.Collections.Generic;
using System.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // Resolved model of the three tables (input, expression, avatar). TableLoader builds it.

    public sealed class InputDef
    {
        public string Name;
        public string Key;              // Which value it is (e.g. v2/EyeLidLeft), to find its built-in expression
        public string Parameter;        // Animator parameter the generated layers read: FT/In/<Name> (Forward to Animator writes it)
        public string Expression;       // Custom expression from the tracking keys
        public bool ExpressionFromTable;  // true: the table's "expression"; false: built in (NativeInputs)
        public string Sync;             // "none" or "float8" as written in the table. Not used: inputs come from the Face Tracking section and are not synced
        // A tracking key used by the rows of neutral and the states (name "key:<key>"). Not in the input table; never
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

    public sealed class TransitionDef
    {
        public string From;
        public string To;
        public List<Condition> When = new List<Condition>();  // all must hold
        public float Duration;
    }

    // A row of a Blendshapes Set: a tracking key moves a part (its blendshape) by key value x Strength x the part's
    // scale. Part null: "Don't move" (a state's override row that takes the key out of the region).
    public sealed class TrackRow
    {
        public string Key;
        public string Part;
        public float Strength = 1;
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
        public string Shape;  // blendshape name
        public float Scale = 1;  // multiplies the expression layer's amounts for this avatar (0 to 1)
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
        // The folder of the state machines' submenus (Menus/<name>.asset). The build-time output changes OutputFolder
        // but not this, so that the avatar's menu points to one asset.
        public string MenuFolder;
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

        // Input layer
        public List<InputDef> Inputs = new List<InputDef>();

        // Expression layer
        public List<string> StopWhileTalking = new List<string>();
        // region -> rows, in table order. Parts not set in the avatar layer are left out (R23).
        public SortedDictionary<string, List<TrackRow>> Neutral = new SortedDictionary<string, List<TrackRow>>(System.StringComparer.Ordinal);
        public List<StateMachineDef> StateMachines = new List<StateMachineDef>();

        // What the loader dropped and why (R23), for reports
        public List<string> Dropped = new List<string>();
        // Notes for the generation log that are not problems (e.g. a table key that is no longer used)
        public List<string> Notes = new List<string>();
        // Problems found while loading that do not stop loading
        public List<string> Warnings = new List<string>();

        public InputDef Input(string name) => Inputs.Find(i => i.Name == name);
        public StateMachineDef StateMachine(string name) => StateMachines.Find(s => s.Name == name);

        // Every part the expression layer uses: neutral rows, override rows and fixed amounts.
        public IEnumerable<string> UsedParts() =>
            Neutral.Values.SelectMany(r => r).Select(r => r.Part)
                .Concat(StateMachines.SelectMany(sm => sm.States).SelectMany(st => st.Regions.Values.SelectMany(rg => rg.Rows).Select(r => r.Part).Concat(st.Fixed.Keys)))
                .Where(p => p != null).Distinct();
    }
}
