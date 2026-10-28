using System.Collections.Generic;
using UnityEngine;

namespace FactoryTwin
{
    public sealed class WarehouseFlowAnimator : MonoBehaviour
    {
        private enum Phase
        {
            Idle,
            MoveToSlot,
            ExtendFork,
            RetractFork,
            MoveToOutfeed,
            PlaceOnOutfeed,
            ClearOutfeed,
            TransferToShuttle,
            Distribute,
            Ready,
            ReturnToHome
        }

        public const float CycleSeconds = 12f;
        private readonly List<Renderer> inventory = new List<Renderer>();
        private readonly List<Transform> outfeedRollers = new List<Transform>();
        private Transform outfeedRoot;
        private Transform distributionShuttle;
        private Transform shuttleLoadAnchor;
        private Transform craneRoot;
        private Transform craneCarriage;
        private Transform forkLoadAnchor;
        private Transform[] craneForks = new Transform[0];
        private Vector3 craneRootRest;
        private Vector3 carriageRest;
        private Vector3[] forkRest = new Vector3[0];
        private Vector3 forkAnchorRestLocal;
        private Vector3 forkAnchorRestWorld;
        private Vector3 shuttleStart;
        private Vector3 shuttleMotionStart;
        private Vector3 craneMotionStart;
        private Renderer statusStrip;
        private FactoryWorkpieceIdentity activeIdentity;
        private Renderer sourceRenderer;
        private GameObject activeCargo;
        private string activeMaterialId;
        private int activeLine;
        private float targetHandoffZ;
        private int inventoryCursor;
        private float phaseStarted;
        private float phaseDuration;
        private float currentLiftOffset;
        private float currentForkExtension;
        private bool handoffClaimed;
        private bool running;
        private float pausedAt = -1f;
        private float carriageMotionStartY;
        private float forkMotionStart;
        private float slotForkExtension;
        private Phase phase;
        private Vector3 cargoMotionStart;

        public bool IsBusy => phase != Phase.Idle;
        public bool IsReady => phase == Phase.Ready && activeCargo != null && !handoffClaimed;
        public bool IsDepleted => inventoryCursor >= inventory.Count && phase == Phase.Idle;
        public int RemainingInventory => Mathf.Max(0, inventory.Count - inventoryCursor);

        public void SetRunning(bool value)
        {
            if (running == value) return;
            if (value)
            {
                if (pausedAt >= 0f && phase != Phase.Idle && phase != Phase.Ready)
                {
                    phaseStarted += Mathf.Max(0f, Time.time - pausedAt);
                }
                pausedAt = -1f;
                running = true;
                return;
            }

            pausedAt = Time.time;
            running = false;
        }

        private void Start()
        {
            FindSceneEquipment();
            BuildSharedLogisticsCell();
            RestoreEquipment();
        }

        public bool RequestMaterial(string materialId, int line, float handoffZ)
        {
            if (phase != Phase.Idle || inventoryCursor >= inventory.Count || string.IsNullOrEmpty(materialId)) return false;

            sourceRenderer = inventory[inventoryCursor++];
            activeMaterialId = materialId;
            activeLine = line;
            targetHandoffZ = handoffZ;
            handoffClaimed = false;
            sourceRenderer.enabled = false;
            activeCargo = CreateCargo(materialId, sourceRenderer.sharedMaterial);
            activeCargo.transform.position = sourceRenderer.transform.position;
            activeIdentity = activeCargo.GetComponent<FactoryWorkpieceIdentity>();
            activeIdentity.SetState(FactoryWorkpieceState.InRack, "stacker_01");
            shuttleMotionStart = distributionShuttle.position;
            SetPhase(Phase.MoveToSlot, 2.4f);
            SetStatusColor(new Color(1f, 0.48f, 0.06f));
            return true;
        }

        public bool TryTakeDeliveredCargo(string materialId, out GameObject cargo)
        {
            cargo = null;
            if (phase != Phase.Ready || handoffClaimed || activeCargo == null || activeMaterialId != materialId) return false;

            cargo = activeCargo;
            cargo.transform.SetParent(null, true);
            activeIdentity.SetState(FactoryWorkpieceState.AtHandoff, "distribution_shuttle");
            handoffClaimed = true;
            return true;
        }

