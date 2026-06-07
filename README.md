# Build Piece Optimizer

Build Piece Optimizer is a Valheim BepInEx plugin focused on reducing large-build performance costs caused by many loaded build pieces, lights, particles, shadows, and effects.

The plugin includes an in-game profiler overlay because optimization work needs measurement. The profiler is a diagnostic companion: it shows what is loaded, what is visible, what is build-piece-specific, and what the optimizer is currently doing.

## Disclaimer

This is an early optimization-first alpha.

The profiler is usable for testing and benchmarking. Optimization features are experimental and opt-in. They may change visual behavior, especially around torches, hearths, braziers, campfires, smoke, flames, shadows, and lights.

Use the optimization features carefully, test in a copy of your world if you are concerned, and report issues with screenshots, config values, and profiler metrics where possible.

## Current Status

### Current optimization features

- Experimental `StaticLight` mode for fire-like build pieces.
- Experimental `FullCull` mode for aggressive testing.
- Visibility-aware fire optimization.
- Occlusion-aware fire relevance checks for fires blocked by walls or other geometry.
- Original fire-light disabling.
- Static proxy light replacement.
- Proxy-light distance limits.
- Shadow disabling for optimized fire pieces.
- Safe cleanup of generated proxy lights.
- Restore logic when optimizations are disabled or when the plugin unloads.

### Diagnostic/profiler features

- In-game profiler overlay.
- Global scene component counts.
- Build-piece-only component counts.
- Renderer enabled/visible counts.
- Collider, Rigidbody, Light, ParticleSystem, and AudioSource counts.
- Build-piece distance buckets.
- Fire candidate detection.
- Renderer-visible, occluded, relevant, and hidden/irrelevant fire candidate metrics.
- Optimizer state metrics, including optimized fire pieces, active proxy lights, disabled original lights, stopped particles, and disabled shadows.
- Configurable profiler and optimizer settings.

### Not yet considered stable

- Particle/smoke culling behavior.
- Flame visual handling in `StaticLight`.
- `FullCull` visual behavior.
- Compatibility with broad performance overhaul mods that also modify lights, particles, smoke, or `WearNTear` behavior.
- Any optimization that touches colliders, `Piece`, `WearNTear`, `ZNetView`, support logic, or save/network state.

## Roadmap

- `v0.3.x` - Fire/light optimization foundation
  - Fire candidate detection.
  - Visibility and occlusion-aware relevance checks.
  - `StaticLight` mode.
  - `FullCull` mode.
  - Proxy light policy and cleanup.
  - Stable restore behavior.
- `v0.4.x` - Stable StaticLight policy and safer proxy-light limits
  - Better fire light replacement rules.
  - Better handling of close, hidden, and occluded lights.
  - More detailed optimizer metrics.
- `v0.5.x` - Particle/smoke-specific optimization
  - Separate smoke and flame particle handling.
  - Safer behavior for visible torches.
  - Potential prefab whitelist/blacklist support.
- `v0.6.x` - Static build-piece visual simplification experiments.
- `v0.7+` - Mesh proxy / batching experiments for safe static build clusters.
- Later - Collider simplification experiments.
- Much later - Optional `WearNTear` / support throttling.

## Requirements

- Valheim
- BepInExPack Valheim
- BepInEx 5
- Windows/Linux compatible with BepInEx, depending on your Valheim setup

## Installation

1. Install BepInExPack Valheim.
2. Start Valheim once so BepInEx creates its folders.
3. Copy `BuildPieceProfiler.dll` into:

   `Valheim/BepInEx/plugins/`

4. Start Valheim.
5. Press `F7` in-game to toggle the profiler overlay.

## Configuration

After the plugin runs once, a config file is created at:

`Valheim/BepInEx/config/mikael.valheim.buildpieceprofiler.cfg`

Depending on the plugin GUID used in your local build, the config filename may differ slightly.

## Profiler Settings

```ini
[Profiler]

EnableProfiler = true
EnableConsoleLogging = false
ShowProfilerOnStart = true
PollIntervalSeconds = 1
ToggleProfilerKey = F7
```

### EnableProfiler

Enables or disables the profiler system. If disabled, the overlay and profiler polling are not used.

### EnableConsoleLogging

Writes profiler counts to the BepInEx log each poll. This is useful for testing, but can create large log files during long sessions.

