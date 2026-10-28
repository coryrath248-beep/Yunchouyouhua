using UnityEngine;

namespace FactoryTwin
{
    public sealed class BillboardLabel : MonoBehaviour
    {
        private void LateUpdate()
        {
            if (Camera.main == null)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(
                transform.position - Camera.main.transform.position,
                Camera.main.transform.up);
        }
    }
}
