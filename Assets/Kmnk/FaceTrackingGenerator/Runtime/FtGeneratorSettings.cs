using UnityEngine;
using VRC.SDKBase;

namespace Kmnk.FaceTrackingGenerator
{
    // Put on the avatar root to generate FT at build time (Play Mode and upload) with NDMF.
    // Points to the avatar table only; it is not uploaded.
    [DisallowMultipleComponent]
    [AddComponentMenu("Kmnk/Face Tracking Generator Settings")]
    public sealed class FtGeneratorSettings : MonoBehaviour, IEditorOnly
    {
        [Tooltip("Asset path of the avatar table (avatar.jsonc)")]
        public string avatarTablePath;
        [Tooltip("GUID of the avatar table; used first when the file has moved")]
        public string avatarTableGuid;
    }
}