        public bool ConfirmPickup(string materialId)
        {
            if (phase != Phase.Ready || !handoffClaimed || activeMaterialId != materialId) return false;

            activeCargo = null;
            activeIdentity = null;
            activeMaterialId = null;
            sourceRenderer = null;
            activeLine = 0;
            handoffClaimed = false;
            shuttleMotionStart = distributionShuttle.position;
            float returnSeconds = Mathf.Max(0.8f, Mathf.Abs(shuttleStart.z - shuttleMotionStart.z) / 4.2f);
            SetPhase(Phase.ReturnToHome, returnSeconds);
            SetStatusColor(new Color(1f, 0.48f, 0.06f));
            return true;
        }

        private void Update()
        {
            if (!running) return;
            if (phase == Phase.Idle || phase == Phase.Ready) return;
            if (phase != Phase.ReturnToHome && activeCargo == null) return;
            float progress = Mathf.Clamp01((Time.time - phaseStarted) / Mathf.Max(0.01f, phaseDuration));
            AnimatePhase(progress);
            foreach (Transform roller in outfeedRollers) roller.Rotate(Vector3.up, 220f * Time.deltaTime, Space.Self);
            if (progress >= 1f) AdvancePhase();
        }

        private void AnimatePhase(float progress)
        {
            float t = Mathf.SmoothStep(0f, 1f, progress);
            // ReturnToHome runs after the handoff has released sourceRenderer.
            // Only retrieval phases need the rack-slot position.
            Vector3 source = sourceRenderer != null ? sourceRenderer.transform.position : Vector3.zero;
            switch (phase)
            {
                case Phase.MoveToSlot:
                    Vector3 slotRoot = craneRootRest + Vector3.right * (source.x - forkAnchorRestWorld.x);
                    float slotCarriageY = carriageRest.y + source.y - forkAnchorRestWorld.y;
                    craneRoot.position = Vector3.Lerp(craneMotionStart, slotRoot, t);
                    SetCarriageHeight(Mathf.Lerp(carriageMotionStartY, slotCarriageY, t));
                    distributionShuttle.position = Vector3.Lerp(shuttleMotionStart, shuttleStart, t);
                    SyncShuttleLoadAnchor();
                    activeCargo.transform.position = source;
                    break;
                case Phase.ExtendFork:
                    SetForkExtension(Mathf.Lerp(forkMotionStart, slotForkExtension, t));
                    activeCargo.transform.position = source;
                    break;
                case Phase.RetractFork:
                    SetForkExtension(Mathf.Lerp(forkMotionStart, 0f, t));
                    activeIdentity.SetState(FactoryWorkpieceState.OnStacker, "stacker_01");
                    break;
                case Phase.MoveToOutfeed:
                    Vector3 outfeedRootPosition = craneRootRest + Vector3.left * 0.60f;
                    craneRoot.position = Vector3.Lerp(craneMotionStart, outfeedRootPosition, t);
                    SetCarriageHeight(Mathf.Lerp(carriageMotionStartY, 1.36f, t));
                    break;
                case Phase.PlaceOnOutfeed:
                    SetCarriageHeight(Mathf.Lerp(carriageMotionStartY, 1.11f, t));
                    break;
                case Phase.ClearOutfeed:
                    craneRoot.position = Vector3.Lerp(craneMotionStart, craneRootRest, t);
                    SetCarriageHeight(Mathf.Lerp(carriageMotionStartY, carriageRest.y, t));
                    break;
                case Phase.TransferToShuttle:
                    activeCargo.transform.position = Vector3.Lerp(cargoMotionStart, ShuttleCargoPoint(shuttleStart), t);
                    break;
                case Phase.Distribute:
                    distributionShuttle.position = Vector3.Lerp(shuttleMotionStart, new Vector3(shuttleStart.x, shuttleStart.y, targetHandoffZ), t);
                    SyncShuttleLoadAnchor();
                    activeIdentity.SetState(FactoryWorkpieceState.OnConveyor, "distribution_shuttle");
                    break;
                case Phase.ReturnToHome:
                    distributionShuttle.position = Vector3.Lerp(shuttleMotionStart, shuttleStart, t);
                    SyncShuttleLoadAnchor();
                    break;
            }
        }

