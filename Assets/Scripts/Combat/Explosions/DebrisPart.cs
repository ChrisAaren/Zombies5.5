using UnityEngine;

namespace Zombies55
{
    // One pooled physics debris part: a mesh, a convex collider and a Rigidbody with the mass from the part catalogue.
    // The prefab is built by the scene generator; the pool owns the lifecycle.
    [RequireComponent(typeof(Rigidbody))]
    public class DebrisPart : MonoBehaviour
    {
        internal Rigidbody Body;
        internal Collider Hull;
        internal Renderer Rend;
        internal int PaintIndex = -1;
        internal DebrisPartDef Def;
        internal int State;          // 0 free, 1 live, 2 sinking
        internal float Spawned, Settled = -1f, SinkStart, StillFor;
        internal DebrisPool Pool;

        void OnCollisionEnter(Collision c)
        {
            if (Pool != null && c.contactCount > 0) Pool.Impact(this, c.GetContact(0).point, c.relativeVelocity.magnitude);
        }
    }
}
