# Quest 3 standalone build

This guide is part of SplatXR (MIT License). It is for a **standalone Quest 3 APK**: the app runs on the headset, not on the PC. Quest Link, Air Link, and Virtual Desktop are PCVR. They render with the Windows graphics API, so a splat can look correct there and still be invisible in an Android build.

The sample project is `QuestSample/`. It references `SplatXRToolkit` with a local package path. You do not need a second copy of the toolkit.

SplatXR's own guidance is still about **200k visible** splats for a comfortable frame rate. Nothing in the code refuses a larger file. 500k is a reasonable experiment on Quest 3. 1,000,000 is the hard ceiling to try, not a target that this renderer is expected to hold at 72 Hz. The numbers below are from the data layout, not from a headset profile.

## Why the skybox can be the only thing you see

The skybox shader ships with Unity and is stereo-aware. The splat is a transparent mesh drawn by `SplatXR/SplatShader`. If that draw produces no on-screen pixels, the cleared skybox is all that remains. There is no magenta error shader in that case, so the failure looks like "the scene is empty."

On this toolkit, three separate things did that on Android while a PCVR or Editor session still worked:

1. **Mesh upload ran off the main thread on Android only.** `BaseRendererSplatXR` and `LoDRendererSplatXR` used `Task.Run` to set `Mesh.vertices` when `Application.platform` was Android and the player was not the Editor. Unity's Mesh API is main-thread only. The task faulted or did nothing, the mesh stayed empty, and the skybox remained. Windows and the Editor used the main-thread path, which is why Link looked fine. Both renderers now upload on the main thread and log the vertex count.
2. **The shader was not multiview-safe.** Quest OpenXR renders both eyes in one pass (Multiview). Without `#pragma multi_compile_instancing`, `UNITY_MATRIX_V` and `UNITY_MATRIX_P` are not per-eye, and the quads are projected off-screen. The shader now enables instancing, sets up the eye index in the fragment stage, and takes focal length from `UNITY_MATRIX_P`.
3. **Frustum culling used the raw quad bounds.** Every splat is a unit quad at the pivot. The shader moves it in clip space from UV data. A mesh bounds of about two meters at the origin is culled as soon as you are not looking at the pivot. After each upload the renderer bounds are forced to a 10 km box around the world origin, which matches the intent of the existing `bounds` assignment in `Start`.

A URP asset that is missing on one quality level causes the same symptom: the skybox still clears, and the splat pass is skipped. The sample assigns one URP asset to every quality level.

The shader target is also set to 3.5 so the covariance channels (`TEXCOORD5` and `TEXCOORD6`) stay legal vertex inputs on mobile. If a compiler dropped them, the eigen test would cull every quad. That change is a precaution. The thread bug and the missing instancing pragma are the defects in the previous source.

## What you need installed

On the Windows PC:

- Unity **6.2** from Unity Hub. The sample's `ProjectSettings/ProjectVersion.txt` says `6000.2.0f1` so a newer 6.2 editor can open it. A Unity 6.0 or 6.1 editor should not be used with the pinned URP 17.2 package.
- That editor's **Android Build Support** module, including Android SDK, NDK, and OpenJDK. Hub shows these as checkboxes on the editor install. Without them, the setup script cannot create OpenXR's Android settings.
- A Quest 3 in **developer mode**. In the Meta Quest phone app: the headset, Headset settings, Developer mode. Connect a USB data cable. Put the headset on and allow USB debugging when it asks.

You do not need the Meta XR All-in-One SDK. The sample uses Unity OpenXR plus the **Meta Quest Support** feature that ships inside `com.unity.xr.openxr`.

## Open the sample

1. Copy or clone this repository. Keep `QuestSample` and `SplatXRToolkit` as siblings. Unity resolves a `file:` dependency relative to the `Packages` folder, so `../` is the project root and the toolkit one level higher is `file:../../SplatXRToolkit` in `QuestSample/Packages/manifest.json`. That line must point at `<repo>/SplatXRToolkit/package.json`, not at a folder inside `QuestSample`.
2. Unity Hub, **Add**, **Add project from disk**, select the `QuestSample` folder (the one that contains `Assets` and `Packages`).
3. Open it with Unity 6.2. The first import downloads URP 17.2.0, OpenXR 1.14.3, XR Management 4.5.1, Input System 1.14.2, and uGUI 2.0.0. If Package Manager says a version does not exist for your editor, install the closest **same major** from the list (URP 17.x, OpenXR 1.14 or newer) and continue.
4. If Unity asks to restart after changing the input system, restart it.
5. Watch the Console. A finished setup prints `[SplatXR Quest] Quest 3 Android settings applied`.
6. If that line never appears, or the Console says Android Build Support is missing, install the module, restart, then run **SplatXR > Quest Sample > Apply Quest 3 Android Settings**.