        private void AdvancePhase()
        {
            switch (phase)
            {
                case Phase.MoveToSlot:
                    slotForkExtension = sourceRenderer.transform.position.z - forkAnchorRestWorld.z;
                    SetPhase(Phase.ExtendFork, 0.9f);
                    break;
                case Phase.ExtendFork:
                    activeCargo.transform.SetParent(forkLoadAnchor, false);
                    activeCargo.transform.localPosition = Vector3.zero;
                    activeCargo.transform.localRotation = Quaternion.identity;
                    activeIdentity.SetState(FactoryWorkpieceState.OnStacker, "stacker_01");
                    SetPhase(Phase.RetractFork, 0.9f);
                    break;
                case Phase.RetractFork:
                    SetPhase(Phase.MoveToOutfeed, 2.3f);
                    break;
                case Phase.MoveToOutfeed:
                    SetPhase(Phase.PlaceOnOutfeed, 0.7f);
                    break;
                case Phase.PlaceOnOutfeed:
                    activeCargo.transform.SetParent(outfeedRoot, true);
                    activeCargo.transform.position = OutfeedCargoPoint();
                    SetPhase(Phase.ClearOutfeed, 0.9f);
                    break;
                case Phase.ClearOutfeed:
                    cargoMotionStart = activeCargo.transform.position;
                    activeIdentity.SetState(FactoryWorkpieceState.OnConveyor, "asrs_outfeed");
                    SetPhase(Phase.TransferToShuttle, 1.0f);
                    break;
                case Phase.TransferToShuttle:
                    shuttleMotionStart = distributionShuttle.position;
                    activeCargo.transform.SetParent(shuttleLoadAnchor, false);
                    activeCargo.transform.localPosition = Vector3.zero;
                    activeCargo.transform.localRotation = Quaternion.identity;
                    SetPhase(Phase.Distribute, Mathf.Max(0.8f, Mathf.Abs(targetHandoffZ - shuttleStart.z) / 4.2f));
                    break;
                case Phase.Distribute:
                    activeCargo.transform.SetParent(shuttleLoadAnchor, false);
                    activeCargo.transform.localPosition = Vector3.zero;
                    activeCargo.transform.localRotation = Quaternion.identity;
                    activeIdentity.SetState(FactoryWorkpieceState.AtHandoff, "line" + activeLine + "_handoff");
                    SetPhase(Phase.Ready, 0f);
                    SetStatusColor(new Color(0.18f, 0.92f, 0.55f));
                    RestoreCraneOnly();
                    break;
                case Phase.ReturnToHome:
                    distributionShuttle.position = shuttleStart;
                    SyncShuttleLoadAnchor();
                    SetPhase(Phase.Idle, 0f);
                    SetStatusColor(new Color(0.18f, 0.65f, 0.72f));
                    break;
            }
        }

        private void FindSceneEquipment()
        {
            GameObject rack = GameObject.Find("LINE-SIDE ASRS / 60 SLOTS");
            if (rack != null)
            {
                foreach (Transform child in rack.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name != "Stored Tote") continue;
                    Renderer renderer = child.GetComponent<Renderer>();
                    if (renderer != null) inventory.Add(renderer);
                }
            }
            inventory.Sort((left, right) =>
            {
                int level = left.transform.position.y.CompareTo(right.transform.position.y);
                return level != 0 ? level : right.transform.position.x.CompareTo(left.transform.position.x);
            });

