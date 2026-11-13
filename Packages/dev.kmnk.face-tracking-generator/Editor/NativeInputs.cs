using System.Collections.Generic;

namespace Kmnk.FaceTrackingGenerator
{
    // The built-in expressions of the inputs: how each value the device tables list (v2/<name>) is made from the
    // tracking keys of VRChat's Face Tracking section. A name that is a tracking key is that key. The others are
    // VRCFaceTracking's definitions of the same names, written as Custom expressions:
    //   https://github.com/benaclejames/VRCFaceTracking/blob/fc0b97b/VRCFaceTracking.Core/Params/Expressions/UnifiedExpressionsParameters.cs
    //   https://github.com/benaclejames/VRCFaceTracking/blob/fc0b97b/VRCFaceTracking.Core/Params/Data/UnifiedData.cs (gaze and eyelids in the keys)
    public static class NativeInputs
    {
        // The parameter that the generated Animator reads for an input, and that Forward to Animator writes.
        public const string ParameterPrefix = "FT/In/";
        public const int MaxParameterLength = 64;  // the SDK inspector's limit on output parameter names

        const string Smile = "(MouthCornerPullLeft + MouthCornerPullRight) * 0.4 + (MouthCornerSlantLeft + MouthCornerSlantRight) * 0.1";

        static readonly Dictionary<string, string> Formulas = new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            ["EyeLidLeft"] = "(1 - EyeClosedLeft) * 0.75 + EyeWideLeft * 0.25",
            ["EyeLidRight"] = "(1 - EyeClosedRight) * 0.75 + EyeWideRight * 0.25",
            ["EyeRightX"] = "EyeLookOutRight - EyeLookInRight",
            ["EyeLeftX"] = "EyeLookInLeft - EyeLookOutLeft",
            ["EyeLeftY"] = "EyeLookUpLeft - EyeLookDownLeft",
            ["EyeRightY"] = "EyeLookUpRight - EyeLookDownRight",
            ["SmileFrown"] = Smile + " - (MouthFrownLeft + MouthFrownRight) * 0.5",
            ["SmileSad"] = Smile + " - (max(MouthFrownLeft, MouthStretchLeft) + max(MouthFrownRight, MouthStretchRight)) * 0.5",
            ["MouthOpen"] = "(MouthUpperUpRight + MouthUpperUpLeft + MouthLowerDownRight + MouthLowerDownLeft) * 0.25",
            ["LipPucker"] = "(LipPuckerUpperRight + LipPuckerUpperLeft + LipPuckerLowerRight + LipPuckerLowerLeft) * 0.25",
            ["CheekPuffSuckLeft"] = "CheekPuffLeft - CheekSuckLeft",
            ["CheekPuffSuckRight"] = "CheekPuffRight - CheekSuckRight",
            ["TongueX"] = "TongueRight - TongueLeft",
            ["TongueY"] = "TongueUp - TongueDown",
        };

        // "v2/EyeLidLeft" -> "EyeLidLeft"
        public static string NameOf(string key) => key == null ? null : key.StartsWith("v2/") ? key.Substring(3) : key;

        // The built-in expression of a key, or null when there is none.
        public static string Expression(string key)
        {
            var name = NameOf(key);
            if (name == null) return null;
            if (Formulas.TryGetValue(name, out var f)) return f;
            return FtExpression.IsKey(name) ? name : null;
        }

        public static string Parameter(string inputName) => ParameterPrefix + inputName;

        // The names with a built-in expression that is not a single key.
        public static IEnumerable<string> FormulaNames => Formulas.Keys;

        // The names an expression uses, or none when it cannot be read.
        public static List<string> NamesIn(string expression)
        {
            if (expression == null) return new List<string>();
            try { return FtExpression.Parse(expression).Names; }
            catch (FtExpression.ParseException) { return new List<string>(); }
        }
    }
}
