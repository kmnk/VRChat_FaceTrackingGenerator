using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // Shows that -executeMethod reaches a type in the checks package (a second embedded package).
    public static class ChecksProbe
    {
        public static void Run()
        {
            Debug.Log("[FTG] checks: ok");
            UnityEditor.EditorApplication.Exit(0);
        }
    }
}
