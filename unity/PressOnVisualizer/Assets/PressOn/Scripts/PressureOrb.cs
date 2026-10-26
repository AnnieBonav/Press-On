using UnityEngine;

namespace PressOn
{
    /// <summary>
    /// Patient view: a soft orb that squashes like the real ball, breathes slowly when relaxed,
    /// and shifts color with pressure. Color is never the only cue: shape and motion change too,
    /// so it still reads for people with color blindness.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class PressureOrb : MonoBehaviour
    {
        [Header("Shape")]
        [Tooltip("How much the orb flattens at full pressure")]
        [Range(0f, 0.6f)] public float squash = 0.35f;
        [Tooltip("Idle breathing size change")]
        [Range(0f, 0.15f)] public float breathAmount = 0.05f;
        [Tooltip("Seconds per breath when relaxed (around 5.5 s is a calm pace)")]
        public float breathPeriod = 5.5f;

        [Header("Color (relaxed to squeezed)")]
        public Gradient colors = DefaultGradient();

        [Header("Wobble when squeezed hard")]
        public float wobbleAbove = 0.8f;
        public float wobbleAmount = 0.03f;

        Renderer _renderer;
        MaterialPropertyBlock _block;
        Vector3 _baseScale;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP
        static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in

        void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _block = new MaterialPropertyBlock();
            _baseScale = transform.localScale;
        }

        void Update()
        {
            var client = PressOnClient.Instance;
            float p = client != null ? client.Pressure : 0f;

            // Breathing fades out as pressure goes up
            float breath = Mathf.Sin(Time.time * 2f * Mathf.PI / Mathf.Max(0.5f, breathPeriod)) * breathAmount * (1f - p);

            // Squash: flatter and wider, keeping roughly the same volume
            float y = 1f - squash * p;
            float xz = 1f / Mathf.Sqrt(Mathf.Max(0.2f, y));

            float wobble = p > wobbleAbove ? Mathf.Sin(Time.time * 40f) * wobbleAmount * Mathf.InverseLerp(wobbleAbove, 1f, p) : 0f;

            transform.localScale = new Vector3(
                _baseScale.x * (xz + breath + wobble),
                _baseScale.y * (y + breath),
                _baseScale.z * (xz + breath - wobble));

            Color c = colors.Evaluate(p);
            _renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, c);
            _block.SetColor(ColorId, c);
            _renderer.SetPropertyBlock(_block);
        }

        static Gradient DefaultGradient()
        {
            // Calm teal -> Press-On yellow -> raspberry
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.36f, 0.78f, 0.74f), 0f),
                    new GradientColorKey(new Color(1.00f, 0.72f, 0.10f), 0.55f),
                    new GradientColorKey(new Color(0.80f, 0.22f, 0.40f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}
