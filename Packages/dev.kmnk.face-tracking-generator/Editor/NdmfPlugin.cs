using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

[assembly: ExportsPlugin(typeof(Kmnk.FaceTrackingGenerator.NdmfPlugin))]

namespace Kmnk.FaceTrackingGenerator
{
    // Build-time output (design.md "生成物の出し先"): runs the same generation as the asset output into
    // <output folder>/Build and points the build copy of the avatar at it. The originals are not changed.
    public sealed class NdmfPlugin : Plugin<NdmfPlugin>
    {
        public override string QualifiedName => "kmnk.face-tracking-generator";
        public override string DisplayName => "Face Tracking Generator";

        protected override void Configure()
        {
            InPhase(BuildPhase.Generating).Run("Generate face tracking", ctx =>
            {
                var settings = ctx.AvatarRootObject.GetComponent<FtGeneratorSettings>();
                if (settings == null) return;
                var path = !string.IsNullOrEmpty(settings.avatarTableGuid) ? AssetDatabase.GUIDToAssetPath(settings.avatarTableGuid) : null;
                if (string.IsNullOrEmpty(path)) path = settings.avatarTablePath;
                var model = TableLoader.Load(path);
                model.OutputFolder += "/Build";
                var r = Generator.Generate(model, writeVariant: false);
                var desc = ctx.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
                Generator.Assign(desc, r);
                foreach (var l in r.Log.Where(l => l.StartsWith("note:") || l.StartsWith("notice:"))) Debug.Log("[FTG] ndmf: " + l);
                Object.DestroyImmediate(settings);
                Debug.Log("[FTG] ndmf: generated face tracking for " + ctx.AvatarRootObject.name + " into " + model.OutputFolder);
            });
        }
    }
}
