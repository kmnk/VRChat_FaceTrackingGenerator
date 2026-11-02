using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // Holds the three tables as text so that Unity's Undo can record edits of the table editor.
    public sealed class TableEditState : ScriptableObject
    {
        public string[] snapshot;
    }
}
