using UnityEngine;

namespace FactoryTwin
{
    /// <summary>
    /// Deterministic ROS-style arm motion. The production coordinator supplies
    /// the real pickup point, delivery point and interaction duration.
    /// </summary>
    public sealed class RobotArmAnimator : MonoBehaviour
    {
        private struct ArmPose
        {
            public float ShoulderYaw;
            public float ShoulderRoll;
            public float ElbowRoll;
            public float WristYaw;
            public float WristRoll;
            public float Grip;

            public ArmPose(float shoulderYaw, float shoulderRoll, float elbowRoll, float wristYaw, float wristRoll, float grip)
            {
                ShoulderYaw = shoulderYaw;
                ShoulderRoll = shoulderRoll;
                ElbowRoll = elbowRoll;
                WristYaw = wristYaw;
                WristRoll = wristRoll;
                Grip = grip;
            }

            public static ArmPose Lerp(ArmPose from, ArmPose to, float t)
            {
                t = Mathf.SmoothStep(0f, 1f, t);
                return new ArmPose(
                    Mathf.LerpAngle(from.ShoulderYaw, to.ShoulderYaw, t),
                    Mathf.LerpAngle(from.ShoulderRoll, to.ShoulderRoll, t),
                    Mathf.LerpAngle(from.ElbowRoll, to.ElbowRoll, t),
                    Mathf.LerpAngle(from.WristYaw, to.WristYaw, t),
                    Mathf.LerpAngle(from.WristRoll, to.WristRoll, t),
                    Mathf.Lerp(from.Grip, to.Grip, t));
            }
        }

        private static readonly float[] StandardPoseTimes = { 0f, 0.16f, 0.28f, 0.43f, 0.60f, 0.74f, 0.85f, 1f };
        private static readonly float[] WarehousePoseTimes = { 0f, 0.42f, 0.53f, 0.64f, 0.72f, 0.82f, 0.90f, 1f };
        private static readonly float[] TargetPoseTimes = { 0f, 0.14f, 0.25f, 0.32f, 0.43f, 0.60f, 0.74f, 0.86f, 1f };

        [SerializeField] private Transform shoulder;
        [SerializeField] private Transform elbow;
        [SerializeField] private Transform wrist;
        [SerializeField] private float operationSeconds = 5.2f;

        private AgvView vehicle;
        private string previousState = string.Empty;
        private float interactionStartedAt;
        private Transform gripperPalm;
        private Transform gripAnchor;
        private Transform leftFinger;
        private Transform rightFinger;
        private Transform payloadAnchor;
        private GameObject payloadVisual;
        private float supplySide;
        private bool warehouseInteraction;
        private bool externalPayload;
        private bool hasInteractionTargets;
        private Vector3 pickupTarget;
        private Vector3 deliveryTarget;
        private float targetedInteractionSeconds;

        /// <summary>
        /// Unscaled tool-space point used by the warehouse flow to attach the real
        /// workpiece to the gripper. Keeping this outside the scaled palm mesh avoids
        /// the visual offset that previously separated the fingers and the cargo.
        /// </summary>
        public Transform GripAnchor => gripAnchor;
        public Transform PayloadAnchor => payloadAnchor;

        public void SetExternalPayload(bool active)
        {
            externalPayload = active;
            if (active)
            {
                SetPayloadVisible(false);
            }
        }

        public void SetInteractionTargets(Vector3 pickupWorldPosition, Vector3 deliveryWorldPosition, float duration = 0f)
        {
            pickupTarget = pickupWorldPosition;
            deliveryTarget = deliveryWorldPosition;
            targetedInteractionSeconds = duration > 0f ? duration : operationSeconds;
            hasInteractionTargets = true;
        }

        public void ClearInteractionTargets()
        {
            hasInteractionTargets = false;
        }

        public void Configure(Transform shoulderJoint, Transform elbowJoint, Transform wristJoint)
        {
            shoulder = shoulderJoint;
            elbow = elbowJoint;
            wrist = wristJoint;
        }

        private void Awake()
        {
            vehicle = GetComponent<AgvView>();
            supplySide = GetSupplySide();
            CreateGripperVisuals();
        }

