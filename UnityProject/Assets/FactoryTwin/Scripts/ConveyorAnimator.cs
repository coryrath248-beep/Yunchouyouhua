using UnityEngine;

namespace FactoryTwin
{
    public sealed class ConveyorAnimator : MonoBehaviour
    {
        [SerializeField] private Transform[] rollers;
        [SerializeField] private float degreesPerSecond = 95f;
        private DeviceView device;

        public void Configure(Transform[] newRollers)
        {
            rollers = newRollers;
        }

        private void Awake()
        {
            device = GetComponent<DeviceView>();
        }

        private void Update()
        {
            if (rollers == null)
            {
                return;
            }
            if (device != null && device.Status != "processing" && device.Status != "working")
            {
                return;
            }
            float rotation = degreesPerSecond * Time.deltaTime;
            foreach (Transform roller in rollers)
            {
                if (roller != null)
                {
                    roller.Rotate(Vector3.up, rotation, Space.Self);
                }
            }
        }
    }
}
