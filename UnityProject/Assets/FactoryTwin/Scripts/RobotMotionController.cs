using UnityEngine;

namespace FactoryTwin
{
    /// <summary>
    /// Owns the rail robot and its arm for one production-line worker slot. The
    /// production orchestrator sequences work through this controller instead of
    /// manipulating AgvView and RobotArmAnimator directly.
    /// </summary>
    public sealed class RobotMotionController
    {
        private const float CoordinateScale = 0.25f;

        public AgvView Robot { get; }
        public RobotArmAnimator Arm { get; }
        public int Index { get; }
        public string Id { get; }
        public float Battery { get; private set; }

        public Transform GripAnchor => Arm != null ? Arm.GripAnchor : null;
        public Transform PayloadAnchor => Arm != null ? Arm.PayloadAnchor : null;

        public RobotMotionController(AgvView robot, RobotArmAnimator arm, int index, string id, float battery)
        {
            Robot = robot;
            Arm = arm;
            Index = index;
            Id = id;
            Battery = battery;
        }

        public Vector3 DeckPoint
        {
            get
            {
                if (Arm != null && Arm.PayloadAnchor != null)
                {
                    return Arm.PayloadAnchor.position;
                }

                return Robot.transform.TransformPoint(new Vector3(0.52f, 0.62f, -0.18f));
            }
        }

        public void ApplyMotion(string status, float targetX, float duration, bool hasPayload, string materialId)
        {
            Vector3 current = Robot.transform.position;
            Battery = Mathf.Max(28f, Battery - (status == "moving" ? 0.12f : 0.006f));
            Robot.Apply(new FactoryTwinController.AgvStatusMessage
            {
                source_id = Id,
                status = status,
                position = FromWorld(current),
                target_position = FromWorld(new Vector3(targetX, current.y, current.z)),
                estimated_time = status == "moving" ? Mathf.Max(0.15f, duration) : 0f,
                battery_level = Battery,
                payload = hasPayload && !string.IsNullOrEmpty(materialId) ? new[] { materialId } : new string[0]
            }, CoordinateScale);
        }

        public void PrepareInteraction(Vector3 from, Vector3 to, float seconds)
        {
            if (Arm != null)
            {
                Arm.SetInteractionTargets(from, to, seconds);
            }
        }

        public void ClearInteraction()
        {
            if (Arm != null)
            {
                Arm.ClearInteractionTargets();
            }
        }

        private static FactoryTwinController.Position2D FromWorld(Vector3 position)
        {
            return new FactoryTwinController.Position2D
            {
                x = position.x / CoordinateScale,
                y = position.z / CoordinateScale
            };
        }
    }
}