        private void Update()
        {
            if (shoulder == null || elbow == null || wrist == null)
            {
                return;
            }

            string state = vehicle == null ? "waiting" : vehicle.Status;
            if (state != previousState)
            {
                if (state == "interacting")
                {
                    interactionStartedAt = Time.time;
                    warehouseInteraction = vehicle != null &&
                                                   vehicle.Key == "line3/AGV_2" &&
                                                   transform.position.x > 18f;
                }
                previousState = state;
            }

            if (state == "interacting")
            {
                float duration = hasInteractionTargets
                    ? targetedInteractionSeconds
                    : warehouseInteraction ? WarehouseFlowAnimator.CycleSeconds : operationSeconds;
                float progress = Mathf.Clamp01((Time.time - interactionStartedAt) / duration);
                ApplyPose(EvaluatePickPose(progress));
                float graspTime = warehouseInteraction && !hasInteractionTargets ? 0.525f : 0.275f;
                float releaseTime = warehouseInteraction && !hasInteractionTargets ? 0.90f : 0.85f;
                // The warehouse cycle moves one real cargo object through every stage.
                // Its ownership is handled by WarehouseFlowAnimator, so do not create
                // a second visual copy in the gripper for that interaction.
                SetPayloadVisible(!warehouseInteraction && !externalPayload && progress >= graspTime && progress < releaseTime);
            }
            else
            {
                ApplyPose(GetHomePose());
                SetPayloadVisible(false);
            }
        }

        private ArmPose EvaluatePickPose(float progress)
        {
            ArmPose[] poses;
            if (hasInteractionTargets)
            {
                ArmPose pickupApproach = SolveTargetPose(pickupTarget + Vector3.up * 0.32f, 0f);
                ArmPose pickupOpen = SolveTargetPose(pickupTarget, 0f);
                ArmPose pickupClosed = SolveTargetPose(pickupTarget, 1f);
                ArmPose pickupLift = SolveTargetPose(pickupTarget + Vector3.up * 0.36f, 1f);
                ArmPose deliveryApproach = SolveTargetPose(deliveryTarget + Vector3.up * 0.36f, 1f);
                ArmPose deliveryClosed = SolveTargetPose(deliveryTarget, 1f);
                ArmPose deliveryOpen = SolveTargetPose(deliveryTarget, 0f);
                poses = new[]
                {
                    GetHomePose(),
                    pickupApproach,
                    pickupOpen,
                    pickupClosed,
                    pickupLift,
                    deliveryApproach,
                    deliveryClosed,
                    deliveryOpen,
                    GetHomePose()
                };
            }
            else
            {
                float pickupYaw = -supplySide * 90f;
                float deliveryYaw = supplySide * 52f;
                poses = new[]
                {
                    GetHomePose(),
                    new ArmPose(pickupYaw, -72f, 122f, 0f, 110f, 0f),
                    new ArmPose(pickupYaw, -90f, 135f, 0f, 135f, 1f),
                    new ArmPose(pickupYaw, -62f, 110f, 0f, 100f, 1f),
                    new ArmPose(deliveryYaw, -52f, 96f, 0f, 78f, 1f),
                    new ArmPose(deliveryYaw, -80f, 125f, 0f, 135f, 1f),
                    new ArmPose(deliveryYaw, -80f, 125f, 0f, 135f, 0f),
                    GetHomePose()
                };
            }

            float[] poseTimes = hasInteractionTargets
                ? TargetPoseTimes
                : warehouseInteraction ? WarehousePoseTimes : StandardPoseTimes;
            for (int i = 0; i < poseTimes.Length - 1; i++)
            {
                if (progress <= poseTimes[i + 1])
                {
                    float segment = Mathf.InverseLerp(poseTimes[i], poseTimes[i + 1], progress);
                    return ArmPose.Lerp(poses[i], poses[i + 1], segment);
                }
            }
            return poses[poses.Length - 1];
        }

        private ArmPose SolveTargetPose(Vector3 worldTarget, float grip)
        {
            Vector3 origin = shoulder != null ? shoulder.position : transform.position;
            Vector3 localDirection = transform.InverseTransformDirection(worldTarget - origin);
            float horizontal = new Vector2(localDirection.x, localDirection.z).magnitude;
            float vertical = localDirection.y;
            const float upperLength = 1.02f;
            const float lowerLength = 1.58f;
            float distance = Mathf.Clamp(
                Mathf.Sqrt(horizontal * horizontal + vertical * vertical),
                Mathf.Abs(lowerLength - upperLength) + 0.01f,
                upperLength + lowerLength - 0.01f);
            float elbowCos = Mathf.Clamp(
                (distance * distance - upperLength * upperLength - lowerLength * lowerLength) /
                (2f * upperLength * lowerLength),
                -1f,
                1f);
            float elbowRadians = -Mathf.Acos(elbowCos);
            float shoulderFromHorizontal = Mathf.Atan2(vertical, horizontal) -
                                           Mathf.Atan2(lowerLength * Mathf.Sin(elbowRadians),
                                               upperLength + lowerLength * Mathf.Cos(elbowRadians));
            float shoulderRoll = shoulderFromHorizontal * Mathf.Rad2Deg - 90f;
            float shoulderYaw = horizontal < 0.001f
                ? 0f
                : Mathf.Atan2(-localDirection.z, localDirection.x) * Mathf.Rad2Deg;
            return new ArmPose(shoulderYaw, shoulderRoll, elbowRadians * Mathf.Rad2Deg, 0f, 0f, grip);
        }

