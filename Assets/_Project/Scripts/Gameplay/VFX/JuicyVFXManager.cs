using System.Collections.Generic;
using DG.Tweening;
using SandSort.Core;
using UnityEngine;

namespace SandSort.Gameplay.VFX
{
    /// <summary>
    /// Lightweight, zero-GC procedural VFX manager for juicy bucket completions,
    /// shockwave rings, sparkle explosions, and celebratory camera punches.
    /// Fully optimized for WebGL with 60 FPS performance.
    /// </summary>
    public sealed class JuicyVFXManager : MonoBehaviour
    {
        public static JuicyVFXManager Instance { get; private set; }

        private ParticleSystem _sparkleParticleSystem;
        private ParticleSystem.EmitParams _emitParams;
        private Camera _gameplayCamera;
        private Vector3 _cameraOriginalPos;
        private Tweener _cameraShakeTweener;

        // Pooled Shockwave Rings
        private readonly List<Transform> _shockwavePool = new List<Transform>(8);
        private Material _shockwaveMaterial;

        private void Awake()
        {
            if (Instance != null && Instance != null)
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

            CreateProceduralSparkleParticleSystem();
            CreateShockwaveMaterial();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            ServiceLocator.Unregister(this);
            _cameraShakeTweener?.Kill();
        }

        private void CreateShockwaveMaterial()
        {
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null) unlitShader = Shader.Find("Unlit/Color");
            if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");

            _shockwaveMaterial = new Material(unlitShader)
            {
                name = "Mat_ShockwaveRing_VFX"
            };
        }

        private void CreateProceduralSparkleParticleSystem()
        {
            GameObject psObj = new GameObject("VFX_ProceduralSparkles");
            psObj.transform.SetParent(transform, false);

            _sparkleParticleSystem = psObj.AddComponent<ParticleSystem>();
            ParticleSystemRenderer psRenderer = psObj.GetComponent<ParticleSystemRenderer>();

            var main = _sparkleParticleSystem.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = 0.55f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.26f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 3.2f;

            var emission = _sparkleParticleSystem.emission;
            emission.enabled = false;

            var shape = _sparkleParticleSystem.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.20f;

            var sizeOverLifetime = _sparkleParticleSystem.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 0.4f);
            sizeCurve.AddKey(0.2f, 1.2f);
            sizeCurve.AddKey(1f, 0f);
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // Shading
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) unlit = Shader.Find("Sprites/Default");
            Material mat = new Material(unlit) { name = "Mat_SparkleParticle_VFX" };
            psRenderer.material = mat;
            psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        }

        /// <summary>
        /// Triggers celebratory juice when a bucket hits 100%:
        /// 1. Vibrant star sparkle spray in bucket's color
        /// 2. Expanding radial shockwave ring
        /// 3. Punchy micro camera shake
        /// </summary>
        public void PlayBucketCompleteVFX(Vector3 bucketWorldPos, Color32 bucketColor)
        {
            Vector3 spawnPos = bucketWorldPos + Vector3.up * 0.35f;

            // 1. Sparkle Particles Burst
            if (_sparkleParticleSystem != null)
            {
                int particleCount = 18;
                for (int i = 0; i < particleCount; i++)
                {
                    _emitParams.position = spawnPos + Random.insideUnitSphere * 0.12f;
                    Vector3 randomDir = (Vector3.up * 1.4f + Random.insideUnitSphere * 0.8f).normalized;
                    _emitParams.velocity = randomDir * Random.Range(2.8f, 5.5f);
                    _emitParams.startColor = Color.Lerp((Color)bucketColor, Color.white, Random.Range(0.15f, 0.70f));
                    _emitParams.startSize = Random.Range(0.14f, 0.28f);
                    _emitParams.startLifetime = Random.Range(0.40f, 0.65f);
                    _sparkleParticleSystem.Emit(_emitParams, 1);
                }
            }

            // 2. Expanding Shockwave Ring
            SpawnExpandingShockwaveRing(spawnPos, bucketColor);

            // 3. Subtle camera micro-punch (0.04f amplitude, 0.14s)
            TriggerCameraMicroPunch(0.045f, 0.14f);
        }

        private void SpawnExpandingShockwaveRing(Vector3 centerWorldPos, Color32 color)
        {
            Transform ring = GetOrCreateShockwaveRing();
            ring.position = centerWorldPos;
            ring.rotation = Quaternion.Euler(90f, 0f, 0f); // flat horizontal
            ring.localScale = Vector3.one * 0.15f;
            ring.gameObject.SetActive(true);

            MeshRenderer mr = ring.GetComponent<MeshRenderer>();
            MaterialPropertyBlock pb = new MaterialPropertyBlock();
            mr.GetPropertyBlock(pb);
            pb.SetColor(GameConstants.BaseColorShaderId, (Color)color);
            pb.SetColor(GameConstants.MainColorShaderId, (Color)color);
            mr.SetPropertyBlock(pb);

            Sequence seq = DOTween.Sequence();
            seq.Append(ring.DOScale(new Vector3(1.4f, 1.4f, 0.05f), 0.26f).SetEase(Ease.OutQuad));
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

            // Create flat quad ring
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
    }
}

