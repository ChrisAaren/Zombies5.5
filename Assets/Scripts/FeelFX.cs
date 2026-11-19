using System.Collections;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Pool;

namespace Zombies55
{
    // The feel layer: listens to the game loop and the player, and answers every state change
    // with something visible and audible. Particle systems are reused (Emit), floating text is pooled.
    public class FeelFX : MonoBehaviour
    {
        [SerializeField] PlayerController player;
        [Tooltip("Scaled for the pickup pop. Not the transform CharacterMotion squashes.")]
        [SerializeField] Transform popTarget;
        [SerializeField] ParticleSystem pickupBurst;
        [SerializeField] ParticleSystem confetti;
        [SerializeField] ParticleSystem landingPuff;
        [SerializeField] TextMeshPro floatingTextTemplate;
        [SerializeField] CinemachineImpulseSource impulse;

        [Header("Tunables")]
        [SerializeField] float popScale = 1.25f;
        [SerializeField] float popDuration = 0.15f;
        [SerializeField] float floatRise = 0.8f;
        [SerializeField] float floatDuration = 0.6f;
        [SerializeField] float landShakeThreshold = 9f;

        ObjectPool<TextMeshPro> _textPool;
        Vector3 _popBaseline;
        Coroutine _popRoutine;
        int _lastTickSecond = -1;

        void Awake()
        {
            _popBaseline = popTarget != null ? popTarget.localScale : Vector3.one;
            floatingTextTemplate.gameObject.SetActive(false);
            _textPool = new ObjectPool<TextMeshPro>(
                createFunc: () => Instantiate(floatingTextTemplate, transform),
                actionOnGet: t => t.gameObject.SetActive(true),
                actionOnRelease: t => t.gameObject.SetActive(false),
                actionOnDestroy: t => Destroy(t.gameObject),
                defaultCapacity: 4);
        }

        void Start()
        {
            var loop = GameLoop.Instance;
            if (loop != null)
            {
                loop.PickupCollected += OnPickup;
                loop.StateChanged += OnStateChanged;
            }
            if (player != null)
            {
                player.Jumped += OnJumped;
                player.Landed += OnLanded;
            }
        }

        void OnDestroy()
        {
            var loop = GameLoop.Instance;
            if (loop != null)
            {
                loop.PickupCollected -= OnPickup;
                loop.StateChanged -= OnStateChanged;
            }
            if (player != null)
            {
                player.Jumped -= OnJumped;
                player.Landed -= OnLanded;
            }
        }

        void Update()
        {
            // Countdown ticks for the last ten seconds.
            var loop = GameLoop.Instance;
            if (loop == null || loop.State != RunState.Running) return;
            int sec = Mathf.CeilToInt(loop.TimeLeft);
            if (sec <= 10 && sec != _lastTickSecond && sec > 0)
            {
                _lastTickSecond = sec;
                GameAudio.Instance?.PlayTick();
            }
        }

        void OnPickup(Pickup p)
        {
            Vector3 pos = p.transform.position + Vector3.up * 0.8f;
            pickupBurst.transform.position = pos;
            pickupBurst.Emit(12);
            GameAudio.Instance?.PlayPickup();
            ShowFloatingText(pos, "+1");
            if (popTarget != null)
            {
                if (_popRoutine != null) StopCoroutine(_popRoutine);
                _popRoutine = StartCoroutine(Pop());
            }
        }

        void OnStateChanged(RunState state)
        {
            switch (state)
            {
                case RunState.Won:
                    confetti.transform.position = player.transform.position + Vector3.up * 0.5f;
                    confetti.Emit(45);
                    impulse?.GenerateImpulse(0.4f);
                    GameAudio.Instance?.PlayWin();
                    break;
                case RunState.Lost:
                    impulse?.GenerateImpulse(0.25f);
                    GameAudio.Instance?.PlayLose();
                    break;
                case RunState.Paused:
                    GameAudio.Instance?.SetPausedDuck(true);
                    break;
                case RunState.Running:
                    GameAudio.Instance?.SetPausedDuck(false);
                    break;
            }
        }

        void OnJumped()
        {
            GameAudio.Instance?.PlayJump();
        }

        void OnLanded(float impact)
        {
            if (impact < 2f) return;
            float strength = Mathf.Clamp01(impact / 15f);
            landingPuff.transform.position = player.transform.position + Vector3.up * 0.05f;
            landingPuff.Emit(4);
            GameAudio.Instance?.PlayLand(strength);
            if (impact > landShakeThreshold) impulse?.GenerateImpulse(0.15f * strength);
        }

        IEnumerator Pop()
        {
            yield return Ease.Tween(popDuration * 0.5f,
                k => popTarget.localScale = _popBaseline * Mathf.LerpUnclamped(1f, popScale, k), Ease.OutCubic, unscaled: false);
            yield return Ease.Tween(popDuration,
                k => popTarget.localScale = _popBaseline * Mathf.LerpUnclamped(popScale, 1f, k), Ease.OutBack, unscaled: false);
            popTarget.localScale = _popBaseline;
            _popRoutine = null;
        }

        void ShowFloatingText(Vector3 position, string text)
        {
            var t = _textPool.Get();
            t.text = text;
            StartCoroutine(FloatText(t, position));
        }

        IEnumerator FloatText(TextMeshPro t, Vector3 start)
        {
            var cam = Camera.main;
            Color baseColor = t.color;
            yield return Ease.Tween(floatDuration, k =>
            {
                t.transform.position = start + Vector3.up * (floatRise * k);
                if (cam != null) t.transform.rotation = Quaternion.LookRotation(t.transform.position - cam.transform.position);
                t.color = Palette.WithAlpha(baseColor, 1f - k);
            }, Ease.OutCubic, unscaled: false);
            t.color = baseColor;
            _textPool.Release(t);
        }
    }
}
