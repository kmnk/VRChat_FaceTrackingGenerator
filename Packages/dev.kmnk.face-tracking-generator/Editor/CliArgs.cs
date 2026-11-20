using System;
using System.Globalization;

namespace Kmnk.FaceTrackingGenerator
{
    // Reads "-name value" pairs from the Unity command line.
    static class CliArgs
    {
        // Arguments used in place of the command line (development checks that call several commands in one run).
        internal static string[] Override;

        public static string Get(string name, string def = null)
        {
            var a = Override ?? Environment.GetCommandLineArgs();
            var i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
        }

        public static string Require(string name)
        {
            var v = Get(name);
            if (v == null) throw new ArgumentException($"{name} is required");
            return v;
        }

        public static float GetFloat(string name, float def)
        {
            var v = Get(name);
            return v == null ? def : float.Parse(v, CultureInfo.InvariantCulture);
        }
    }
}
