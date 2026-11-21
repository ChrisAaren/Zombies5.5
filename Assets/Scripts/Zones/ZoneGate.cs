using UnityEngine;

namespace Zombies55
{
    // Terminal on the owned side of a barrier. Hold the Buy key (default E / gamepad X) to buy the district beyond.
    // ZoneManager shows it only while owner is owned and target is not.
    public class ZoneGate : MonoBehaviour
    {
        const float BuyHoldSeconds = 0.6f;

        public static bool PlayerAtGate { get; private set; }
        public static float HoldProgress { get; private set; }   // 0..1 while holding at a gate

        [SerializeField] int ownerZone;
        [SerializeField] int targetZone;
        [SerializeField] float useRange = 2.6f;

        float _hold;
        bool _consumed, _near;

        public int OwnerZone => ownerZone;
        public int TargetZone => targetZone;

        public void Configure(int owner, int target)
        {
            ownerZone = owner;
            targetZone = target;
        }

        void OnDisable() => Leave();

        void Update()
        {
            var zm = ZoneManager.Instance;
            var player = PlayerHealth.Instance;
            var cam = Camera.main;
            bool running = GameLoop.Instance == null || GameLoop.Instance.State == RunState.Running;
            var info = zm != null ? zm.GetZone(targetZone) : null;
            if (zm == null || info == null || player == null || cam == null || !running) { Leave(); return; }

            Vector3 to = transform.position - player.transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            Vector3 look = cam.transform.forward;
            look.y = 0f;
            bool facing = dist < 0.3f || Vector3.Dot(look.normalized, to / Mathf.Max(0.01f, dist)) > 0.35f;
            if (dist > useRange || !facing) { Leave(); return; }

            _near = true;
            PlayerAtGate = true;
            bool affordable = PlayerWallet.Instance != null && PlayerWallet.Instance.CanAfford(info.Cost);
            HudPrompt.Show(this, "Hold " + GameInput.PromptKey(GameAction.Interact) + " to open " + info.Name + " [" + info.Cost + "]", affordable);

            bool held = GameInput.IsPressed(GameAction.Interact);
            if (!held) { _hold = 0f; _consumed = false; HoldProgress = 0f; return; }
            if (_consumed) return;

            _hold += Time.deltaTime;
            HoldProgress = Mathf.Clamp01(_hold / BuyHoldSeconds);
            if (_hold < BuyHoldSeconds) return;

            _consumed = true;
            HoldProgress = 0f;
            if (zm.TryBuy(targetZone)) CombatAudio.Instance?.PlayBuy();
            else CombatAudio.Instance?.PlayDeny();
        }

        void Leave()
        {
            if (!_near) return;
            _near = false;
            _hold = 0f;
            _consumed = false;
            PlayerAtGate = false;
            HoldProgress = 0f;
            HudPrompt.Clear(this);
        }
    }
}
