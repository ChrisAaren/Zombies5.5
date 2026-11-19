using UnityEngine;

namespace Zombies55
{
    // Raises both arms during an enemy's attack windup. Runs after CharacterMotion's LateUpdate
    // (order 0) and overrides only the ArmL / ArmR local rotations.
    [DefaultExecutionOrder(110)]
    public class EnemyAttackPose : MonoBehaviour
    {
        [SerializeField] Transform armL;
        [SerializeField] Transform armR;
        [SerializeField] float raiseDegrees = 115f;
        [SerializeField] float spreadDegrees = 8f;

        Enemy _enemy;

        void Awake()
        {
            _enemy = GetComponentInParent<Enemy>();
            if (armL == null) armL = FindDeep(transform, "ArmL");
            if (armR == null) armR = FindDeep(transform, "ArmR");
        }

        void LateUpdate()
        {
            if (_enemy == null || armL == null || armR == null) return;
            float w = _enemy.AttackPose;
            if (w <= 0.001f) return;
            // Arms hang down (-Y); a negative X rotation swings them forward and up.
            var l = Quaternion.Euler(-raiseDegrees, 0f, spreadDegrees);
            var r = Quaternion.Euler(-raiseDegrees, 0f, -spreadDegrees);
            armL.localRotation = Quaternion.Slerp(armL.localRotation, l, w);
            armR.localRotation = Quaternion.Slerp(armR.localRotation, r, w);
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
