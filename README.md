# Build Piece Profiler

Build Piece Profiler is a Valheim BepInEx plugin for investigating large-build performance problems.

The plugin displays an in-game overlay with counts for loaded scene objects, build-piece-specific components, renderer visibility, colliders, effects, rigidbodies, and distance buckets around the player.

The current version is primarily a measurement/profiling tool. Optimization features are planned and are controlled by configuration switches.

## Requirements

- Valheim
- BepInExPack Valheim
- BepInEx 5
- Windows/Linux compatible with BepInEx, depending on your Valheim setup

## Installation

1. Install BepInExPack Valheim.
2. Start Valheim once so BepInEx creates its folders.
3. Copy `BuildPieceProfiler.dll` into: Valheim/BepInEx/plugins/
4. Start Valheim.
5. Press F7 in-game to toggle the profiler overlay.

## Configuration

After the plugin runs once, a config file is created at: Valheim/BepInEx/config/mikael.valheim.buildpieceprofiler.cfg

## Profiler settings

[Profiler]

EnableProfiler = true

Enables or disables the profiler system.

ShowProfilerOnStart = true

Controls whether the overlay is visible when the plugin starts.

PollIntervalSeconds = 1

Controls how often the profiler updates. Lower values update faster but cost more performance.

ToggleProfilerKey = F7

Controls the key used to show or hide the overlay.

[Optimizations]

EnableOptimizations = false

Master switch for future optimization features. In the current measurement-focused version, this setting is present so future optimization systems can be added safely.

[DistanceThresholds]

NearDistance = 10
MediumDistance = 25
FarDistance = 50
VeryFarDistance = 100
ExtremeDistance = 200

These thresholds are used by the profiler to group build pieces by distance from the local player.

The intended future meaning is:

Near: pieces should remain fully vanilla and interactive.
Medium: safe visual reductions may be possible.
Far: stronger visual optimizations may be possible.
Very Far: proxy rendering or mesh combining may be appropriate.
Extreme: impostor/proxy-only representation may be appropriate.

## Overlay Metrics Explained

Global Scene Counts

These counts include all active loaded scene objects, not just player-built pieces.

Total Piece count

Number of loaded Piece components. These are Valheim build-piece components.

Total WearNTear count

Number of loaded WearNTear components. These usually handle health, damage, repair, rain decay, support/stability, destruction, and visual wear state.

Total ZNetView count

Number of loaded ZNetView components. These represent networked/persistent objects.

Global Renderers
Total MeshRenderer count

Number of loaded MeshRenderer components in the scene.

Enabled MeshRenderer count

Number of loaded MeshRenderers that are enabled and active in the hierarchy.

Visible MeshRenderer count

Number of enabled MeshRenderers currently considered visible by Unity.

This can change when turning the camera. A renderer can be enabled but not visible.

Total LODGroup count

Number of loaded LODGroup components.

Global Physics
Total Collider count

Number of loaded Collider components in the scene.

Enabled Collider count

Number of colliders that are enabled and active.

Total active Rigidbody count

Number of loaded rigidbodies that are active and not sleeping.

Global Effects
Total Light count

Number of loaded Light components.

Enabled Light count

Number of Light components that are enabled and active.

Total ParticleSystem count

Number of loaded ParticleSystem components.

Active ParticleSystem count

Number of ParticleSystem components that are alive.

Total AudioSource count

Number of loaded AudioSource components.

Build-Piece-Only Counts

These counts only include components found under loaded Piece objects and their children.

This section is important because it separates player-built structures from general world objects, terrain clutter, creatures, items, UI objects, and other scene components.

Build-piece MeshRenderer count

Total MeshRenderer components under Piece objects.

Build-piece enabled MeshRenderer count

Enabled and active MeshRenderers under Piece objects.

This is a key metric for future renderer optimization.

Build-piece visible MeshRenderer count

Build-piece MeshRenderers currently considered visible by Unity.

If this changes when turning the camera, normal renderer visibility culling is happening. If enabled counts remain the same, the objects are still active even while not visible.

Build-piece LODGroup count

LODGroups under Piece objects.

Build-Piece Distance Buckets

These are cumulative distance buckets around the local player.

For example, if:

Pieces within Near: 10
Pieces within Medium: 40
Pieces within Far: 80

that means:

10 pieces are within the Near threshold.
40 pieces are within the Medium threshold total.
80 pieces are within the Far threshold total.

The buckets are useful for deciding future optimization thresholds.

Build-Piece Physics
Build-piece Collider count

Total Collider components under Piece objects.

Build-piece enabled Collider count

Enabled and active Colliders under Piece objects.

This is important for future collider simplification or distance-based collider disabling experiments.

Build-piece Rigidbody count

Rigidbody components under Piece objects.

Build-piece active Rigidbody count

Build-piece rigidbodies that are active and not sleeping.

Build-Piece Effects
Build-piece Light count

Light components under Piece objects.

Build-piece enabled Light count

Enabled and active Light components under Piece objects.

Build-piece ParticleSystem count

ParticleSystem components under Piece objects.

Build-piece active ParticleSystem count

Particle systems under Piece objects that are alive.

Build-piece AudioSource count

AudioSource components under Piece objects.

## How to Benchmark

Use repeatable testing conditions.

Suggested procedure:

Load into the same world.
Stand at the same position.
Set the same time of day if using devcommands. (for example, tod 0.35)
Open Valheim's F2 performance panel.
Open the Build Piece Profiler overlay.
Wait 3–5 seconds after moving or turning.
Take a screenshot.
Turn toward/away from the build.
Wait 3–5 seconds again.
Take another screenshot.

Recommended notes per screenshot:

Location
Camera direction
Time of day
F2 instances
FPS / frame time
Build-piece enabled MeshRenderer count
Build-piece visible MeshRenderer count
Build-piece enabled Collider count
Build-piece enabled Light count
Build-piece active ParticleSystem count
Interpreting Results

If visible MeshRenderer count changes when turning the camera, Unity renderer visibility culling is working.

If enabled MeshRenderer, Collider, Piece, WearNTear, and ZNetView counts stay the same, then objects are still loaded and active even when not visible.

This means the game may avoid drawing off-screen objects, but it does not necessarily unload, disable, merge, or sleep build pieces.

## Planned Optimization Ideas

Planned features include:

Culling distant build-piece shadows.
Culling or reducing distant lights.
Culling or reducing distant particles and smoke.
Distance-based renderer simplification.
Distant build-piece proxy rendering.
Mesh combining for static build clusters.
Collider simplification for far-away structures.
Optional WearNTear/support throttling.

The safest first optimizations are visual-only features such as shadow, light, and particle culling.

Known Limitations
Renderer.isVisible can be affected by any camera, not only the main player camera.
Counts may fluctuate due to zone loading, temporary effects, items, creatures, UI, or runtime object creation/destruction.
The profiler uses periodic object searches, so very low poll intervals may cause overhead.
Build-piece-only counts include child components under Piece objects, which can include disabled variant renderers or LOD-related child objects.
The plugin is currently a profiling tool, not a full optimizer.
