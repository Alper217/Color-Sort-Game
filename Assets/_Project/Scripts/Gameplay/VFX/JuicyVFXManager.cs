using System.Collections.Generic;
using DG.Tweening;
using SandSort.Core;
using TMPro;
using UnityEngine;

namespace SandSort.Gameplay.VFX
{
    /// <summary>
    /// High-performance, zero-GC procedural VFX manager for juicy bucket completions,
    /// soft radial shockwave rings, 4-point star explosions, confetti cannons,
    /// sand intake splashes, and celebratory camera punches.
    /// Built for 60 FPS WebGL and Mobile casual puzzle games (Dream / Peak / Good Job quality).
    /// </summary>
    public sealed class JuicyVFXManager : MonoBehaviour
    {
        public static JuicyVFXManager Instance { get; private set; }

        // Particle Systems
        private ParticleSystem _starParticleSystem;
        private ParticleSystem _dropletParticleSystem;
        private ParticleSystem _microSplashParticleSystem;
        private ParticleSystem _confettiParticleSystem;
        private ParticleSystem _smokeParticleSystem;
        private ParticleSystem.EmitParams _emitParams;

        [Header("Cartoon FX Remaster (Custom Prefabs)")]
        [SerializeField] private GameObject _bucketCompleteCustomPrefab;
        [SerializeField] private GameObject _winFireworksCustomPrefab;
        [SerializeField] private GameObject _deadlockSmokeCustomPrefab;
        [Tooltip("Kovanın boyutuna göre ölçek çarpanı (0.22 - 0.28 kova ağzı için daha tatlı, küçük hypercasual boyuttur)")]
        [SerializeField] private float _bucketEffectScale = 0.25f;
        [Tooltip("Efektin oynatma hızı çarpanı (daha yumuşak, acelesiz animasyon için)")]
        [SerializeField] private float _bucketEffectSpeed = 0.75f;

        // Camera & Shake
        private Camera _gameplayCamera;
        private Vector3 _cameraOriginalPos;
        private Tweener _cameraShakeTweener;

        // Shockwave Pool & Material
        private readonly List<Transform> _shockwavePool = new List<Transform>(8);
        private Material _shockwaveMaterial;

        // Pop text / Checkmark 3D label pool
        private readonly List<TextMeshPro> _popTextPool = new List<TextMeshPro>(6);

        // Procedural Textures (Generated once at runtime)
        private static Texture2D s_starTexture;
        private static Texture2D s_softCircleTexture;
        private static Texture2D s_ringTexture;
        private static Texture2D s_confettiTexture;

        private float _lastMicroSplashTime;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            ServiceLocator.Register(this);

            _gameplayCamera = Camera.main;
            if (_gameplayCamera != null)
            {
                _cameraOriginalPos = _gameplayCamera.transform.position;
            }

            EnsureProceduralTextures();
            InitializeParticleSystems();
            CreateShockwaveMaterial();
            ResolveCustomVFXPrefabs();
        }

        private void ResolveCustomVFXPrefabs()
        {
            if (_bucketCompleteCustomPrefab == null)
                _bucketCompleteCustomPrefab = Resources.Load<GameObject>("VFX/CFXR_BucketComplete");
            if (_winFireworksCustomPrefab == null)
                _winFireworksCustomPrefab = Resources.Load<GameObject>("VFX/CFXR_WinFireworks");
            if (_deadlockSmokeCustomPrefab == null)
                _deadlockSmokeCustomPrefab = Resources.Load<GameObject>("VFX/CFXR_DeadlockPoof");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            ServiceLocator.Unregister(this);
            _cameraShakeTweener?.Kill();
        }

        #region Procedural Texture Generation

        private static void EnsureProceduralTextures()
        {
            if (s_starTexture == null) s_starTexture = CreateStarTexture(64);
            if (s_softCircleTexture == null) s_softCircleTexture = CreateSoftCircleTexture(64);
            if (s_ringTexture == null) s_ringTexture = CreateRingTexture(128);
            if (s_confettiTexture == null) s_confettiTexture = CreateConfettiTexture(32);
        }