### ShowProfilerOnStart

Controls whether the overlay is visible when the plugin starts.

### PollIntervalSeconds

Controls how often the profiler updates. Lower values update faster but cost more performance.

### ToggleProfilerKey

Controls the key used to show or hide the overlay.

## Optimization Settings

```ini
[Optimizations]

EnableOptimizations = false
OptimizerUpdateIntervalSeconds = 0.5
```

### EnableOptimizations

Master switch for optimization features.

When disabled, the plugin should restore tracked fire optimization states and remove proxy lights created by the plugin.

### OptimizerUpdateIntervalSeconds

Controls how often the optimizer scans fire candidates and updates their optimization state. Lower values react faster, but cost more performance.

## Distance Threshold Settings

```ini
[DistanceThresholds]

NearDistance = 10
MediumDistance = 25
FarDistance = 50
VeryFarDistance = 100
ExtremeDistance = 200
```

These thresholds are used by the profiler to group build pieces by distance from the local player.

Intended meaning:

- Near: pieces should usually remain fully vanilla and interactive.
- Medium: safe visual reductions may be possible.
- Far: stronger visual optimizations may be possible.
- Very Far: proxy rendering or mesh combining may be appropriate.
- Extreme: impostor/proxy-only representation may be appropriate.

## Fire Culling Settings

```ini
[FireCulling]

FireCullingMode = StaticLight
UseVisibilityCulling = true
UseOcclusionCulling = true
VisibilityGraceSeconds = 1.5
RestoreGraceSeconds = 1.0
NeverOptimizeFireWithin = 2
MinimumFullCullDistance = 15
FireDistanceCullStart = 50
FireDistanceRestore = 40
StaticLightIntensityMultiplier = 0.45
StaticLightRangeMultiplier = 0.65
StaticLightProxyMaxDistance = 50
StaticLightOccludedProxyMaxDistance = 20
OcclusionRayRadius = 0.05
DebugFireOcclusion = false
```

### FireCullingMode

Controls how fire-like build pieces are optimized.

Valid modes:

- `Off`
  - No fire optimization.
  - Any tracked optimized fires should be restored.
- `StaticLight`
  - Intended default experimental mode.
  - Disables original dynamic fire lights.
  - Uses a limited static proxy light when policy allows.
  - Disables shadows for optimized fires.
  - Particle/flame handling is intentionally conservative and still under development.
- `FullCull`
  - Aggressive experimental mode.
  - Disables original lights.
  - Disables proxy light.
  - May stop particles.
  - Disables shadows.
  - This mode is visually destructive and mainly useful for testing maximum possible gains.

### UseVisibilityCulling

If true, fire effects may be optimized when a fire is hidden or irrelevant for long enough.

### UseOcclusionCulling

If true, fire candidates that are inside the camera view but blocked by solid geometry can be treated as hidden/irrelevant.

This is useful for cases such as torches behind walls.

### VisibilityGraceSeconds

How long a fire must remain hidden/irrelevant before visibility-based optimization can activate.

### RestoreGraceSeconds

How long a fire must remain relevant before optimized fire effects are restored. This helps prevent rapid on/off cycling near occlusion edges.

### NeverOptimizeFireWithin

Fire pieces closer than this distance are restored and should remain vanilla. This is a safety threshold.

### MinimumFullCullDistance

Minimum distance required before `FullCull` mode may fully remove fire light/effects due to visibility or occlusion.

### FireDistanceCullStart

Distance beyond which fire optimization can activate regardless of visibility.

### FireDistanceRestore

Distance within which fire optimization can restore to normal if the fire is relevant long enough. This should usually be lower than `FireDistanceCullStart` to avoid threshold flickering.

### StaticLightIntensityMultiplier

Multiplier applied to the original fire light intensity when creating a proxy static light.

Lower values are safer for performance and reduce over-brightening.

### StaticLightRangeMultiplier

Multiplier applied to the original fire light range when creating a proxy static light.

Lower values reduce light overlap and improve performance.

### StaticLightProxyMaxDistance

Maximum distance at which `StaticLight` mode may create a proxy light for relevant/visible fires. Beyond this, optimized fires may receive no replacement light.

### StaticLightOccludedProxyMaxDistance

Maximum distance at which `StaticLight` mode may create a proxy light for hidden or occluded fires. Farther hidden fires receive no proxy light.

