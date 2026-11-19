using UnityEngine;

namespace Zombies55
{
    // A glowing gold token. Spins and bobs; collected when the player touches its trigger.
    [RequireComponent(typeof(Collider))]
    public class Pickup : MonoBehaviour
    {
        [SerializeField] Transform visual;
        [SerializeField] float spinSpeed = 120f;
        [SerializeField] float bobHeight = 0.18f;
        [SerializeField] float bobFrequency = 0.8f;

        Vector3 _visualRest;
        float _phaseOffset;

        public bool IsCollected { get; private set; }

        void Awake()
        {
            if (visual != null) _visualRest = visual.localPosition;
            _phaseOffset = (transform.position.x * 0.37f + transform.position.z * 0.61f) % 6.28f;
        }

        void Update()
        {
            if (visual == null) return;
            float t = Time.time * bobFrequency * Mathf.PI * 2f + _phaseOffset;
            visual.localPosition = _visualRest + Vector3.up * (Mathf.Sin(t) * bobHeight);
            visual.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        }

        void OnTriggerEnter(Collider other)
        {
            if (IsCollected || !other.CompareTag("Player")) return;
            var loop = GameLoop.Instance;
            if (loop == null || loop.State != RunState.Running) return;
            IsCollected = true;
            loop.Collect(this);
            gameObject.SetActive(false);
        }
    }
}
