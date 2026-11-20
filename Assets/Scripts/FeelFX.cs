using Unity.Cinemachine;
using UnityEngine;

namespace Zombies55
{
    // The feel layer: listens to the game loop and the player, and answers every state change
    // with something visible and audible. Particle systems are reused (Emit).
    public class FeelFX : MonoBehaviour
    {
        [SerializeField] PlayerController player;
        [SerializeField] ParticleSystem landingPuff;
        [SerializeField] CinemachineImpulseSource impulse;

        [Header("Tunables")]
        [SerializeField] float landShakeThreshold = 9f;

        void Start()
        {
            var loop = GameLoop.Instance;
            if (loop != null)
            {
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
                loop.StateChanged -= OnStateChanged;
            }
            if (player != null)
            {
                player.Jumped -= OnJumped;
                player.Landed -= OnLanded;
            }
        }

        void OnStateChanged(RunState state)
        {
            switch (state)
            {
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
            // The dust only marks a real drop or jump landing (4 m/s is about a 0.3 m fall); small hops make a sound, not a puff.
            if (impact >= 4f)
            {
                landingPuff.transform.position = player.transform.position + Vector3.up * 0.05f;
                landingPuff.Emit(4);
            }
            GameAudio.Instance?.PlayLand(strength);
            if (impact > landShakeThreshold) impulse?.GenerateImpulse(0.15f * strength);
        }

    }
}