### OcclusionRayRadius

Radius used for fire occlusion spherecasts.

- `0` uses a normal raycast.
- Small values such as `0.01` to `0.05` may make occlusion checks more forgiving.
- Larger values can incorrectly classify nearby geometry as blocking the fire.

### DebugFireOcclusion

Attempts to draw debug rays for occlusion checks. In a built Valheim game, Unity `Debug.DrawLine` may not be visible in the normal game view, so the overlay metrics are usually the more useful debugging tool.

## Overlay Metrics Explained

## Global Scene Counts

These counts include all active loaded scene objects, not just player-built pieces.

### Total Piece count

Number of loaded `Piece` components. These are Valheim build-piece components.

### Total WearNTear count

Number of loaded `WearNTear` components. These usually handle health, damage, repair, rain decay, support/stability, destruction, and visual wear state.

### Total ZNetView count

Number of loaded `ZNetView` components. These represent networked/persistent objects.

## Global Renderers

### Total MeshRenderer count

Number of loaded `MeshRenderer` components in the scene.

### Enabled MeshRenderer count

Number of loaded MeshRenderers that are enabled and active in the hierarchy.

### Visible MeshRenderer count

Number of enabled MeshRenderers currently considered visible by Unity.

This can change when turning the camera. A renderer can be enabled but not visible.

### Total LODGroup count

Number of loaded `LODGroup` components.

## Global Physics

### Total Collider count

Number of loaded `Collider` components in the scene.

### Enabled Collider count

Number of colliders that are enabled and active.

### Total active Rigidbody count

Number of loaded rigidbodies that are active and not sleeping.

## Global Effects

### Total Light count

Number of loaded `Light` components.

### Enabled Light count

Number of Light components that are enabled and active.

### Total ParticleSystem count

Number of loaded `ParticleSystem` components.

### Active ParticleSystem count

Number of ParticleSystem components that are alive.

### Total AudioSource count

Number of loaded `AudioSource` components.

## Build-Piece-Only Counts

These counts only include components found under loaded `Piece` objects and their children.

This section is important because it separates player-built structures from general world objects, terrain clutter, creatures, items, UI objects, and other scene components.

### Build-piece MeshRenderer count

Total MeshRenderer components under Piece objects.

### Build-piece enabled MeshRenderer count

Enabled and active MeshRenderers under Piece objects.

This is a key metric for future renderer optimization.

### Build-piece visible MeshRenderer count

Build-piece MeshRenderers currently considered visible by Unity.

If this changes when turning the camera, normal renderer visibility culling is happening. If enabled counts remain the same, the objects are still active even while not visible.

### Build-piece LODGroup count

LODGroups under Piece objects.

## Build-Piece Distance Buckets

These are cumulative distance buckets around the local player.

For example:

```text
Pieces within Near: 10
Pieces within Medium: 40
Pieces within Far: 80
```

This means:

- 10 pieces are within the Near threshold.
- 40 pieces are within the Medium threshold total.
- 80 pieces are within the Far threshold total.

The buckets are useful for deciding optimization thresholds.

## Build-Piece Physics

### Build-piece Collider count

Total Collider components under Piece objects.

### Build-piece enabled Collider count

Enabled and active Colliders under Piece objects.

This is important for future collider simplification or distance-based collider disabling experiments.

### Build-piece Rigidbody count

Rigidbody components under Piece objects.

### Build-piece active Rigidbody count

Build-piece rigidbodies that are active and not sleeping.

## Build-Piece Effects

### Build-piece Light count

Light components under Piece objects.

### Build-piece enabled Light count

Enabled and active Light components under Piece objects.

### Build-piece ParticleSystem count

ParticleSystem components under Piece objects.

### Build-piece active ParticleSystem count

Particle systems under Piece objects that are alive.

### Build-piece AudioSource count

AudioSource components under Piece objects.

## Fire Optimization Metrics

### Fire candidates

Number of build pieces detected as fire-like candidates.

The current detection rule is component-based:

```text
original build-piece lights > 0
AND
particle systems > 0
```

Proxy lights created by this plugin are ignored when detecting original fire candidates.

### Renderer-visible fire candidates

Fire candidates with at least one child MeshRenderer that Unity currently considers visible.

### Occluded fire candidates