The menu is safe to run again. It rewrites the Android/OpenXR/URP settings.

## Settings the script applies

| Setting | Value | Why |
| --- | --- | --- |
| Build target | Android | Standalone Quest is an Android APK |
| Scripting backend | IL2CPP | Required for ARM64 Quest players |
| Target architectures | ARM64 only | Quest 3 is arm64. ARMv7 will not run. |
| Minimum API | 32 | Quest 3 OS is Android 12 or newer |
| Color space | Linear | URP + Quest default. A Gamma project changes brightness, not visibility. |
| Graphics API | Vulkan only | OpenGL ES is removed so the player cannot silently fall back |
| Stereo rendering | Multiview (`Single Pass Instanced` in the OpenXR enum) | Quest's fast path. The splat shader has to be instanced for this. |
| XR loader | OpenXR, initialize on startup, **Android tab only** | Editor Play on Windows uses the Standalone tab, which is left without a loader so Play still shows a normal camera |
| Feature group | Meta Quest Support | This is the OpenXR "Meta Quest feature group" |
| Interaction profiles | Oculus Touch and Meta Quest Touch Plus | Thumbsticks on Quest 3 and Quest 3S controllers |
| Meta Quest devices | Quest 3 (`eureka`) and Quest 3S enabled | Written into the Android manifest by the OpenXR feature |
| Symmetric projection | On | Vulkan multiview performance option inside Meta Quest Support |
| Multiview Render Regions | Off | Can clip a full-screen transparent pass. Turn it on only after the splat is visible. |
| URP | `Assets/Settings/URP-Quest.asset` on Graphics **and every quality level** | MSAA 4x, HDR off, render scale 1, no depth/opaque copy |
| Input | Input System Package | OpenXR and the head pose driver need it |
| Managed stripping | Minimal | Lowers the chance IL2CPP strips the XR loader |
| Shader | `SplatXR/SplatShader` added to Always Included Shaders | Stops the Android build from stripping it |
| Scenes in build | `Assets/Scenes/QuestSplatTest.unity` | |
| Development Build | On, script debugging off | `Debug.Log` shows up in logcat. Debugging left off so the headset does not wait for a debugger. |

Confirm by hand, once:

1. **Edit > Project Settings > XR Plug-in Management**. Select the **Android** tab (the Android robot icon). **OpenXR** is checked. Initialize XR on Startup is checked.
2. Open **XR Plug-in Management > OpenXR**. The top of that page has a platform icon. Select **Android**. Render Mode is **Single Pass Instanced** (the UI labels this Multiview on Android). Under OpenXR Feature Groups, **Meta Quest Support** is checked. Under Interaction Profiles, **Oculus Touch Controller Profile** and **Meta Quest Touch Plus Controller Profile** are checked.
3. Click the gear next to Meta Quest Support. Symmetric Projection is on. Multiview Render Regions is **None**. Quest 3 is in the target device list.
4. If the OpenXR tab shows a yellow or red project-validation box, click **Fix All**, then look again. Do not enable extra experimental Vulkan options before the first successful splat.
5. **Edit > Project Settings > Graphics**. Scriptable Render Pipeline Settings is `URP-Quest`.
6. **Edit > Project Settings > Quality**. Click each quality level. Custom Render Pipeline is `URP-Quest` on every row, including the one with the green checkmark.

Editor Play does **not** start OpenXR, because the Standalone tab has no loader. That is deliberate. Press Play to confirm the floor, the cyan cube, and (after you add a PLY) the splat on the monitor. Head tracking and the thumbsticks only work in the APK.

## Scene

Open `Assets/Scenes/QuestSplatTest.unity`.

Hierarchy before Play:

- **QuestBootstrap** creates the rig when the app starts: an XR Origin in floor tracking mode, a camera tagged `MainCamera`, a tracked pose driver, left-stick locomotion, and right-stick snap turn. It also creates a dark floor quad and a small cyan cube at about `(0, 1.3, 2)`.
- **SplatSlot** is a marker at the origin. The wire box is an editor gizmo. You do not have to parent the splat under it.

On device:

