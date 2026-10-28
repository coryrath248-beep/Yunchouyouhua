using System;
using System.Collections.Generic;
using UnityEngine;

namespace FactoryTwin
{
    public enum FactoryWorkpieceState
    {
        InRack,
        OnStacker,
        OnConveyor,
        AtHandoff,
        OnGripper,
        OnRobot,
        InMachine,
        Finished
    }

    public sealed class FactoryWorkpieceIdentity : MonoBehaviour
    {
        public string MaterialId { get; private set; }
        public int Line { get; private set; }
        public FactoryWorkpieceState State { get; private set; }
        public string OwnerId { get; private set; }
        public int TransitionIndex { get; private set; }
        public float LastStateChangeTime { get; private set; }

        public void Initialize(string materialId, int line)
        {
            MaterialId = materialId;
            Line = line;
            State = FactoryWorkpieceState.InRack;
            TransitionIndex = 0;
            LastStateChangeTime = Time.time;
            name = materialId;
        }

        public bool SetState(FactoryWorkpieceState state, string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId))
            {
                Debug.LogError("Workpiece " + MaterialId + " cannot enter " + state + " without an owner.");
                return false;
            }

            if (state != State && !IsLegalTransition(State, state))
            {
                Debug.LogError("Illegal workpiece transition " + MaterialId + ": " + State + " → " + state + ".");
                return false;
            }

            if (state == State && ownerId == OwnerId) return true;

