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
        // The welcome background is intentionally bright. These fixed pools are therefore
        // large enough to read clearly from a 55-inch screen, while still remaining sparse.
        private const int GlobeParticleCount = 34;
        private const int StarCount = 42;

        [Header("Effect switches")]
        [SerializeField] private bool effectsEnabled = true;
        [SerializeField] private bool globeParticlesEnabled = true;
        [SerializeField] private bool baseGlowEnabled = true;
        [SerializeField] private bool starTwinkleEnabled = true;

        [Header("Global")]
        [SerializeField, Range(0f, 1.5f)] private float overallIntensity = 1.20f;

        [Header("Globe particles")]
        [SerializeField, Range(.1f, 2f)] private float globeParticleSpeed = .84f;
        [SerializeField, Range(.5f, 2f)] private float globeParticleSize = 1.24f;
        [SerializeField, Range(0f, 1f)] private float globeParticleOpacity = .92f;

        [Header("Base glow")]
        [SerializeField, Range(1f, 10f)] private float baseBreathSeconds = 4f;
        [SerializeField, Range(0f, 1f)] private float baseGlowOpacity = .72f;

        [Header("Stars")]
        [SerializeField, Range(.1f, 2f)] private float starTwinkleSpeed = .78f;
        [SerializeField, Range(.5f, 2f)] private float starSize = 1.28f;
        [SerializeField, Range(0f, 1f)] private float starOpacity = .78f;

        private Particle[] globeParticles;
        private Particle[] stars;
        private Image baseGlow;
        private readonly Image[] baseRipples = new Image[2];
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

        public void Initialize(Sprite glowSprite, Sprite ringSprite)
        {
            if (initialized) return;
            initialized = true;
            raycastTarget = false;
            color = Color.white;
            animationStartedAt = Time.unscaledTime;

            BuildParticleData();
            BuildMaterial();
            BuildBaseVisuals(glowSprite, ringSprite);
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
                globeParticles[index] = new Particle
                {
                    x = Next(random, .135f, .215f),
                    y = Next(random, .165f, .252f),
                    phase = Next(random, 0f, Mathf.PI * 2f),
                    speed = Next(random, .13f, .30f) * (index % 4 == 0 ? -1f : 1f),
                    size = Next(random, 10f, 19f),
                    opacity = Next(random, .62f, 1f)
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
                    size = Next(random, 5.5f, 11f),
                    opacity = Next(random, .45f, 1f)
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

        private void BuildBaseVisuals(Sprite glowSprite, Sprite ringSprite)
        {
            baseGlow = CreateImage("Welcome Base Breathing Glow", glowSprite,
                new Color(.22f, .82f, 1f, 0f));
            Anchor(baseGlow.rectTransform, -505f, -318f, 505f, -178f);

            for (var index = 0; index < baseRipples.Length; index++)
            {
                baseRipples[index] = CreateImage("Welcome Base Diffusion Ring " + index, ringSprite,
                    new Color(.29f, .88f, 1f, 0f));
                Anchor(baseRipples[index].rectTransform, -580f, -340f, 580f, -156f);
            }
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
            var color = new Color(.76f, .96f, 1f, 1f);
            for (var index = 0; index < stars.Length; index++)
            {
                var particle = stars[index];
                var wave = .5f + .5f * Mathf.Sin(particle.phase + elapsed * particle.speed * starTwinkleSpeed);
                wave = Mathf.SmoothStep(.12f, 1f, wave);
                color.a = particle.opacity * starOpacity * overallIntensity * (.24f + wave * .76f);
                var size = particle.size * starSize * (.82f + wave * .36f);
                var position = Point(area, particle.x, particle.y);
                AddLightPoint(helper, position, size, color);

                // Only the brighter peak of a star opens into a small cross. This preserves
                // a calm sky while giving the visitor an obvious twinkle to notice.
                if (wave > .82f)
                {
                    var glint = color;
                    glint.a *= (wave - .82f) / .18f * .72f;
                    AddStreak(helper, position, new Vector2(1f, 0f), size * 2.8f, size * .24f, glint);
                    AddStreak(helper, position, new Vector2(0f, 1f), size * 2.8f, size * .24f, glint);
                }
            }
        }

        private void PopulateGlobeParticles(VertexHelper helper, Rect area, float elapsed)
        {
            var color = new Color(.48f, .93f, 1f, 1f);
            var center = Point(area, .5f, .465f);
            for (var index = 0; index < globeParticles.Length; index++)
            {
                var particle = globeParticles[index];
                var angle = particle.phase + elapsed * particle.speed * globeParticleSpeed;
                var position = center + new Vector2(
                    Mathf.Cos(angle) * area.width * particle.x,
                    Mathf.Sin(angle) * area.height * particle.y);
                var depth = .5f + .5f * Mathf.Sin(angle + particle.phase * .37f);
                color.a = particle.opacity * globeParticleOpacity * overallIntensity * (.45f + depth * .55f);
                var size = particle.size * globeParticleSize * (.88f + depth * .40f);
                AddLightPoint(helper, position, size, color);

                // A short, fading tail makes the orbit legible against the already detailed
                // globe artwork without becoming the phase-two energy-ribbon effect.
                var tangent = new Vector2(-Mathf.Sin(angle) * area.width * particle.x,
                    Mathf.Cos(angle) * area.height * particle.y).normalized;
                var tail = color;
                tail.a *= .62f;
                AddStreak(helper, position - tangent * size * 1.15f, tangent,
                    size * 5.8f, size * .48f, tail);
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
                baseGlowOpacity * overallIntensity * (.48f + breath * .52f));
            baseGlow.rectTransform.localScale = new Vector3(1f + breath * .075f, 1f + breath * .21f, 1f);

            for (var index = 0; index < baseRipples.Length; index++)
            {
                var ripple = baseRipples[index];
                var cycle = Mathf.Repeat(elapsed / 5.2f + index * .5f, 1f);
                var fade = 1f - Mathf.SmoothStep(.12f, 1f, cycle);
                ripple.color = new Color(.29f, .88f, 1f, fade * .36f * overallIntensity);
                ripple.rectTransform.localScale = new Vector3(.84f + cycle * .38f, .80f + cycle * .52f, 1f);
            }
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
            for (var index = 0; index < baseRipples.Length; index++)
            {
                var ripple = baseRipples[index];
                if (ripple != null && ripple.gameObject.activeSelf != visible)
                    ripple.gameObject.SetActive(visible);
            }
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

        private static void AddLightPoint(VertexHelper helper, Vector2 center, float size, Color color)
        {
            // The enlarged low-alpha halo makes motion readable against the luminous stock
            // background; the smaller core keeps the point crisp rather than foggy.
            var halo = color;
            halo.a *= .30f;
            AddQuad(helper, center, size * 2.6f, halo);
            AddQuad(helper, center, size, color);
        }

        private static void AddStreak(VertexHelper helper, Vector2 center, Vector2 direction,
            float length, float thickness, Color color)
        {
            var halfLength = direction.normalized * (length * .5f);
            var normal = new Vector2(-direction.y, direction.x).normalized * (thickness * .5f);
            var start = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;

            vertex.position = center - halfLength - normal;
            vertex.uv0 = new Vector2(0f, 0f);
            helper.AddVert(vertex);
            vertex.position = center - halfLength + normal;
            vertex.uv0 = new Vector2(0f, 1f);
            helper.AddVert(vertex);
            vertex.position = center + halfLength + normal;
            vertex.uv0 = new Vector2(1f, 1f);
            helper.AddVert(vertex);
            vertex.position = center + halfLength - normal;
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
