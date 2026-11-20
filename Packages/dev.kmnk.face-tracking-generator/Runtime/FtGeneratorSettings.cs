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
        // The manual that the Inspector's ? and the generator window's Manual open. The README's Use section for now;
        // a page made for the manual is planned, and then only this changes.
        public const string ManualUrl = "https://github.com/kmnk/VRChat_FaceTrackingGenerator#use";

        [Tooltip("Asset path of the avatar table (avatar.jsonc)")]
        public string avatarTablePath;
        [Tooltip("GUID of the avatar table; used first when the file has moved")]
        public string avatarTableGuid;
    }
}