Renderer-visible fire candidates that appear to be blocked from the camera by geometry according to an occlusion raycast/spherecast.

### Relevant fire candidates

Fire candidates that are renderer-visible and not occluded.

### Hidden/irrelevant fire candidates

Fire candidates that are either not renderer-visible or are considered occluded.

### Optimized fire pieces

Number of fire candidates currently in an optimized state.

### StaticLight fire pieces

Number of optimized fire pieces currently using `StaticLight` mode.

### FullCull fire pieces

Number of optimized fire pieces currently using `FullCull` mode.

### Fire proxy lights active

Number of plugin-created proxy static lights currently active.

If this number is too high, FPS can drop because even no-shadow lights still cost performance.

### Original fire lights disabled

Number of original fire lights that were originally enabled and are currently disabled by the optimizer.

### Fire particles stopped

Number of tracked fire particle systems that were originally alive and are currently stopped.

This metric is mainly relevant to `FullCull` and particle-culling experiments. `StaticLight` particle behavior is still being refined.

### Fire shadows disabled

Number of tracked fire renderers whose original shadow mode was not `Off` and is currently forced to `Off`.

## How to Benchmark

Use repeatable testing conditions.

Suggested procedure:

1. Load into the same world.
2. Stand at the same position.
3. Set the same time of day if using devcommands, for example `tod 0.35`.
4. Open Valheim's F2 performance panel.
5. Open the Build Piece Optimizer overlay.
6. Wait 3-5 seconds after moving or turning.
7. Take a screenshot.
8. Turn toward/away from the build.
9. Wait 3-5 seconds again.
10. Take another screenshot.

Recommended notes per screenshot:

- Location
- Camera direction
- Time of day
- F2 instances
- FPS / frame time
- Build-piece enabled MeshRenderer count
- Build-piece visible MeshRenderer count
- Build-piece enabled Collider count
- Build-piece enabled Light count
- Build-piece active ParticleSystem count
- Fire candidates
- Renderer-visible fire candidates
- Occluded fire candidates
- Relevant fire candidates
- Hidden/irrelevant fire candidates
- Optimized fire pieces
- Proxy lights active

## Interpreting Results

If visible MeshRenderer count changes when turning the camera, Unity renderer visibility culling is working.

If enabled MeshRenderer, Collider, Piece, WearNTear, and ZNetView counts stay the same, then objects are still loaded and active even when not visible.

This means the game may avoid drawing off-screen objects, but it does not necessarily unload, disable, merge, or sleep build pieces.

If `Renderer-visible fire candidates` is high but `Occluded fire candidates` is also high, the plugin is detecting fires that are inside the camera frustum but blocked by geometry, such as torches behind walls.

If `Fire proxy lights active` is close to `Optimized fire pieces`, `StaticLight` may still be adding too many lights. Lower `StaticLightProxyMaxDistance`, `StaticLightOccludedProxyMaxDistance`, `StaticLightIntensityMultiplier`, or `StaticLightRangeMultiplier`.

## Compatibility Notes

Broad performance overhaul mods may overlap with this plugin.

Potentially overlapping systems include:

- Light culling or light LOD.
- Light flicker optimization.
- Smoke/particle optimization.
- Decor/build-piece batching.
- WearNTear sleeping or support caching.
- Engine quality setting changes.

For clean testing, use separate mod profiles:

- Profile A: Build Piece Optimizer only.
- Profile B: Other performance mod only.
- Profile C: Build Piece Optimizer plus other performance mods.

Develop and benchmark new features in Profile A first, then test compatibility in Profile C.

## Known Limitations

- Optimization features are experimental.
- `Renderer.isVisible` can be affected by any camera, not only the main player camera.
- Occlusion checks are approximate.
- `Debug.DrawLine` may not be visible in the normal game view.
- Counts may fluctuate due to zone loading, temporary effects, items, creatures, UI, or runtime object creation/destruction.
- The profiler uses periodic object searches, so very low poll intervals may cause overhead.
- Build-piece-only counts include child components under Piece objects, which can include disabled variant renderers or LOD-related child objects.
- Static proxy lights are not free. Too many active proxy lights can reduce FPS.
- StaticLight particle/flame behavior is still being refined.
- FullCull is visually destructive by design.
- The plugin should not currently touch colliders, Piece, WearNTear, ZNetView, support logic, or save/network state.
