namespace Kmnk.FaceTrackingGenerator
{
    // The names the generated output uses: Animator parameters, layers, clips, Blendshapes Sets, the VRChat built-in
    // parameters it reads, and the output paths. Renaming one changes avatars that already use it (AGENTS.md
    // Versioning: names in the generated output). No Unity editor types, so the tests outside Unity compile it.
    public static class FtNames
    {
        // ---- parameters the generator adds
        public const string One = "FT/One";
        public const string Active = "FT/Active";
        public const string MouthActive = "FT/MouthActive";
        public const string InputPrefix = "FT/In/";
        public static string SwitchParam(string sm, string state) => "FT/Sw/" + sm + "/" + state;
        public static string MenuParam(string sm) => "FT/Menu/" + sm;
        public static string FaceMotion(string group) => "FT/Face/" + group;
        public static string DeviceWeight(string device) => DeviceLayer + "/" + device;
        public const string AfkPlaying = "AFK/Playing";

        // ---- layers (clips and states inside a layer are named "<layer>/<name>")
        public const string GateLayer = "FT/Gate";
        public const string DeviceLayer = "FT/Device";
        public const string FaceLayer = "FT/Face";
        public const string VisemesLayer = "FT/Visemes";
        public static string StateMachineLayer(string sm) => "FT/SM/" + sm;
        public static string SetsLayer(string group) => "FT/Sets/" + group;
        public const string AfkActionLayer = "AFK/Action";
        public const string AfkFaceLayer = "AFK/FX";
        public const string AfkFlagLayer = "AFK/Flag";

        // ---- Blendshapes Sets of the Face Tracking section ("FTG/neutral/<region>", "FTG/<sm>/<state>/<region>")
        public const string SetPrefix = "FTG/";
        // One VRC Animator Face Tracking Blendshapes Set behaviour holds at most this many sets.
        public const int MaxSetsPerBehavior = 64;

        // ---- VRChat's built-in parameters the generated layers read
        public const string VRMode = "VRMode";
        public const string UserFaceTracking = "UserFaceTracking";
        public const string Voice = "Voice";
        public const string VRCEmote = "VRCEmote";

        // ---- output paths
        public const string OutputRoot = "Assets/FaceTrackingGeneratorOutput/";
        public const string BuildFolder = "Build";   // the build-time output, inside the output folder
        public const string StateMenusFolder = "Menus";   // the state machines' submenus, inside the menu folder
        public static string Controller(string folder, string type) => $"{folder}/{type}.controller";
        public static string ExpressionParameters(string folder) => folder + "/ExpressionParameters.asset";
        public static string Variant(string folder, string avatar) => $"{folder}/{avatar}_FT.prefab";
    }
}
