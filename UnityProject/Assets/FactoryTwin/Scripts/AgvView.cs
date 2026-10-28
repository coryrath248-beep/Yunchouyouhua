using UnityEngine;

namespace FactoryTwin
{
    public sealed class AgvView : MonoBehaviour
    {
        [SerializeField] private string lineId;
        [SerializeField] private string agvId;
        [SerializeField] private TextMesh label;
        [SerializeField] private Renderer[] statusRenderers;

        private Vector3 motionStart;
        private Vector3 motionEnd;
        private float motionStartTime;
        private float motionDuration;
        private bool isAnimating;
        private bool isInitialized;
        private bool isSettling;
        private bool hasMotionTarget;
        private Vector3 settleTarget;
        private float lastMessageTimestamp = float.NegativeInfinity;
        private const float SameTargetTolerance = 0.08f;
        private const float TeleportThreshold = 4f;
        private MaterialPropertyBlock colorBlock;

        public string Key => lineId + "/" + agvId;
        public string Status { get; private set; } = "waiting";
        public float Battery { get; private set; }

        private void Awake()
        {
            colorBlock = new MaterialPropertyBlock();
        }

        public void Configure(string newLineId, string newAgvId, TextMesh newLabel, Renderer[] renderers)
        {
            lineId = newLineId;
            agvId = newAgvId;
            label = newLabel;
            statusRenderers = renderers;
            UpdateLabel();
        }

        public void Apply(FactoryTwinController.AgvStatusMessage state, float coordinateScale)
        {
            if (state == null)
            {
                return;
            }

            // MQTT callbacks may arrive on a different cadence from rendering. Ignore an
            // older retained/out-of-order frame so it cannot pull the vehicle backwards.
            if (state.timestamp > 0f && state.timestamp + 0.001f < lastMessageTimestamp)
            {
                return;
            }
            if (state.timestamp > 0f)
            {
                lastMessageTimestamp = state.timestamp;
            }

            Status = string.IsNullOrEmpty(state.status) ? "unknown" : state.status;
            Battery = state.battery_level;

            Vector3 reportedPosition = ToWorld(state.position, coordinateScale, transform.position.y);
            if (!isInitialized)
            {
                transform.position = reportedPosition;
                settleTarget = reportedPosition;
                isInitialized = true;
            }

            if (Status == "moving" && state.target_position != null && state.estimated_time > 0.01f)
            {
                Vector3 newEnd = ToWorld(state.target_position, coordinateScale, transform.position.y);
                bool continuingSameTrip = hasMotionTarget &&
                                          Vector3.Distance(motionEnd, newEnd) <= SameTargetTolerance;

                if (continuingSameTrip)
                {
                    // A moving status is broadcast repeatedly. Keep the existing visual
                    // journey instead of restarting it from the reported start each time.
                    motionEnd = newEnd;
                }
                else
                {
                    if (Vector3.Distance(transform.position, reportedPosition) > TeleportThreshold)
                    {
                        transform.position = reportedPosition;
                    }
                    motionStart = transform.position;
                    motionEnd = newEnd;
                    motionStartTime = Time.time;
                    motionDuration = Mathf.Max(0.15f, state.estimated_time);
                    isAnimating = true;
                    hasMotionTarget = true;
                }
                isSettling = false;
            }
            else
            {
                isAnimating = false;
                hasMotionTarget = false;
                settleTarget = reportedPosition;
                isSettling = true;
            }

            ApplyStatusColor(Status);
            UpdateLabel();
        }

        public void Tick()
        {
            if (isAnimating)
            {
                float t = Mathf.Clamp01((Time.time - motionStartTime) / motionDuration);
                transform.position = Vector3.Lerp(motionStart, motionEnd, Mathf.SmoothStep(0f, 1f, t));
                // These units are rail-mounted robot platforms, not free-steering AGVs.
                // Keep the base aligned with the fixed rack/outfeed cell while travelling;
                // only the robot shoulder should rotate during loading.
                transform.rotation = Quaternion.identity;
                if (t >= 1f)
                {
                    transform.position = motionEnd;
                    isAnimating = false;
                }
            }
            else if (isSettling)
            {
                // Reconcile small authoritative corrections without a visible snap or
                // overshoot. A large correction is still handled in Apply as a teleport.
                float distance = Vector3.Distance(transform.position, settleTarget);
                float speed = Mathf.Max(0.6f, distance * 5f);
                transform.position = Vector3.MoveTowards(
                    transform.position,
                    settleTarget,
                    speed * Time.deltaTime);
                if (distance < 0.005f)
                {
                    transform.position = settleTarget;
                    isSettling = false;
                }
            }

            if (label != null && Camera.main != null)
            {
                label.transform.rotation = Quaternion.LookRotation(
                    label.transform.position - Camera.main.transform.position,
                    Camera.main.transform.up);
            }
        }

        private void ApplyStatusColor(string state)
        {
            Color color;
            switch (state)
            {
                case "moving": color = new Color(0.1f, 0.75f, 1f); break;
                case "charging": color = new Color(1f, 0.75f, 0.1f); break;
                case "fault": color = new Color(1f, 0.15f, 0.1f); break;
                case "interacting": color = new Color(0.8f, 0.25f, 1f); break;
                default: color = new Color(0.1f, 0.85f, 0.45f); break;
            }

            colorBlock.SetColor("_Color", color);
            colorBlock.SetColor("_BaseColor", color);
            colorBlock.SetColor("_EmissionColor", color * 2.5f);
            if (statusRenderers == null)
            {
                return;
            }
            foreach (Renderer item in statusRenderers)
            {
                item.SetPropertyBlock(colorBlock);
            }
        }

        private void UpdateLabel()
        {
            if (label != null)
            {
                label.text = "R" + lineId.Replace("line", string.Empty) + "-" +
                             agvId.Replace("AGV_", string.Empty);
            }
        }

        private static Vector3 ToWorld(FactoryTwinController.Position2D point, float scale, float height)
        {
            if (point == null)
            {
                return new Vector3(0f, height, 0f);
            }
            return new Vector3(point.x * scale, height, point.y * scale);
        }
    }
}
