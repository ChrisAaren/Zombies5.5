using System;
using UnityEngine;

namespace Zombies55
{
    public enum DebrisGroup { Hood, Door, Bumper, Trunk, Wheel, Scrap, Chunk, BarrelLid, BarrelHalf }

    [Serializable]
    public class DebrisPartDef
    {
        public string name;
        public DebrisGroup group;
        public GameObject prefab;   // mesh + one convex MeshCollider, no rigidbody (the pool adds it with the mass below)
        public float mass = 5f;
        public Vector3 size = Vector3.one;
        public int poolCount = 3;
    }

    // Everything the explosion director needs that is an asset: materials (built by the scene generator), wreck materials and the debris catalogue.
    [CreateAssetMenu(menuName = "Zombies55/Explosion Assets")]
    public class ExplosionAssets : ScriptableObject
    {
        public Material fireball, smoke, dust, fireLoop, ring, spark, ember, flash, bits, scorch;
        public Material wreckBody, wreckGlass;
        public Material debrisPaint;   // tinted per part with a property block (the car's body colour)
        public DebrisPartDef[] parts;
    }
}