- Look slightly down. A world-space panel shows FPS, graphics API, whether OpenXR started, loaded splat count, how many splats are in the mesh, and whether the shader is supported.
- Left stick moves you along the floor, relative to your head yaw, at 1.5 m/s. Change `Move Speed` on QuestBootstrap.
- Right stick snap-turns 45 degrees. Push the stick back to center before the next snap.
- The sample does not change the headset refresh rate. Unity 6.6 no longer has `XRDisplaySubsystem.TryRequestDisplayRefreshRate`. Logcat prints the rate the runtime already chose. Set 72 Hz from the Quest quick settings if you want that target.
- The cyan cube and the floor are not splats. If you can see them and walk, tracking and URP work, and a missing room is a splat problem. If you see only the skybox and not the cube, the camera or the render pipeline is wrong.

SplatXR sorts from the camera tagged `MainCamera`. The bootstrap tags its camera. Do not add a second camera with that tag.

## Add your PLY

1. Stay in `QuestSplatTest`.
2. **SplatXR > Building Blocks Panel**.
3. **Splat Renderer**. Renderer = **Base Renderer**. Object name, for example `Room`.
4. **Browse** next to Input Splat File (PLY) and choose your `.ply`.
5. **PLY Up Direction**. The panel default is `(0, -1, 0)` (Y-down). Many 3DGS files are Y-up `(0, 1, 0)` or Z-up `(0, 0, 1)`. If the room is on its side, change this and click **Add to Scene** again. The panel writes a new usplat CSV under `Assets/Resources/Usplats/`.
6. **Add to Scene**. This creates a GameObject, converts the PLY to the usplat CSV, and assigns `SplatXR/SplatShader`. The first conversion of a 500k PLY is slow. Wait until the Console logs `[SplatXR] Loaded N splats`.
7. Leave **Sorting Rate** at `1` second for the first device build. Lower it only after the frame rate is acceptable. The sort itself is on a background thread. Copying the mesh to the GPU is on the main thread and is the hitch you feel.
8. Save the scene (**Ctrl+S**).

Do not add Splat Effects or Splat Blending for this test. Those shaders do not have the multiview change yet.

Press Play. You should see the room in the Game view, plus the floor, the cube, and the debug panel. Splats in the Game view are sorted for `MainCamera`. That is the view that matches the build. A 500k CSV can stall the Editor for a bit on the first sort. That stall is not the headset frame time.

## Build and install

1. Close the Meta Quest Link app, or unplug and replug the cable after Link has been quit, so the headset is not in a PCVR session. A Link session runs the PC, not your APK.
2. **File > Build Settings**. Platform **Android**. If the button says Switch Platform, click it and wait.
3. The scene `QuestSplatTest` is checked. If the list is empty, run the SplatXR menu item above or drag the scene into the list.
4. **Build And Run**. Pick a folder outside `Assets`, for example `QuestSample/Build`. The first IL2CPP build takes a long time. Later builds are faster.
5. Put the headset on, allow the USB prompt if it appears, and wait for the app. The package name is `com.splatxr.questsample`.

