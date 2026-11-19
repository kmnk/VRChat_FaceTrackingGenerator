using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kmnk.FaceTrackingGenerator
{
    // Parts whose target is a material Float or Range property of a renderer ("property" in the avatar table) instead of
    // a blendshape. A state's fixed amount a (0 to 100, times the part's scale) is written as min + (max - min) * a / 100,
    // where min and max are the Range's limits (0 and 1 for a Float), read from the first of the renderer's materials
    // whose shader has the property.
    public static class PropertyParts
    {
        public struct Found
        {
            public string Material;   // the material's name
            public float Min, Max;
        }

        static bool IsNumber(ShaderPropertyType t) => t == ShaderPropertyType.Float || t == ShaderPropertyType.Range;

        // The property on the renderer's materials, or null when none of them has it as a Float or Range.
        public static Found? Find(Renderer r, string property)
        {
            if (r == null || string.IsNullOrEmpty(property)) return null;
            foreach (var mat in r.sharedMaterials)
            {
                var sh = mat != null ? mat.shader : null;
                if (sh == null) continue;
                var i = sh.FindPropertyIndex(property);
                if (i < 0) continue;
                var t = sh.GetPropertyType(i);
                if (!IsNumber(t)) continue;
                var lim = t == ShaderPropertyType.Range ? sh.GetPropertyRangeLimits(i) : new Vector2(0, 1);
                return new Found { Material = mat.name, Min = lim.x, Max = lim.y };
            }
            return null;
        }

        // The Float and Range properties of the renderer's materials, by material in slot order and in the order the
        // shader declares them. A name already listed under an earlier material is not listed again. Null when there is
        // no renderer (the materials could not be read).
        public static List<(string material, List<string> properties)> List(Renderer r)
        {
            if (r == null) return null;
            var result = new List<(string, List<string>)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mat in r.sharedMaterials)
            {
                var sh = mat != null ? mat.shader : null;
                if (sh == null) continue;
                var props = new List<string>();
                for (int i = 0; i < sh.GetPropertyCount(); i++)
                    if (IsNumber(sh.GetPropertyType(i)) && seen.Add(sh.GetPropertyName(i))) props.Add(sh.GetPropertyName(i));
                if (props.Count > 0) result.Add((mat.name, props));
            }
            return result;
        }

        public static Renderer RendererAt(GameObject avatar, string path)
        {
            var t = avatar != null && path != null ? avatar.transform.Find(path) : null;
            return t != null ? t.GetComponent<SkinnedMeshRenderer>() : null;
        }

        // Sets PropertyMin and PropertyMax of the model's property parts from the avatar's renderers. A part whose
        // property is not found keeps 0 and 1 (StaticChecker reports it).
        public static void Resolve(TableModel m, GameObject avatar)
        {
            foreach (var t in m.Parts.Values.Where(p => p.IsProperty))
            {
                var f = Find(RendererAt(avatar, t.Mesh), t.Property);
                if (f == null) continue;
                t.PropertyMin = f.Value.Min;
                t.PropertyMax = f.Value.Max;
            }
        }

        // The value a fixed amount (0 to 100, scale already applied) writes to the part's property.
        public static float Value(PartTarget t, float amount) => t.PropertyMin + (t.PropertyMax - t.PropertyMin) * amount / 100f;
    }
}
