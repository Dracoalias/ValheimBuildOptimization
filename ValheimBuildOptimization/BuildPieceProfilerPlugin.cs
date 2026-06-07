using BepInEx;
using System;
using System.Collections.Generic;
using UnityEngine;
using BepInEx.Configuration;
using UnityEngine.Rendering;

namespace BuildPieceProfiler
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class BuildPieceProfilerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "valheim.buildpieceprofiler";
        public const string PluginName = "Build Piece Profiler";
        public const string PluginVersion = "0.2.0";

        private const string StaticFireLightProxyName = "BuildPieceProfiler_StaticFireLightProxy";

        private readonly Rect _windowRectDefault = new Rect(20f, 40f, 520f, 980f);
        private Rect _windowRect;
        private Vector2 _scrollPosition = Vector2.zero;

        private bool _showOverlay;
        private float _nextPollTime;

        private bool IsFireCandidate(Light[] lights, ParticleSystem[] particles)
        {
            if (lights == null || particles == null || lights.Length == 0 || particles.Length == 0)
            {
                return false;
            }

            bool hasLight = false;
            bool hasParticle = false;

            foreach (Light light in lights)
            {
                if (light != null)
                {
                    hasLight = true;
                    break;
                }
            }

            foreach (ParticleSystem particle in particles)
            {
                if (particle != null)
                {
                    hasParticle = true;
                    break;
                }
            }

            return hasLight && hasParticle;
        }

        private bool IsPieceVisible(MeshRenderer[] renderers)
        {
            if (renderers == null)
            {
                return false;
            }

            foreach (MeshRenderer renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                if (renderer.enabled &&
                    renderer.gameObject.activeInHierarchy &&
                    renderer.isVisible)
                {
                    return true;
                }
            }

            return false;
        }

        private void LateUpdate()
        {
            if (!_enableOptimizations.Value)
            {
                return;
            }

            if (_fireCullingMode.Value == FireCullingMode.Off)
            {
                return;
            }

            EnforceOptimizedFireLightsOnly();
        }

        private void EnforceOptimizedFireLightsOnly()
        {
            for (int i = _activeFireStates.Count - 1; i >= 0; i--)
            {
                FireOptimizationState state = _activeFireStates[i];

                if (state == null || state.AppliedMode == AppliedFireMode.None)
                {
                    _activeFireStates.RemoveAt(i);
                    continue;
                }

                foreach (KeyValuePair<Light, bool> kvp in state.OriginalLightEnabled)
                {
                    Light light = kvp.Key;

                    if (light == null || IsOurProxyLight(light))
                    {
                        continue;
                    }

                    if (light.enabled)
                    {
                        light.enabled = false;
                    }
                }

                if (state.AppliedMode == AppliedFireMode.FullCull)
                {
                    DisableProxyLight(state);
                }
            }
        }

        private void RestoreFire(Piece piece)
        {
            if (piece == null)
            {
                return;
            }

            if (!_fireStates.TryGetValue(piece, out FireOptimizationState state))
            {
                return;
            }

            foreach (KeyValuePair<Light, bool> kvp in state.OriginalLightEnabled)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.enabled = kvp.Value;
                }
            }

            foreach (KeyValuePair<MeshRenderer, ShadowCastingMode> kvp in state.OriginalShadowModes)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.shadowCastingMode = kvp.Value;
                }
            }

            foreach (KeyValuePair<ParticleSystem, bool> kvp in state.OriginalParticlePlaying)
            {
                ParticleSystem particle = kvp.Key;

                if (particle == null)
                {
                    continue;
                }

                if (kvp.Value)
                {
                    particle.Play(true);
                }
                else
                {
                    particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }

            DisableProxyLight(state);

            UntrackActiveFireState(state);
            state.AppliedMode = AppliedFireMode.None;
            state.OriginalLightEnabled.Clear();
            state.OriginalShadowModes.Clear();
            state.OriginalParticlePlaying.Clear();
        }

        private void RestoreAllFireOptimizations()
        {
            List<Piece> piecesToRestore = new List<Piece>(_fireStates.Keys);

            foreach (Piece piece in piecesToRestore)
            {
                RestoreFire(piece);
            }

            DestroyAllProxyLights();
        }

        private bool HasActiveFireOptimizations()
        {
            for (int i = _activeFireStates.Count - 1; i >= 0; i--)
            {
                FireOptimizationState state = _activeFireStates[i];

                if (state != null && state.AppliedMode != AppliedFireMode.None)
                {
                    return true;
                }

                _activeFireStates.RemoveAt(i);
            }

            return false;
        }

        private void TrackActiveFireState(FireOptimizationState state)
        {
            if (state == null || state.AppliedMode == AppliedFireMode.None)
            {
                return;
            }

            if (!_activeFireStates.Contains(state))
            {
                _activeFireStates.Add(state);
            }
        }

        private void UntrackActiveFireState(FireOptimizationState state)
        {
            if (state != null)
            {
                _activeFireStates.Remove(state);
            }
        }

        private void ResetFireCandidateCache()
        {
            _fireCandidates.Clear();
            _nextFireCandidateRefreshTime = 0f;
            _fireMetrics = new FireMetrics();
        }

        private void CleanupDestroyedFireStates()
        {
            List<KeyValuePair<Piece, FireOptimizationState>> destroyedStates = null;

            foreach (KeyValuePair<Piece, FireOptimizationState> kvp in _fireStates)
            {
                Piece piece = kvp.Key;

                if (piece != null)
                {
                    continue;
                }

                if (destroyedStates == null)
                {
                    destroyedStates = new List<KeyValuePair<Piece, FireOptimizationState>>();
                }

                destroyedStates.Add(kvp);
            }

            if (destroyedStates == null)
            {
                return;
            }

            foreach (KeyValuePair<Piece, FireOptimizationState> kvp in destroyedStates)
            {
                UntrackActiveFireState(kvp.Value);
                _fireStates.Remove(kvp.Key);
            }
        }

        private void RefreshFireCandidateCache()
        {
            _fireCandidates.Clear();

            Piece[] pieces = FindObjectsByType<Piece>(FindObjectsSortMode.None);
            HashSet<Piece> candidatePieces = new HashSet<Piece>();

            foreach (Piece piece in pieces)
            {
                if (piece == null)
                {
                    continue;
                }

                Light[] originalLights = GetOriginalLights(piece.GetComponentsInChildren<Light>(true));

                if (originalLights.Length == 0)
                {
                    continue;
                }

                ParticleSystem[] particles = piece.GetComponentsInChildren<ParticleSystem>(true);

                if (!IsFireCandidate(originalLights, particles))
                {
                    continue;
                }

                _fireCandidates.Add(new FireCandidate
                {
                    Piece = piece,
                    Renderers = piece.GetComponentsInChildren<MeshRenderer>(true),
                    OriginalLights = originalLights,
                    Particles = particles
                });

                candidatePieces.Add(piece);
            }

            List<Piece> staleOptimizedPieces = null;

            foreach (KeyValuePair<Piece, FireOptimizationState> kvp in _fireStates)
            {
                Piece piece = kvp.Key;
                FireOptimizationState state = kvp.Value;

                if (piece == null || state == null || state.AppliedMode == AppliedFireMode.None)
                {
                    continue;
                }

                if (candidatePieces.Contains(piece))
                {
                    continue;
                }

                if (staleOptimizedPieces == null)
                {
                    staleOptimizedPieces = new List<Piece>();
                }

                staleOptimizedPieces.Add(piece);
            }

            if (staleOptimizedPieces != null)
            {
                foreach (Piece piece in staleOptimizedPieces)
                {
                    RestoreFire(piece);
                }
            }

            _nextFireCandidateRefreshTime = Time.time + Mathf.Max(1f, _fireCandidateRefreshInterval.Value);
        }

        private void OnDestroy()
        {
            RestoreAllFireOptimizations();
        }

        private void ApplyStaticLight(
    Piece piece,
    FireOptimizationState state,
    MeshRenderer[] renderers,
    Light[] lights,
    ParticleSystem[] particles,
    bool useProxyLight,
    bool stopParticles)
        {
            if (state.AppliedMode != AppliedFireMode.StaticLight)
            {
                RestoreFire(piece);

                state = new FireOptimizationState
                {
                    Piece = piece,
                    LastVisibleTime = Time.time,
                    LastIrrelevantTime = Time.time
                };

                Light[] originalLightsForStore = lights ?? new Light[0];
                StoreOriginalStates(state, renderers, originalLightsForStore, particles);

                state.AppliedMode = AppliedFireMode.StaticLight;
                _fireStates[piece] = state;
            }

            TrackActiveFireState(state);

            Light[] originalLights = lights ?? new Light[0];
            StoreOriginalStates(state, renderers, originalLights, particles);

            if (useProxyLight)
            {
                Light sourceLight = GetFirstUsableLight(originalLights);

                if (sourceLight != null)
                {
                    EnsureProxyLight(piece, state, sourceLight);
                }
                else
                {
                    DisableProxyLight(state);
                }
            }
            else
            {
                DisableProxyLight(state);
            }

            DisableOriginalLights(originalLights);
            if (stopParticles)
            {
                StopParticles(particles);
            }
            else
            {
                RestoreParticles(state, particles);
            }
            DisableShadows(renderers);
        }

        private void ApplyFullCull(
    Piece piece,
    FireOptimizationState state,
    MeshRenderer[] renderers,
    Light[] lights,
    ParticleSystem[] particles)
        {
            if (state.AppliedMode != AppliedFireMode.FullCull)
            {
                RestoreFire(piece);

                state = GetOrCreateFireState(piece);

                Light[] originalLightsForStore = lights ?? new Light[0];
                StoreOriginalStates(state, renderers, originalLightsForStore, particles);

                state.AppliedMode = AppliedFireMode.FullCull;
            }

            TrackActiveFireState(state);

            Light[] originalLights = lights ?? new Light[0];
            StoreOriginalStates(state, renderers, originalLights, particles);

            DisableProxyLight(state);
            DisableOriginalLights(originalLights);
            StopParticles(particles);
            DisableShadows(renderers);
        }

        private void StoreOriginalStates(
            FireOptimizationState state,
            MeshRenderer[] renderers,
            Light[] lights,
            ParticleSystem[] particles)
        {
            foreach (Light light in lights)
            {
                if (light == null || state.OriginalLightEnabled.ContainsKey(light))
                {
                    continue;
                }

                state.OriginalLightEnabled[light] = light.enabled;
            }

            foreach (ParticleSystem particle in particles)
            {
                if (particle == null || state.OriginalParticlePlaying.ContainsKey(particle))
                {
                    continue;
                }

                state.OriginalParticlePlaying[particle] = particle.IsAlive(true);
            }

            foreach (MeshRenderer renderer in renderers)
            {
                if (renderer == null || state.OriginalShadowModes.ContainsKey(renderer))
                {
                    continue;
                }

                state.OriginalShadowModes[renderer] = renderer.shadowCastingMode;
            }
        }

        private void DisableOriginalLights(Light[] lights)
        {
            foreach (Light light in lights)
            {
                if (light == null)
                {
                    continue;
                }

                light.enabled = false;
            }
        }

        private void DestroyAllProxyLights()
        {
            foreach (Piece piece in FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                if (piece == null)
                {
                    continue;
                }

                foreach (Transform child in piece.GetComponentsInChildren<Transform>(true))
                {
                    if (child != null && child.gameObject.name == StaticFireLightProxyName)
                    {
                        Destroy(child.gameObject);
                    }
                }
            }
        }

        private void RestoreParticles(FireOptimizationState state, ParticleSystem[] particles)
        {
            if (state == null || particles == null)
            {
                return;
            }

            foreach (ParticleSystem particle in particles)
            {
                if (particle == null)
                {
                    continue;
                }

                if (!state.OriginalParticlePlaying.TryGetValue(particle, out bool wasPlaying) || !wasPlaying)
                {
                    continue;
                }

                if (!particle.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!particle.isPlaying)
                {
                    particle.Play(true);
                }
            }
        }

        private void StopParticles(ParticleSystem[] particles)
        {
            foreach (ParticleSystem particle in particles)
            {
                if (particle == null)
                {
                    continue;
                }

                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void DisableShadows(MeshRenderer[] renderers)
        {
            foreach (MeshRenderer renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        private Light GetFirstUsableLight(Light[] lights)
        {
            foreach (Light light in lights)
            {
                if (light != null)
                {
                    return light;
                }
            }

            return null;
        }

        private void EnsureProxyLight(Piece piece, FireOptimizationState state, Light sourceLight)
        {
            if (piece == null || sourceLight == null || state == null)
            {
                return;
            }

            if (state.ProxyLightObject == null)
            {
                Transform existingProxy = piece.transform.Find(StaticFireLightProxyName);

                if (existingProxy != null)
                {
                    state.ProxyLightObject = existingProxy.gameObject;
                    state.ProxyLight = state.ProxyLightObject.GetComponent<Light>();

                    if (state.ProxyLight == null)
                    {
                        state.ProxyLight = state.ProxyLightObject.AddComponent<Light>();
                    }
                }
                else
                {
                    state.ProxyLightObject = new GameObject(StaticFireLightProxyName);
                    state.ProxyLightObject.transform.SetParent(piece.transform, false);
                    state.ProxyLight = state.ProxyLightObject.AddComponent<Light>();
                }
            }

            state.ProxyLightObject.transform.position = sourceLight.transform.position;
            state.ProxyLightObject.transform.rotation = sourceLight.transform.rotation;
            state.ProxyLightObject.SetActive(true);

            state.ProxyLight.type = LightType.Point;
            state.ProxyLight.color = sourceLight.color;
            state.ProxyLight.intensity = sourceLight.intensity * Mathf.Max(0f, _staticLightIntensityMultiplier.Value);
            state.ProxyLight.range = sourceLight.range * Mathf.Max(0f, _staticLightRangeMultiplier.Value);
            state.ProxyLight.shadows = LightShadows.None;
            state.ProxyLight.enabled = true;
        }

        private void DisableProxyLight(FireOptimizationState state)
        {
            if (state.ProxyLight != null)
            {
                state.ProxyLight.enabled = false;
            }

            if (state.ProxyLightObject != null)
            {
                state.ProxyLightObject.SetActive(false);
            }
        }

        private FireOptimizationState GetOrCreateFireState(Piece piece)
        {
            if (_fireStates.TryGetValue(piece, out FireOptimizationState state))
            {
                return state;
            }

            state = new FireOptimizationState
            {
                Piece = piece,
                LastVisibleTime = Time.time,
                LastIrrelevantTime = Time.time
            };

            _fireStates[piece] = state;
            return state;
        }

        private enum AppliedFireMode
        {
            None,
            StaticLight,
            FullCull
        }

        private class FireCandidate
        {
            public Piece Piece;
            public MeshRenderer[] Renderers;
            public Light[] OriginalLights;
            public ParticleSystem[] Particles;
        }

        private class FireOptimizationState
        {
            public Piece Piece;
            public AppliedFireMode AppliedMode = AppliedFireMode.None;

            public readonly Dictionary<Light, bool> OriginalLightEnabled =
                new Dictionary<Light, bool>();

            public readonly Dictionary<ParticleSystem, bool> OriginalParticlePlaying =
                new Dictionary<ParticleSystem, bool>();

            public readonly Dictionary<MeshRenderer, ShadowCastingMode> OriginalShadowModes =
                new Dictionary<MeshRenderer, ShadowCastingMode>();

            public GameObject ProxyLightObject;
            public Light ProxyLight;

            public float LastVisibleTime;
            public float LastIrrelevantTime;
        }

        private Vector3 GetFireTargetPosition(Piece piece, Light[] lights, MeshRenderer[] renderers)
        {
            if (lights != null && lights.Length > 0)
            {
                Vector3 sum = Vector3.zero;
                int count = 0;

                foreach (Light light in lights)
                {
                    if (light == null)
                    {
                        continue;
                    }

                    sum += light.transform.position;
                    count++;
                }

                if (count > 0)
                {
                    return sum / count;
                }
            }

            if (renderers != null && renderers.Length > 0)
            {
                Bounds bounds = new Bounds(piece.transform.position, Vector3.zero);
                bool hasBounds = false;

                foreach (MeshRenderer renderer in renderers)
                {
                    if (renderer == null)
                    {
                        continue;
                    }

                    if (!hasBounds)
                    {
                        bounds = renderer.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }

                if (hasBounds)
                {
                    return bounds.center;
                }
            }

            return piece.transform.position;
        }

        private bool IsOurProxyLight(Light light)
        {
            if (light == null)
            {
                return false;
            }

            if (light.gameObject == null)
            {
                return false;
            }

            return light.gameObject.name == StaticFireLightProxyName;
        }

        private Light[] GetOriginalLights(Light[] lights)
        {
            if (lights == null || lights.Length == 0)
            {
                return new Light[0];
            }

            List<Light> originalLights = new List<Light>();

            foreach (Light light in lights)
            {
                if (light == null)
                {
                    continue;
                }

                if (IsOurProxyLight(light))
                {
                    continue;
                }

                originalLights.Add(light);
            }

            return originalLights.ToArray();
        }

        private bool HitBelongsToPiece(RaycastHit hit, Piece piece)
        {
            if (piece == null || hit.collider == null)
            {
                return false;
            }

            Transform hitTransform = hit.collider.transform;

            while (hitTransform != null)
            {
                if (hitTransform == piece.transform)
                {
                    return true;
                }

                hitTransform = hitTransform.parent;
            }

            return false;
        }

        private bool IsFireOccludedFromCamera(
            Piece piece,
            Light[] lights,
            MeshRenderer[] renderers,
            Camera mainCamera)
        {
            if (!_useFireOcclusionCulling.Value)
            {
                return false;
            }

            if (piece == null || mainCamera == null)
            {
                return false;
            }

            Vector3 origin = mainCamera.transform.position;
            Vector3 target = GetFireTargetPosition(piece, lights, renderers);

            Vector3 direction = target - origin;
            float distance = direction.magnitude;

            if (distance <= 0.1f)
            {
                return false;
            }

            direction /= distance;

            RaycastHit hit;
            bool hasHit;

            float radius = Mathf.Max(0f, _fireOcclusionRayRadius.Value);

            if (radius > 0f)
            {
                hasHit = Physics.SphereCast(
                    origin,
                    radius,
                    direction,
                    out hit,
                    distance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore
                );
            }
            else
            {
                hasHit = Physics.Raycast(
                    origin,
                    direction,
                    out hit,
                    distance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore
                );
            }

            if (_debugFireOcclusion.Value)
            {
                Debug.DrawLine(
                    origin,
                    target,
                    hasHit ? Color.red : Color.green,
                    _optimizerUpdateInterval != null ? Mathf.Max(0.1f, _optimizerUpdateInterval.Value) : 0.5f
                );
            }

            if (!hasHit)
            {
                return false;
            }

            if (HitBelongsToPiece(hit, piece))
            {
                return false;
            }

            return true;
        }

        private bool ShouldUseStaticProxyLight(float distance, bool relevant)
        {
            if (relevant)
            {
                return distance <= Mathf.Max(0f, _staticLightProxyMaxDistance.Value);
            }

            return distance <= Mathf.Max(0f, _staticLightOccludedProxyMaxDistance.Value);
        }

        private void AddFireOptimizationStateMetrics(ref FireMetrics metrics)
        {
            foreach (FireOptimizationState state in _fireStates.Values)
            {
                if (state == null || state.AppliedMode == AppliedFireMode.None)
                {
                    continue;
                }

                metrics.OptimizedFirePieces++;

                if (state.AppliedMode == AppliedFireMode.StaticLight)
                {
                    metrics.StaticLightFirePieces++;
                }

                if (state.AppliedMode == AppliedFireMode.FullCull)
                {
                    metrics.FullCullFirePieces++;
                }

                if (state.ProxyLight != null &&
                    state.ProxyLight.enabled &&
                    state.ProxyLightObject != null &&
                    state.ProxyLightObject.activeInHierarchy)
                {
                    metrics.FireProxyLightsActive++;
                }

                foreach (KeyValuePair<Light, bool> kvp in state.OriginalLightEnabled)
                {
                    if (kvp.Key != null &&
                        !IsOurProxyLight(kvp.Key) &&
                        !kvp.Key.enabled &&
                        kvp.Value)
                    {
                        metrics.FireOriginalLightsDisabled++;
                    }
                }

                foreach (KeyValuePair<ParticleSystem, bool> kvp in state.OriginalParticlePlaying)
                {
                    if (kvp.Key != null && kvp.Value && !kvp.Key.IsAlive(true))
                    {
                        metrics.FireParticlesStopped++;
                    }
                }

                foreach (KeyValuePair<MeshRenderer, ShadowCastingMode> kvp in state.OriginalShadowModes)
                {
                    if (kvp.Key != null &&
                        kvp.Key.shadowCastingMode == ShadowCastingMode.Off &&
                        kvp.Value != ShadowCastingMode.Off)
                    {
                        metrics.FireShadowsDisabled++;
                    }
                }
            }
        }

        private void UpdateFireOptimizations()
        {
            if (_fireCullingMode.Value == FireCullingMode.Off)
            {
                if (HasActiveFireOptimizations())
                {
                    RestoreAllFireOptimizations();
                }

                ResetFireCandidateCache();
                return;
            }

            if (Player.m_localPlayer == null)
            {
                if (HasActiveFireOptimizations())
                {
                    RestoreAllFireOptimizations();
                }

                ResetFireCandidateCache();
                return;
            }

            Vector3 playerPosition = Player.m_localPlayer.transform.position;
            Camera mainCamera = Camera.main;

            if (Time.time >= _nextFireCandidateRefreshTime || _fireCandidates.Count == 0)
            {
                RefreshFireCandidateCache();
            }

            FireMetrics metrics = new FireMetrics();

            for (int i = _fireCandidates.Count - 1; i >= 0; i--)
            {
                FireCandidate candidate = _fireCandidates[i];

                if (candidate == null || candidate.Piece == null)
                {
                    _fireCandidates.RemoveAt(i);
                    continue;
                }

                Piece piece = candidate.Piece;

                if (piece == null)
                {
                    _fireCandidates.RemoveAt(i);
                    continue;
                }

                MeshRenderer[] renderers = candidate.Renderers;
                Light[] originalLights = candidate.OriginalLights;
                ParticleSystem[] particles = candidate.Particles;

                if (!IsFireCandidate(originalLights, particles))
                {
                    RestoreFire(piece);
                    _fireCandidates.RemoveAt(i);
                    continue;
                }

                bool rendererVisible = IsPieceVisible(renderers);
                bool occluded = rendererVisible && IsFireOccludedFromCamera(piece, originalLights, renderers, mainCamera);
                bool relevant = rendererVisible && !occluded;

                metrics.FireCandidates++;

                if (rendererVisible)
                {
                    metrics.RendererVisibleFireCandidates++;
                }

                if (occluded)
                {
                    metrics.OccludedFireCandidates++;
                }

                if (relevant)
                {
                    metrics.RelevantFireCandidates++;
                }
                else
                {
                    metrics.HiddenOrIrrelevantFireCandidates++;
                }

                float distance = Vector3.Distance(playerPosition, piece.transform.position);

                FireOptimizationState state = GetOrCreateFireState(piece);

                if (relevant)
                {
                    state.LastVisibleTime = Time.time;
                }
                else
                {
                    state.LastIrrelevantTime = Time.time;
                }

                bool hiddenOrIrrelevant = !relevant;

                bool hiddenLongEnough =
    _useFireVisibilityCulling.Value &&
    hiddenOrIrrelevant &&
    Time.time - state.LastVisibleTime >= Mathf.Max(0f, _fireVisibilityGraceSeconds.Value);

                bool relevantLongEnough =
                    relevant &&
                    Time.time - state.LastIrrelevantTime >= Mathf.Max(0f, _fireRestoreGraceSeconds.Value);

                bool beyondDistanceCull =
                    distance >= Mathf.Max(1f, _fireDistanceCullStart.Value);

                bool insideRestoreDistance =
                    distance <= Mathf.Max(1f, _fireDistanceRestore.Value);

                bool tooCloseToOptimize =
                    distance <= Mathf.Max(0f, _neverOptimizeFireWithin.Value);

                bool shouldOptimize =
                    !tooCloseToOptimize &&
                    (beyondDistanceCull || hiddenLongEnough);

                bool alreadyOptimized = state.AppliedMode != AppliedFireMode.None;
                bool shouldRestoreOptimizedFire =
                    relevantLongEnough &&
                    (insideRestoreDistance || !beyondDistanceCull);

                if (tooCloseToOptimize)
                {
                    RestoreFire(piece);
                    continue;
                }

                if (alreadyOptimized)
                {
                    if (shouldRestoreOptimizedFire)
                    {
                        RestoreFire(piece);
                        continue;
                    }
                }
                else
                {
                    if (!shouldOptimize)
                    {
                        continue;
                    }
                }

                if (_fireCullingMode.Value == FireCullingMode.StaticLight)
                {
                    bool useProxyLight = ShouldUseStaticProxyLight(distance, relevant);
                    bool stopParticles = !relevant;

                    ApplyStaticLight(
                        piece,
                        state,
                        renderers,
                        originalLights,
                        particles,
                        useProxyLight,
                        stopParticles
                    );
                }
                else if (_fireCullingMode.Value == FireCullingMode.FullCull)
                {
                    float minimumFullCullDistance = Mathf.Max(0f, _minimumFullCullDistance.Value);

                    if (distance >= minimumFullCullDistance || beyondDistanceCull)
                    {
                        ApplyFullCull(piece, state, renderers, originalLights, particles);
                    }
                    else
                    {
                        bool useProxyLight = ShouldUseStaticProxyLight(distance, relevant);
                        bool stopParticles = !relevant;

                        ApplyStaticLight(
                            piece,
                            state,
                            renderers,
                            originalLights,
                            particles,
                            useProxyLight,
                            stopParticles
                        );
                    }
                }
            }

            AddFireOptimizationStateMetrics(ref metrics);
            _fireMetrics = metrics;

            CleanupDestroyedFireStates();
        }

        private Counts _counts = new Counts();
        //start
        private ConfigEntry<bool> _enableProfiler;
        private ConfigEntry<bool> _enableConsoleLogging;
        private ConfigEntry<bool> _showProfilerOnStart;
        private ConfigEntry<float> _profilerPollInterval;
        private ConfigEntry<KeyCode> _toggleProfilerKey;

        private ConfigEntry<bool> _enableOptimizations;

        private ConfigEntry<float> _nearDistance;
        private ConfigEntry<float> _mediumDistance;
        private ConfigEntry<float> _farDistance;
        private ConfigEntry<float> _veryFarDistance;
        private ConfigEntry<float> _extremeDistance;
        private enum FireCullingMode
        {
            Off,
            StaticLight,
            FullCull
        }

        private readonly Dictionary<Piece, FireOptimizationState> _fireStates =
            new Dictionary<Piece, FireOptimizationState>();

        private readonly List<FireOptimizationState> _activeFireStates = new List<FireOptimizationState>();
        private readonly List<FireCandidate> _fireCandidates = new List<FireCandidate>();
        private FireMetrics _fireMetrics = new FireMetrics();
        private float _nextOptimizerUpdateTime;
        private float _nextFireCandidateRefreshTime;
        private bool _optimizationsWereActive;

        private ConfigEntry<float> _optimizerUpdateInterval;
        private ConfigEntry<float> _fireCandidateRefreshInterval;

        private ConfigEntry<FireCullingMode> _fireCullingMode;
        private ConfigEntry<bool> _useFireVisibilityCulling;
        private ConfigEntry<float> _fireVisibilityGraceSeconds;
        private ConfigEntry<float> _neverOptimizeFireWithin;
        private ConfigEntry<float> _minimumFullCullDistance;
        private ConfigEntry<float> _fireDistanceCullStart;
        private ConfigEntry<float> _fireDistanceRestore;
        private ConfigEntry<float> _staticLightIntensityMultiplier;
        private ConfigEntry<float> _staticLightRangeMultiplier;

        private ConfigEntry<bool> _useFireOcclusionCulling;
        private ConfigEntry<float> _fireOcclusionRayRadius;
        private ConfigEntry<bool> _debugFireOcclusion;

        private ConfigEntry<float> _staticLightProxyMaxDistance;
        private ConfigEntry<float> _staticLightOccludedProxyMaxDistance;

        private ConfigEntry<float> _fireRestoreGraceSeconds;
        //finish
        private void Awake()
        {
            _windowRect = _windowRectDefault;

            _enableProfiler = Config.Bind(
                "Profiler",
                "EnableProfiler",
                true,
                "Enables the profiler system. If false, no polling or overlay drawing is performed."
            );

            _enableConsoleLogging = Config.Bind(
                "Profiler",
                "EnableConsoleLogging",
                false,
                "Writes profiler counts to the BepInEx log each poll. Useful for testing, but can create large log files."
            );

            _showProfilerOnStart = Config.Bind(
                "Profiler",
                "ShowProfilerOnStart",
                true,
                "Shows the profiler overlay when the world loads."
            );

            _profilerPollInterval = Config.Bind(
                "Profiler",
                "PollIntervalSeconds",
                1.0f,
                "How often the profiler updates its counts, in seconds. Lower values update faster but cost more performance."
            );

            _toggleProfilerKey = Config.Bind(
                "Profiler",
                "ToggleProfilerKey",
                KeyCode.F7,
                "Keyboard key used to toggle the profiler overlay."
            );

            _enableOptimizations = Config.Bind(
                "Optimizations",
                "EnableOptimizations",
                false,
                "Master switch for optimization features. Currently reserved for future optimizer systems."
            );

            _nearDistance = Config.Bind(
                "DistanceThresholds",
                "NearDistance",
                10f,
                "Near distance in meters. Pieces within this range should usually remain fully vanilla."
            );

            _mediumDistance = Config.Bind(
                "DistanceThresholds",
                "MediumDistance",
                25f,
                "Medium distance in meters. Good candidate range for safe shadow/effect reductions."
            );

            _farDistance = Config.Bind(
                "DistanceThresholds",
                "FarDistance",
                50f,
                "Far distance in meters. Good candidate range for more aggressive visual optimizations."
            );

            _veryFarDistance = Config.Bind(
                "DistanceThresholds",
                "VeryFarDistance",
                100f,
                "Very far distance in meters. Future proxy/mesh-combining territory."
            );

            _extremeDistance = Config.Bind(
                "DistanceThresholds",
                "ExtremeDistance",
                200f,
                "Extreme distance in meters. Future impostor/proxy-only territory."
            );

            _optimizerUpdateInterval = Config.Bind(
                "Optimizations",
                "OptimizerUpdateIntervalSeconds",
                0.5f,
                "How often the optimizer checks fire pieces, in seconds. Lower values react faster but cost more performance."
            );

            _fireCandidateRefreshInterval = Config.Bind(
                "Optimizations",
                "FireCandidateRefreshIntervalSeconds",
                5.0f,
                "How often the optimizer does a full scene scan to discover fire pieces. Cached candidates are used between scans."
            );

            _fireRestoreGraceSeconds = Config.Bind(
            "FireCulling",
            "RestoreGraceSeconds",
            1.0f,
            "How long a fire must remain relevant/visible before optimized fire effects are restored. Helps prevent flickering near occlusion edges."
            );

            _fireCullingMode = Config.Bind(
                "FireCulling",
                "FireCullingMode",
                FireCullingMode.StaticLight,
                "Fire optimization mode. Off = no fire optimization, StaticLight = replace dynamic fire effects with a cheap static light, FullCull = disable fire visuals/effects at distance."
);

            _useFireOcclusionCulling = Config.Bind(
                "FireCulling",
                "UseOcclusionCulling",
                true,
                "If true, fire candidates that are inside the camera view but blocked by solid geometry are treated as hidden/irrelevant."
);

            _fireOcclusionRayRadius = Config.Bind(
                "FireCulling",
                "OcclusionRayRadius",
                0.05f,
                "Radius used for fire occlusion spherecasts. 0 uses a normal raycast. Small values like 0.05-0.15 can make occlusion detection more forgiving."
            );

            _debugFireOcclusion = Config.Bind(
                "FireCulling",
                "DebugFireOcclusion",
                false,
                "If true, draws debug rays for fire occlusion checks in the Unity scene view/logical debug view where supported."
            );

            _useFireVisibilityCulling = Config.Bind(
                "FireCulling",
                "UseVisibilityCulling",
                true,
                "If true, fire effects may be optimized when the fire is not visible for a short grace period."
            );

            _fireVisibilityGraceSeconds = Config.Bind(
                "FireCulling",
                "VisibilityGraceSeconds",
                1.5f,
                "How long a fire must remain not visible before visibility-based optimization can activate."
            );

            _neverOptimizeFireWithin = Config.Bind(
                "FireCulling",
                "NeverOptimizeFireWithin",
                2f,
                "Fire pieces closer than this distance are never optimized."
            );

            _minimumFullCullDistance = Config.Bind(
                "FireCulling",
                "MinimumFullCullDistance",
                15f,
                "Minimum distance required before FullCull mode may fully remove fire light/effects due to visibility."
            );

            _fireDistanceCullStart = Config.Bind(
                "FireCulling",
                "FireDistanceCullStart",
                50f,
                "Distance beyond which fire optimization activates regardless of visibility."
            );

            _fireDistanceRestore = Config.Bind(
                "FireCulling",
                "FireDistanceRestore",
                40f,
                "Distance within which fire optimization restores to normal. Should be lower than FireDistanceCullStart."
            );

            _staticLightIntensityMultiplier = Config.Bind(
                "FireCulling",
                "StaticLightIntensityMultiplier",
                0.65f,
                "Multiplier for proxy static light intensity in StaticLight mode."
            );

            _staticLightRangeMultiplier = Config.Bind(
                "FireCulling",
                "StaticLightRangeMultiplier",
                0.85f,
                "Multiplier for proxy static light range in StaticLight mode."
            );

            _staticLightProxyMaxDistance = Config.Bind(
                "FireCulling",
                "StaticLightProxyMaxDistance",
                50f,
                "Maximum distance at which StaticLight mode may create a proxy light for relevant/visible fires. Beyond this, optimized fires get particles/shadows removed but no replacement light."
            );

            _staticLightOccludedProxyMaxDistance = Config.Bind(
                "FireCulling",
                "StaticLightOccludedProxyMaxDistance",
                25f,
                "Maximum distance at which StaticLight mode may create a proxy light for hidden or occluded fires. Farther hidden fires receive no proxy light."
            );

            _showOverlay = _showProfilerOnStart.Value;

            _opaqueBackground = MakeTexture(
                2,
                2,
                new Color(0.05f, 0.05f, 0.05f, 1.0f)
            );

            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        private void Update()
        {
            if (_enableProfiler.Value)
            {
                if (UnityEngine.Input.GetKeyDown(_toggleProfilerKey.Value))
                {
                    _showOverlay = !_showOverlay;
                }

                bool shouldPollProfiler = _showOverlay || _enableConsoleLogging.Value;
                float pollInterval = Mathf.Max(0.1f, _profilerPollInterval.Value);

                if (shouldPollProfiler && Time.time >= _nextPollTime)
                {
                    _nextPollTime = Time.time + pollInterval;
                    _counts = PollCounts();

                    if (_enableConsoleLogging.Value)
                    {
                        Logger.LogInfo(
                            $"Pieces={_counts.Pieces}, " +
                            $"WearNTear={_counts.WearNTear}, " +
                            $"ZNetView={_counts.ZNetView}, " +
                            $"MeshRenderer={_counts.MeshRenderer}, " +
                            $"EnabledMeshRenderer={_counts.EnabledMeshRenderer}, " +
                            $"VisibleMeshRenderer={_counts.VisibleMeshRenderer}, " +
                            $"Collider={_counts.Collider}, " +
                            $"EnabledCollider={_counts.EnabledCollider}, " +
                            $"LODGroup={_counts.LODGroup}, " +
                            $"Light={_counts.Light}, " +
                            $"EnabledLight={_counts.EnabledLight}, " +
                            $"ParticleSystem={_counts.ParticleSystem}, " +
                            $"ActiveParticleSystem={_counts.ActiveParticleSystem}, " +
                            $"AudioSource={_counts.AudioSource}, " +
                            $"ActiveRigidbody={_counts.ActiveRigidbody}, " +
                            $"PieceMeshRenderer={_counts.PieceMeshRenderer}, " +
                            $"PieceEnabledMeshRenderer={_counts.PieceEnabledMeshRenderer}, " +
                            $"PieceVisibleMeshRenderer={_counts.PieceVisibleMeshRenderer}, " +
                            $"PieceCollider={_counts.PieceCollider}, " +
                            $"PieceEnabledCollider={_counts.PieceEnabledCollider}"
                        );
                    }
                }
            }

            bool optimizationsActive =
                _enableOptimizations.Value &&
                _fireCullingMode.Value != FireCullingMode.Off;

            if (!optimizationsActive)
            {
                if (_optimizationsWereActive || HasActiveFireOptimizations())
                {
                    RestoreAllFireOptimizations();
                    ResetFireCandidateCache();
                }

                _optimizationsWereActive = false;
                return;
            }

            if (!_optimizationsWereActive)
            {
                _nextOptimizerUpdateTime = 0f;
                _nextFireCandidateRefreshTime = 0f;
                _optimizationsWereActive = true;
            }

            float optimizerInterval = Mathf.Max(0.1f, _optimizerUpdateInterval.Value);

            if (Time.time >= _nextOptimizerUpdateTime)
            {
                _nextOptimizerUpdateTime = Time.time + optimizerInterval;
                UpdateFireOptimizations();
            }
        }

        private void OnGUI()
        {
            if (!_enableProfiler.Value || !_showOverlay)
            {
                return;
            }

            _windowRect = GUILayout.Window(
                872391,
                _windowRect,
                DrawWindow,
                "Build Piece Profiler",
                GUILayout.Width(520f),
                GUILayout.Height(940f)
            );
        }
        
        private void DrawWindow(int windowId)
        {
            GUI.DrawTexture(
                new Rect(0f, 0f, _windowRect.width, _windowRect.height),
                _opaqueBackground,
                ScaleMode.StretchToFill
            );

            _scrollPosition = GUILayout.BeginScrollView(
                _scrollPosition,
                GUILayout.Width(500f),
                GUILayout.Height(1050f)
            );

            GUILayout.Label("Toggle overlay: F7");
            GUILayout.Label($"Poll interval: {_profilerPollInterval.Value:0.0}s");
            GUILayout.Label($"Optimizations enabled: {_enableOptimizations.Value}");

            GUILayout.Space(8);
            GUILayout.Label("Global Scene Counts");
            GUILayout.Label($"Total Piece count: {_counts.Pieces}");
            GUILayout.Label($"Total WearNTear count: {_counts.WearNTear}");
            GUILayout.Label($"Total ZNetView count: {_counts.ZNetView}");

            GUILayout.Space(8);
            GUILayout.Label("Global Renderers");
            GUILayout.Label($"Total MeshRenderer count: {_counts.MeshRenderer}");
            GUILayout.Label($"Enabled MeshRenderer count: {_counts.EnabledMeshRenderer}");
            GUILayout.Label($"Visible MeshRenderer count: {_counts.VisibleMeshRenderer}");
            GUILayout.Label($"Total LODGroup count: {_counts.LODGroup}");

            GUILayout.Space(8);
            GUILayout.Label("Global Physics");
            GUILayout.Label($"Total Collider count: {_counts.Collider}");
            GUILayout.Label($"Enabled Collider count: {_counts.EnabledCollider}");
            GUILayout.Label($"Total active Rigidbody count: {_counts.ActiveRigidbody}");

            GUILayout.Space(8);
            GUILayout.Label("Global Effects");
            GUILayout.Label($"Total Light count: {_counts.Light}");
            GUILayout.Label($"Enabled Light count: {_counts.EnabledLight}");
            GUILayout.Label($"Total ParticleSystem count: {_counts.ParticleSystem}");
            GUILayout.Label($"Active ParticleSystem count: {_counts.ActiveParticleSystem}");
            GUILayout.Label($"Total AudioSource count: {_counts.AudioSource}");

            GUILayout.Space(12);
            GUILayout.Label("Build-Piece-Only Counts");
            GUILayout.Label($"Build-piece MeshRenderer count: {_counts.PieceMeshRenderer}");
            GUILayout.Label($"Build-piece enabled MeshRenderer count: {_counts.PieceEnabledMeshRenderer}");
            GUILayout.Label($"Build-piece visible MeshRenderer count: {_counts.PieceVisibleMeshRenderer}");
            GUILayout.Label($"Build-piece LODGroup count: {_counts.PieceLODGroup}");

            GUILayout.Space(8);
            GUILayout.Label("Build-Piece Distance Buckets");
            GUILayout.Label($"Pieces within Near ({_nearDistance.Value:0}m): {_counts.PiecesWithinNear}");
            GUILayout.Label($"Pieces within Medium ({_mediumDistance.Value:0}m): {_counts.PiecesWithinMedium}");
            GUILayout.Label($"Pieces within Far ({_farDistance.Value:0}m): {_counts.PiecesWithinFar}");
            GUILayout.Label($"Pieces within Very Far ({_veryFarDistance.Value:0}m): {_counts.PiecesWithinVeryFar}");
            GUILayout.Label($"Pieces within Extreme ({_extremeDistance.Value:0}m): {_counts.PiecesWithinExtreme}");
            GUILayout.Label($"Pieces beyond Extreme: {_counts.PiecesBeyondExtreme}");
            GUILayout.Label($"Nearest piece distance: {_counts.NearestPieceDistance:0.0}m");
            GUILayout.Label($"Farthest piece distance: {_counts.FarthestPieceDistance:0.0}m");

            GUILayout.Space(8);
            GUILayout.Label("Build-Piece Physics");
            GUILayout.Label($"Build-piece Collider count: {_counts.PieceCollider}");
            GUILayout.Label($"Build-piece enabled Collider count: {_counts.PieceEnabledCollider}");
            GUILayout.Label($"Build-piece Rigidbody count: {_counts.PieceRigidbody}");
            GUILayout.Label($"Build-piece active Rigidbody count: {_counts.PieceActiveRigidbody}");

            GUILayout.Space(8);
            GUILayout.Label("Build-Piece Effects");
            GUILayout.Label($"Build-piece Light count: {_counts.PieceLight}");
            GUILayout.Label($"Build-piece enabled Light count: {_counts.PieceEnabledLight}");
            GUILayout.Label($"Build-piece ParticleSystem count: {_counts.PieceParticleSystem}");
            GUILayout.Label($"Build-piece active ParticleSystem count: {_counts.PieceActiveParticleSystem}");
            GUILayout.Label($"Build-piece AudioSource count: {_counts.PieceAudioSource}");

            GUILayout.Space(8);
            GUILayout.Label("Fire Optimization Candidates");
            GUILayout.Label($"Fire candidates: {_counts.FireCandidates}");
            GUILayout.Label($"Renderer-visible fire candidates: {_counts.RendererVisibleFireCandidates}");
            GUILayout.Label($"Occluded fire candidates: {_counts.OccludedFireCandidates}");
            GUILayout.Label($"Relevant fire candidates: {_counts.RelevantFireCandidates}");
            GUILayout.Label($"Hidden/irrelevant fire candidates: {_counts.HiddenOrIrrelevantFireCandidates}");

            GUILayout.Label($"Fire culling mode: {_fireCullingMode.Value}");
            GUILayout.Label($"Visibility culling: {_useFireVisibilityCulling.Value}");
            GUILayout.Label($"Occlusion culling: {_useFireOcclusionCulling.Value}");

            GUILayout.Label($"Optimized fire pieces: {_counts.OptimizedFirePieces}");
            GUILayout.Label($"StaticLight fire pieces: {_counts.StaticLightFirePieces}");
            GUILayout.Label($"FullCull fire pieces: {_counts.FullCullFirePieces}");
            GUILayout.Label($"Fire proxy lights active: {_counts.FireProxyLightsActive}");
            GUILayout.Label($"Original fire lights disabled: {_counts.FireOriginalLightsDisabled}");
            GUILayout.Label($"Fire particles stopped: {_counts.FireParticlesStopped}");
            GUILayout.Label($"Fire shadows disabled: {_counts.FireShadowsDisabled}");

            GUILayout.Space(8);

            if (GUILayout.Button("Poll now"))
            {
                _counts = PollCounts();
            }

            GUILayout.EndScrollView();

            GUI.DragWindow();
        }

                private Texture2D _opaqueBackground;
                private Texture2D MakeTexture(int width, int height, Color color)
                {
                    Color[] pixels = new Color[width * height];

                    for (int i = 0; i < pixels.Length; i++)
                    {
                        pixels[i] = color;
                    }

                    Texture2D texture = new Texture2D(width, height);
                    texture.SetPixels(pixels);
                    texture.Apply();

                    return texture;
                }

        private Counts PollCounts()
        {
            // Global loaded-scene counts
            Piece[] pieces = FindObjectsByType<Piece>(FindObjectsSortMode.None);
            WearNTear[] wearNTears = FindObjectsByType<WearNTear>(FindObjectsSortMode.None);
            ZNetView[] zNetViews = FindObjectsByType<ZNetView>(FindObjectsSortMode.None);
            MeshRenderer[] meshRenderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            Collider[] colliders = FindObjectsByType<Collider>(FindObjectsSortMode.None);
            LODGroup[] lodGroups = FindObjectsByType<LODGroup>(FindObjectsSortMode.None);
            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            ParticleSystem[] particleSystems = FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
            AudioSource[] audioSources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            Rigidbody[] rigidbodies = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);

            int enabledMeshRenderers = 0;
            int visibleMeshRenderers = 0;
            int enabledColliders = 0;
            int enabledLights = 0;
            int activeParticles = 0;
            int activeRigidbodies = 0;

            foreach (MeshRenderer renderer in meshRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                {
                    enabledMeshRenderers++;
                }

                if (renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.isVisible)
                {
                    visibleMeshRenderers++;
                }
            }

            foreach (Collider collider in colliders)
            {
                if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy)
                {
                    enabledColliders++;
                }
            }

            foreach (Light light in lights)
            {
                if (light != null && light.enabled && light.gameObject.activeInHierarchy)
                {
                    enabledLights++;
                }
            }

            foreach (ParticleSystem ps in particleSystems)
            {
                if (ps != null && ps.gameObject.activeInHierarchy && ps.IsAlive(true))
                {
                    activeParticles++;
                }
            }

            foreach (Rigidbody rb in rigidbodies)
            {
                if (rb != null && rb.gameObject.activeInHierarchy && !rb.IsSleeping())
                {
                    activeRigidbodies++;
                }
            }

            // Build-piece-only counts
            int pieceMeshRenderers = 0;
            int pieceEnabledMeshRenderers = 0;
            int pieceVisibleMeshRenderers = 0;

            int pieceColliders = 0;
            int pieceEnabledColliders = 0;

            int pieceLODGroups = 0;

            int pieceLights = 0;
            int pieceEnabledLights = 0;

            int pieceParticleSystems = 0;
            int pieceActiveParticleSystems = 0;

            int pieceAudioSources = 0;

            int pieceRigidbodies = 0;
            int pieceActiveRigidbodies = 0;

            Vector3 playerPosition = Vector3.zero;
            bool hasPlayer = Player.m_localPlayer != null;

            if (hasPlayer)
            {
                playerPosition = Player.m_localPlayer.transform.position;
            }

            float near = Mathf.Max(1f, _nearDistance.Value);
            float medium = Mathf.Max(near, _mediumDistance.Value);
            float far = Mathf.Max(medium, _farDistance.Value);
            float veryFar = Mathf.Max(far, _veryFarDistance.Value);
            float extreme = Mathf.Max(veryFar, _extremeDistance.Value);

            int piecesWithinNear = 0;
            int piecesWithinMedium = 0;
            int piecesWithinFar = 0;
            int piecesWithinVeryFar = 0;
            int piecesWithinExtreme = 0;
            int piecesBeyondExtreme = 0;

            float nearestPieceDistance = float.MaxValue;
            float farthestPieceDistance = 0f;

            int fireCandidates = 0;
            int rendererVisibleFireCandidates = 0;
            int occludedFireCandidates = 0;
            int relevantFireCandidates = 0;
            int hiddenOrIrrelevantFireCandidates = 0;
            bool useCachedFireMetrics =
                _enableOptimizations.Value &&
                _fireCullingMode.Value != FireCullingMode.Off;
            Camera profilerCamera = useCachedFireMetrics ? null : Camera.main;

            foreach (Piece piece in pieces)
            {
                if (piece == null)
                {
                    continue;
                }

                if (hasPlayer)
                {
                    float distance = Vector3.Distance(playerPosition, piece.transform.position);

                    if (distance <= near)
                    {
                        piecesWithinNear++;
                    }

                    if (distance <= medium)
                    {
                        piecesWithinMedium++;
                    }

                    if (distance <= far)
                    {
                        piecesWithinFar++;
                    }

                    if (distance <= veryFar)
                    {
                        piecesWithinVeryFar++;
                    }

                    if (distance <= extreme)
                    {
                        piecesWithinExtreme++;
                    }
                    else
                    {
                        piecesBeyondExtreme++;
                    }

                    if (distance < nearestPieceDistance)
                    {
                        nearestPieceDistance = distance;
                    }

                    if (distance > farthestPieceDistance)
                    {
                        farthestPieceDistance = distance;
                    }
                }

                MeshRenderer[] pieceRenderers = piece.GetComponentsInChildren<MeshRenderer>(true);
                Collider[] pieceCols = piece.GetComponentsInChildren<Collider>(true);
                LODGroup[] pieceLods = piece.GetComponentsInChildren<LODGroup>(true);
                Light[] pieceLightComponents = piece.GetComponentsInChildren<Light>(true);
                ParticleSystem[] piecePsComponents = piece.GetComponentsInChildren<ParticleSystem>(true);

                if (!useCachedFireMetrics)
                {
                    Light[] originalPieceLights = GetOriginalLights(pieceLightComponents);

                    if (IsFireCandidate(originalPieceLights, piecePsComponents))
                    {
                        fireCandidates++;

                        bool rendererVisible = IsPieceVisible(pieceRenderers);
                        bool occluded = rendererVisible &&
                            IsFireOccludedFromCamera(piece, originalPieceLights, pieceRenderers, profilerCamera);
                        bool relevant = rendererVisible && !occluded;

                        if (rendererVisible)
                        {
                            rendererVisibleFireCandidates++;
                        }

                        if (occluded)
                        {
                            occludedFireCandidates++;
                        }

                        if (relevant)
                        {
                            relevantFireCandidates++;
                        }
                        else
                        {
                            hiddenOrIrrelevantFireCandidates++;
                        }
                    }
                }

                AudioSource[] pieceAudioComponents = piece.GetComponentsInChildren<AudioSource>(true);
                Rigidbody[] pieceRbComponents = piece.GetComponentsInChildren<Rigidbody>(true);

                pieceMeshRenderers += pieceRenderers.Length;
                pieceColliders += pieceCols.Length;
                pieceLODGroups += pieceLods.Length;
                pieceLights += pieceLightComponents.Length;
                pieceParticleSystems += piecePsComponents.Length;
                pieceAudioSources += pieceAudioComponents.Length;
                pieceRigidbodies += pieceRbComponents.Length;

                foreach (MeshRenderer renderer in pieceRenderers)
                {
                    if (renderer == null)
                    {
                        continue;
                    }

                    if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                    {
                        pieceEnabledMeshRenderers++;
                    }

                    if (renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.isVisible)
                    {
                        pieceVisibleMeshRenderers++;
                    }
                }

                foreach (Collider collider in pieceCols)
                {
                    if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy)
                    {
                        pieceEnabledColliders++;
                    }
                }

                foreach (Light light in pieceLightComponents)
                {
                    if (light != null && light.enabled && light.gameObject.activeInHierarchy)
                    {
                        pieceEnabledLights++;
                    }
                }

                foreach (ParticleSystem ps in piecePsComponents)
                {
                    if (ps != null && ps.gameObject.activeInHierarchy && ps.IsAlive(true))
                    {
                        pieceActiveParticleSystems++;
                    }
                }

                foreach (Rigidbody rb in pieceRbComponents)
                {
                    if (rb != null && rb.gameObject.activeInHierarchy && !rb.IsSleeping())
                    {
                        pieceActiveRigidbodies++;
                    }
                }
            }

            if (!hasPlayer || pieces.Length == 0)
            {
                nearestPieceDistance = 0f;
                farthestPieceDistance = 0f;
            }

            FireMetrics fireMetrics = useCachedFireMetrics
                ? _fireMetrics
                : new FireMetrics
                {
                    FireCandidates = fireCandidates,
                    RendererVisibleFireCandidates = rendererVisibleFireCandidates,
                    OccludedFireCandidates = occludedFireCandidates,
                    RelevantFireCandidates = relevantFireCandidates,
                    HiddenOrIrrelevantFireCandidates = hiddenOrIrrelevantFireCandidates
                };

            if (!useCachedFireMetrics)
            {
                AddFireOptimizationStateMetrics(ref fireMetrics);
            }

            return new Counts
            {
                // Global
                Pieces = pieces.Length,
                WearNTear = wearNTears.Length,
                ZNetView = zNetViews.Length,

                MeshRenderer = meshRenderers.Length,
                EnabledMeshRenderer = enabledMeshRenderers,
                VisibleMeshRenderer = visibleMeshRenderers,

                Collider = colliders.Length,
                EnabledCollider = enabledColliders,

                LODGroup = lodGroups.Length,

                Light = lights.Length,
                EnabledLight = enabledLights,

                ParticleSystem = particleSystems.Length,
                ActiveParticleSystem = activeParticles,

                AudioSource = audioSources.Length,
                ActiveRigidbody = activeRigidbodies,

                // Build-piece-only
                PieceMeshRenderer = pieceMeshRenderers,
                PieceEnabledMeshRenderer = pieceEnabledMeshRenderers,
                PieceVisibleMeshRenderer = pieceVisibleMeshRenderers,

                PieceCollider = pieceColliders,
                PieceEnabledCollider = pieceEnabledColliders,

                PieceLODGroup = pieceLODGroups,

                PieceLight = pieceLights,
                PieceEnabledLight = pieceEnabledLights,

                PieceParticleSystem = pieceParticleSystems,
                PieceActiveParticleSystem = pieceActiveParticleSystems,

                PieceAudioSource = pieceAudioSources,
                PieceRigidbody = pieceRigidbodies,
                PieceActiveRigidbody = pieceActiveRigidbodies,

                //Build-Piece Distance Buckets
                PiecesWithinNear = piecesWithinNear,
                PiecesWithinMedium = piecesWithinMedium,
                PiecesWithinFar = piecesWithinFar,
                PiecesWithinVeryFar = piecesWithinVeryFar,
                PiecesWithinExtreme = piecesWithinExtreme,
                PiecesBeyondExtreme = piecesBeyondExtreme,

                NearestPieceDistance = nearestPieceDistance,
                FarthestPieceDistance = farthestPieceDistance,

                // Fire Optimization Candidates
                FireCandidates = fireMetrics.FireCandidates,
                RendererVisibleFireCandidates = fireMetrics.RendererVisibleFireCandidates,
                OccludedFireCandidates = fireMetrics.OccludedFireCandidates,
                RelevantFireCandidates = fireMetrics.RelevantFireCandidates,
                HiddenOrIrrelevantFireCandidates = fireMetrics.HiddenOrIrrelevantFireCandidates,

                OptimizedFirePieces = fireMetrics.OptimizedFirePieces,
                StaticLightFirePieces = fireMetrics.StaticLightFirePieces,
                FullCullFirePieces = fireMetrics.FullCullFirePieces,
                FireProxyLightsActive = fireMetrics.FireProxyLightsActive,
                FireOriginalLightsDisabled = fireMetrics.FireOriginalLightsDisabled,
                FireParticlesStopped = fireMetrics.FireParticlesStopped,
                FireShadowsDisabled = fireMetrics.FireShadowsDisabled
            };
        }

        private struct FireMetrics
        {
            public int FireCandidates;
            public int RendererVisibleFireCandidates;
            public int OccludedFireCandidates;
            public int RelevantFireCandidates;
            public int HiddenOrIrrelevantFireCandidates;

            public int OptimizedFirePieces;
            public int StaticLightFirePieces;
            public int FullCullFirePieces;
            public int FireProxyLightsActive;
            public int FireOriginalLightsDisabled;
            public int FireParticlesStopped;
            public int FireShadowsDisabled;
        }

      //Counts struct 
        private struct Counts
        {
            // Global scene counts
            public int Pieces;
            public int WearNTear;
            public int ZNetView;

            public int MeshRenderer;
            public int EnabledMeshRenderer;
            public int VisibleMeshRenderer;

            public int Collider;
            public int EnabledCollider;

            public int LODGroup;

            public int Light;
            public int EnabledLight;

            public int ParticleSystem;
            public int ActiveParticleSystem;

            public int AudioSource;
            public int ActiveRigidbody;

            // Build-piece-only counts
            public int PieceMeshRenderer;
            public int PieceEnabledMeshRenderer;
            public int PieceVisibleMeshRenderer;

            public int PieceCollider;
            public int PieceEnabledCollider;

            public int PieceLODGroup;

            public int PieceLight;
            public int PieceEnabledLight;

            public int PieceParticleSystem;
            public int PieceActiveParticleSystem;

            public int PieceAudioSource;
            public int PieceRigidbody;
            public int PieceActiveRigidbody;

            // Build-Piece Distance Buckets
            public int PiecesWithinNear;
            public int PiecesWithinMedium;
            public int PiecesWithinFar;
            public int PiecesWithinVeryFar;
            public int PiecesWithinExtreme;
            public int PiecesBeyondExtreme;

            public float NearestPieceDistance;
            public float FarthestPieceDistance;

            // Fire Optimization Candidates
            public int FireCandidates;
            public int RendererVisibleFireCandidates;
            public int OccludedFireCandidates;
            public int RelevantFireCandidates;
            public int HiddenOrIrrelevantFireCandidates;

            public int OptimizedFirePieces;
            public int StaticLightFirePieces;
            public int FullCullFirePieces;
            public int FireProxyLightsActive;
            public int FireOriginalLightsDisabled;
            public int FireParticlesStopped;
            public int FireShadowsDisabled;

        }
    }
}