        private static ArmPose GetHomePose()
        {
            return new ArmPose(0f, -84f, 114f, 0f, 120f, 0f);
        }

        private void ApplyPose(ArmPose pose)
        {
            shoulder.localRotation = Quaternion.Euler(0f, pose.ShoulderYaw, pose.ShoulderRoll);
            elbow.localRotation = Quaternion.Euler(0f, 0f, pose.ElbowRoll);
            wrist.localRotation = Quaternion.Euler(0f, pose.WristYaw, pose.WristRoll);

            float fingerGap = Mathf.Lerp(0.25f, 0.185f, pose.Grip);
            if (leftFinger != null)
            {
                leftFinger.localPosition = new Vector3(-fingerGap, 0.27f, 0f);
            }
            if (rightFinger != null)
            {
                rightFinger.localPosition = new Vector3(fingerGap, 0.27f, 0f);
            }
        }

        private void CreateGripperVisuals()
        {
            if (wrist == null)
            {
                return;
            }

            Transform palmVisual = wrist.Find("Gripper");
            if (palmVisual == null)
            {
                return;
            }

            // The visible palm is a non-uniformly scaled cube. Children placed below it
            // inherit that scale and become both tiny and displaced. Use a scale-one
            // tool mount at the same pose for fingers, cargo and hand-off coordinates.
            GameObject toolMount = new GameObject("Gripper Tool Mount");
            toolMount.transform.SetParent(wrist, false);
            toolMount.transform.localPosition = palmVisual.localPosition;
            toolMount.transform.localRotation = palmVisual.localRotation;
            gripperPalm = toolMount.transform;

            Material material = palmVisual.GetComponent<Renderer>()?.sharedMaterial;
            leftFinger = CreateCube("Left Gripper Finger", gripperPalm, new Vector3(-0.25f, 0.27f, 0f), new Vector3(0.07f, 0.42f, 0.09f), material, new Color(0.10f, 0.13f, 0.14f)).transform;
            rightFinger = CreateCube("Right Gripper Finger", gripperPalm, new Vector3(0.25f, 0.27f, 0f), new Vector3(0.07f, 0.42f, 0.09f), material, new Color(0.10f, 0.13f, 0.14f)).transform;

            GameObject anchorObject = new GameObject("Grip Anchor");
            anchorObject.transform.SetParent(gripperPalm, false);
            anchorObject.transform.localPosition = new Vector3(0f, 0.27f, 0f);
            gripAnchor = anchorObject.transform;

            // A raised, visible carrier keeps the real workpiece clear of the robot
            // base while travelling. Previously it was placed directly beside the
            // shoulder column and appeared to vanish inside the base mesh.
            CreateCube("Payload Carrier Tray", transform, new Vector3(0.52f, 0.44f, -0.18f),
                new Vector3(0.46f, 0.06f, 0.46f), material, new Color(0.08f, 0.12f, 0.14f));
            GameObject payloadAnchorObject = new GameObject("Payload Carrier Anchor");
            payloadAnchorObject.transform.SetParent(transform, false);
            payloadAnchorObject.transform.localPosition = new Vector3(0.52f, 0.62f, -0.18f);
            payloadAnchor = payloadAnchorObject.transform;

            payloadVisual = CreateCube("Picked Workpiece", gripAnchor, Vector3.zero, new Vector3(0.30f, 0.28f, 0.30f), material, new Color(1f, 0.43f, 0.04f));
            payloadVisual.SetActive(false);
        }

        private float GetSupplySide()
        {
            int railIndex = Mathf.Max(0, Mathf.RoundToInt((transform.position.z - 2.5f) / 5f));
            return railIndex % 2 == 0 ? -1f : 1f;
        }

        private static GameObject CreateCube(string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material, Color color)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            Renderer renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            block.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(block);
            Destroy(part.GetComponent<Collider>());
            return part;
        }

        private void SetPayloadVisible(bool visible)
        {
            if (payloadVisual != null && payloadVisual.activeSelf != visible)
            {
                payloadVisual.SetActive(visible);
            }
        }
    }
}
