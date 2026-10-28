using UnityEngine;

namespace FactoryTwin
{
    public sealed class MachineDoorAnimator : MonoBehaviour
    {
        [SerializeField] private Transform door;
        [SerializeField] private Vector3 openLocalPosition;
        [SerializeField] private Vector3 closedLocalPosition;
        [SerializeField] private float speed = 2.6f;
        private bool shouldClose;

        public void Configure(Transform newDoor, Vector3 openPosition, Vector3 closedPosition)
        {
            door = newDoor;
            openLocalPosition = openPosition;
            closedLocalPosition = closedPosition;
            if (door != null)
            {
                door.localPosition = openLocalPosition;
            }
        }

        public void ApplyStatus(string status)
        {
            shouldClose = status == "processing" || status == "working";
        }

        private void Update()
        {
            if (door == null)
            {
                return;
            }

            Vector3 target = shouldClose ? closedLocalPosition : openLocalPosition;
            door.localPosition = Vector3.MoveTowards(door.localPosition, target, speed * Time.deltaTime);
        }
    }
}
