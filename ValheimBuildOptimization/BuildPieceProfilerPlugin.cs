using BepInEx;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BepInEx.Configuration;

namespace BuildPieceProfiler
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class BuildPieceProfilerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "valheim.buildpieceprofiler";
        public const string PluginName = "Build Piece Profiler";
        public const string PluginVersion = "0.2.0";

        private readonly Rect _windowRectDefault = new Rect(20f, 40f, 520f, 980f);
        private Rect _windowRect;
        private Vector2 _scrollPosition = Vector2.zero;

        private bool _showOverlay;
        private float _nextPollTime;

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
            if (!_enableProfiler.Value)
            {
                return;
            }

            if (UnityEngine.Input.GetKeyDown(_toggleProfilerKey.Value))
            {
                _showOverlay = !_showOverlay;
            }

            float pollInterval = Mathf.Max(0.1f, _profilerPollInterval.Value);

            if (Time.time >= _nextPollTime)
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
            // Global loaded-scene counts.
            Piece[] pieces = FindObjectsOfType<Piece>();
            WearNTear[] wearNTears = FindObjectsOfType<WearNTear>();
            ZNetView[] zNetViews = FindObjectsOfType<ZNetView>();
            MeshRenderer[] meshRenderers = FindObjectsOfType<MeshRenderer>();
            Collider[] colliders = FindObjectsOfType<Collider>();
            LODGroup[] lodGroups = FindObjectsOfType<LODGroup>();
            Light[] lights = FindObjectsOfType<Light>();
            ParticleSystem[] particleSystems = FindObjectsOfType<ParticleSystem>();
            AudioSource[] audioSources = FindObjectsOfType<AudioSource>();
            Rigidbody[] rigidbodies = FindObjectsOfType<Rigidbody>();

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

            // Build-piece-only counts.
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
                FarthestPieceDistance = farthestPieceDistance
            };
        }

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
        }
    }
}

