using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace PressOn
{
    /// <summary>
    /// Records a session to CSV (time, pressure, raw, source) so it can be reviewed later.
    /// Files go to Application.persistentDataPath/PressOnSessions.
    /// </summary>
    public class SessionRecorder : MonoBehaviour
    {
        public bool IsRecording { get; private set; }
        public string CurrentFile { get; private set; }
        public float Elapsed => IsRecording ? Time.realtimeSinceStartup - _startTime : 0f;

        StreamWriter _writer;
        float _startTime;

        void OnEnable()
        {
            if (PressOnClient.Instance != null) PressOnClient.Instance.ReadingReceived += OnReading;
        }

        void Start()
        {
            // In case the client woke up after us
            if (PressOnClient.Instance != null)
            {
                PressOnClient.Instance.ReadingReceived -= OnReading;
                PressOnClient.Instance.ReadingReceived += OnReading;
            }
        }

        void OnDisable()
        {
            if (PressOnClient.Instance != null) PressOnClient.Instance.ReadingReceived -= OnReading;
            StopRecording();
        }

        public void StartRecording()
        {
            if (IsRecording) return;
            string dir = Path.Combine(Application.persistentDataPath, "PressOnSessions");
            Directory.CreateDirectory(dir);
            CurrentFile = Path.Combine(dir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            _writer = new StreamWriter(CurrentFile, false, Encoding.UTF8);
            _writer.WriteLine("seconds,pressure,raw,source");
            _startTime = Time.realtimeSinceStartup;
            IsRecording = true;
            Debug.Log($"[Press-On] Recording to {CurrentFile}");
        }

        public void StopRecording()
        {
            if (!IsRecording) return;
            IsRecording = false;
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
            Debug.Log($"[Press-On] Saved {CurrentFile}");
        }

        void OnReading(PressOnReading r)
        {
            if (!IsRecording || _writer == null) return;
            float t = Time.realtimeSinceStartup - _startTime;
            _writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:F3},{1:F4},{2},{3}", t, r.pressure, r.raw, r.source));
        }
    }
}
