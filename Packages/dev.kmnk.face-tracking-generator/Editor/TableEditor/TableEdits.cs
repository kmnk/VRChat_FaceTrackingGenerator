using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Kmnk.FaceTrackingGenerator
{
    // Edits of the avatar table (JSON tree) made by the table editor window and by TableEditorChecks.Edit.
    // Keys and lists the editor does not show are left as they are.
    public static class TableEdits
    {
        public static readonly string[] Controllers = { "fx", "gesture", "action" };
        public static readonly string[] LayerLists = { "remove", "stopWhileFaceTrack", "muteWhileFaceTrack" };

        // ---- parts

        // The blendshape of a part, or null when the part has none. A part is "shape" or {"mesh", "shape"}.
        public static string PartShape(JObject avatar, string part)
        {
            var v = (avatar["parts"] as JObject)?[part];
            if (v == null) return null;
            if (v.Type == JTokenType.String) return (string)v;
            return v is JObject o && o["shape"]?.Type == JTokenType.String ? (string)o["shape"] : null;
        }

        // The mesh of a part when it names its own ({"mesh", "shape"} with "mesh"), else null.
        public static string PartMesh(JObject avatar, string part)
        {
            var v = (avatar["parts"] as JObject)?[part] as JObject;
            return v?["mesh"]?.Type == JTokenType.String ? (string)v["mesh"] : null;
        }

        // Sets the blendshape of a part; null removes the part from "parts". {"mesh", "shape"} keeps its mesh.
        public static void SetPart(JObject avatar, string part, string shape)
        {
            var parts = avatar["parts"] as JObject;
            if (shape == null)
            {
                parts?.Remove(part);
                return;
            }
            if (parts == null) avatar["parts"] = parts = new JObject();
            if (parts[part] is JObject o) o["shape"] = shape;
            else parts[part] = shape;
        }

        // The scale of a part ({"shape", "scale"}), or 1 when it has none.
        public static float PartScale(JObject avatar, string part)
        {
            var v = ((avatar["parts"] as JObject)?[part] as JObject)?["scale"];
            return v != null && (v.Type == JTokenType.Float || v.Type == JTokenType.Integer) ? (float)v : 1f;
        }

        // Sets the scale of a part that has a blendshape, clamped to 0..1. "shape" becomes {"shape", "scale"};
        // 1 removes "scale", and {"shape"} alone goes back to "shape".
        public static void SetPartScale(JObject avatar, string part, float scale)
        {
            var parts = avatar["parts"] as JObject;
            var v = parts?[part];
            if (v == null) return;
            scale = float.IsNaN(scale) ? 1f : Math.Max(0f, Math.Min(1f, scale));
            if (v.Type == JTokenType.String)
            {
                if (scale == 1f) return;
                parts[part] = v = new JObject { ["shape"] = (string)v };
            }
            if (!(v is JObject o)) return;
            if (scale == 1f)
            {
                o.Remove("scale");
                if (o.Count == 1 && o["shape"]?.Type == JTokenType.String) parts[part] = (string)o["shape"];
            }
            else o["scale"] = Math.Round((double)scale, 4);
        }

        // Removes a part from the avatar table ("parts" and "missingParts").
        public static void RemovePart(JObject avatar, string part)
        {
            (avatar["parts"] as JObject)?.Remove(part);
            if (avatar["missingParts"] is JArray list)
                foreach (var x in list.Where(x => x.Type == JTokenType.String && (string)x == part).ToList()) x.Remove();
        }

        public static bool IsMissing(JObject avatar, string part) =>
            avatar["missingParts"] is JArray a && a.Any(x => x.Type == JTokenType.String && (string)x == part);

        // Marks a part as missing: adds it to "missingParts" and removes it from "parts", because the generator
        // uses a part that is in "parts" even when it is also in "missingParts". The removed value goes to
        // `removed` so that unmarking can put it back.
        public static void SetMissing(JObject avatar, string part, bool missing, IDictionary<string, JToken> removed = null)
        {
            var list = avatar["missingParts"] as JArray;
            if (missing)
            {
                if (list == null) avatar["missingParts"] = list = new JArray();
                if (!IsMissing(avatar, part)) list.Add(part);
                if ((avatar["parts"] as JObject)?[part] is JToken old)
                {
                    if (removed != null) removed[part] = old.DeepClone();
                    ((JObject)avatar["parts"]).Remove(part);
                }
            }
            else
            {
                if (list != null)
                    foreach (var x in list.Where(x => x.Type == JTokenType.String && (string)x == part).ToList()) x.Remove();
                if (removed != null && removed.TryGetValue(part, out var back) && (avatar["parts"] as JObject)?[part] == null)
                {
                    if (!(avatar["parts"] is JObject)) avatar["parts"] = new JObject();
                    avatar["parts"][part] = back;
                    removed.Remove(part);
                }
            }
        }

        // ---- layers

        public static bool HasLayer(JObject avatar, string controller, string list, string layer) =>
            avatar["layers"]?[controller]?[list] is JArray a && a.Any(x => x.Type == JTokenType.String && (string)x == layer);

        public static void SetLayer(JObject avatar, string controller, string list, string layer, bool on)
        {
            if (!Controllers.Contains(controller)) throw new ArgumentException("controller must be fx, gesture or action: " + controller);
            if (!LayerLists.Contains(list)) throw new ArgumentException("unknown layer list: " + list);
            if (list == "muteWhileFaceTrack" && controller != "fx") throw new ArgumentException("muteWhileFaceTrack is supported only for fx");
            if (on)
            {
                if (!(avatar["layers"] is JObject layers)) avatar["layers"] = layers = new JObject();
                if (!(layers[controller] is JObject c)) layers[controller] = c = new JObject();
                if (!(c[list] is JArray a)) c[list] = a = new JArray();
                if (!HasLayer(avatar, controller, list, layer)) a.Add(layer);
            }
            else if (avatar["layers"]?[controller]?[list] is JArray a)
                foreach (var x in a.Where(x => x.Type == JTokenType.String && (string)x == layer).ToList()) x.Remove();
        }

        // ---- other keys

        public static void SetAssetRef(JObject avatar, string key, string guid, string path)
        {
            if (string.IsNullOrEmpty(path)) avatar[key] = JValue.CreateNull();
            else avatar[key] = new JObject { ["guid"] = guid ?? "", ["path"] = path };
        }

        public static void SetString(JObject avatar, string key, string value) => avatar[key] = value;

        public static void SetBool(JObject avatar, string key, bool value) => avatar[key] = value;

        // ---- operations as JSON (TableEditorChecks.Edit)
        //   {"op": "setPart", "part": "eye.close_L", "shape": "..."}          shape null removes the part
        //   {"op": "setPartScale", "part": "mouth.smile", "scale": 0.7}         1 removes the scale
        //   {"op": "setMissing", "part": "eye.happy", "missing": true}
        //   {"op": "setLayer", "controller": "fx", "list": "stopWhileFaceTrack", "layer": "Left Hand", "on": true}
        //   {"op": "setString", "key": "mesh", "value": "Body"}
        //   {"op": "setBool", "key": "stopAutoEyes", "value": true}
        //   {"op": "setAssetRef", "key": "prefab", "guid": "...", "path": "Assets/..."}
        public static void Apply(JObject avatar, JObject op, IDictionary<string, JToken> removed = null)
        {
            var name = (string)op["op"];
            switch (name)
            {
                case "setPart": SetPart(avatar, (string)op["part"], op["shape"]?.Type == JTokenType.String ? (string)op["shape"] : null); break;
                case "setPartScale": SetPartScale(avatar, (string)op["part"], (float)op["scale"]); break;
                case "setMissing": SetMissing(avatar, (string)op["part"], (bool)op["missing"], removed); break;
                case "setLayer": SetLayer(avatar, (string)op["controller"], (string)op["list"], (string)op["layer"], (bool)op["on"]); break;
                case "setString": SetString(avatar, (string)op["key"], (string)op["value"]); break;
                case "setBool": SetBool(avatar, (string)op["key"], (bool)op["value"]); break;
                case "setAssetRef": SetAssetRef(avatar, (string)op["key"], (string)op["guid"], (string)op["path"]); break;
                default: throw new ArgumentException("unknown op: " + name);
            }
        }
    }
}
