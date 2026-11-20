using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // Holds the two tables as text so that Unity's Undo can record edits of the table editor.
    public sealed class TableEditState : ScriptableObject
    {
        public string[] snapshot;
        // The blendshapes of the face movements that Missing took out of the avatar table (part -> JSON), so that
        // turning Missing off puts them back. Recorded with the tables, so that Undo restores them too.
        public string[] removedParts = new string[0];
        public string[] removedValues = new string[0];
    }
}
