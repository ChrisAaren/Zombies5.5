using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace Zombies55
{
    // One barrier segment on a cell edge between two different districts. Opens (sinks into the ground)
    // once both districts are owned. Local +Z of this transform points toward zoneB.
    public class ZoneBarrier : MonoBehaviour
    {
        [SerializeField] int zoneA;
        [SerializeField] int zoneB;
        [SerializeField] Transform visual;
        [SerializeField] float sinkDepth = 3.4f;
        [SerializeField] float sinkSeconds = 1f;

        public int ZoneA => zoneA;
        public int ZoneB => zoneB;
        public bool IsOpen { get; private set; }

        public void Configure(int a, int b, Transform visualRoot)
        {
            zoneA = a;
            zoneB = b;
            visual = visualRoot;
        }

        public void SetOpen(bool open, bool animate, ZoneManager owner)
        {
            if (open == IsOpen) return;
            IsOpen = open;
            foreach (var c in GetComponents<Collider>()) c.enabled = !open;
            foreach (var o in GetComponents<NavMeshObstacle>()) o.enabled = !open;
            if (visual == null) return;
            if (open)
            {
                if (animate && isActiveAndEnabled) StartCoroutine(Sink(owner));
                else visual.gameObject.SetActive(false);
            }
            else
            {
                StopAllCoroutines();
                visual.localPosition = Vector3.zero;
                visual.gameObject.SetActive(true);
            }
        }

        IEnumerator Sink(ZoneManager owner)
        {
            var box = GetComponent<BoxCollider>();
            if (owner != null)
            {
                Vector3 c = transform.TransformPoint(box != null ? box.center : Vector3.zero);
                float len = box != null ? box.size.x : 38f;
                owner.PlayBarrierOpen(c, transform.right, len);
            }
            float t = 0f;
            while (t < sinkSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / sinkSeconds);
                k = k * k * (3f - 2f * k);
                visual.localPosition = new Vector3(0f, -sinkDepth * k, 0f);
                yield return null;
            }
            visual.gameObject.SetActive(false);
        }
    }
}
