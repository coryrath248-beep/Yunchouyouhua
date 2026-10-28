using System.Collections.Generic;
using UnityEngine;

namespace FactoryTwin
{
    /// <summary>
    /// Lightweight runtime invariants for the local visual flow. It never creates,
    /// moves or repairs cargo; it only reports ownership or transform corruption once.
    /// </summary>
    public sealed class FactoryFlowIntegrityMonitor : MonoBehaviour
    {
        private const float CheckInterval = 0.5f;
        private readonly HashSet<string> reported = new HashSet<string>();
        private float nextCheck;

        public int ActiveCargoCount { get; private set; }
        public int FinishedCargoCount { get; private set; }
        public int ViolationCount => reported.Count;
        public string LastViolation { get; private set; }

        private void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + CheckInterval;
            ValidateWorkpieces();
        }

        private void ValidateWorkpieces()
        {
            var activeIds = new HashSet<string>();
            ActiveCargoCount = 0;
            FinishedCargoCount = 0;

            foreach (FactoryWorkpieceIdentity cargo in FindObjectsOfType<FactoryWorkpieceIdentity>(true))
            {
                if (cargo == null || !cargo.gameObject.activeInHierarchy) continue;
                ActiveCargoCount++;
                if (cargo.State == FactoryWorkpieceState.Finished) FinishedCargoCount++;

                string id = cargo.MaterialId;
                if (string.IsNullOrEmpty(id))
                {
                    ReportOnce("missing-id:" + cargo.GetInstanceID(), "Active cargo has no material id: " + cargo.name);
                    continue;
                }
                if (!activeIds.Add(id))
                {
                    ReportOnce("duplicate:" + id, "Duplicate active material id: " + id);
                }
                if (string.IsNullOrEmpty(cargo.OwnerId))
                {
                    ReportOnce("owner:" + id, "Cargo has no owner: " + id + " / " + cargo.State);
                }

                Vector3 position = cargo.transform.position;
                if (!IsFinite(position) || position.x < -2f || position.x > 30f ||
                    position.y < -0.2f || position.y > 8f || position.z < -2f || position.z > 34f)
                {
                    ReportOnce("position:" + id, "Cargo outside factory bounds: " + id + " @ " + position);
                }

                string parentName = cargo.transform.parent != null ? cargo.transform.parent.name : string.Empty;
                if (cargo.State == FactoryWorkpieceState.OnGripper && parentName != "Grip Anchor")
                {
                    ReportOnce("gripper-parent:" + id, "Gripped cargo is not attached to Grip Anchor: " + id);
                }
                else if (cargo.State == FactoryWorkpieceState.OnRobot && parentName != "Payload Carrier Anchor")
                {
                    ReportOnce("robot-parent:" + id, "Robot cargo is not attached to Payload Carrier Anchor: " + id);
                }
                else if (cargo.State == FactoryWorkpieceState.Finished &&
                         parentName != "FINISHED MATERIAL BUFFER / IDENTIFIED")
                {
                    ReportOnce("finished-parent:" + id, "Finished cargo is outside the finished buffer: " + id);
                }
                else if (cargo.State == FactoryWorkpieceState.Finished && IsInsideLogisticsRail(position))
                {
                    ReportOnce("finished-on-rail:" + id, "Finished cargo was placed inside a logistics rail corridor: " + id);
                }
            }
        }

        private void ReportOnce(string key, string message)
        {
            if (!reported.Add(key)) return;
            LastViolation = message;
            Debug.LogError("[Factory Flow Integrity] " + message);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool IsInsideLogisticsRail(Vector3 position)
        {
            // The cross-aisle rails run along Z. Finished goods must stay on the
            // line-side pallets and never become an obstacle for the shuttle.
            return position.x >= 22.45f && position.x <= 23.85f;
        }
    }
}
