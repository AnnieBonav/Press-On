using UnityEngine;

namespace PressOn
{
    /// <summary>
    /// Therapist view: a scrolling line of the last few seconds of pressure,
    /// with a dashed-looking guide at the "hard squeeze" threshold.
    /// Draws in local space inside a width x height box, bottom-left at this transform.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class PressureTimeline : MonoBehaviour
    {
        public float windowSeconds = 20f;
        public int samples = 300;
        public float width = 6f;
        public float height = 2f;
        [Range(0f, 1f)] public float hardThreshold = 0.8f;
        public Color lineColor = new Color(0.80f, 0.22f, 0.40f);
        public Color guideColor = new Color(0.80f, 0.22f, 0.40f, 0.25f);

        LineRenderer _line;
        LineRenderer _guide;
        float[] _values;
        int _head;
        float _accumulator;

        void Awake()
        {
            _values = new float[samples];
            _line = GetComponent<LineRenderer>();
            Setup(_line, lineColor, 0.04f, samples);

            var guideGo = new GameObject("Hard squeeze guide");
            guideGo.transform.SetParent(transform, false);
            _guide = guideGo.AddComponent<LineRenderer>();
            Setup(_guide, guideColor, 0.02f, 2);
            _guide.SetPosition(0, new Vector3(0, hardThreshold * height, 0));
            _guide.SetPosition(1, new Vector3(width, hardThreshold * height, 0));
        }

        static void Setup(LineRenderer lr, Color color, float thickness, int count)
        {
            lr.useWorldSpace = false;
            lr.positionCount = count;
            lr.widthMultiplier = thickness;
            lr.numCapVertices = 4;
            if (lr.sharedMaterial == null) lr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = lr.endColor = color;
        }

        void Update()
        {
            var client = PressOnClient.Instance;
            float p = client != null ? client.Pressure : 0f;

            float step = windowSeconds / samples;
            _accumulator += Time.deltaTime;
            while (_accumulator >= step)
            {
                _accumulator -= step;
                _values[_head] = p;
                _head = (_head + 1) % samples;
            }

            for (int i = 0; i < samples; i++)
            {
                float v = _values[(_head + i) % samples];
                _line.SetPosition(i, new Vector3(i / (float)(samples - 1) * width, v * height, 0));
            }
        }
    }
}