        private static Texture2D CreateStarTexture(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Tex_Procedural_Star",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            float invRadius = 1f / center;

            for (int y = 0; y < size; y++)
            {
                float ny = Mathf.Abs((y - center) * invRadius);
                for (int x = 0; x < size; x++)
                {
                    float nx = Mathf.Abs((x - center) * invRadius);
                    float needleX = Mathf.Pow(Mathf.Clamp01(1f - nx), 4f) * Mathf.Pow(Mathf.Clamp01(1f - ny), 0.5f);
                    float needleY = Mathf.Pow(Mathf.Clamp01(1f - ny), 4f) * Mathf.Pow(Mathf.Clamp01(1f - nx), 0.5f);
                    float dist = Mathf.Sqrt(nx * nx + ny * ny);
                    float core = Mathf.Clamp01(1f - dist * 1.8f);
                    core = core * core;

                    float alpha = Mathf.Clamp01(Mathf.Max(needleX, needleY) * 0.92f + core * 0.85f);
                    byte a = (byte)(alpha * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D CreateSoftCircleTexture(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Tex_Procedural_SoftCircle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            float invRadius = 1f / center;

            for (int y = 0; y < size; y++)
            {
                float dy = (y - center) * invRadius;
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) * invRadius;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(1f - dist);
                    alpha = alpha * alpha * (3f - 2f * alpha);
                    byte a = (byte)(alpha * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D CreateRingTexture(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Tex_Procedural_Ring",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            float invRadius = 1f / center;
            const float ringR = 0.70f;
            const float ringTh = 0.18f;

            for (int y = 0; y < size; y++)
            {
                float dy = (y - center) * invRadius;
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) * invRadius;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float diff = Mathf.Abs(dist - ringR) / ringTh;
                    float alpha = Mathf.Clamp01(1f - diff);
                    alpha = alpha * alpha * (3f - 2f * alpha);
                    byte a = (byte)(alpha * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D CreateConfettiTexture(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Tex_Procedural_Confetti",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                float ny = Mathf.Abs((y - center) / center);
                for (int x = 0; x < size; x++)
                {
                    float nx = Mathf.Abs((x - center) / center);
                    float rx = Mathf.Max(0f, nx - 0.40f) / 0.60f;
                    float ry = Mathf.Max(0f, ny - 0.70f) / 0.30f;
                    float cornerDist = Mathf.Sqrt(rx * rx + ry * ry);
                    float alpha = cornerDist > 1f ? Mathf.Clamp01(1f - (cornerDist - 1f) * 4f) : 1f;
                    byte a = (byte)(alpha * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        #endregion

        #region Material & Particle System Initialization

        private static Material CreateParticleMaterial(string matName, Texture2D texture, bool additive = false)
        {
            Shader shader = null;
            if (additive)
            {
                shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) shader = Shader.Find("Mobile/Particles/Additive");
                if (shader == null) shader = Shader.Find("Particles/Additive");
            }
            else
            {
                shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            }

            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");

            Material mat = new Material(shader)
            {
                name = matName,
                renderQueue = 3100
            };

            if (mat.HasProperty("_MainTex")) mat.mainTexture = texture;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", additive ? 1 : 0);
            return mat;
        }

        private void CreateShockwaveMaterial()
        {
            _shockwaveMaterial = CreateParticleMaterial("Mat_ShockwaveRing_Procedural", s_ringTexture, false);
        }

        private void InitializeParticleSystems()
        {
            // 1. Star Sparkles (Celebratory 4-point stars)
            Material starMat = CreateParticleMaterial("Mat_StarParticle_VFX", s_starTexture, false);
            _starParticleSystem = CreateParticleEmitter("PS_StarSparkles", starMat, ps =>
            {
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.40f, 0.65f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2.8f, 5.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.35f);
                main.gravityModifier = 1.4f;

                var rotOverLife = ps.rotationOverLifetime;
                rotOverLife.enabled = true;
                rotOverLife.z = new ParticleSystem.MinMaxCurve(-360f, 360f);

                var sizeOverLife = ps.sizeOverLifetime;
                sizeOverLife.enabled = true;
                AnimationCurve curve = new AnimationCurve();
                curve.AddKey(0f, 0.4f);
                curve.AddKey(0.25f, 1.35f);
                curve.AddKey(1f, 0f);
                sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, curve);
            });

            // 2. Color Droplets (Juicy sand/paint splash balls)
            Material dropletMat = CreateParticleMaterial("Mat_DropletParticle_VFX", s_softCircleTexture, false);
            _dropletParticleSystem = CreateParticleEmitter("PS_ColorDroplets", dropletMat, ps =>
            {
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(3.2f, 6.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
                main.gravityModifier = 3.2f;

                var sizeOverLife = ps.sizeOverLifetime;
                sizeOverLife.enabled = true;
                AnimationCurve curve = new AnimationCurve();
                curve.AddKey(0f, 1f);
                curve.AddKey(0.6f, 0.85f);
                curve.AddKey(1f, 0f);
                sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, curve);
            });

            // 3. Micro Splash (Triggered as sand grains enter bucket rim)
            _microSplashParticleSystem = CreateParticleEmitter("PS_MicroSplash", dropletMat, ps =>
            {
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.24f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
                main.gravityModifier = 2.0f;
            });

            // 4. Confetti Cannons (Level Won Celebration)
            Material confettiMat = CreateParticleMaterial("Mat_ConfettiParticle_VFX", s_confettiTexture, false);
            _confettiParticleSystem = CreateParticleEmitter("PS_ConfettiCannons", confettiMat, ps =>
            {
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(6.0f, 11.0f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.32f);
                main.gravityModifier = 1.8f;

                var rotOverLife = ps.rotationOverLifetime;
                rotOverLife.enabled = true;
                rotOverLife.z = new ParticleSystem.MinMaxCurve(-450f, 450f);

                var sizeOverLife = ps.sizeOverLifetime;
                sizeOverLife.enabled = true;
                AnimationCurve curve = new AnimationCurve();
                curve.AddKey(0f, 0.8f);
                curve.AddKey(0.2f, 1.2f);
                curve.AddKey(0.85f, 1.0f);
                curve.AddKey(1f, 0f);
                sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, curve);
            });

            // 5. Smoke Puffs (Deadlock / Stuck Buckets)
            _smokeParticleSystem = CreateParticleEmitter("PS_DeadlockSmoke", dropletMat, ps =>
            {
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.65f, 1.10f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.40f);
                main.gravityModifier = -0.3f; // gently floats upward

                var sizeOverLife = ps.sizeOverLifetime;
                sizeOverLife.enabled = true;
                AnimationCurve curve = new AnimationCurve();
                curve.AddKey(0f, 0.3f);
                curve.AddKey(0.4f, 1.0f);
                curve.AddKey(1f, 1.4f);
                sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, curve);
            });
        }

        private ParticleSystem CreateParticleEmitter(string name, Material mat, System.Action<ParticleSystem> configure)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystemRenderer psRenderer = go.GetComponent<ParticleSystemRenderer>();

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;

            configure?.Invoke(ps);

            psRenderer.material = mat;
            psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            psRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psRenderer.receiveShadows = false;

            return ps;
        }

        #endregion

        #region Public VFX API

        /// <summary>
        /// Fast, micro sand splash at bucket intake rim when a grain arrives.
        /// </summary>
        public void PlayGrainSplashMicroVFX(Vector3 rimWorldPos, Color32 grainColor)
        {
            if (_microSplashParticleSystem == null) return;
            if (Time.time - _lastMicroSplashTime < 0.035f) return;
            _lastMicroSplashTime = Time.time;

            int count = Random.Range(2, 4);
            for (int i = 0; i < count; i++)
            {
                _emitParams.position = rimWorldPos + Random.insideUnitSphere * 0.05f;
                Vector3 dir = (Vector3.up * 1.1f + Random.insideUnitSphere * 0.7f).normalized;
                _emitParams.velocity = dir * Random.Range(1.3f, 2.4f);
                _emitParams.startColor = Color.Lerp((Color)grainColor, Color.white, Random.Range(0.2f, 0.5f));
                _emitParams.startSize = Random.Range(0.06f, 0.11f);
                _emitParams.startLifetime = Random.Range(0.14f, 0.24f);
                _microSplashParticleSystem.Emit(_emitParams, 1);
            }
        }

        /// <summary>
        /// Dream / Peak / Good Job style celebratory VFX sequence when a bucket hits 100%:
        /// Uses Cartoon FX Remaster prefab if available (tinted to bucket color), with procedural fallback.
        /// </summary>
        public void PlayBucketCompleteVFX(Vector3 bucketWorldPos, Color32 bucketColor)
        {
            Vector3 spawnPos = bucketWorldPos + Vector3.up * 0.10f;

            // 1. Cartoon FX Remaster Custom Prefab (Hit Light / Star Burst with auto color tint)
            if (_bucketCompleteCustomPrefab != null)
            {
                GameObject fx = Instantiate(_bucketCompleteCustomPrefab, spawnPos, Quaternion.identity);
                fx.transform.localScale = Vector3.one * _bucketEffectScale;

                // Dynamically tint Cartoon FX particle systems to the exact bucket color and adjust speed
                ParticleSystem[] pss = fx.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < pss.Length; i++)
                {
                    var main = pss[i].main;
                    main.simulationSpeed = _bucketEffectSpeed;
                    Color c = bucketColor;
                    Color cLight = Color.Lerp(bucketColor, Color.white, 0.40f);
                    main.startColor = new ParticleSystem.MinMaxGradient(c, cLight);
                }

                // Disable point lights on instantiated effect to prevent overexposure/flash glare in hypercasual style
                Light[] lights = fx.GetComponentsInChildren<Light>(true);
                for (int i = 0; i < lights.Length; i++)
                {
                    lights[i].enabled = false;
                }

                TriggerCameraMicroPunch(0.025f, 0.08f);
                return;
            }

            // 2. Procedural Fallback: Starburst Particles (Crisp, sparkling, tight)
            if (_starParticleSystem != null)
            {
                int count = 7;
                for (int i = 0; i < count; i++)
                {
                    _emitParams.position = spawnPos + Random.insideUnitSphere * 0.06f;
                    Vector3 randomDir = (Vector3.up * 1.1f + Random.insideUnitSphere * 0.7f).normalized;
                    _emitParams.velocity = randomDir * Random.Range(1.8f, 3.2f);
                    _emitParams.startColor = Color.Lerp((Color)bucketColor, Color.white, Random.Range(0.35f, 0.85f));
                    _emitParams.startSize = Random.Range(0.08f, 0.14f);
                    _emitParams.startLifetime = Random.Range(0.28f, 0.42f);
                    _emitParams.rotation = Random.Range(0f, 360f);
                    _starParticleSystem.Emit(_emitParams, 1);
                }
            }

            // 3. Procedural Color Droplets
            if (_dropletParticleSystem != null)
            {
                int count = 8;
                for (int i = 0; i < count; i++)
                {
                    _emitParams.position = spawnPos + Random.insideUnitSphere * 0.05f;
                    Vector3 randomDir = (Vector3.up * 1.2f + Random.insideUnitSphere * 0.6f).normalized;
                    _emitParams.velocity = randomDir * Random.Range(1.8f, 3.4f);
                    Color baseCol = (i % 2 == 0) ? (Color)bucketColor : Color.Lerp((Color)bucketColor, Color.white, 0.35f);
                    _emitParams.startColor = baseCol;
                    _emitParams.startSize = Random.Range(0.06f, 0.11f);
                    _emitParams.startLifetime = Random.Range(0.25f, 0.40f);
                    _dropletParticleSystem.Emit(_emitParams, 1);
                }
            }

            // 4. Radial Shockwave Ring
            SpawnExpandingShockwaveRing(spawnPos, bucketColor);

            TriggerCameraMicroPunch(0.040f, 0.12f);
        }

        /// <summary>
        /// Fires dual confetti cannons from screen bottom corners + Cartoon FX fireworks for Level Complete.
        /// </summary>
        public void PlayWinConfettiCannons()
        {
            Camera cam = _gameplayCamera != null ? _gameplayCamera : Camera.main;

            // 1. Cartoon FX Fireworks in upper sky
            if (_winFireworksCustomPrefab != null && cam != null)
            {
                float zDist = Mathf.Abs(cam.transform.position.z - 0.2f);
                Vector3 leftUpper = cam.ViewportToWorldPoint(new Vector3(0.28f, 0.72f, zDist));
                Vector3 rightUpper = cam.ViewportToWorldPoint(new Vector3(0.72f, 0.76f, zDist));

                GameObject fw1 = Instantiate(_winFireworksCustomPrefab, leftUpper, Quaternion.identity);
                fw1.transform.localScale = Vector3.one * 0.65f;

                DOVirtual.DelayedCall(0.18f, () =>
                {
                    if (this != null && _winFireworksCustomPrefab != null)
                    {
                        GameObject fw2 = Instantiate(_winFireworksCustomPrefab, rightUpper, Quaternion.identity);
                        fw2.transform.localScale = Vector3.one * 0.65f;
                    }
                });
            }

            if (_confettiParticleSystem == null) return;
            if (cam == null) return;

            float zDistConfetti = Mathf.Abs(cam.transform.position.z - 0.2f);
            Vector3 leftCannon = cam.ViewportToWorldPoint(new Vector3(0.08f, 0.04f, zDistConfetti));
            Vector3 rightCannon = cam.ViewportToWorldPoint(new Vector3(0.92f, 0.04f, zDistConfetti));

            Color32[] confettiColors = new Color32[]
            {
                new Color32(255, 215, 0, 255),   // Gold
                new Color32(255, 75, 130, 255),  // Hot Pink
                new Color32(0, 225, 255, 255),   // Electric Cyan
                new Color32(46, 204, 113, 255),  // Lime Green
                new Color32(255, 140, 0, 255),   // Vibrant Orange
                new Color32(170, 70, 255, 255)   // Purple
            };

            // Left Cannon
            int count = 42;
            for (int i = 0; i < count; i++)
            {
                _emitParams.position = leftCannon + (Vector3)Random.insideUnitCircle * 0.22f;
                Vector3 dir = (Vector3.up * 1.55f + Vector3.right * Random.Range(0.7f, 1.8f) + Vector3.forward * Random.Range(-0.3f, 0.3f)).normalized;
                _emitParams.velocity = dir * Random.Range(6.5f, 11.5f);
                _emitParams.startColor = confettiColors[Random.Range(0, confettiColors.Length)];
                _emitParams.startSize = Random.Range(0.18f, 0.32f);
                _emitParams.startLifetime = Random.Range(1.6f, 2.4f);
                _emitParams.rotation = Random.Range(0f, 360f);
                _confettiParticleSystem.Emit(_emitParams, 1);
            }

            // Right Cannon
            for (int i = 0; i < count; i++)
            {
                _emitParams.position = rightCannon + (Vector3)Random.insideUnitCircle * 0.22f;
                Vector3 dir = (Vector3.up * 1.55f + Vector3.left * Random.Range(0.7f, 1.8f) + Vector3.forward * Random.Range(-0.3f, 0.3f)).normalized;
                _emitParams.velocity = dir * Random.Range(6.5f, 11.5f);
                _emitParams.startColor = confettiColors[Random.Range(0, confettiColors.Length)];
                _emitParams.startSize = Random.Range(0.18f, 0.32f);
                _emitParams.startLifetime = Random.Range(1.6f, 2.4f);
                _emitParams.rotation = Random.Range(0f, 360f);
                _confettiParticleSystem.Emit(_emitParams, 1);
            }

            TriggerCameraMicroPunch(0.06f, 0.20f);
        }

        /// <summary>
        /// Fires disappointment smoke puffs over stuck buckets when deadlock occurs.
        /// </summary>
        public void PlayFailDeadlockVFX(IReadOnlyList<Vector3> stuckBucketPositions)
        {
            if (_deadlockSmokeCustomPrefab != null && stuckBucketPositions != null)
            {
                for (int b = 0; b < stuckBucketPositions.Count; b++)
                {
                    Vector3 pos = stuckBucketPositions[b] + Vector3.up * 0.12f;
                    GameObject poof = Instantiate(_deadlockSmokeCustomPrefab, pos, Quaternion.identity);
                    poof.transform.localScale = Vector3.one * 0.35f;
                }
            }
            else if (_smokeParticleSystem != null && stuckBucketPositions != null)
            {
                for (int b = 0; b < stuckBucketPositions.Count; b++)
                {
                    Vector3 pos = stuckBucketPositions[b] + Vector3.up * 0.15f;
                    for (int i = 0; i < 6; i++)
                    {
                        _emitParams.position = pos + (Vector3)Random.insideUnitCircle * 0.12f;
                        _emitParams.velocity = (Vector3.up * 0.85f + (Vector3)Random.insideUnitCircle * 0.45f);
                        _emitParams.startColor = new Color32(185, 175, 205, 160);
                        _emitParams.startSize = Random.Range(0.16f, 0.36f);
                        _emitParams.startLifetime = Random.Range(0.65f, 1.05f);
                        _smokeParticleSystem.Emit(_emitParams, 1);
                    }
                }
            }

            TriggerCameraMicroPunch(0.05f, 0.22f);
        }

        #endregion

        #region Shockwave & Pop Label Helpers

        private void SpawnExpandingShockwaveRing(Vector3 centerWorldPos, Color32 color)
        {
            Transform ring = GetOrCreateShockwaveRing();
            ring.position = centerWorldPos;
            ring.rotation = Quaternion.Euler(90f, 0f, 0f);
            ring.localScale = Vector3.one * 0.10f;
            ring.gameObject.SetActive(true);

            MeshRenderer mr = ring.GetComponent<MeshRenderer>();
            MaterialPropertyBlock pb = new MaterialPropertyBlock();
            mr.GetPropertyBlock(pb);

            Sequence seq = DOTween.Sequence();
            float duration = 0.20f;
            seq.Append(ring.DOScale(new Vector3(0.65f, 0.65f, 0.05f), duration).SetEase(Ease.OutCubic));
            seq.Join(DOTween.To(() => 1f, x =>
            {
                Color c = color;
                c.a = x;
                pb.SetColor(GameConstants.BaseColorShaderId, c);
                pb.SetColor(GameConstants.MainColorShaderId, c);
                pb.SetColor("_Color", c);
                mr.SetPropertyBlock(pb);
            }, 0f, duration).SetEase(Ease.OutQuad));
            seq.OnComplete(() =>
            {
                ring.gameObject.SetActive(false);
            });
        }

        private Transform GetOrCreateShockwaveRing()
        {
            for (int i = 0; i < _shockwavePool.Count; i++)
            {
                if (!_shockwavePool[i].gameObject.activeSelf)
                {
                    return _shockwavePool[i];
                }
            }

            GameObject quadObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadObj.name = $"VFX_ShockwaveRing_{_shockwavePool.Count}";
            Destroy(quadObj.GetComponent<Collider>());

            MeshRenderer mr = quadObj.GetComponent<MeshRenderer>();
            mr.material = _shockwaveMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            quadObj.transform.SetParent(transform, false);
            quadObj.SetActive(false);
            _shockwavePool.Add(quadObj.transform);
            return quadObj.transform;
        }

        public void SpawnFloatingPopLabel(Vector3 worldPos, string text, Color32 color)
        {
            TextMeshPro tmp = GetOrCreatePopText();
            tmp.transform.position = worldPos + Vector3.up * 0.15f;
            tmp.transform.localScale = Vector3.one * 0.4f;
            tmp.text = text;
            tmp.color = color;
            tmp.gameObject.SetActive(true);

            Sequence seq = DOTween.Sequence();
            seq.Append(tmp.transform.DOScale(Vector3.one * 1.15f, 0.14f).SetEase(Ease.OutBack));
            seq.Join(tmp.transform.DOMoveY(worldPos.y + 0.65f, 0.40f).SetEase(Ease.OutCubic));
            seq.Append(tmp.transform.DOScale(Vector3.zero, 0.12f).SetEase(Ease.InBack));
            seq.OnComplete(() => tmp.gameObject.SetActive(false));
        }

        private TextMeshPro GetOrCreatePopText()
        {
            for (int i = 0; i < _popTextPool.Count; i++)
            {
                if (!_popTextPool[i].gameObject.activeSelf)
                {
                    return _popTextPool[i];
                }
            }

            GameObject go = new GameObject($"VFX_PopLabel_{_popTextPool.Count}");
            go.transform.SetParent(transform, false);
            TextMeshPro tmp = go.AddComponent<TextMeshPro>();
            if (TMP_Settings.defaultFontAsset != null)
            {
                tmp.font = TMP_Settings.defaultFontAsset;
            }
            tmp.fontSize = 2.4f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            try
            {
                tmp.outlineWidth = 0.25f;
                tmp.outlineColor = new Color32(20, 15, 30, 255);
            }
            catch
            {
                // Safe fallback if shared material is not yet initialized
            }

            MeshRenderer mr = tmp.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = 35;

            go.SetActive(false);
            _popTextPool.Add(tmp);
            return tmp;
        }

        public void TriggerCameraMicroPunch(float strength = 0.04f, float duration = 0.14f)
        {
            if (_gameplayCamera == null)
            {
                _gameplayCamera = Camera.main;
                if (_gameplayCamera == null) return;
                _cameraOriginalPos = _gameplayCamera.transform.position;
            }

            _cameraShakeTweener?.Kill();
            _gameplayCamera.transform.position = _cameraOriginalPos;
            _cameraShakeTweener = _gameplayCamera.transform
                .DOShakePosition(duration, strength, 22, 90f, false, true)
                .OnComplete(() =>
                {
                    if (_gameplayCamera != null)
                    {
                        _gameplayCamera.transform.position = _cameraOriginalPos;
                    }
                });
        }

        #endregion
    }
}