            GameObject crane = GameObject.Find("ASRS STACKER CRANE");
            if (crane == null) return;
            craneRoot = crane.transform;
            craneRootRest = craneRoot.position;
            craneCarriage = FindChild(craneRoot, "Crane Carriage");
            var forks = new List<Transform>();
            foreach (Transform child in crane.GetComponentsInChildren<Transform>(true)) if (child.name == "Fork") forks.Add(child);
            craneForks = forks.ToArray();
            carriageRest = craneCarriage != null ? craneCarriage.position : Vector3.zero;
            forkRest = new Vector3[craneForks.Length];
            for (int i = 0; i < craneForks.Length; i++) forkRest[i] = craneForks[i].localPosition;
            if (craneForks.Length > 0)
            {
                Vector3 sum = Vector3.zero;
                foreach (Vector3 rest in forkRest) sum += rest;
                // Carry the tote on the front half of the forks. The old anchor was
                // centred under the yellow carriage, so their meshes overlapped after
                // retraction and visually read as one vibrating object.
                forkAnchorRestLocal = sum / craneForks.Length + Vector3.up * 0.19f + Vector3.back * 0.23f;
                GameObject anchor = new GameObject("Fork Load Anchor");
                anchor.transform.SetParent(craneRoot, false);
                anchor.transform.localPosition = forkAnchorRestLocal;
                forkLoadAnchor = anchor.transform;
                forkAnchorRestWorld = forkLoadAnchor.position;
            }
        }

        private void BuildSharedLogisticsCell()
        {
            outfeedRoot = new GameObject("ASRS OUTFEED + CENTRAL DISTRIBUTION").transform;
            Material reference = inventory.Count > 0 ? inventory[0].sharedMaterial : null;
            CreateBox("Outfeed Chassis", outfeedRoot, new Vector3(22.05f, 0.65f, 28.55f), new Vector3(0.90f, 0.18f, 1.72f), reference, new Color(0.31f, 0.39f, 0.42f));
            CreateBox("Outfeed Belt", outfeedRoot, new Vector3(22.05f, 0.77f, 28.55f), new Vector3(0.72f, 0.055f, 1.58f), reference, new Color(0.055f, 0.09f, 0.105f));
            for (int i = 0; i < 7; i++)
            {
                GameObject roller = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                roller.name = "Outfeed Roller";
                roller.transform.SetParent(outfeedRoot, true);
                roller.transform.position = new Vector3(22.05f, 0.82f, 27.92f + i * 0.21f);
                roller.transform.localScale = new Vector3(0.055f, 0.34f, 0.055f);
                roller.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                ApplyMaterial(roller.GetComponent<Renderer>(), reference, new Color(0.55f, 0.62f, 0.64f));
                Destroy(roller.GetComponent<Collider>());
                outfeedRollers.Add(roller.transform);
            }
            statusStrip = CreateBox("Outfeed Status", outfeedRoot, new Vector3(21.57f, 0.72f, 28.55f), new Vector3(0.035f, 0.08f, 1.55f), reference, new Color(0.06f, 0.62f, 0.68f)).GetComponent<Renderer>();

            Transform distribution = new GameObject("CENTRAL CROSS-AISLE DISTRIBUTION").transform;
            CreateBox("Distribution Rail L", distribution, new Vector3(22.82f, 0.22f, 15.25f), new Vector3(0.07f, 0.08f, 27.1f), reference, new Color(0.06f, 0.62f, 0.68f));
            CreateBox("Distribution Rail R", distribution, new Vector3(23.48f, 0.22f, 15.25f), new Vector3(0.07f, 0.08f, 27.1f), reference, new Color(0.06f, 0.62f, 0.68f));
            distributionShuttle = CreateBox("Distribution Shuttle", distribution, new Vector3(23.15f, 0.54f, 28.55f), new Vector3(0.84f, 0.18f, 0.92f), reference, new Color(0.95f, 0.58f, 0.08f)).transform;
            shuttleStart = distributionShuttle.position;

            // The shuttle mesh is non-uniformly scaled. Parenting cargo directly to
            // that mesh flattened it and pulled it inside the orange platform. Keep
            // a scale-one sibling anchor on the actual deck surface instead.
            GameObject loadAnchor = new GameObject("Distribution Shuttle Load Anchor");
            loadAnchor.transform.SetParent(distribution, true);
            loadAnchor.transform.position = ShuttleCargoPoint(shuttleStart);
            shuttleLoadAnchor = loadAnchor.transform;
        }

