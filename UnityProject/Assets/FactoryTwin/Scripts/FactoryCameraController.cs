using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FactoryTwin
{
    public sealed class FactoryCameraController : MonoBehaviour
    {
        [SerializeField] private Vector3 focus = new Vector3(12.5f, 0.2f, 15f);
        [SerializeField] private float distance = 32f;
        [SerializeField] private float yaw = -38f;
        [SerializeField] private float pitch = 70f;
        [SerializeField] private Vector2 xBounds = new Vector2(2f, 24f);
        [SerializeField] private Vector2 zBounds = new Vector2(4f, 31f);
        private Camera controlledCamera;
        private Vector3 smoothFocus;
        private Vector3 focusVelocity;
        private int activePreset = 1;

        public void Configure(Vector3 newFocus, float newDistance, float newYaw, float newPitch, Vector2 newXBounds, Vector2 newZBounds)
        {
            focus = newFocus;
            smoothFocus = newFocus;
            distance = newDistance;
            yaw = newYaw;
            pitch = newPitch;
            xBounds = newXBounds;
            zBounds = newZBounds;
        }

        private void Awake()
        {
            controlledCamera = GetComponent<Camera>();
            if (controlledCamera != null)
            {
                controlledCamera.fieldOfView = 38f;
                controlledCamera.backgroundColor = new Color(0.12f, 0.16f, 0.19f);
                UniversalAdditionalCameraData cameraData = controlledCamera.GetUniversalAdditionalCameraData();
                cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                cameraData.antialiasingQuality = AntialiasingQuality.High;
            }

            // Start with the complete workshop. Detailed line and warehouse views remain
            // available through 2 and 3, but they are no longer forced on startup.
            ApplyPreset(1);
        }

        private void LateUpdate()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                ApplyPreset(1);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                ApplyPreset(2);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                ApplyPreset(3);
            }

            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * 3f;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 3f, 25f, 85f);
            }

            if (controlledCamera != null && controlledCamera.orthographic)
            {
                controlledCamera.orthographicSize = Mathf.Clamp(
                    controlledCamera.orthographicSize - Input.mouseScrollDelta.y * 1.5f,
                    6f,
                    30f);
            }
            else
            {
                distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y * 2f, 8f, 55f);
            }
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            float move = 10f * Time.unscaledDeltaTime;
            focus += (forward * Input.GetAxisRaw("Vertical") + right * Input.GetAxisRaw("Horizontal")) * move;
            focus.x = Mathf.Clamp(focus.x, xBounds.x, xBounds.y);
            focus.z = Mathf.Clamp(focus.z, zBounds.x, zBounds.y);
            smoothFocus = Vector3.SmoothDamp(smoothFocus, focus, ref focusVelocity, 0.12f, Mathf.Infinity, Time.unscaledDeltaTime);

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = smoothFocus - rotation * Vector3.forward * distance;
            transform.rotation = rotation;
        }

        private void ApplyPreset(int preset)
        {
            activePreset = preset;
            switch (activePreset)
            {
                case 1:
                    // Whole-factory overview.
                    focus = new Vector3(12.5f, 0.55f, 15f);
                    distance = 55f;
                    yaw = -42f;
                    pitch = 55f;
                    if (controlledCamera != null) controlledCamera.fieldOfView = 42f;
                    break;
                case 3:
                    // ASRS outfeed close-up: large rack, stacker crane, fixed conveyor
                    // and R3-2 stay in the same frame during the synchronized cycle.
                    focus = new Vector3(21.2f, 1.15f, 28.25f);
                    distance = 11.8f;
                    yaw = 25f;
                    pitch = 30f;
                    if (controlledCamera != null) controlledCamera.fieldOfView = 35f;
                    break;
                default:
                    // Default presentation view: one complete production line plus context.
                    focus = new Vector3(11.4f, 0.75f, 15f);
                    distance = 29.5f;
                    yaw = -42f;
                    pitch = 46f;
                    if (controlledCamera != null) controlledCamera.fieldOfView = 38f;
                    break;
            }

            smoothFocus = focus;
            focusVelocity = Vector3.zero;
        }
    }
}
