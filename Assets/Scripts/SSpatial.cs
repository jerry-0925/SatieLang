using UnityEngine;

namespace Satie
{
    public class SSpatial : MonoBehaviour
    {
        public Statement.WanderType type = Statement.WanderType.None;
        public Vector3 minPos, maxPos;
        public float   hz = 0.3f; // cycles / sec

        private Vector3 seed;

        void Start()
        {
            seed = new Vector3(
                Random.value * 1000f,
                Random.value * 1000f,
                Random.value * 1000f);
        }

        void Update()
        {
            if (type == Statement.WanderType.None) return;

            float t = Time.time * hz * 2f * Mathf.PI;

            Vector3 noise = new Vector3(
                Mathf.PerlinNoise(seed.x, t)       - 0.5f,
                Mathf.PerlinNoise(seed.y, t * 0.8f) - 0.5f,
                Mathf.PerlinNoise(seed.z, t * 1.3f) - 0.5f);

            Vector3 half = (maxPos - minPos) * 0.5f;
            Vector3 cen  = (maxPos + minPos) * 0.5f;
            Vector3 off  = Vector3.Scale(noise * 2f, half);

            if (type == Statement.WanderType.Walk) off.y = 0f;

            transform.position = cen + off;
        }
    }
}