        private GameObject CreateCargo(string materialId, Material material)
        {
            GameObject cargo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cargo.name = materialId;
            cargo.transform.localScale = new Vector3(0.34f, 0.30f, 0.34f);
            ApplyMaterial(cargo.GetComponent<Renderer>(), material, Color.white);
            Destroy(cargo.GetComponent<Collider>());
            cargo.AddComponent<FactoryWorkpieceIdentity>().Initialize(materialId, activeLine);
            return cargo;
        }

        private Vector3 ForkCargoPoint()
        {
            return forkLoadAnchor != null
                ? forkLoadAnchor.position
                : craneCarriage != null ? craneCarriage.position : Vector3.zero;
        }

        private void SetForkExtension(float distance)
        {
            currentForkExtension = distance;
            ApplyForkPose();
        }

        private void SetCarriageHeight(float y)
        {
            if (craneCarriage == null) return;
            Vector3 position = craneCarriage.position;
            position.y = y;
            craneCarriage.position = position;
            currentLiftOffset = y - carriageRest.y;
            ApplyForkPose();
        }

        private void ApplyForkPose()
        {
            for (int i = 0; i < craneForks.Length; i++)
            {
                Vector3 local = forkRest[i];
                local.y += currentLiftOffset;
                local.z += currentForkExtension;
                craneForks[i].localPosition = local;
            }
            if (forkLoadAnchor != null)
            {
                Vector3 anchor = forkAnchorRestLocal;
                anchor.y += currentLiftOffset;
                anchor.z += currentForkExtension;
                forkLoadAnchor.localPosition = anchor;
            }
        }

        private void RestoreEquipment()
        {
            RestoreCraneOnly();
            if (distributionShuttle != null) distributionShuttle.position = shuttleStart;
            SyncShuttleLoadAnchor();
            handoffClaimed = false;
            phase = Phase.Idle;
            SetStatusColor(new Color(0.18f, 0.65f, 0.72f));
        }

        private void RestoreCraneOnly()
        {
            if (craneRoot != null) craneRoot.position = craneRootRest;
            if (craneCarriage != null) craneCarriage.position = carriageRest;
            currentLiftOffset = 0f;
            currentForkExtension = 0f;
            ApplyForkPose();
        }

        private void SetPhase(Phase next, float duration)
        {
            phase = next;
            phaseStarted = Time.time;
            phaseDuration = duration;
            craneMotionStart = craneRoot != null ? craneRoot.position : Vector3.zero;
            carriageMotionStartY = craneCarriage != null ? craneCarriage.position.y : 0f;
            forkMotionStart = currentForkExtension;
        }

        private static Vector3 OutfeedCargoPoint()
        {
            return new Vector3(22.05f, 1.03f, 28.39f);
        }

        private static Vector3 ShuttleCargoPoint(Vector3 shuttlePosition)
        {
            // Shuttle top: 0.54 + 0.18 / 2. Cargo half-height: 0.30 / 2.
            // A small clearance keeps the meshes visually separated without floating.
            return shuttlePosition + Vector3.up * 0.255f;
        }

        private void SyncShuttleLoadAnchor()
        {
            if (shuttleLoadAnchor == null || distributionShuttle == null) return;
            shuttleLoadAnchor.position = ShuttleCargoPoint(distributionShuttle.position);
            shuttleLoadAnchor.rotation = Quaternion.identity;
            shuttleLoadAnchor.localScale = Vector3.one;
        }

        private void SetStatusColor(Color color)
        {
            if (statusStrip == null) return;
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            block.SetColor("_BaseColor", color);
            block.SetColor("_EmissionColor", color * 1.6f);
            statusStrip.SetPropertyBlock(block);
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) if (child.name == name) return child;
            return null;
        }

        private static GameObject CreateBox(string name, Transform parent, Vector3 position, Vector3 scale, Material material, Color color)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, true);
            box.transform.position = position;
            box.transform.localScale = scale;
            ApplyMaterial(box.GetComponent<Renderer>(), material, color);
            Destroy(box.GetComponent<Collider>());
            return box;
        }

        private static void ApplyMaterial(Renderer renderer, Material material, Color color)
        {
            if (material != null) renderer.sharedMaterial = material;
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            block.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(block);
        }
    }
}
