using System.Collections.Generic;

namespace Kmnk.FaceTrackingGenerator
{
    // Resolved model of the three tables (input, expression, avatar). TableLoader builds it.

    public sealed class SmoothingDef
    {
        public string Name;
        public float LocalTau;   // seconds
        public float RemoteTau;  // seconds
    }

    public sealed class InputDef
    {
        public string Name;
        public string Key;              // Animator parameter sent by the device (e.g. a v2/* key). Null for derived inputs
        public string Smoothing;        // SmoothingDef name, or null for no smoothing
        public string Sync;             // "none" or "float8"
        public List<string> Average;    // derived input: average of these inputs
        public bool IsDerived => Average != null;
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

    public sealed class SwitchDef
    {
        public string Name;
        public List<string> States = new List<string>();  // first is the default state
        public List<TransitionDef> Transitions = new List<TransitionDef>();
    }

    public abstract class Node { }

    // Leaf: part name -> value (0-100). An empty pose writes nothing.
    public sealed class PoseNode : Node
    {
        public SortedDictionary<string, float> Parts = new SortedDictionary<string, float>(System.StringComparer.Ordinal);
    }

    public sealed class Blend1DNode : Node
    {
        public string Input;
        public List<(float threshold, Node node)> Children = new List<(float, Node)>();
    }

    // 2D Freeform Cartesian
    public sealed class Blend2DNode : Node
    {
        public string InputX, InputY;
        public List<(float x, float y, Node node)> Children = new List<(float, float, Node)>();
    }

    public sealed class SwitchNode : Node
    {
        public string Switch;
        public List<(string state, Node node)> Cases = new List<(string, Node)>();
    }

    public sealed class TreeDef
    {
        public string Name;
        public Node Root;
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
        public string DefaultMesh;
        public SortedDictionary<string, PartTarget> Parts = new SortedDictionary<string, PartTarget>(System.StringComparer.Ordinal);
        public string Gaze = "shared";
        public string FaceTrackParameter = "FaceTrack";
        public string AfkPlayParameter = "AFKPlay";
        public List<AfkGroup> AfkGroups = new List<AfkGroup>();
        public bool StopAutoEyes;
        public SortedDictionary<string, LayerAdjust> Layers = new SortedDictionary<string, LayerAdjust>(System.StringComparer.Ordinal);  // "fx", "gesture", "action"
        public AssetRef Menu;
        public string SubMenu;   // when set, the toggles go into a submenu of this name inside Menu
        public AfkClips AfkClips = new AfkClips();

        // Input layer
        public List<SmoothingDef> Smoothings = new List<SmoothingDef>();
        public List<InputDef> Inputs = new List<InputDef>();

        // Expression layer
        public List<string> StopWhileTalking = new List<string>();
        public List<SwitchDef> Switches = new List<SwitchDef>();
        public List<TreeDef> Trees = new List<TreeDef>();

        // What the loader dropped and why (R23), for reports
        public List<string> Dropped = new List<string>();
        // Problems found while loading that do not stop loading
        public List<string> Warnings = new List<string>();

        public InputDef Input(string name) => Inputs.Find(i => i.Name == name);
        public SwitchDef Switch(string name) => Switches.Find(s => s.Name == name);
    }
}