            State = state;
            OwnerId = ownerId;
            TransitionIndex++;
            LastStateChangeTime = Time.time;
            return true;
        }

        private static bool IsLegalTransition(FactoryWorkpieceState from, FactoryWorkpieceState to)
        {
            switch (from)
            {
                case FactoryWorkpieceState.InRack:
                    return to == FactoryWorkpieceState.OnStacker;
                case FactoryWorkpieceState.OnStacker:
                    return to == FactoryWorkpieceState.OnConveyor;
                case FactoryWorkpieceState.OnConveyor:
                    return to == FactoryWorkpieceState.AtHandoff || to == FactoryWorkpieceState.OnGripper;
                case FactoryWorkpieceState.AtHandoff:
                    return to == FactoryWorkpieceState.OnGripper;
                case FactoryWorkpieceState.OnGripper:
                    return to == FactoryWorkpieceState.OnRobot ||
                           to == FactoryWorkpieceState.InMachine ||
                           to == FactoryWorkpieceState.OnConveyor ||
                           to == FactoryWorkpieceState.Finished;
                case FactoryWorkpieceState.OnRobot:
                case FactoryWorkpieceState.InMachine:
                    return to == FactoryWorkpieceState.OnGripper;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Runs one traceable workpiece per production line. The two robots on a line
    /// alternate between stations and exchange the same physical workpiece through
    /// the three real conveyor objects. No fallback or duplicate cargo is created.
    /// </summary>
    public sealed class FactoryProductionFlowAnimator : MonoBehaviour
    {
        private enum Stage
        {
            Delay,
            TravelToSource,
            RequestWarehouse,
            WaitWarehouse,
            SourcePick,
            TravelToMachine,
            MachineLoad,
            Processing,
            MachineUnload,
            TravelToConveyor,
            ConveyorDrop,
            ConveyorTransfer,
            ConveyorPickup,
            TravelToOutput,
            OutputDrop
        }

        private sealed class LineJob
        {
            public int Line;
            public readonly RobotMotionController[] Workers = new RobotMotionController[2];
            public int WorkerIndex;
            public int StationIndex;
            public Stage Stage;
            public float StageStarted;
            public float StageDuration;
            public string MaterialId;
            public GameObject Cargo;
            public FactoryWorkpieceIdentity Identity;
            public Vector3 CargoStart;
            public Vector3 CargoTarget;
            public Vector3 MachinePort;
            public Vector3 FinishedPosition;
            public MachineFlowController Machine;
            public ConveyorFlowController Conveyor;

            public RobotMotionController ActiveWorker => Workers[WorkerIndex];
        }

        private static readonly string[] StationNames =
        {
            "StationA", "StationB", "StationC", "QualityCheck"
        };

        private static readonly string[] ConveyorNames =
        {
            "Conveyor_AB", "Conveyor_BC", "Conveyor_CQ"
        };

        private const float SourceX = 22f;
        private const float TravelSpeed = 3.25f;
        private const float InteractionSeconds = 4.4f;
        private const float ConveyorSeconds = 3.8f;
        private readonly List<LineJob> lines = new List<LineJob>();
        private readonly Dictionary<string, DeviceView> devices = new Dictionary<string, DeviceView>();
        private readonly Dictionary<string, MachineFlowController> machines =
            new Dictionary<string, MachineFlowController>();
        private readonly Dictionary<string, ConveyorFlowController> conveyors =
            new Dictionary<string, ConveyorFlowController>();
        private readonly List<GameObject> finishedCargo = new List<GameObject>();
        private readonly int[] completedByLine = new int[3];
        private WarehouseFlowAnimator warehouse;
        private FactoryTwinController controller;
        private ResourcePool resources;
        private Transform transitRoot;
        private Transform finishedRoot;
        private bool running;
        private float pausedAt = -1f;
        private int completedJobs;
        private int eventCount;
        private int materialSerial;

        public int CompletedJobs => completedJobs;
        public int EventCount => eventCount;

        private void Start()
        {
            resources = new ResourcePool();
            warehouse = FindObjectOfType<WarehouseFlowAnimator>();
            controller = GetComponent<FactoryTwinController>();
            foreach (DeviceView device in FindObjectsOfType<DeviceView>())
            {
                devices[device.Key] = device;
                device.Apply("idle", "Awaiting identified material");
            }

            for (int line = 1; line <= 3; line++)
            {
                for (int station = 0; station < StationNames.Length; station++)
                {
                    var machine = new MachineFlowController(line, station);
                    devices.TryGetValue("line" + line + "/" + StationNames[station], out DeviceView machineView);
                    machine.Bind(machineView);
                    machines[machine.Id] = machine;
                }

                for (int station = 0; station < ConveyorNames.Length; station++)
                {
                    var conveyor = new ConveyorFlowController(line, station);
                    devices.TryGetValue("line" + line + "/" + ConveyorNames[station], out DeviceView conveyorView);
                    conveyor.Bind(conveyorView);
                    conveyors[conveyor.Id] = conveyor;
                }
            }

            transitRoot = new GameObject("LIVE MATERIAL FLOW / IN TRANSIT").transform;
            finishedRoot = new GameObject("FINISHED MATERIAL BUFFER / IDENTIFIED").transform;
            BuildVisibleMachineLoadNests();
            BuildFinishedGoodsBuffers();

            var robots = new List<AgvView>(FindObjectsOfType<AgvView>());
            robots.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
            for (int line = 1; line <= 3; line++)
            {
                var job = new LineJob
                {
                    Line = line,
                    Stage = Stage.Delay,
                    StageStarted = Time.time,
                    StageDuration = 0.7f + (line - 1) * 1.1f
                };

                foreach (AgvView robot in robots)
                {
                    if (!robot.Key.StartsWith("line" + line + "/", StringComparison.Ordinal)) continue;
                    int index = robot.Key.EndsWith("AGV_2", StringComparison.Ordinal) ? 1 : 0;
                    job.Workers[index] = new RobotMotionController(
                        robot,
                        robot.GetComponent<RobotArmAnimator>(),
                        index,
                        RobotId(line, index),
                        97f - (line - 1) * 4f - index * 2f);
                    job.Workers[index].Arm?.SetExternalPayload(true);
                }

                if (job.Workers[0] == null || job.Workers[1] == null)
                {
                    Debug.LogError("Production line " + line + " does not have both robot workers.");
                    continue;
                }

                lines.Add(job);
                SetRobot(job, job.Workers[0], "waiting", job.Workers[0].Robot.transform.position.x, 0.1f, false);
                SetRobot(job, job.Workers[1], "waiting", job.Workers[1].Robot.transform.position.x, 0.1f, false);
            }
        }

        public void SetRunning(bool value)
        {
            if (running == value) return;

            if (value)
            {
                if (pausedAt >= 0f)
                {
                    float pauseDuration = Mathf.Max(0f, Time.time - pausedAt);
                    foreach (LineJob job in lines) job.StageStarted += pauseDuration;
                }
                pausedAt = -1f;
                running = true;
                warehouse?.SetRunning(true);
                return;
            }

            running = value;
            pausedAt = Time.time;
            warehouse?.SetRunning(false);
            foreach (LineJob job in lines)
            {
                foreach (RobotMotionController worker in job.Workers)
                {
                    worker.ClearInteraction();
                    SetRobot(job, worker, "waiting", worker.Robot.transform.position.x, 0.1f, false);
                }
            }
        }

        private void Update()
        {
            if (!running || warehouse == null) return;
            foreach (LineJob job in lines) UpdateLine(job);
        }

        private void UpdateLine(LineJob job)
        {
            float progress = Mathf.Clamp01((Time.time - job.StageStarted) / Mathf.Max(0.01f, job.StageDuration));
            switch (job.Stage)
            {
                case Stage.Delay:
                    if (progress >= 1f) BeginTravelToSource(job);
                    break;
                case Stage.TravelToSource:
                    if (progress >= 1f) SetStage(job, Stage.RequestWarehouse, 0.1f);
                    break;
                case Stage.RequestWarehouse:
                    TryRequestWarehouse(job);
                    break;
                case Stage.WaitWarehouse:
                    TryAcceptWarehouseCargo(job);
                    break;
                case Stage.SourcePick:
                    AnimatePickToRobot(job, progress);
                    if (progress >= 1f) BeginTravelToMachine(job);
                    break;
                case Stage.TravelToMachine:
                    if (progress >= 1f) BeginMachineLoad(job);
                    break;
                case Stage.MachineLoad:
                    AnimateRobotToTarget(job, progress, job.MachinePort, FactoryWorkpieceState.InMachine,
                        MachineFlowController.MachineId(job.Line, job.StationIndex));
                    if (progress >= 1f) BeginProcessing(job);
                    break;
                case Stage.Processing:
                    if (progress >= 1f) BeginMachineUnload(job);
                    break;
                case Stage.MachineUnload:
                    AnimatePickToRobot(job, progress);
                    if (progress >= 1f)
                    {
                        if (job.StationIndex < 3) BeginTravelToConveyor(job);
                        else BeginTravelToOutput(job);
                    }
                    break;
                case Stage.TravelToConveyor:
                    if (progress >= 1f) BeginConveyorDrop(job);
                    break;
                case Stage.ConveyorDrop:
                    AnimateRobotToTarget(job, progress, job.CargoTarget, FactoryWorkpieceState.OnConveyor,
                        ConveyorFlowController.ConveyorId(job.Line, job.StationIndex));
                    if (progress >= 1f) BeginConveyorTransfer(job);
                    break;
                case Stage.ConveyorTransfer:
                    AnimateConveyor(job, progress);
                    if (progress >= 1f) BeginConveyorPickup(job);
                    break;
                case Stage.ConveyorPickup:
                    AnimatePickToRobot(job, progress);
                    if (progress >= 1f)
                    {
                        job.StationIndex++;
                        BeginTravelToMachine(job);
                    }
                    break;
                case Stage.TravelToOutput:
                    if (progress >= 1f) BeginOutputDrop(job);
                    break;
                case Stage.OutputDrop:
                    AnimateRobotToTarget(job, progress, job.FinishedPosition, FactoryWorkpieceState.Finished, "finished_goods");
                    if (progress >= 1f) FinishJob(job);
                    break;
            }
        }

        private void BeginTravelToSource(LineJob job)
        {
            job.MaterialId = "PART-C-" + (++materialSerial).ToString("D5");
            job.StationIndex = 0;
            job.WorkerIndex = 0;
            RobotMotionController worker = job.ActiveWorker;
            float duration = TravelDuration(worker.Robot.transform.position.x, SourceX);
            SetStage(job, Stage.TravelToSource, duration);
            SetRobot(job, worker, "moving", SourceX, duration, false);
            SetOtherWorkerWaiting(job);
        }

        private void TryRequestWarehouse(LineJob job)
        {
            RobotMotionController worker = job.ActiveWorker;
            if (warehouse.RequestMaterial(job.MaterialId, job.Line, worker.Robot.transform.position.z))
            {
                SetStage(job, Stage.WaitWarehouse, 1f);
                SetRobot(job, worker, "waiting", SourceX, 0.1f, false);
                Report(job.MaterialId + "  ASRS RETRIEVAL  →  L" + job.Line);
            }
            else if (warehouse.IsDepleted)
            {
                SetRobot(job, worker, "waiting", SourceX, 0.1f, false);
            }
        }

        private void TryAcceptWarehouseCargo(LineJob job)
        {
            if (!warehouse.TryTakeDeliveredCargo(job.MaterialId, out GameObject cargo)) return;
            job.Cargo = cargo;
            job.Identity = cargo.GetComponent<FactoryWorkpieceIdentity>();
            job.CargoStart = cargo.transform.position;
            RobotMotionController worker = job.ActiveWorker;
            Vector3 deck = worker.DeckPoint;
            worker.PrepareInteraction(job.CargoStart, deck, InteractionSeconds);
            SetStage(job, Stage.SourcePick, InteractionSeconds);
            SetRobot(job, worker, "interacting", SourceX, InteractionSeconds, true);
            Report(job.MaterialId + "  DISTRIBUTION HANDOFF  →  " + worker.Id);
        }

        private void BeginTravelToMachine(LineJob job)
        {
            RobotMotionController worker = job.ActiveWorker;
            ResolveMachine(job);
            float targetX = LineLayout.MachineX(job.StationIndex);
            float duration = TravelDuration(worker.Robot.transform.position.x, targetX);
            SetStage(job, Stage.TravelToMachine, duration);
            SetRobot(job, worker, "moving", targetX, duration, true);
            SetOtherWorkerWaiting(job);
        }

        private void BeginMachineLoad(LineJob job)
        {
            RobotMotionController worker = job.ActiveWorker;
            ResolveMachine(job);
            job.MachinePort = MachineLoadPoint(job, worker);
            job.CargoStart = job.Cargo.transform.position;
            worker.PrepareInteraction(job.CargoStart, job.MachinePort, InteractionSeconds);
            SetStage(job, Stage.MachineLoad, InteractionSeconds);
            SetRobot(job, worker, "interacting", LineLayout.MachineX(job.StationIndex), InteractionSeconds, true);
            job.Machine?.Loading(job.MaterialId);
            Report(job.MaterialId + "  LOAD  →  " + MachineFlowController.MachineId(job.Line, job.StationIndex));
        }

        private void BeginProcessing(LineJob job)
        {
            job.Cargo.transform.SetParent(transitRoot, true);
            job.Cargo.transform.position = job.MachinePort;
            job.Identity.SetState(FactoryWorkpieceState.InMachine,
                MachineFlowController.MachineId(job.Line, job.StationIndex));
            float duration = job.StationIndex == 3 ? 4.2f : 5.2f + job.StationIndex * 0.55f;
            SetStage(job, Stage.Processing, duration);
            SetRobot(job, job.ActiveWorker, "waiting", LineLayout.MachineX(job.StationIndex), duration, false);
            job.Machine?.Processing(job.MaterialId);
            Report(job.MaterialId + "  PROCESSING  @  " + MachineFlowController.MachineId(job.Line, job.StationIndex));
        }

        private void BeginMachineUnload(LineJob job)
        {
            RobotMotionController worker = job.ActiveWorker;
            job.CargoStart = job.Cargo.transform.position;
            worker.PrepareInteraction(job.CargoStart, worker.DeckPoint, InteractionSeconds);
            SetStage(job, Stage.MachineUnload, InteractionSeconds);
            SetRobot(job, worker, "interacting", LineLayout.MachineX(job.StationIndex), InteractionSeconds, true);
            job.Machine?.Unloading(job.MaterialId);
        }

        private void BeginTravelToConveyor(LineJob job)
        {
            job.Machine?.Idle();
            RobotMotionController worker = job.ActiveWorker;
            ResolveConveyor(job);
            float x = LineLayout.ConveyorX(job.StationIndex);
            float duration = TravelDuration(worker.Robot.transform.position.x, x);
            SetStage(job, Stage.TravelToConveyor, duration);
            SetRobot(job, worker, "moving", x, duration, true);
        }

        private void BeginConveyorDrop(LineJob job)
        {
            RobotMotionController worker = job.ActiveWorker;
            job.CargoStart = job.Cargo.transform.position;
            job.CargoTarget = ConveyorStart(job);
            worker.PrepareInteraction(job.CargoStart, job.CargoTarget, InteractionSeconds);
            SetStage(job, Stage.ConveyorDrop, InteractionSeconds);
            SetRobot(job, worker, "interacting", LineLayout.ConveyorX(job.StationIndex), InteractionSeconds, true);
            job.Conveyor?.Receiving(job.MaterialId);
        }

        private void BeginConveyorTransfer(LineJob job)
        {
            job.Cargo.transform.SetParent(transitRoot, true);
            job.Cargo.transform.position = job.CargoTarget;
            job.CargoStart = job.CargoTarget;
            job.CargoTarget = ConveyorEnd(job);
            job.Identity.SetState(FactoryWorkpieceState.OnConveyor,
                ConveyorFlowController.ConveyorId(job.Line, job.StationIndex));
            job.Conveyor?.Transferring(job.MaterialId);

            RobotMotionController previous = job.ActiveWorker;
            RobotMotionController next = job.Workers[(job.StationIndex + 1) % 2];
            float robotTravel = TravelDuration(next.Robot.transform.position.x, job.CargoTarget.x);
            float duration = Mathf.Max(ConveyorSeconds, robotTravel);
            SetRobot(job, previous, "waiting", previous.Robot.transform.position.x, duration, false);
            SetRobot(job, next, "moving", job.CargoTarget.x, duration, false);
            SetStage(job, Stage.ConveyorTransfer, duration);
            Report(job.MaterialId + "  " + ConveyorFlowController.ConveyorId(job.Line, job.StationIndex) + "  →  " + next.Id);
        }

        private void AnimateConveyor(LineJob job, float progress)
        {
            if (job.Cargo == null) return;
            job.Cargo.transform.position = Vector3.Lerp(job.CargoStart, job.CargoTarget, Mathf.SmoothStep(0f, 1f, progress));
        }

        private void BeginConveyorPickup(LineJob job)
        {
            job.Conveyor?.HandingOff(job.MaterialId);
            job.WorkerIndex = (job.StationIndex + 1) % 2;
            RobotMotionController worker = job.ActiveWorker;
            job.CargoStart = job.CargoTarget;
            worker.PrepareInteraction(job.CargoStart, worker.DeckPoint, InteractionSeconds);
            SetStage(job, Stage.ConveyorPickup, InteractionSeconds);
            SetRobot(job, worker, "interacting", job.CargoTarget.x, InteractionSeconds, true);
        }

        private void BeginTravelToOutput(LineJob job)
        {
            job.Machine?.Idle();
            RobotMotionController worker = job.ActiveWorker;
            float duration = TravelDuration(worker.Robot.transform.position.x, SourceX);
            SetStage(job, Stage.TravelToOutput, duration);
            SetRobot(job, worker, "moving", SourceX, duration, true);
            SetOtherWorkerWaiting(job);
        }

        private void BeginOutputDrop(LineJob job)
        {
            RobotMotionController worker = job.ActiveWorker;
            job.CargoStart = job.Cargo.transform.position;
            job.FinishedPosition = FinishedBufferPosition(job, completedByLine[job.Line - 1]);
            worker.PrepareInteraction(job.CargoStart, job.FinishedPosition, InteractionSeconds);
            SetStage(job, Stage.OutputDrop, InteractionSeconds);
            SetRobot(job, worker, "interacting", SourceX, InteractionSeconds, true);
        }

        private void AnimatePickToRobot(LineJob job, float progress)
        {
            if (!HasTool(job)) return;
            RobotMotionController worker = job.ActiveWorker;
            if (progress < 0.28f)
            {
                job.Cargo.transform.SetParent(transitRoot, true);
                job.Cargo.transform.position = job.CargoStart;
            }
            else if (progress < 0.85f)
            {
                // The shuttle remains stopped under the cargo until this exact frame,
                // when the real workpiece becomes owned by the gripper. Only then may
                // the warehouse return the now-empty shuttle to its home position.
                if (job.Identity.State == FactoryWorkpieceState.AtHandoff)
                {
                    if (warehouse != null && warehouse.ConfirmPickup(job.MaterialId))
                    {
                        Report(job.MaterialId + "  GRASP CONFIRMED  →  SHUTTLE RETURN");
                    }
                }
                Attach(job.Cargo, worker.Arm.GripAnchor, Vector3.zero);
                job.Identity.SetState(FactoryWorkpieceState.OnGripper, worker.Id);
            }
            else
            {
                Transform carrier = worker.Arm.PayloadAnchor != null ? worker.Arm.PayloadAnchor : worker.Robot.transform;
                Attach(job.Cargo, carrier, Vector3.zero);
                job.Identity.SetState(FactoryWorkpieceState.OnRobot, worker.Id);
            }
        }

        private void AnimateRobotToTarget(LineJob job, float progress, Vector3 target,
            FactoryWorkpieceState finalState, string owner)
        {
            if (!HasTool(job)) return;
            RobotMotionController worker = job.ActiveWorker;
            if (progress < 0.28f)
            {
                // Keep the real workpiece locked to the carrier until the gripper
                // reaches it. Detaching it during approach creates a ghost handoff.
                Transform carrier = worker.Arm.PayloadAnchor != null
                    ? worker.Arm.PayloadAnchor
                    : worker.Robot.transform;
                Attach(job.Cargo, carrier, Vector3.zero);
                job.Identity.SetState(FactoryWorkpieceState.OnRobot, worker.Id);
            }
            else if (progress < 0.85f)
            {
                Attach(job.Cargo, worker.Arm.GripAnchor, Vector3.zero);
                job.Identity.SetState(FactoryWorkpieceState.OnGripper, worker.Id);
            }
            else
            {
                job.Cargo.transform.SetParent(finalState == FactoryWorkpieceState.Finished ? finishedRoot : transitRoot, true);
                job.Cargo.transform.position = target;
                job.Identity.SetState(finalState, owner);
            }
        }

        private void FinishJob(LineJob job)
        {
            job.Cargo.transform.SetParent(finishedRoot, true);
            job.Cargo.transform.position = job.FinishedPosition;
            job.Identity.SetState(FactoryWorkpieceState.Finished, "finished_goods");
            finishedCargo.Add(job.Cargo);
            if (finishedCargo.Count > 30) finishedCargo[finishedCargo.Count - 31].SetActive(false);
            Report(job.MaterialId + "  QC PASS  →  FINISHED GOODS");
            foreach (RobotMotionController worker in job.Workers)
            {
                resources?.Release(worker.Id, job.MaterialId);
            }
            job.Cargo = null;
            job.Identity = null;
            job.MaterialId = null;
            completedJobs++;
            completedByLine[job.Line - 1]++;
            SetStage(job, Stage.Delay, 1.2f + job.Line * 0.25f);
            foreach (RobotMotionController worker in job.Workers)
            {
                worker.ClearInteraction();
                SetRobot(job, worker, "waiting", worker.Robot.transform.position.x, 0.1f, false);
            }
        }

        private void ResolveMachine(LineJob job)
        {
            machines.TryGetValue(MachineFlowController.MachineId(job.Line, job.StationIndex), out job.Machine);
        }

        private void ResolveConveyor(LineJob job)
        {
            conveyors.TryGetValue(ConveyorFlowController.ConveyorId(job.Line, job.StationIndex), out job.Conveyor);
        }

        private void SetOtherWorkerWaiting(LineJob job)
        {
            RobotMotionController other = job.Workers[1 - job.WorkerIndex];
            SetRobot(job, other, "waiting", other.Robot.transform.position.x, 0.1f, false);
        }

        private void Report(string text)
        {
            eventCount++;
            controller?.ReportFlowEvent(text);
        }

        private static string RobotId(int line, int index)
        {
            return "robot_" + (((line - 1) * 2) + index + 1).ToString("D2");
        }

        private static bool HasTool(LineJob job)
        {
            RobotMotionController worker = job.ActiveWorker;
            return job.Cargo != null && job.Identity != null && worker?.Arm != null && worker.Arm.GripAnchor != null;
        }

        private void SetRobot(LineJob job, RobotMotionController worker, string status, float targetX, float duration, bool hasPayload)
        {
            string owner = job.Cargo != null ? job.MaterialId : null;
            if (resources != null)
            {
                if (status == "waiting")
                {
                    resources.Release(worker.Id, owner);
                }
                else
                {
                    resources.TryAcquire(worker.Id, owner);
                }
            }
            worker.ApplyMotion(status, targetX, duration, hasPayload, owner);
        }

        private static void SetStage(LineJob job, Stage stage, float duration)
        {
            job.Stage = stage;
            job.StageStarted = Time.time;
            job.StageDuration = Mathf.Max(0.01f, duration);
        }

        private static float TravelDuration(float fromX, float toX) => Mathf.Max(0.8f, Mathf.Abs(toX - fromX) / TravelSpeed);
        private static float Smooth(float from, float to, float value) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));
        private static Vector3 MachineLoadPoint(LineJob job, RobotMotionController worker)
        {
            float side = worker.Index == 0 ? -1f : 1f;
            return new Vector3(LineLayout.MachineX(job.StationIndex), 0.86f,
                LineLayout.LineCenterZ(job.Line) + side * 1.05f);
        }

        private void BuildVisibleMachineLoadNests()
        {
            Transform root = new GameObject("VISIBLE MACHINE LOAD NESTS").transform;
            for (int line = 1; line <= 3; line++)
            {
                for (int station = 0; station < StationNames.Length; station++)
                {
                    float side = station % 2 == 0 ? -1f : 1f;
                    Vector3 position = new Vector3(LineLayout.MachineX(station), 0.64f,
                        LineLayout.LineCenterZ(line) + side * 1.05f);
                    GameObject nest = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    nest.name = "L" + line + " M" + (((line - 1) * 4) + station + 1).ToString("D2") + " Load Nest";
                    nest.transform.SetParent(root, true);
                    nest.transform.position = position;
                    nest.transform.localScale = new Vector3(0.78f, 0.08f, 0.60f);
                    Destroy(nest.GetComponent<Collider>());

                    Renderer renderer = nest.GetComponent<Renderer>();
                    if (devices.TryGetValue("line" + line + "/" + StationNames[station], out DeviceView machine))
                    {
                        Renderer reference = machine.GetComponentInChildren<Renderer>();
                        if (reference != null) renderer.sharedMaterial = reference.sharedMaterial;
                    }
                    var block = new MaterialPropertyBlock();
                    block.SetColor("_Color", new Color(0.07f, 0.15f, 0.17f));
                    block.SetColor("_BaseColor", new Color(0.07f, 0.15f, 0.17f));
                    block.SetColor("_EmissionColor", new Color(0.02f, 0.28f, 0.31f));
                    renderer.SetPropertyBlock(block);
                }
            }
        }

        private void BuildFinishedGoodsBuffers()
        {
            for (int line = 1; line <= 3; line++)
            {
                float centerZ = LineLayout.LineCenterZ(line) + 1.22f;
                Transform buffer = new GameObject("L" + line + " FINISHED GOODS PALLET").transform;
                buffer.SetParent(finishedRoot, true);

                Material reference = null;
                if (devices.TryGetValue("line" + line + "/QualityCheck", out DeviceView quality))
                {
                    Renderer renderer = quality.GetComponentInChildren<Renderer>();
                    if (renderer != null) reference = renderer.sharedMaterial;
                }

                CreateFinishedBufferPart("Pallet Deck", buffer,
                    new Vector3(21.15f, 0.31f, centerZ), new Vector3(1.25f, 0.18f, 1.65f),
                    reference, new Color(0.18f, 0.24f, 0.25f));
                CreateFinishedBufferPart("Pallet Inner Guard", buffer,
                    new Vector3(20.54f, 0.45f, centerZ), new Vector3(0.06f, 0.30f, 1.65f),
                    reference, new Color(0.95f, 0.58f, 0.08f));
                CreateFinishedBufferPart("Pallet Outer Guard", buffer,
                    new Vector3(21.76f, 0.45f, centerZ), new Vector3(0.06f, 0.30f, 1.65f),
                    reference, new Color(0.95f, 0.58f, 0.08f));
            }
        }

        private static void CreateFinishedBufferPart(string name, Transform parent, Vector3 position,
            Vector3 scale, Material material, Color color)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, true);
            part.transform.position = position;
            part.transform.localScale = scale;
            Destroy(part.GetComponent<Collider>());

            Renderer renderer = part.GetComponent<Renderer>();
            if (material != null) renderer.sharedMaterial = material;
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            block.SetColor("_BaseColor", color);
            block.SetColor("_EmissionColor", color * 0.10f);
            renderer.SetPropertyBlock(block);
        }

        private static Vector3 ConveyorStart(LineJob job)
        {
            return new Vector3(LineLayout.ConveyorX(job.StationIndex) - 1.02f, 0.82f,
                LineLayout.LineCenterZ(job.Line));
        }

        private static Vector3 ConveyorEnd(LineJob job)
        {
            return new Vector3(LineLayout.ConveyorX(job.StationIndex) + 1.02f, 0.82f,
                LineLayout.LineCenterZ(job.Line));
        }

        private static Vector3 FinishedBufferPosition(LineJob job, int lineIndex)
        {
            int slot = lineIndex % 10;
            int column = slot / 5;
            int row = slot % 5;
            // Finished parts belong on the dedicated line-side pallet, not on the
            // central distribution rails at x=22.82/23.48.
            return new Vector3(20.90f + column * 0.50f, 0.56f,
                LineLayout.LineCenterZ(job.Line) + 0.65f + row * 0.30f);
        }

        private static void Attach(GameObject cargo, Transform parent, Vector3 localPosition)
        {
            if (cargo.transform.parent != parent) cargo.transform.SetParent(parent, false);
            cargo.transform.localPosition = localPosition;
            cargo.transform.localRotation = Quaternion.identity;
            cargo.transform.localScale = new Vector3(0.34f, 0.30f, 0.34f);
        }
    }
}
