using System.Linq;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    public static partial class TableEditorChecks
    {
        // Prints what the Inputs tab shows for -ftTable and a device, applies its edits, and saves with -ftSave 1.
        //   -ftDevice <id>                 a file name in Templates/Devices without .jsonc (default: none, "Settings")
        //   -ftAddInput "<name>;..."       check rows of "Not in the settings" (names as written, "BrowInnerUp{side}")
        //   -ftRemoveInput "<name>;..."    uncheck rows of "In the settings"
        //   -ftTips 1                      prints the tooltips (in the -ftHelp language) and the rows without a description
        // Lines:
        //   [FTG] inputs in=<n> out=<n>
        //   [FTG] inputs in <name> | <display> | expression=<expression or -> added=<bool> locked=<bool> not-sent=<bool> | <note>
        //   [FTG] inputs out <name> | <display> | expression=<expression>
        //   [FTG] inputs add|remove <name>: done=<bool>
        //   [FTG] inputs saved <path>
        public static void Inputs() => Cli.Run(() =>
        {
            var help = CliArgs.Get("-ftHelp");
            EditorText.Override = help == "en" ? EditorText.Language.English : help == "off" ? EditorText.Language.Off : EditorText.Language.Japanese;
            var doc = TableDocument.Open(CliArgs.Require("-ftTable"));
            var deviceId = CliArgs.Get("-ftDevice");
            var device = deviceId == null ? null : Devices.Load().FirstOrDefault(d => d.Id == deviceId)
                ?? throw new TableLoader.TableException("no device " + deviceId);
            InputChoices.Result Rows()
            {
                var m = doc.Model();
                return InputChoices.Rows(doc.Json(TableDocument.Input), doc.Json(TableDocument.Expression), m, device,
                    doc.SavedJson(TableDocument.Input));
            }
            void Print(InputChoices.Result r)
            {
                Debug.Log($"[FTG] inputs in={r.InTable.Count} out={r.NotInTable.Count}");
                foreach (var row in r.InTable)
                    Debug.Log($"[FTG] inputs in {row.Name} | {row.Display} | expression={InputChoices.ExpressionText(row) ?? "-"} added={row.Added.ToString().ToLower()} locked={row.Locked.ToString().ToLower()} not-sent={row.NotSent.ToString().ToLower()} | {InputChoices.Note(row) ?? "-"}");
                foreach (var row in r.NotInTable)
                    Debug.Log($"[FTG] inputs out {row.Name} | {row.Display} | expression={InputChoices.ExpressionText(row) ?? "-"}");
            }
            Print(Rows());
            if (CliArgs.Get("-ftTips") == "1")
            {
                // The tooltips: every row, and every input of the model (the Preview sliders).
                string One(string t) => t == null ? "-" : t.Replace("\n", " / ");
                var r0 = Rows();
                var rows = r0.InTable.Concat(r0.NotInTable).ToList();
                foreach (var row in rows) Debug.Log($"[FTG] inputs tip row {row.Name}: {One(InputChoices.Tooltip(row))}");
                var m = doc.Model();
                foreach (var i in m.Inputs) Debug.Log($"[FTG] inputs tip input {i.Name}: {One(ParameterHelp.Tooltip(m, i))}");
                var missing = rows.Where(x => ParameterHelp.Describe(x.Sided && x.Keys.Count > 0 && x.Keys[0].Contains("Left") ? x.Keys[0].Replace("Left", "{side}") : x.Keys.FirstOrDefault()) == null).Select(x => x.Name)
                    .Concat(m.Inputs.Where(i => !i.IsTrack && ParameterHelp.Describe(i.Key) == null).Select(i => i.Name)).ToList();
                Debug.Log($"[FTG] inputs tips: rows={rows.Count} inputs={m.Inputs.Count} without-description={missing.Count} {string.Join(",", missing)}");
            }

            var changed = false;
            foreach (var name in (CliArgs.Get("-ftAddInput") ?? "").Split(';').Where(x => x != ""))
            {
                var row = Rows().NotInTable.FirstOrDefault(x => x.Name == name);
                if (row != null) InputChoices.Add(doc.Json(TableDocument.Input), row, device);
                Debug.Log($"[FTG] inputs add {name}: done={(row != null).ToString().ToLower()}");
                changed |= row != null;
            }
            foreach (var name in (CliArgs.Get("-ftRemoveInput") ?? "").Split(';').Where(x => x != ""))
            {
                var row = Rows().InTable.FirstOrDefault(x => x.Name == name);
                var done = row != null && InputChoices.Remove(doc.Json(TableDocument.Input), row);
                Debug.Log($"[FTG] inputs remove {name}: done={done.ToString().ToLower()}" + (row != null && row.Locked ? $" ({InputChoices.Note(row)})" : ""));
                changed |= done;
            }
            if (changed) Print(Rows());

            if (CliArgs.Get("-ftSave") == "1")
                foreach (var p in doc.Save()) Debug.Log($"[FTG] inputs saved {p}");
            return 0;
        });
    }
}
