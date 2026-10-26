using UnityEngine;

namespace PressOn
{
    /// <summary>
    /// Quick on-screen panel (no UI package needed): connection, live value,
    /// calibration, haptics, coach mode and recording. Replace with your real UI later.
    /// Toggle with the "show" checkbox in the Inspector.
    /// </summary>
    public class PressOnDebugPanel : MonoBehaviour
    {
        public bool show = true;
        public HapticCoach coach;
        public SessionRecorder recorder;

        GUIStyle _box, _title, _big;

        void OnGUI()
        {
            if (!show) return;
            var client = PressOnClient.Instance;
            if (client == null) return;

            if (_box == null)
            {
                _box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(12, 12, 10, 10) };
                _title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                _big = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold };
            }

            GUILayout.BeginArea(new Rect(16, 16, 300, Screen.height - 32), _box);
            GUILayout.Label("Press-On", _title);

            string status = client.IsConnected ? $"Connected ({client.Source})" : "Waiting for the bridge…";
            GUILayout.Label(status);
            if (client.IsConnected && client.Battery > 0) GUILayout.Label($"Battery {client.Battery}%");
            GUILayout.Label($"{Mathf.RoundToInt(client.Pressure * 100)}%", _big);

            if (!client.IsConnected)
            {
                GUILayout.Label("Manual pressure (testing)");
                client.manualPressure = GUILayout.HorizontalSlider(client.manualPressure, 0f, 1f);
                GUILayout.Space(8);
            }

            GUILayout.Space(6);
            GUILayout.Label("Calibrate");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Relaxed")) client.CalibrateRelaxed();
            if (GUILayout.Button("Squeezed")) client.CalibrateSqueezed();
            if (GUILayout.Button("Reset")) client.ResetCalibration();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Haptics");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Tick")) client.PlayHaptic(PressOnClient.HapticPattern.Tick);
            if (GUILayout.Button("Ease off")) client.PlayHaptic(PressOnClient.HapticPattern.EaseOff);
            if (GUILayout.Button("Breathe")) client.PlayHaptic(PressOnClient.HapticPattern.Breathe);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Stop haptics")) client.StopHaptics();

            if (coach != null)
            {
                GUILayout.Space(6);
                GUILayout.Label($"Coach: {coach.mode}");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Off")) coach.SetMode(HapticCoach.Mode.Off);
                if (GUILayout.Button("Guard")) coach.SetMode(HapticCoach.Mode.Guard);
                if (GUILayout.Button("Breathing")) coach.SetMode(HapticCoach.Mode.Breathing);
                GUILayout.EndHorizontal();
            }

            if (recorder != null)
            {
                GUILayout.Space(6);
                if (!recorder.IsRecording)
                {
                    if (GUILayout.Button("● Record session")) recorder.StartRecording();
                }
                else
                {
                    if (GUILayout.Button($"■ Stop ({recorder.Elapsed:0}s)")) recorder.StopRecording();
                }
            }
            GUILayout.EndArea();
        }
    }
}
