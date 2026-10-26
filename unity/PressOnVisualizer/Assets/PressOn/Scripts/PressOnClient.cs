using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace PressOn
{
    /// <summary>One reading from the ball, as sent by bridge/press_on_bridge.py.</summary>
    [Serializable]
    public class PressOnReading
    {
        public float t;          // seconds since the bridge started
        public int raw;          // ADC reading
        public int smooth;       // smoothed ADC reading
        public float pressure;   // 0 = untouched, 1 = squeezed as hard as calibrated
        public int battery;      // percent (0 if unknown)
        public bool connected;
        public string source;    // "ble", "serial" or "sim"
        public int seq;
    }

    /// <summary>
    /// Receives the ball's readings from the bridge over UDP and sends commands back.
    /// Put one in the scene; everything else reads from <see cref="Instance"/>.
    /// </summary>
    public class PressOnClient : MonoBehaviour
    {
        public static PressOnClient Instance { get; private set; }

        [Header("Bridge")]
        [Tooltip("Port the bridge sends readings to")]
        public int listenPort = 9050;
        [Tooltip("Port the bridge listens on for commands")]
        public int commandPort = 9051;
        public string bridgeHost = "127.0.0.1";

        [Header("Smoothing on the Unity side")]
        [Range(0.01f, 1f)] public float responsiveness = 0.35f;

        [Header("Testing without the ball")]
        [Tooltip("When nothing arrives from the bridge, use the slider below instead")]
        public bool useManualWhenDisconnected = true;
        [Range(0f, 1f)] public float manualPressure = 0f;

        /// <summary>Smoothed pressure, 0..1. Read this every frame.</summary>
        public float Pressure { get; private set; }
        /// <summary>Latest value straight from the ball, 0..1.</summary>
        public float RawPressure { get; private set; }
        public bool IsConnected { get; private set; }
        public int Battery { get; private set; }
        public string Source { get; private set; } = "none";
        public PressOnReading Latest { get; private set; }

        /// <summary>Fires on the main thread for every reading.</summary>
        public event Action<PressOnReading> ReadingReceived;
        public event Action<bool> ConnectionChanged;

        UdpClient _receiver;
        UdpClient _sender;
        Thread _thread;
        volatile bool _running;
        readonly object _lock = new object();
        PressOnReading _pending;
        double _lastPacketTime = -999;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnEnable()
        {
            try
            {
                _receiver = new UdpClient(new IPEndPoint(IPAddress.Any, listenPort));
                _sender = new UdpClient();
                _running = true;
                _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "PressOn UDP" };
                _thread.Start();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Press-On] Could not listen on UDP {listenPort}: {e.Message}");
            }
        }

        void OnDisable()
        {
            _running = false;
            _receiver?.Close();
            _sender?.Close();
            _receiver = null;
            _sender = null;
        }

        void ReceiveLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = _receiver.Receive(ref any);
                    var reading = JsonUtility.FromJson<PressOnReading>(Encoding.UTF8.GetString(data));
                    lock (_lock) { _pending = reading; }
                }
                catch (SocketException) { /* closed on disable */ }
                catch (ObjectDisposedException) { break; }
                catch (Exception e) { Debug.LogWarning($"[Press-On] Bad packet: {e.Message}"); }
            }
        }

        void Update()
        {
            PressOnReading reading = null;
            lock (_lock)
            {
                if (_pending != null) { reading = _pending; _pending = null; }
            }

            if (reading != null)
            {
                _lastPacketTime = Time.realtimeSinceStartupAsDouble;
                Latest = reading;
                RawPressure = Mathf.Clamp01(reading.pressure);
                Battery = reading.battery;
                Source = reading.source;
                SetConnected(reading.connected);
                ReadingReceived?.Invoke(reading);
            }
            else if (Time.realtimeSinceStartupAsDouble - _lastPacketTime > 2.0)
            {
                SetConnected(false);
                if (useManualWhenDisconnected) { RawPressure = manualPressure; Source = "manual"; }
            }

            // Frame-rate independent smoothing
            float k = 1f - Mathf.Pow(1f - responsiveness, Time.deltaTime * 60f);
            Pressure = Mathf.Lerp(Pressure, RawPressure, k);
        }

        void SetConnected(bool value)
        {
            if (IsConnected == value) return;
            IsConnected = value;
            ConnectionChanged?.Invoke(value);
        }

        // -------------------------------------------------------------------
        // Commands (see docs/BLE_PROTOCOL.md for patterns)
        // -------------------------------------------------------------------
        public enum HapticPattern { Off = 0, Tick = 1, EaseOff = 2, PressMore = 3, Breathe = 4, Heartbeat = 5 }

        public void PlayHaptic(HapticPattern pattern, float intensity = 1f) =>
            Send($"{{\"cmd\":\"haptic\",\"pattern\":{(int)pattern},\"intensity\":{Mathf.RoundToInt(Mathf.Clamp01(intensity) * 255)}}}");

        public void SetHapticLevel(float strength) =>
            Send($"{{\"cmd\":\"haptic_level\",\"strength\":{Mathf.RoundToInt(Mathf.Clamp01(strength) * 255)}}}");

        public void StopHaptics() => Send("{\"cmd\":\"haptic_stop\"}");

        public void CalibrateRelaxed() => Send("{\"cmd\":\"calibrate\",\"which\":\"relaxed\"}");
        public void CalibrateSqueezed() => Send("{\"cmd\":\"calibrate\",\"which\":\"squeezed\"}");
        public void ResetCalibration() => Send("{\"cmd\":\"calibrate\",\"which\":\"reset\"}");

        void Send(string json)
        {
            if (_sender == null) return;
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            try { _sender.Send(bytes, bytes.Length, bridgeHost, commandPort); }
            catch (Exception e) { Debug.LogWarning($"[Press-On] Could not send command: {e.Message}"); }
        }
    }
}
