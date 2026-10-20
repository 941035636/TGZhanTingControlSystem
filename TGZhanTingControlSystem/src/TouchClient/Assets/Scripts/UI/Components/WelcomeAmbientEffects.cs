using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Components
{
    /// <summary>
    /// Lightweight ambient presentation for the full-screen welcome page.
    /// A single pooled UGUI mesh is used instead of a world-space ParticleSystem because the
    /// application canvas is ScreenSpaceOverlay and the effect must stay between its background
    /// and interactive controls.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WelcomeAmbientEffects : MaskableGraphic
    {
        private const int GlobeParticleCount = 16;
        private const int StarCount = 24;

        [Header("Effect switches")]
        [SerializeField] private bool effectsEnabled = true;
        [SerializeField] private bool globeParticlesEnabled = true;
        [SerializeField] private bool baseGlowEnabled = true;
        [SerializeField] private bool starTwinkleEnabled = true;

        [Header("Global")]
        [SerializeField, Range(0f, 1.5f)] private float overallIntensity = .52f;

        [Header("Globe particles")]
        [SerializeField, Range(.1f, 2f)] private float globeParticleSpeed = .42f;
        [SerializeField, Range(.5f, 2f)] private float globeParticleSize = .78f;
        [SerializeField, Range(0f, 1f)] private float globeParticleOpacity = .44f;

        [Header("Base glow")]
        [SerializeField, Range(1f, 10f)] private float baseBreathSeconds = 5f;
        [SerializeField, Range(0f, 1f)] private float baseGlowOpacity = .24f;

        [Header("Stars")]
        [SerializeField, Range(.1f, 2f)] private float starTwinkleSpeed = .46f;
        [SerializeField, Range(.5f, 2f)] private float starSize = .72f;
        [SerializeField, Range(0f, 1f)] private float starOpacity = .34f;

        private Particle[] globeParticles;
        private Particle[] stars;
        private Image baseGlow;
        private Material runtimeMaterial;
        private float animationStartedAt;
        private bool initialized;

        public bool EffectsEnabled
        {
            get => effectsEnabled;
            set
            {
                if (effectsEnabled == value) return;
                effectsEnabled = value;
                RefreshVisualState();
            }
        }

        public bool GlobeParticlesEnabled
        {
            get => globeParticlesEnabled;
            set
            {
                if (globeParticlesEnabled == value) return;
                globeParticlesEnabled = value;
                SetVerticesDirty();
            }
        }

        public bool BaseGlowEnabled
        {
            get => baseGlowEnabled;
            set
            {
                if (baseGlowEnabled == value) return;
                baseGlowEnabled = value;
                RefreshBaseVisibility();
            }
        }

        public bool StarTwinkleEnabled
        {
            get => starTwinkleEnabled;
            set
            {
                if (starTwinkleEnabled == value) return;
                starTwinkleEnabled = value;
                SetVerticesDirty();
            }
        }

        public float OverallIntensity
        {
            get => overallIntensity;
            set => overallIntensity = Mathf.Clamp(value, 0f, 1.5f);
        }

        public void Initialize(Sprite glowSprite)
        {
            if (initialized) return;
            initialized = true;
            raycastTarget = false;
            color = Color.white;
            animationStartedAt = Time.unscaledTime;

            BuildParticleData();
            BuildMaterial();
            BuildBaseVisuals(glowSprite);
            RefreshVisualState();
            SetAllDirty();
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            animationStartedAt = Time.unscaledTime;
            SetVerticesDirty();
        }

        protected override void OnDestroy()
        {
            if (runtimeMaterial != null)
            {
                if (Application.isPlaying) Destroy(runtimeMaterial);
                else DestroyImmediate(runtimeMaterial);
                runtimeMaterial = null;
            }
            base.OnDestroy();
        }

        private void Update()
        {
            if (!initialized || !isActiveAndEnabled || !effectsEnabled) return;
            SetVerticesDirty();
            AnimateBaseGlow(Time.unscaledTime - animationStartedAt);
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            if (!initialized || !effectsEnabled) return;

            var elapsed = Time.unscaledTime - animationStartedAt;
            var area = rectTransform.rect;
            if (starTwinkleEnabled) PopulateStars(helper, area, elapsed);
            if (globeParticlesEnabled) PopulateGlobeParticles(helper, area, elapsed);
        }

        private void BuildParticleData()
        {
            globeParticles = new Particle[GlobeParticleCount];
            stars = new Particle[StarCount];
            var random = new System.Random(20261019);

            for (var index = 0; index < globeParticles.Length; index++)
            {
                var angle = Next(random, 0f, Mathf.PI * 2f);
                var radius = Next(random, .98f, 1.18f);
                globeParticles[index] = new Particle
                {
                    x = .5f + Mathf.Cos(angle) * Next(random, .118f, .145f) * radius,
                    y = .465f + Mathf.Sin(angle) * Next(random, .185f, .225f) * radius,
                    phase = Next(random, 0f, Mathf.PI * 2f),
                    speed = Next(random, .34f, .62f),
                    size = Next(random, 3.2f, 6.2f),
                    opacity = Next(random, .45f, .90f)
                };
            }

            for (var index = 0; index < stars.Length; index++)
            {
                stars[index] = new Particle
                {
                    x = Next(random, .10f, .90f),
                    y = Next(random, .64f, .965f),
                    phase = Next(random, 0f, Mathf.PI * 2f),
                    speed = Next(random, .35f, .86f),
                    size = Next(random, 2.4f, 5.2f),
                    opacity = Next(random, .35f, .82f)
                };
            }
        }

        private void BuildMaterial()
        {
            var shader = Resources.Load<Shader>("Shaders/WelcomeParticle");
            if (shader == null)
            {
                Debug.LogError("Welcome particle shader is missing from Resources/Shaders.");
                return;
            }

            runtimeMaterial = new Material(shader)
            {
                name = "TG Welcome Ambient Particle Material",
                hideFlags = HideFlags.HideAndDontSave
            };
            material = runtimeMaterial;
        }

        private void BuildBaseVisuals(Sprite glowSprite)
        {
            baseGlow = CreateImage("Welcome Base Breathing Glow", glowSprite,
                new Color(.22f, .82f, 1f, 0f));
            Anchor(baseGlow.rectTransform, -410f, -286f, 410f, -202f);
        }

        private Image CreateImage(string objectName, Sprite sprite, Color tint)
        {
            var imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(transform, false);
            var image = imageObject.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.color = tint;
            image.raycastTarget = false;
            image.canvasRenderer.cullTransparentMesh = true;
            return image;
        }

        private void PopulateStars(VertexHelper helper, Rect area, float elapsed)
        {
            var color = new Color(.70f, .93f, 1f, 1f);
            for (var index = 0; index < stars.Length; index++)
            {
                var particle = stars[index];
                var wave = .5f + .5f * Mathf.Sin(particle.phase + elapsed * particle.speed * starTwinkleSpeed);
                wave = Mathf.SmoothStep(.12f, 1f, wave);
                color.a = particle.opacity * starOpacity * overallIntensity * (.30f + wave * .70f);
                var size = particle.size * starSize * (.88f + wave * .20f);
                AddQuad(helper, Point(area, particle.x, particle.y), size, color);
            }
        }

        private void PopulateGlobeParticles(VertexHelper helper, Rect area, float elapsed)
        {
            var color = new Color(.48f, .88f, 1f, 1f);
            for (var index = 0; index < globeParticles.Length; index++)
            {
                var particle = globeParticles[index];
                var phase = particle.phase + elapsed * particle.speed * globeParticleSpeed;
                var basePosition = Point(area, particle.x, particle.y);
                var drift = new Vector2(Mathf.Sin(phase) * 7f, Mathf.Cos(phase * .73f) * 5f);
                var shimmer = .5f + .5f * Mathf.Sin(phase * 1.27f);
                color.a = particle.opacity * globeParticleOpacity * overallIntensity * (.38f + shimmer * .62f);
                var size = particle.size * globeParticleSize * (.90f + shimmer * .16f);
                AddQuad(helper, basePosition + drift, size, color);
            }
        }

        private void AnimateBaseGlow(float elapsed)
        {
            if (baseGlow == null) return;
            RefreshBaseVisibility();
            if (!baseGlow.gameObject.activeSelf) return;

            var period = Mathf.Max(1f, baseBreathSeconds);
            var breath = .5f + .5f * Mathf.Sin(elapsed * Mathf.PI * 2f / period - Mathf.PI * .5f);
            breath = Mathf.SmoothStep(0f, 1f, breath);
            baseGlow.color = new Color(.22f, .82f, 1f,
                baseGlowOpacity * overallIntensity * (.56f + breath * .44f));
            baseGlow.rectTransform.localScale = new Vector3(1f + breath * .025f, 1f + breath * .08f, 1f);
        }

        private void RefreshVisualState()
        {
            RefreshBaseVisibility();
            SetVerticesDirty();
        }

        private void RefreshBaseVisibility()
        {
            var visible = effectsEnabled && baseGlowEnabled;
            if (baseGlow != null && baseGlow.gameObject.activeSelf != visible)
                baseGlow.gameObject.SetActive(visible);
        }

        private static void AddQuad(VertexHelper helper, Vector2 center, float size, Color color)
        {
            var half = size * .5f;
            var start = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;

            vertex.position = new Vector3(center.x - half, center.y - half);
            vertex.uv0 = new Vector2(0f, 0f);
            helper.AddVert(vertex);
            vertex.position = new Vector3(center.x - half, center.y + half);
            vertex.uv0 = new Vector2(0f, 1f);
            helper.AddVert(vertex);
            vertex.position = new Vector3(center.x + half, center.y + half);
            vertex.uv0 = new Vector2(1f, 1f);
            helper.AddVert(vertex);
            vertex.position = new Vector3(center.x + half, center.y - half);
            vertex.uv0 = new Vector2(1f, 0f);
            helper.AddVert(vertex);

            helper.AddTriangle(start, start + 1, start + 2);
            helper.AddTriangle(start + 2, start + 3, start);
        }

        private static Vector2 Point(Rect area, float normalizedX, float normalizedY) =>
            new Vector2(area.xMin + area.width * normalizedX, area.yMin + area.height * normalizedY);

        private static float Next(System.Random random, float minimum, float maximum) =>
            minimum + (float)random.NextDouble() * (maximum - minimum);

        private static void Anchor(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(right, top);
        }

        private struct Particle
        {
            public float x;
            public float y;
            public float phase;
            public float speed;
            public float size;
            public float opacity;
        }
    }
}
