using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Zombies55
{
    // Test-only command-line hook for the graphics settings (no player-facing input). Usage:
    //   "Zombies 5.5.exe" -gfxTest "preset:ultra;view:vista;settle;shot:ultra_vista;log:ultra;exit" -gfxOut C:\dir
    // -gfxTest also makes GameSettings ephemeral: nothing is read from or written to the player's saved settings.
    // Steps (separated by ;):
    //   preset:low|medium|high|ultra   apply a preset         set:SettingId=index   set one setting (index into its options, 0/1 for toggles)
    //   view:vista|closeup|x,z,yaw,pitch   put the gameplay camera there (the player is warped, the crowd is switched off)
    //   settle   wait 2 s     wait:seconds     shot:name   save a PNG of the game screen (its own backbuffer, never the desktop) to -gfxOut
    //   log:label   print the applied graphics state     state:label   print game-state flags     hideui | showui
    //   menu:tab   open the settings menu on a tab (4 graphics, 5 benchmark; see SettingsMenu tab constants)     click:ObjectName   click an active button
    //   waitbench:seconds   wait until a menu benchmark has finished and its results screen is up     exit
    static class GfxTestHook
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            string script = null, outDir = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-gfxTest", StringComparison.OrdinalIgnoreCase)) script = args[i + 1];
                else if (string.Equals(args[i], "-gfxOut", StringComparison.OrdinalIgnoreCase)) outDir = args[i + 1];
            }
            if (script == null) return;
            var go = new GameObject("GfxTestHook") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(go);
            var r = go.AddComponent<Runner>();
            r.Script = script; r.OutDir = outDir ?? Application.persistentDataPath;
        }

        sealed class Runner : MonoBehaviour
        {
            public string Script, OutDir;
            bool _benchStarted;

            void Awake() { Invoke(nameof(Bail), 1500f); }
            void Bail() { Debug.Log("[GfxTest] timeout, quitting"); Application.Quit(); }

            IEnumerator Start()
            {
                Directory.CreateDirectory(OutDir);
                for (int i = 0; i < 60; i++) yield return null;
                foreach (var raw in Script.Split(';'))
                {
                    string step = raw.Trim(); if (step.Length == 0) continue;
                    string key = step, val = "";
                    int c = step.IndexOf(':'); if (c > 0) { key = step.Substring(0, c); val = step.Substring(c + 1); }
                    switch (key)
                    {
                        case "preset":
                        {
                            int p = Array.FindIndex(GameSettings.PresetNames, n => string.Equals(n, val, StringComparison.OrdinalIgnoreCase));
                            GameSettings.ApplyPreset(p);
                            yield return Wait(1.0f);
                            break;
                        }
                        case "set":
                        {
                            int e = val.IndexOf('=');
                            var id = (SettingId)Enum.Parse(typeof(SettingId), val.Substring(0, e));
                            GameSettings.SetFloat(id, float.Parse(val.Substring(e + 1), CultureInfo.InvariantCulture));
                            yield return Wait(1.0f);
                            break;
                        }
                        case "view": yield return View(val); break;
                        case "settle": yield return Wait(2f); break;
                        case "wait": yield return Wait(float.Parse(val, CultureInfo.InvariantCulture)); break;
                        case "shot": yield return Shot(val); break;
                        case "log": Log(val); break;
                        case "state": StateDump(val); break;
                        case "hideui": SetUi(false); break;
                        case "showui": SetUi(true); break;
                        case "menu": OpenMenu(int.Parse(val, CultureInfo.InvariantCulture)); yield return Wait(0.5f); break;
                        case "click": Click(val); yield return Wait(0.5f); break;
                        case "waitbench": yield return WaitBench(float.Parse(val, CultureInfo.InvariantCulture)); break;
                        case "exit": Application.Quit(); yield break;
                        default: Debug.Log("[GfxTest] unknown step " + step); break;
                    }
                }
                Application.Quit();
            }

            static IEnumerator Wait(float s) { yield return new WaitForSecondsRealtime(s); }

            IEnumerator Shot(string name)
            {
                yield return new WaitForEndOfFrame();
                string path = Path.Combine(OutDir, name + ".png");
                ScreenCapture.CaptureScreenshot(path);
                yield return null; yield return null; yield return null;
                Debug.Log("[GfxTest] shot " + path + " " + Screen.width + "x" + Screen.height);
            }

            static void SetUi(bool on)
            {
                foreach (var cv in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
                    if (cv.gameObject.name == "UI") cv.enabled = on;
            }

            // Player warped to (x, z), the camera looks along yaw/pitch, enemy spawning off, run started (the camera only follows scripted
            // look while Running).
            IEnumerator View(string v)
            {
                float x, z, yaw, pitch;
                if (v == "vista") { x = 19f; z = -80f; yaw = 0f; pitch = 6f; }
                else if (v == "closeup") { x = 19.5f; z = -3.5f; yaw = 78f; pitch = 8f; }
                else
                {
                    var p = v.Split(',');
                    x = float.Parse(p[0], CultureInfo.InvariantCulture); z = float.Parse(p[1], CultureInfo.InvariantCulture);
                    yaw = float.Parse(p[2], CultureInfo.InvariantCulture); pitch = float.Parse(p[3], CultureInfo.InvariantCulture);
                }
                if (GameLoop.Instance != null && GameLoop.Instance.State == RunState.Ready) GameLoop.Instance.StartRun();
                yield return null;
                PlayerHealth.GodMode = true;
                if (ZoneManager.Instance != null) ZoneManager.Instance.UnlockAll();
                if (RoundManager.Instance != null) RoundManager.Instance.enabled = false;
                foreach (var en in FindObjectsByType<Enemy>(FindObjectsInactive.Exclude)) en.gameObject.SetActive(false);
                PlayerController.ScriptedInput = true;
                PlayerController.ScriptedMove = Vector2.zero;
                CameraOrbit.ScriptedLook = true;
                CameraOrbit.ScriptedYaw = yaw; CameraOrbit.ScriptedPitch = pitch;
                var player = PlayerHealth.Instance != null ? PlayerHealth.Instance.transform : null;
                if (player != null)
                {
                    var cc = player.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    player.position = new Vector3(x, player.position.y, z);
                    player.rotation = Quaternion.Euler(0f, yaw, 0f);
                    if (cc != null) cc.enabled = true;
                }
                SetUi(false);
                yield return Wait(2.5f);
            }

            static void OpenMenu(int tab)
            {
                var menu = FindObjectsByType<SettingsMenu>(FindObjectsInactive.Include);
                if (menu.Length > 0) menu[0].Open(tab);
            }

            static void Click(string name)
            {
                foreach (var b in FindObjectsByType<UnityEngine.UI.Button>(FindObjectsInactive.Exclude))
                    if (b.name == name && b.gameObject.activeInHierarchy) { b.onClick.Invoke(); Debug.Log("[GfxTest] clicked " + name); return; }
                Debug.Log("[GfxTest] no active button " + name);
            }

            IEnumerator WaitBench(float timeout)
            {
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < timeout)
                {
                    if (BenchmarkRunner.Active) _benchStarted = true;
                    if (_benchStarted && !BenchmarkRunner.Active && SettingsMenu.Current != null) break;
                    yield return null;
                }
                Debug.Log("[GfxTest] waitbench done after " + (Time.realtimeSinceStartup - t0).ToString("0.0") + " s, active=" + BenchmarkRunner.Active + ", menu=" + (SettingsMenu.Current != null));
                yield return Wait(2f);
            }

            static void StateDump(string label)
            {
                var sb = new StringBuilder("[GfxTest] state " + label + ": ");
                sb.Append("GodMode=").Append(PlayerHealth.GodMode).Append(" BenchmarkMode=").Append(RoundManager.BenchmarkMode)
                  .Append(" ScriptedInput=").Append(PlayerController.ScriptedInput).Append(" ScriptedLook=").Append(CameraOrbit.ScriptedLook)
                  .Append(" ForceUncapped=").Append(FramePacing.ForceUncapped).Append(" HideCounter=").Append(FramePacing.HideCounter)
                  .Append(" owned=").Append(ZoneManager.Instance != null ? ZoneManager.Instance.OwnedCount : -1)
                  .Append(" points=").Append(PlayerWallet.Instance != null ? PlayerWallet.Instance.TotalEarned : -1)
                  .Append(" alive=").Append(RoundManager.Instance != null ? RoundManager.Instance.AliveCount : -1)
                  .Append(" state=").Append(GameLoop.Instance != null ? GameLoop.Instance.State.ToString() : "none")
                  .Append(" timeScale=").Append(Time.timeScale).Append(" benchActive=").Append(BenchmarkRunner.Active)
                  .Append(" vsync=").Append(QualitySettings.vSyncCount).Append(" target=").Append(Application.targetFrameRate);
                Debug.Log(sb.ToString());
            }

            static void Log(string label)
            {
                var sb = new StringBuilder("[GfxTest] gfx " + label + ": ");
                var a = GraphicsApplier.Asset;
                sb.Append("level=").Append(GraphicsApplier.LevelName).Append(" pipeline=").Append(GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "none");
                if (a != null)
                    sb.Append(" scale=").Append(a.renderScale).Append(" upscaler=").Append(a.upscalingFilter).Append(" shadowRes=").Append(a.mainLightShadowmapResolution)
                      .Append(" cascades=").Append(a.shadowCascadeCount).Append(" shadowDist=").Append(a.shadowDistance).Append(" addRes=").Append(a.additionalLightsShadowmapResolution)
                      .Append(" opaqueTex=").Append(a.supportsCameraOpaqueTexture);
                sb.Append(" lodBias=").Append(QualitySettings.lodBias).Append(" aniso=").Append(QualitySettings.anisotropicFiltering)
                  .Append(" skin=").Append(QualitySettings.skinWeights).Append(" mipLimit=").Append(QualitySettings.globalTextureMipmapLimit)
                  .Append(" raycastBudget=").Append(QualitySettings.particleRaycastBudget)
                  .Append(" fx=").Append(EffectsDetail.Level).Append(" particleCollision=").Append(EffectsDetail.ParticleCollision)
                  .Append(" kneeOff=").Append(KneeAlign.Disabled);
                var cam = Camera.main;
                if (cam != null)
                {
                    sb.Append(" fov=").Append(cam.fieldOfView.ToString("0.#", CultureInfo.InvariantCulture)).Append(" res=").Append(cam.pixelWidth).Append("x").Append(cam.pixelHeight);
                    var cull = cam.layerCullDistances;
                    if (cull != null && cull.Length == 32) sb.Append(" clutterCull=").Append(cull[GameLayers.Clutter]).Append("/").Append(cull[GameLayers.ClutterDecal]);
                    if (cam.TryGetComponent<UniversalAdditionalCameraData>(out var d))
                        sb.Append(" aa=").Append(d.antialiasing).Append("/").Append(d.antialiasingQuality).Append(" aoRenderer=").Append(GraphicsApplier.AppliedAoRenderer);
                }
                var sun = RenderSettings.sun;
                if (sun != null && sun.TryGetComponent<UniversalAdditionalLightData>(out var ld)) sb.Append(" softShadow=").Append(ld.softShadowQuality);
                var vol = FindAnyObjectByType<Volume>();
                if (vol != null)
                {
                    var p = vol.profile;
                    if (p.TryGet(out Bloom b)) sb.Append(" bloom=").Append(b.active).Append("/hq=").Append(b.highQualityFiltering.value);
                    if (p.TryGet(out MotionBlur mb)) sb.Append(" motionBlur=").Append(mb.intensity.value);
                    if (p.TryGet(out FilmGrain fg)) sb.Append(" grain=").Append(fg.active);
                    if (p.TryGet(out DepthOfField dof)) sb.Append(" dof=").Append(dof.active);
                    if (p.TryGet(out ScreenSpaceLensFlare lf)) sb.Append(" flare=").Append(lf.active);
                    if (p.TryGet(out ColorAdjustments ca)) sb.Append(" exposure=").Append(ca.postExposure.value.ToString("0.00", CultureInfo.InvariantCulture));
                }
                sb.Append(" vsync=").Append(QualitySettings.vSyncCount).Append(" target=").Append(Application.targetFrameRate)
                  .Append(" ftEnabled=").Append(FrameTimingManager.IsFeatureEnabled());
                sb.Append(" api=").Append(SystemInfo.graphicsDeviceType);
                Debug.Log(sb.ToString());
            }
        }
    }
}
