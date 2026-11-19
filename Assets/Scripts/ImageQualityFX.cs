using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Zombies55
{
    // Applies the Graphics settings to the global volume and the sun flare: far-distance depth of field and lens flare.
    // Works on a runtime copy of the volume profile so toggling never edits the saved asset.
    public class ImageQualityFX : MonoBehaviour
    {
        [SerializeField] Volume volume;
        [SerializeField] LensFlareComponentSRP sunFlare;

        DepthOfField _dof;
        ScreenSpaceLensFlare _ssFlare;
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
        }
    }
}
