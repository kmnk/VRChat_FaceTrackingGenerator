using System.IO;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // Saves AFK clips into a copy of a table whose afkClips value has a comment with "{" (a hand-edited table), as
    // "Save AFK clips" does, and prints whether the table on disk is still valid JSON and whether it changed.
    //   scripts/unity-batch.sh <project> Kmnk.FaceTrackingGenerator.TableWriterRepro.CommentInValue -ftTable <avatar.jsonc>
    // Prints "[FTG] repro comment-in-value: wrote=<bool> valid=<bool> changed=<bool>". The copy is under
    // Assets/FTGReproTmp and is deleted at the end.
    public static class TableWriterRepro
    {
        public static void CommentInValue() => Cli.Run(() =>
        {
            const string dir = "Assets/FTGReproTmp";
            var src = CliArgs.Require("-ftTable");
            AssetDatabase.DeleteAsset(dir);
            Directory.CreateDirectory(dir);
            var table = dir + "/avatar.jsonc";
            File.Copy(src, table);
            File.Copy(Path.Combine(Path.GetDirectoryName(src), "expression.jsonc"), dir + "/expression.jsonc");
            var text = File.ReadAllText(table);
            var at = text.IndexOf("\"afkClips\": {");
            if (at < 0) throw new System.ArgumentException("the table has no afkClips object");
            at = text.IndexOf('\n', at) + 1;
            File.WriteAllText(table, text.Substring(0, at) + "    // clips of the AFK layer (written as { guid, path )\n" + text.Substring(at));
            AssetDatabase.Refresh();
            var before = File.ReadAllText(table);
            var wrote = true;
            try { TableWriter.SetAfkClips(table, new AnimationClip[6]); }
            catch (System.Exception e) { wrote = false; Debug.Log("[FTG] repro comment-in-value: error " + e.Message.Split('\n')[0]); }
            var after = File.ReadAllText(table);
            var valid = true;
            try { TableLoader.Read(table); } catch (TableLoader.TableException) { valid = false; }
            Debug.Log($"[FTG] repro comment-in-value: wrote={wrote.ToString().ToLower()} valid={valid.ToString().ToLower()} changed={(before != after).ToString().ToLower()}");
            AssetDatabase.DeleteAsset(dir);
            return 0;
        });
    }
}
