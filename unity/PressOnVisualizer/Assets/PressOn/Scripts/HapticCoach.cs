using UnityEngine;

namespace PressOn
{
    /// <summary>
    /// Turns pressure into haptic feedback on the ball.
    /// - Guard: if someone squeezes hard for too long, the ball tells them to ease off.
    /// - Breathing exercise: the ball pulses a slow breath to follow.
    /// Tune everything in the Inspector.
    /// </summary>
    public class HapticCoach : MonoBehaviour
    {
        public enum Mode { Off, Guard, Breathing }
        public Mode mode = Mode.Guard;

        [Header("Guard")]
        [Range(0f, 1f)] public float hardThreshold = 0.8f;
        [Tooltip("Seconds of hard squeezing before the ball reacts")]
        public float holdSeconds = 2.5f;
        [Tooltip("Minimum seconds between reminders")]
        public float cooldown = 8f;

        [Header("Breathing exercise")]
        [Tooltip("Seconds per breath; the ball's breathe pattern lasts about 4.4 s plus a pause")]
        public float breathEvery = 5.6f;
        [Range(0f, 1f)] public float intensity = 0.7f;

        public float HardFor { get; private set; }
        float _lastCue = -999f;
        float _nextBreath;

        void Update()
        {
            var client = PressOnClient.Instance;
            if (client == null || mode == Mode.Off) return;

            if (mode == Mode.Guard)
            {
                HardFor = client.Pressure >= hardThreshold ? HardFor + Time.deltaTime : 0f;
                if (HardFor >= holdSeconds && Time.time - _lastCue > cooldown)
                {
                    client.PlayHaptic(PressOnClient.HapticPattern.EaseOff, intensity);
                    _lastCue = Time.time;
                    HardFor = 0f;
                }
            }
            else if (mode == Mode.Breathing && Time.time >= _nextBreath)
            {
                client.PlayHaptic(PressOnClient.HapticPattern.Breathe, intensity);
                _nextBreath = Time.time + breathEvery;
            }
        }

        public void SetMode(Mode newMode)
        {
            mode = newMode;
            HardFor = 0f;
            _nextBreath = Time.time;
            if (newMode == Mode.Off && PressOnClient.Instance != null) PressOnClient.Instance.StopHaptics();
        }
    }
}
