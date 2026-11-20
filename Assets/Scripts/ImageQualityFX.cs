using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Zombies55
{
    // Applies the Graphics settings that live in the global volume and on the sun flare: bloom, depth of field, motion blur,
    // film grain, lens flare and brightness. Works on a runtime copy of the volume profile (volume.profile instantiates it),
    // so toggling never edits the saved asset.
    //
    // Shader stripping: every post-processing variant a setting can switch on (bloom HQ, camera motion blur) is kept in a build by
    // the coverage profile Assets/Settings/Quality/GraphicsVariantCoverage.asset (see SceneBuilder.Graphics.cs).
    public class ImageQualityFX : MonoBehaviour
    {
        [SerializeField] Volume volume;
        [SerializeField] LensFlareComponentSRP sunFlare;

        DepthOfField _dof;
        ScreenSpaceLensFlare _ssFlare;
        Bloom _bloom;
        MotionBlur _motionBlur;
        FilmGrain _grain;
        ColorAdjustments _grade;
        float _baseExposure;
        float _baseFlare;
        float _visible;
        Camera _cam;
        Light _sun;
        static readonly Vector2[] Taps = { Vector2.zero, new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1) };

        void OnEnable()
        {
            if (volume != null)
            {
                var profile = volume.profile;
                profile.TryGet(out _dof);
                profile.TryGet(out _ssFlare);
                profile.TryGet(out _bloom);
                profile.TryGet(out _grain);
                if (profile.TryGet(out _grade)) _baseExposure = _grade.postExposure.value;
                if (!profile.TryGet(out _motionBlur)) _motionBlur = profile.Add<MotionBlur>(true);
                _motionBlur.mode.value = MotionBlurMode.CameraOnly;
                _motionBlur.quality.value = MotionBlurQuality.Medium;
                _motionBlur.clamp.value = 0.05f;
                _motionBlur.intensity.value = 0f;
            }
            if (sunFlare != null) { _baseFlare = sunFlare.intensity; _sun = sunFlare.GetComponent<Light>(); }
            GameSettings.Changed += Apply;
            Apply();
        }

        // The built-in occlusion test does not see our skybox sun, so the flare fades with a few rays from the camera towards the sun
        // (buildings and trees have colliders); a soft result as the sun slips behind an edge.
        void LateUpdate()
        {
            if (sunFlare == null || !sunFlare.enabled || _sun == null) return;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;
            Vector3 toSun = -_sun.transform.forward;
            Vector3 right = _cam.transform.right, up = _cam.transform.up;
            int open = 0;
            foreach (var t in Taps)
            {
                Vector3 d = (toSun + (right * t.x + up * t.y) * 0.02f).normalized;
                if (!Physics.Raycast(_cam.transform.position, d, 300f, 1 << GameLayers.Default, QueryTriggerInteraction.Ignore)) open++;
            }
            float target = open / (float)Taps.Length;
            _visible = Mathf.MoveTowards(_visible, target, Time.unscaledDeltaTime * 4f);
            sunFlare.intensity = _baseFlare * _visible;
        }

        void OnDisable() => GameSettings.Changed -= Apply;

        void Apply()
        {
            if (_dof != null) _dof.active = GameSettings.DepthOfField;
            bool flare = GameSettings.LensFlare;
            if (_ssFlare != null) _ssFlare.active = flare;
            if (sunFlare != null) sunFlare.enabled = flare;

            int bloom = GameSettings.GetInt(SettingId.Bloom); // 0 off, 1 on, 2 high quality filtering
            if (_bloom != null)
            {
                _bloom.active = bloom > 0;
                _bloom.highQualityFiltering.value = bloom == 2;
                _bloom.maxIterations.value = bloom == 2 ? 8 : 6;
            }
            // Always active with intensity 0 when off: an inactive component would fall back to the default profile's values.
            if (_motionBlur != null) { _motionBlur.active = true; _motionBlur.intensity.value = GameSettings.MotionBlur ? 0.5f : 0f; }
            if (_grain != null) _grain.active = GameSettings.FilmGrain;
            if (_grade != null) _grade.postExposure.value = _baseExposure + GameSettings.Brightness;
        }
    }
}
