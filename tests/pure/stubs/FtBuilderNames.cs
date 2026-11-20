namespace Kmnk.FaceTrackingGenerator
{
    // The names NativeSets takes from FtBuilder, for compiling without Unity's animation editor types.
    // Remove when the names have their own file (R1).
    public sealed class FtBuilder
    {
        public const string Active = "FT/Active";
        public const string MouthActive = "FT/MouthActive";
        public static string SwitchParam(string sm, string state) => "FT/Sw/" + sm + "/" + state;
    }
}