If Build And Run cannot find the headset, install [Meta's ADB drivers](https://developer.oculus.com/downloads/package/oculus-adb-drivers/) and run `adb devices` (path below). The list should show the headset as `device`, not `unauthorized`.

## Read logcat

Unity's SDK adb is typically:

```text
C:\Program Files\Unity\Hub\Editor\<your-editor-version>\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe
```

From a Command Prompt in that `platform-tools` folder:

```bat
adb devices
adb logcat -c
adb logcat -s Unity
```

Launch the app on the headset. Lines from this project start with `[SplatXR]` or `[SplatXR Quest]`.

What a healthy run looks like:

- `Bootstrap starting. gfx=Vulkan`
- `XR loader: OpenXRLoader` (the exact type name ends in Loader)
- `Loaded 500,000 splats` (your count)
- `Mesh applied on '...': loaded=500000, submittedSplats=500000, ... shaderSupported=True, gfx=Vulkan`
- Every few seconds, one `[SplatXR Quest]` status line with FPS and `In mesh`

What a broken run looks like, and what it means:

| Log line | What to do |
| --- | --- |
| `gfx=OpenGLES3` or anything other than Vulkan | Player Settings > Android > uncheck Auto Graphics API and leave only Vulkan. Rebuild. |
| `XR did not start` or `XR loader: none` on device | OpenXR is not enabled on the **Android** tab, or Initialize XR on Startup is off. Fix and rebuild. Editor Play saying `none` is normal. |
| `No usplat CSV assigned` or `produced 0 splats` | The PLY was not added with the Building Blocks panel, or the CSV did not import. The usplat asset must be referenced on the renderer in the scene you built. |
| `Mesh upload threw` | Read the exception on the next line. An index-format or "65535" error means the mesh fell back to 16-bit indices (limit 16,383 splats). The uploader sets 32-bit indices before and after `Mesh.Clear`. |
| `shaderSupported=False` or `Shader compiler` / `Vulkan` error | The shader failed on the Adreno compiler. Copy that log block. Do not switch to OpenGL ES until Vulkan has a compiler message. |
| `Loaded N` but `submittedSplats=0` after several seconds | Sort or mesh build failed. Scroll up for `[SplatXR] Splat sorting failed` or `mesh building failed`. |
| `No BaseRendererSplatXR` | The build scene does not contain the splat object. Save the scene after Add to Scene and confirm that scene is the one in Build Settings. |
| App killed, `lowmemorykiller`, or a crash with no shader line | The CSV plus the mesh exceeded memory. Decimate and try again. See the count section. |

Stop logcat with Ctrl+C. `adb logcat -s Unity` hides most of the system noise. If you need the crash that killed the process, run `adb logcat -d > %USERPROFILE%\Desktop\quest.txt` without `-s` and search the file for `SplatXR`, `FATAL`, and `lowmemory`.

## Only the skybox: checklist

Work top to bottom. Stop at the first failure.

1. You launched the **APK**, not Link. In the headset library this app is "SplatXR Quest Sample". Link's empty sky is the PC project's sky, and it does not prove the APK.
2. The cyan cube and floor are visible.
   - **No.** URP or the camera is wrong. Check the quality-level table above. Console on device should still log the bootstrap line. If there is no bootstrap line, the wrong scene was built.
   - **Yes, but no room.** The pipeline and camera work. Use the log table.
3. Console in the Editor, after Play, shows `Loaded N splats` with N greater than 0, and the room is visible in the Game view. If the Editor cannot show it, the device will not either. Fix the PLY / up direction / material first.
4. The splat material's shader is `SplatXR/SplatShader`, render queue 3001. A fallback shader (`Unlit/Texture`, `Standard`) draws nothing useful for splats.
5. Exactly one **MainCamera**.
6. Logcat `shaderSupported=True` and `submittedSplats` equal to the loaded count (cropping off).
7. You are not standing inside a splat that was scaled to a few centimeters, or hundreds of meters away. Select the splat object and check scale is 1 and position is near the origin. Use the left stick to look around the origin.
8. Still empty with a good log: on the OpenXR Android page set Render Mode to **Multi Pass**, rebuild once. Multi Pass is slower but does not need instancing. If the splat appears only in Multi Pass, the stereo shader path is the problem. Put it back to Single Pass Instanced after the test and keep the log.

## Splat count on Quest

There is **no hardcoded maximum**. `SplatCsvLoader.RecommendedMaxSplats` is 200,000 and only logs a warning. `Mesh.indexFormat` is `UInt32`, which is enough for far more than 1,000,000 splats (4 vertices each). A 16-bit mesh would die at 16,383 splats. The uploader sets 32-bit indices again after `Clear`, because `Clear` can drop the format.

What actually limits you:

**Memory, per splat, roughly**

| Data | Bytes per splat | 500k | 1,000k |
| --- | --- | --- | --- |
| Parsed arrays (position, color, opacity, two covariance vectors) | 52 | 26 MB | 52 MB |
| Usplat CSV text asset (order of magnitude; depends on how many digits were written) | ~100–150 | 50–75 MB | 100–150 MB |
| Mesh vertex buffers (4 verts, 5 float3s, one float4, 6 indices) | 328 | 164 MB | 328 MB |
| Temporary copy of those mesh arrays during a sort, before upload | 328 | 164 MB | 328 MB |

Peak during a sort is on the order of **250 MB at 500k** and **500 MB at 1,000k**, plus Unity, XR, and the CSV string. Quest 3 has 8 GB shared with the OS. 500k fits. 1,000k can fit and can also get the process killed, especially if the Editor's CSV import or a second splat is also resident. The CSV is text on purpose (the usplat format). It is the bulky part. A binary container would be a later change, not a setting.

**CPU.** Splats are sorted on a worker thread every `sortingRate` seconds (default 1). The main thread then uploads the whole mesh. At 500k that upload is the hitch. Raise Sorting Rate to 1.5–2 seconds if the hitch is worse than the pop in sort order. Sorting less often does not reduce the GPU cost of drawing the mesh every frame.

**GPU.** Each splat is two triangles with a heavy vertex shader (covariance, eigenvalues) and a short fragment shader. Multiview runs the vertex shader once for both eyes. An indoor scan viewed from inside the room has a lot of overdraw: many splats cover the same pixels. That fill cost, not a vertex cap, is why SplatXR says ~200k visible. Quest 3's GPU can draw more than that. Whether it holds **72 Hz** at 500k has to be measured with the overlay. Expect 500k to be playable or close, and 1,000k to miss 72 Hz on this CPU-sort mesh renderer.

The LoD renderer does **not** help a single room you are standing inside of, unless the high-detail file is already the reduced count. It picks a level by distance (defaults: high within 2 m, medium within 5 m, low beyond). Walls around you are mostly inside 5 m, so they come from the high file. Use LoD when the capture includes a large exterior you only need at low detail.

### Concrete options, in order

1. Measure the overlay FPS on a **decimated** copy before the full 500k. Tools outside this repo that people use to thin a PLY: [SuperSplat](https://superspl.at/), the `splat-transform` CLI, and the original trainer's densification settings. Export a 200k, a 500k, and only then a 1,000k file.
2. Keep one Base Renderer. Set Sorting Rate to 1 or higher. Confirm logcat `submittedSplats` matches the file.
3. If FPS is under 72 at 500k, select `Assets/Settings/URP-Quest.asset` and set **Render Scale** to `0.85` or `0.8`. That cuts fragment cost on both eyes. Rebuild or it will not change the APK.
4. After the splat is visible, the Meta Quest Support gear can enable **Optimize Buffer Discards** (the script already tries to turn this on) and, as a later experiment, **Multiview Render Regions = All Passes**. If the splat disappears or gets cut off, set it back to None.
5. Fixed foveated rendering is the same gear menu, off by default here. It helps fill rate and blurs the periphery. Enable it only after you know the splat renders.
6. For a big environment rather than one room, build three PLYs (high / medium / low) and use the LoD Renderer in the Building Blocks panel. Set the high file to the count that held 72 Hz up close.
7. Do not stack several 500k renderers. Counts add. The warning in the Console is per file.

1,000,000 is worth one test if 500k is stable and memory in logcat does not show a kill. If the app dies during load with no `Mesh applied` line, the CSV allocation is the first suspect. Thin the file. This pass does not add a GPU sort or a compressed runtime format. Those would be a different renderer.

## What you still do in Unity yourself

The script cannot do these:

- Install the Android module, accept the headset's USB prompt, and turn on developer mode.
- Pick or convert your PLY. Add it with the Building Blocks panel and save the scene.
- Choose PLY Up Direction if the room is rotated.
- Click **Fix All** on any OpenXR validation item the script did not cover.
- Wait for the IL2CPP build, and install with Build And Run.
- If the package versions in the manifest do not resolve, choose the 17.x URP and 1.14+ OpenXR versions Package Manager offers.
- Turn off Development Build later, for a smaller faster APK, once logcat is no longer needed. **File > Build Settings**, uncheck Development Build.

## Needs a headset to confirm

This environment cannot run Unity or a Quest, so the following are unverified on device:

- That the generated `URP-Quest` asset's renderer data is accepted by your exact 6.2 patch. If Play shows only the skybox **and** no cyan cube, create the pipeline by hand: **Assets > Create > Rendering > URP Asset (with Universal Renderer)**, assign it everywhere the table says, set MSAA to 4x and HDR off.
- FPS at 500k and at 1,000k, and whether the sort upload hitch is acceptable at a 1 second interval.
- Whether Symmetric Projection changes the splat (it should not hide it). Multiview Render Regions was left off because it might.
- Thumbstick handedness if a future OpenXR update renames the primary 2D axis. The overlay line `Sticks L/R` reports whether the XR device returned an axis.
- Brightness. The shader runs splat colors through `GammaToLinearSpace` in a Linear project. The room can look darker or more contrasty than a desktop viewer. That is existing shader behavior. It does not remove the splat.
- Effect and blending shaders. They were not given the instancing pragma. Use the Base Renderer for this test.
- A Development Build is slightly slower than a release build. FPS in the overlay is a bit pessimistic until you turn Development Build off.

## License

SplatXR, the sample, and this guide are under the MIT License in `LICENSE`. The splat shader is adapted from GaussianSplats3D by Mark Kellogg, also MIT. See `THIRD_PARTY_NOTICES.md`.
