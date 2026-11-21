using UnityEngine;
using VRC.SDKBase;

namespace Kmnk.FaceTrackingGenerator
{
    // Put on the avatar root to generate FT at build time (Play Mode and upload) with NDMF.
    // Points to the avatar table only; it is not uploaded.
    [DisallowMultipleComponent]
    [AddComponentMenu("Kmnk/Face Tracking Generator Settings")]
    [HelpURL(ManualUrl)]
    public sealed class FtGeneratorSettings : MonoBehaviour, IEditorOnly
    {
        // The Japanese manual on GitHub (docs/manual-ja/README.md of main, which holds only released commits).
        // The Inspector's ? opens it; HelpURL takes only a constant, so it cannot point into the installed package.
        // The generator window's Manual opens the copy in the package first (ManualFiles.Open).
        public const string ManualUrl = "https://github.com/kmnk/VRChat_FaceTrackingGenerator/blob/main/docs/manual-ja/README.md";

        [Tooltip("Asset path of the avatar table (avatar.jsonc)")]
        public string avatarTablePath;
        [Tooltip("GUID of the avatar table; used first when the file has moved")]
        public string avatarTableGuid;
    }
}
