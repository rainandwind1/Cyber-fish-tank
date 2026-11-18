using System;
using UnityEngine;

namespace CyberFishTank.Demo
{
    /// <summary>Small sinking food particle used by the aquarium feeding demo.</summary>
    public sealed class FishFoodPellet : MonoBehaviour
    {
        private float substrateY;
        private float phase;
        private float age;
        private bool claimed;
        private Action onExpire;

        public bool IsAvailable { get; private set; } = true;
        public bool CanBeClaimed => IsAvailable && !claimed;
        public Vector3 Position => transform.position;

        public void Initialize(float floorY, float motionPhase, Action expired)
        {
            substrateY = floorY;
            phase = motionPhase;
            onExpire = expired;
        }

        private void Update()
        {
            if (!IsAvailable) return;
            var deltaTime = Time.deltaTime;
            age += deltaTime;
            var position = transform.position;
            position.y = Mathf.MoveTowards(position.y, substrateY, 0.2f * deltaTime);
            position.x += Mathf.Sin(age * 0.8f + phase) * 0.012f * deltaTime;
            position.z += Mathf.Cos(age * 0.62f + phase) * 0.012f * deltaTime;
            transform.position = position;
            if (age >= 38f) Expire();
        }

        public bool TryConsume()
        {
            if (!IsAvailable) return false;
            IsAvailable = false;
            Destroy(gameObject);
            return true;
        }

        public bool TryClaim()
        {
            if (!CanBeClaimed) return false;
            claimed = true;
            return true;
        }

        private void Expire()
        {
            if (!IsAvailable) return;
            IsAvailable = false;
            onExpire?.Invoke();
            Destroy(gameObject);
        }
    }
}
