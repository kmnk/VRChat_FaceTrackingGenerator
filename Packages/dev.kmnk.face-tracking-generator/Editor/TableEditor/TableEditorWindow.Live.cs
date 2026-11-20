using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using UnityEditor;
using UnityEngine;

namespace Kmnk.FaceTrackingGenerator
{
    // The Preview's Live input: receives /tracking/face/v1 (LiveFaceReceiver) and puts the values into the inputs, so
    // that the sliders, the states, Weights, the avatar in the scene (while previewing) and Face View follow the face.
    public sealed partial class TableEditorWindow
    {
        const string LivePortPrefKey = "Kmnk.FaceTrackingGenerator.LivePort";
        const double LiveInterval = 0.05;  // seconds between two applies at most (spec: assumption)

        readonly LiveFaceReceiver live = new LiveFaceReceiver();
        int livePort = -1;
        long liveSeq;
        double liveAppliedAt, liveStartedAt, liveRepaintAt;
        int liveApplies;
        float[] liveValues;
        Dictionary<string, float> liveWeights;

        internal bool LiveRunning => live.Running;

        int LivePort
        {
            get { if (livePort < 0) livePort = EditorPrefs.GetInt(LivePortPrefKey, 9000); return livePort; }
            set => livePort = value;
        }

        void StartLive()
        {
            var port = Math.Max(1, Math.Min(65535, LivePort));
            LivePort = port;
            EditorPrefs.SetInt(LivePortPrefKey, port);
            EditorApplication.update -= LiveTick;
            if (live.Start(port))
            {
                liveSeq = 0;
                liveApplies = 0;
                liveValues = null;
                liveWeights = null;
                liveStartedAt = EditorApplication.timeSinceStartup;
                EditorApplication.update += LiveTick;
            }
            Repaint();
        }

        void StopLive()
        {
            EditorApplication.update -= LiveTick;
            if (live.Running) live.Stop();
            Repaint();
        }

        void LiveTick()
        {
            if (!live.Running) { EditorApplication.update -= LiveTick; return; }
            var now = EditorApplication.timeSinceStartup;
            if (now - liveAppliedAt >= LiveInterval)
            {
                var (values, seq) = live.Latest();
                if (seq != liveSeq && values != null)
                {
                    liveSeq = seq;
                    liveAppliedAt = now;
                    ApplyLive(values);
                    Repaint();
                    return;
                }
            }
            if (now - liveRepaintAt > 0.25) { liveRepaintAt = now; Repaint(); }  // the status line
        }

        void ApplyLive(float[] values)
        {
            liveValues = values;
            liveApplies++;
            foreach (var kv in OscFace.Inputs(values)) inputs[kv.Key] = kv.Value;
            if (model == null) return;
            var states = switches.Update(model, inputs);
            liveWeights = new Dictionary<string, float>(FaceEval.Evaluate(model, inputs, states));
            if (preview.Running) ApplyPreview(states);
            else FaceView.Show(model, Prefab(), liveWeights, "Live input");
        }

        // The value of a device key (v2/<name> or a tracking key) from the received values, by its built-in expression.
        float LiveValueOf(string key)
        {
            if (liveValues == null) return 0;
            var expr = NativeInputs.Expression(key);
            if (expr == null) return 0;
            var names = NativeKeys.Names.ToList();
            float Of(string n) { var i = names.IndexOf(n); return i >= 0 && i < liveValues.Length ? liveValues[i] : 0; }
            return FtExpression.Parse(expr).Evaluate(Of);  // built-in expressions (NativeInputs) always parse
        }

        // Face View while receiving and not previewing shows the received face (instead of the chosen face movement).
        bool ShowLiveFace()
        {
            if (!live.Running || liveWeights == null || preview.Running || model == null) return false;
            FaceView.Show(model, Prefab(), liveWeights, "Live input");
            return true;
        }

        string LiveState => live.Running ? (live.Status().sinceLast > 1 ? "waiting" : "receiving") : live.Error != null ? "error" : "stopped";

        void DrawLiveInput()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Live input", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Port", GUILayout.ExpandWidth(false));
                using (new EditorGUI.DisabledScope(live.Running))
                    LivePort = EditorGUILayout.IntField(LivePort, GUILayout.Width(70));
                var running = live.Running;
                var press = GUILayout.Toggle(running, new GUIContent(running ? "Stop" : "Start", EditorText.Get("tip.live")), "Button", GUILayout.Width(110));
                if (press != running) { if (press) StartLive(); else StopLive(); }
                GUILayout.FlexibleSpace();
            }
            var (perSecond, _, ignored) = live.Status();
            var state = LiveState;
            string line;
            if (state == "error") line = string.Format(EditorText.Get("live.error"), live.Port, live.Error);
            else if (state == "receiving") line = string.Format(EditorText.Get("live.receiving"), live.Port, perSecond);
            else if (state == "waiting") line = string.Format(EditorText.Get("live.waiting"), live.Port);
            else line = EditorText.Get("live.stopped");
            if (ignored > 0) line += string.Format(EditorText.Get("live.ignored"), ignored);
            if (state == "error") EditorGUILayout.HelpBox(line, MessageType.Warning);
            else EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
        }

        // ---- TableEditorChecks.Capture: -ftLive <port> starts the Live input; the state is printed before the grab.

        void ApplyLiveCaptureArgs()
        {
            var port = CliArgs.Get("-ftLive");
            if (port == null) return;
            LivePort = int.Parse(port, CultureInfo.InvariantCulture);
            StartLive();
        }

        internal string LiveCaptureState()
        {
            var (perSecond, since, ignored) = live.Status();
            var seconds = live.Running ? EditorApplication.timeSinceStartup - liveStartedAt : 0;
            var vals = model == null ? "" : string.Join(",", model.Inputs.Where(i => i.IsTrack && inputs.TryGetValue(i.Name, out var v) && v != 0)
                .Select(i => i.Key + "=" + inputs[i.Name].ToString("0.###", CultureInfo.InvariantCulture)));
            var face = FaceView.Current?.Weights == null ? "" : string.Join(",", FaceView.Current.Weights.Where(kv => kv.Value > 0)
                .Select(kv => kv.Key + "=" + kv.Value.ToString("0.#", CultureInfo.InvariantCulture)));
            var unused = liveValues == null ? "" : $" unused-JawOpen={LiveValueOf("v2/JawOpen").ToString("0.###", CultureInfo.InvariantCulture)} unused-EyeLidLeft={LiveValueOf("v2/EyeLidLeft").ToString("0.###", CultureInfo.InvariantCulture)}";
            return $"state={LiveState} port={(live.Running || live.Error != null ? live.Port : LivePort)} messages={live.Latest().sequence} per-second={perSecond} ignored={ignored} applies={liveApplies} seconds={seconds.ToString("0.00", CultureInfo.InvariantCulture)} caption={FaceView.Current?.Caption} inputs={vals} faceview={face}{unused}" + (live.Error != null ? $" error={live.Error}" : "");
        }

        // -ftLiveEnd reload: as the Reload button (the same table is read again).
        internal void ReloadForCapture() => Load();

        internal static bool PortFree(int port)
        {
            try { using (new UdpClient(new IPEndPoint(IPAddress.Loopback, port))) return true; }
            catch (SocketException) { return false; }
        }
    }
}
