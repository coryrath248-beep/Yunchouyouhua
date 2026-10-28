using System.Collections.Generic;
using System.IO;
using FactoryTwin;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FactoryTwinEditor
{
    public static class FactorySceneBuilder
    {
        private const float CoordinateScale = 0.25f;
        private const string ScenePath = "Assets/Scenes/FactoryMachiningTwin.unity";
        private const string GeneratedFolder = "Assets/FactoryTwin/Generated";
        private const string MaterialFolder = GeneratedFolder + "/Materials";
        private const string PipelinePath = GeneratedFolder + "/FactoryTwinURP.asset";
        private const string RendererPath = "Assets/UniversalRenderer.asset";
        private const string ProfilePath = GeneratedFolder + "/FactoryTwinVolume.asset";
        private const string TextureFolder = "Assets/ThirdParty/RoboticsWarehouse/Textures";

        private static Material floorBase;
        private static Material floorSurface;
        private static Material floorZone;
        private static Material gridLine;
        private static Material cyan;
        private static Material cyanEmissive;
        private static Material yellow;
        private static Material orange;
        private static Material white;
        private static Material darkMetal;
        private static Material steel;
        private static Material machine;
        private static Material machineDark;
        private static Material roller;
        private static Material glass;
        private static Material agvShell;
        private static Material wheel;
        private static Material boxBlue;
        private static Material boxWhite;
        private static Material boxOrange;
        private static Material rackSteel;
        private static Material pallet;

        [MenuItem("Factory Twin/Build Demo Scene")]
        public static void Build()
        {
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory(MaterialFolder);
            ConfigurePipeline();
            CreateMaterials();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject environment = new GameObject("SMART FACTORY / ENVIRONMENT");
            BuildFloor(environment.transform);
            BuildArchitecture(environment.transform);

            float[] lineZ = { 5f, 15f, 25f };
            for (int i = 0; i < lineZ.Length; i++)
            {
                BuildProductionLine(environment.transform, i + 1, lineZ[i]);
            }

            BuildWarehouseAreas(environment.transform, lineZ);
            BuildFleet();

            GameObject systems = new GameObject("SYSTEMS / MQTT + SIMPY");
            systems.AddComponent<FactoryTwinController>();
            BuildCameraAndLighting();
            BuildHud();

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.skybox = null;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.70f, 0.71f);
            RenderSettings.ambientEquatorColor = new Color(0.51f, 0.56f, 0.57f);
            RenderSettings.ambientGroundColor = new Color(0.34f, 0.38f, 0.39f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.89f, 0.92f, 0.93f);
            RenderSettings.fogStartDistance = 42f;
            RenderSettings.fogEndDistance = 95f;

            PlayerSettings.productName = "SUPCON Smart Factory Digital Twin";
            PlayerSettings.companyName = "SUPCON Digital Twin Lab";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Factory Twin showcase scene built: " + ScenePath);
        }

        [MenuItem("Factory Twin/Build Windows Demo")]
        public static void BuildWindows()
        {
            Build();
            Directory.CreateDirectory("Builds/Windows");
            BuildReport report = BuildPipeline.BuildPlayer(
                new[] { ScenePath },
                "Builds/Windows/FactoryTwin.exe",
                BuildTarget.StandaloneWindows64,
                BuildOptions.None);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException("Windows build failed: " + report.summary.result);
            }
            Debug.Log("Factory Twin Windows build: " + report.summary.totalSize + " bytes");
        }

        [MenuItem("Factory Twin/Open And Run Scene")]
        public static void OpenAndRun()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.delayCall += () =>
            {
                EditorApplication.ExecuteMenuItem("Window/General/Game");
                EditorApplication.isPlaying = true;
            };
        }

        private static void ConfigurePipeline()
        {
            UniversalRenderPipelineAsset pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create();
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
                pipeline.LoadBuiltinRendererData();
            }

            UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = pipeline.LoadBuiltinRendererData() as UniversalRendererData;
            }
            ResourceReloader.ReloadAllNullIn(rendererData, UniversalRenderPipelineAsset.packagePath);

            SerializedObject serializedPipeline = new SerializedObject(pipeline);
            SerializedProperty rendererList = serializedPipeline.FindProperty("m_RendererDataList");
            rendererList.arraySize = 1;
            rendererList.GetArrayElementAtIndex(0).objectReferenceValue = rendererData;
            serializedPipeline.FindProperty("m_RendererData").objectReferenceValue = rendererData;
            serializedPipeline.FindProperty("m_DefaultRendererIndex").intValue = 0;
            serializedPipeline.ApplyModifiedPropertiesWithoutUndo();

            pipeline.supportsCameraDepthTexture = true;
            pipeline.supportsCameraOpaqueTexture = true;
            pipeline.msaaSampleCount = 4;
            pipeline.renderScale = 1.25f;
            pipeline.shadowDistance = 55f;
            pipeline.shadowCascadeCount = 2;
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            QualitySettings.shadows = UnityEngine.ShadowQuality.All;
            QualitySettings.shadowResolution = UnityEngine.ShadowResolution.High;
            QualitySettings.vSyncCount = 1;
            EditorUtility.SetDirty(pipeline);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
        }

        private static void CreateMaterials()
        {
            floorBase = GetMaterial("FloorBase", new Color(0.66f, 0.70f, 0.71f), 0.05f, 0.22f);
            floorSurface = GetMaterial("Concrete", new Color(0.76f, 0.79f, 0.80f), 0f, 0.24f);
            floorZone = GetMaterial("ProductionZone", new Color(0.68f, 0.72f, 0.73f), 0f, 0.26f);
            gridLine = GetMaterial("GridLine", new Color(0.72f, 0.77f, 0.79f), 0f, 0.2f);
            cyan = GetMaterial("DigitalCyan", new Color(0.11f, 0.46f, 0.53f), 0.16f, 0.48f);
            cyanEmissive = GetMaterial("DigitalCyanGlow", new Color(0.08f, 0.63f, 0.62f), 0.08f, 0.56f, true);
            yellow = GetMaterial("SafetyYellow", new Color(0.95f, 0.66f, 0.16f), 0.05f, 0.42f);
            orange = GetMaterial("SignalOrange", new Color(0.95f, 0.43f, 0.08f), 0.12f, 0.48f, true);
            white = GetMaterial("CleanWhite", new Color(0.83f, 0.86f, 0.87f), 0.03f, 0.38f);
            darkMetal = GetMaterial("DarkMetal", new Color(0.17f, 0.22f, 0.24f), 0.58f, 0.48f);
            steel = GetMaterial("Steel", new Color(0.46f, 0.53f, 0.55f), 0.72f, 0.56f);
            machine = GetMaterial("MachineShell", new Color(0.72f, 0.76f, 0.77f), 0.08f, 0.42f);
            machineDark = GetMaterial("MachineInset", new Color(0.055f, 0.10f, 0.12f), 0.18f, 0.38f);
            roller = GetMaterial("Roller", new Color(0.68f, 0.74f, 0.76f), 0.8f, 0.68f);
            glass = GetMaterial("GlassPanel", new Color(0.025f, 0.16f, 0.22f), 0.32f, 0.78f);
            agvShell = GetMaterial("AGVShell", new Color(0.95f, 0.43f, 0.08f), 0.18f, 0.54f);
            wheel = GetMaterial("Wheel", new Color(0.06f, 0.075f, 0.08f), 0.12f, 0.3f);

            boxBlue = GetMaterial("RackBoxBlue", Color.white, 0f, 0.35f, false, TextureFolder + "/UDS_BoxesLLogoAL.png");
            boxWhite = GetMaterial("RackBoxWhite", Color.white, 0f, 0.35f, false, TextureFolder + "/UDS_BoxesWhiteAL.png");
            boxOrange = GetMaterial("RackBoxOrange", Color.white, 0f, 0.35f, false, TextureFolder + "/UDS_BoxesSLogoAL.png");
            rackSteel = GetMaterial("RackSteel", new Color(0.10f, 0.40f, 0.47f), 0.42f, 0.48f);
            pallet = GetMaterial("Pallet", new Color(0.90f, 0.63f, 0.20f), 0f, 0.3f);
        }

        private static void BuildFloor(Transform parent)
        {
            CreateBox("Foundation", parent, new Vector3(12.5f, -0.23f, 15f), new Vector3(29f, 0.46f, 34f), floorBase);
            CreateBox("Factory Floor", parent, new Vector3(12.5f, 0.025f, 15f), new Vector3(27.8f, 0.08f, 32.8f), floorSurface);

            for (float x = -1.25f; x <= 26.25f; x += 1.25f)
            {
                CreateBox("Floor Grid X", parent, new Vector3(x, 0.075f, 15f), new Vector3(x % 5f == 0f ? 0.026f : 0.012f, 0.008f, 32.5f), gridLine);
            }
            for (float z = -1.25f; z <= 31.25f; z += 1.25f)
            {
                CreateBox("Floor Grid Z", parent, new Vector3(12.5f, 0.075f, z), new Vector3(27.5f, 0.008f, z % 5f == 0f ? 0.026f : 0.012f), gridLine);
            }

            float[] laneZ = { 2.5f, 7.5f, 12.5f, 17.5f, 22.5f, 27.5f };
            foreach (float z in laneZ)
            {
                CreateBox("Rail Bed", parent, new Vector3(12.5f, 0.10f, z), new Vector3(24.8f, 0.07f, 0.66f), floorZone);
                CreateBox("Rail", parent, new Vector3(12.5f, 0.17f, z - 0.21f), new Vector3(24.8f, 0.07f, 0.065f), cyan);
                CreateBox("Rail", parent, new Vector3(12.5f, 0.17f, z + 0.21f), new Vector3(24.8f, 0.07f, 0.065f), cyan);
                for (float x = 0.4f; x < 24.8f; x += 1.25f)
                {
                    CreateBox("Rail Sleeper", parent, new Vector3(x, 0.145f, z), new Vector3(0.12f, 0.045f, 0.58f), steel);
                }
            }
        }

        private static void BuildArchitecture(Transform parent)
        {
            CreateBox("North Curb", parent, new Vector3(12.5f, 0.13f, 31.3f), new Vector3(28f, 0.18f, 0.18f), steel);
            CreateBox("South Curb", parent, new Vector3(12.5f, 0.13f, -1.3f), new Vector3(28f, 0.18f, 0.18f), steel);
            CreateBox("West Curb", parent, new Vector3(-1.3f, 0.13f, 15f), new Vector3(0.18f, 0.18f, 32.5f), steel);
            CreateBox("East Curb", parent, new Vector3(26.3f, 0.13f, 15f), new Vector3(0.18f, 0.18f, 32.5f), steel);

            for (int i = 0; i < 4; i++)
            {
                float x = 2.5f + i * 7f;
                CreateBox("Hall Mast", parent, new Vector3(x, 2.6f, 30.9f), new Vector3(0.16f, 5.2f, 0.16f), white);
                CreateBox("Mast Luminaire", parent, new Vector3(x, 4.95f, 30.66f), new Vector3(1.25f, 0.08f, 0.12f), cyanEmissive);
            }
        }

        private static void BuildProductionLine(Transform parent, int lineNumber, float z)
        {
            GameObject line = new GameObject("LINE " + lineNumber.ToString("00"));
            line.transform.SetParent(parent);
            CreateBox("Cell Border North", line.transform, new Vector3(11.25f, 0.095f, z + 1.42f), new Vector3(21.2f, 0.025f, 0.045f), cyan);
            CreateBox("Cell Border South", line.transform, new Vector3(11.25f, 0.095f, z - 1.42f), new Vector3(21.2f, 0.025f, 0.045f), cyan);

            CreateLineGateway(line.transform, lineNumber, new Vector3(1.2f, 0f, z));

            string lineId = "line" + lineNumber;
            string[] stationIds = { "StationA", "StationB", "StationC", "QualityCheck" };
            string[] processes = { "CUT", "ASSEMBLY", "PACK", "VISION" };
            float[] stationX = { 4f, 8.75f, 13.5f, 18.25f };
            string[] conveyorIds = { "Conveyor_AB", "Conveyor_BC", "Conveyor_CQ" };
            float[] conveyorX = { 6.38f, 11.13f, 15.88f };

            for (int i = 0; i < stationIds.Length; i++)
            {
                int machineNumber = (lineNumber - 1) * 4 + i + 1;
                string displayName = "M" + machineNumber.ToString("00") + " / " + processes[i];
                CreateStation(line.transform, lineId + "/" + stationIds[i], displayName, new Vector3(stationX[i], 0f, z), i);
            }
            for (int i = 0; i < conveyorIds.Length; i++)
            {
                CreateConveyor(line.transform, lineId + "/" + conveyorIds[i], new Vector3(conveyorX[i], 0f, z));
            }
        }

        private static void CreateLineGateway(Transform parent, int lineNumber, Vector3 position)
        {
            CreateBox("Gateway Post", parent, position + new Vector3(-0.48f, 0.64f, 0f), new Vector3(0.10f, 1.28f, 0.10f), rackSteel);
            CreateBox("Gateway Post", parent, position + new Vector3(0.48f, 0.64f, 0f), new Vector3(0.10f, 1.28f, 0.10f), rackSteel);
            CreateBox("Gateway Header", parent, position + new Vector3(0f, 1.30f, 0f), new Vector3(1.08f, 0.16f, 0.16f), white);
            CreateBox("Gateway Accent", parent, position + new Vector3(0f, 1.21f, -0.09f), new Vector3(1.08f, 0.05f, 0.025f), cyanEmissive);
            TextMesh label = CreateWorldLabel("CELL " + lineNumber.ToString("00"), position + new Vector3(0f, 1.58f, 0f), 0.075f, new Color(0.12f, 0.22f, 0.25f));
            label.transform.SetParent(parent, true);
        }

        private static void CreateStation(Transform parent, string key, string displayName, Vector3 position, int variant)
        {
            GameObject root = new GameObject(displayName);
            root.transform.SetParent(parent);
            root.transform.position = position;

            CreateBox("Machine Plinth", root.transform, position + new Vector3(0f, 0.12f, 0f), new Vector3(2.25f, 0.24f, 1.72f), darkMetal);

            float bodyWidth = 2.05f;
            float bodyDepth = 1.48f;
            CreateBox("Machine Back", root.transform, position + new Vector3(0f, 0.93f, 0.66f), new Vector3(bodyWidth, 1.48f, 0.12f), machine);
            CreateBox("Machine Roof", root.transform, position + new Vector3(0f, 1.55f, 0f), new Vector3(bodyWidth, 0.24f, bodyDepth), machine);
            CreateBox("Machine Sill", root.transform, position + new Vector3(0f, 0.39f, 0f), new Vector3(bodyWidth, 0.28f, bodyDepth), machine);
            CreateBox("Left Housing", root.transform, position + new Vector3(-0.79f, 0.96f, 0f), new Vector3(0.47f, 0.92f, bodyDepth), machine);
            CreateBox("Right Housing", root.transform, position + new Vector3(0.79f, 0.96f, 0f), new Vector3(0.47f, 0.92f, bodyDepth), machine);
            CreateBox("Machining Cavity", root.transform, position + new Vector3(0f, 0.96f, -0.755f), new Vector3(1.02f, 0.78f, 0.08f), machineDark);
            CreateCylinder("Work Spindle", root.transform, position + new Vector3(0f, 0.97f, -0.82f), new Vector3(0.17f, 0.08f, 0.17f), steel).transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            GameObject door = CreateBox("Sliding Safety Door", root.transform, position + new Vector3(0f, 0.96f, -0.81f), new Vector3(1.10f, 0.82f, 0.075f), glass);
            Vector3 closedDoor = door.transform.localPosition;
            Vector3 openDoor = closedDoor + Vector3.left * 0.72f;
            MachineDoorAnimator doorAnimator = root.AddComponent<MachineDoorAnimator>();
            doorAnimator.Configure(door.transform, openDoor, closedDoor);
            CreateBox("Door Rail", root.transform, position + new Vector3(0f, 1.40f, -0.83f), new Vector3(1.48f, 0.04f, 0.04f), steel);
            CreateBox("Door Handle", door.transform, position + new Vector3(0.40f, 0.96f, -0.86f), new Vector3(0.035f, 0.22f, 0.035f), steel).transform.SetParent(door.transform, true);

            CreateBox("Control Console", root.transform, position + new Vector3(0.91f, 0.91f, -0.86f), new Vector3(0.34f, 0.95f, 0.28f), white);
            CreateBox("Control Screen", root.transform, position + new Vector3(0.91f, 1.12f, -1.02f), new Vector3(0.24f, 0.30f, 0.035f), glass);
            CreateBox("Machine Accent", root.transform, position + new Vector3(0f, 0.30f, -0.88f), new Vector3(2.05f, 0.11f, 0.035f), variant == 3 ? yellow : cyanEmissive);

            GameObject beaconBase = CreateCylinder("Beacon Base", root.transform, position + new Vector3(0.74f, 1.75f, 0f), new Vector3(0.085f, 0.055f, 0.085f), darkMetal);
            GameObject beacon = CreateCylinder("Status Beacon", root.transform, position + new Vector3(0.74f, 1.86f, 0f), new Vector3(0.065f, 0.06f, 0.065f), cyanEmissive);
            beaconBase.isStatic = true;

            TextMesh label = CreateWorldLabel(displayName.Substring(0, 3), position + new Vector3(0f, 2.04f, 0f), 0.055f, new Color(0.10f, 0.18f, 0.20f));
            label.transform.SetParent(root.transform, true);
            DeviceView view = root.AddComponent<DeviceView>();
            view.Configure(key, label, beacon.GetComponent<Renderer>(), doorAnimator);

            CreateBollard(root.transform, position + new Vector3(-1.16f, 0f, -0.92f));
            CreateBollard(root.transform, position + new Vector3(1.16f, 0f, -0.92f));
        }

        private static void CreateConveyor(Transform parent, string key, Vector3 position)
        {
            GameObject root = new GameObject(key.Substring(key.LastIndexOf('/') + 1));
            root.transform.SetParent(parent);
            root.transform.position = position;

            CreateBox("Conveyor Chassis", root.transform, position + new Vector3(0f, 0.33f, 0f), new Vector3(2.22f, 0.18f, 0.88f), steel);
            CreateBox("Conveyor Bed", root.transform, position + new Vector3(0f, 0.45f, 0f), new Vector3(2.08f, 0.08f, 0.66f), machineDark);
            CreateBox("Guard Rail", root.transform, position + new Vector3(0f, 0.63f, -0.46f), new Vector3(2.26f, 0.07f, 0.06f), darkMetal);
            CreateBox("Guard Rail", root.transform, position + new Vector3(0f, 0.63f, 0.46f), new Vector3(2.26f, 0.07f, 0.06f), darkMetal);
            GameObject statusStrip = CreateBox("Conveyor Status", root.transform, position + new Vector3(0f, 0.43f, -0.50f), new Vector3(1.95f, 0.05f, 0.03f), cyanEmissive);

            var rollers = new List<Transform>();
            for (int i = 0; i < 9; i++)
            {
                float x = -0.92f + i * 0.23f;
                GameObject item = CreateCylinder("Roller", root.transform, position + new Vector3(x, 0.52f, 0f), new Vector3(0.065f, 0.34f, 0.065f), roller);
                item.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                rollers.Add(item.transform);
            }

            DeviceView view = root.AddComponent<DeviceView>();
            view.Configure(key, null, statusStrip.GetComponent<Renderer>());
            root.AddComponent<ConveyorAnimator>().Configure(rollers.ToArray());
        }

        private static void CreateSafetyRail(Transform parent, Vector3 position, float length)
        {
            CreateBox("Safety Rail", parent, position + new Vector3(0f, 0.72f, 0f), new Vector3(length, 0.075f, 0.075f), yellow);
            CreateBox("Safety Rail", parent, position + new Vector3(0f, 0.38f, 0f), new Vector3(length, 0.055f, 0.055f), yellow);
            for (float x = -length * 0.5f; x <= length * 0.5f + 0.01f; x += 2.4f)
            {
                CreateBox("Safety Post", parent, position + new Vector3(x, 0.38f, 0f), new Vector3(0.075f, 0.76f, 0.075f), yellow);
            }
        }

        private static void CreateBollard(Transform parent, Vector3 position)
        {
            CreateCylinder("Safety Bollard", parent, position + new Vector3(0f, 0.32f, 0f), new Vector3(0.095f, 0.32f, 0.095f), yellow);
            CreateCylinder("Bollard Cap", parent, position + new Vector3(0f, 0.66f, 0f), new Vector3(0.115f, 0.035f, 0.115f), darkMetal);
        }

        private static void BuildWarehouseAreas(Transform parent, float[] lineZ)
        {
            GameObject raw = new GameObject("RAW MATERIAL INBOUND");
            raw.transform.SetParent(parent);
            CreateBox("Inbound Dock", raw.transform, new Vector3(-0.55f, 0.72f, 15f), new Vector3(0.72f, 1.44f, 5.7f), white);
            GameObject rawLight = CreateBox("Inbound Status", raw.transform, new Vector3(-0.16f, 1.18f, 15f), new Vector3(0.07f, 0.13f, 5.1f), cyanEmissive);
            raw.AddComponent<DeviceView>().Configure("warehouse/RawMaterial", null, rawLight.GetComponent<Renderer>());

            GameObject rack = CreateLineSideRack(parent, new Vector3(12.5f, 0f, 30.0f), 15, 4);
            GameObject rackLight = CreateBox("Rack Status", rack.transform, new Vector3(12.5f, 4.66f, 29.35f), new Vector3(5.2f, 0.07f, 0.04f), cyanEmissive);
            rack.AddComponent<DeviceView>().Configure("warehouse/Warehouse", null, rackLight.GetComponent<Renderer>());

            CreateStackerCrane(parent, new Vector3(22.65f, 0f, 29.1f));
            CreateHumanWorkstation(parent, new Vector3(1.8f, 0f, 9.7f), "OPERATOR 01");
            CreateHumanWorkstation(parent, new Vector3(1.8f, 0f, 20.3f), "OPERATOR 02");
            CreateTransferTable(parent, new Vector3(3.0f, 0f, 28.4f));
            CreateTransferTable(parent, new Vector3(7.0f, 0f, 28.4f));

            GameObject output = new GameObject("FINISHED GOODS");
            output.transform.SetParent(parent);
            CreateBox("Output Status Tower", output.transform, new Vector3(25.15f, 1.05f, 15f), new Vector3(0.32f, 2.1f, 1f), white);
            CreateBox("Output Accent", output.transform, new Vector3(24.96f, 1.2f, 15f), new Vector3(0.07f, 1.25f, 0.62f), yellow);
        }

        private static GameObject CreateLineSideRack(Transform parent, Vector3 position, int columns, int levels)
        {
            GameObject root = new GameObject("LINE-SIDE ASRS / 60 SLOTS");
            root.transform.SetParent(parent);
            root.transform.position = position;
            float pitch = 1.28f;
            float width = columns * pitch;
            float height = 4.45f;
            float depth = 1.10f;

            for (int column = 0; column <= columns; column++)
            {
                float x = position.x + (column - columns * 0.5f) * pitch;
                CreateBox("Rack Post", root.transform, new Vector3(x, height * 0.5f, position.z - depth * 0.5f), new Vector3(0.075f, height, 0.075f), rackSteel);
                CreateBox("Rack Post", root.transform, new Vector3(x, height * 0.5f, position.z + depth * 0.5f), new Vector3(0.075f, height, 0.075f), rackSteel);
            }
            for (int level = 0; level <= levels; level++)
            {
                float y = 0.42f + level * 0.93f;
                CreateBox("Rack Beam", root.transform, new Vector3(position.x, y, position.z - depth * 0.5f), new Vector3(width, 0.065f, 0.06f), cyan);
                CreateBox("Rack Beam", root.transform, new Vector3(position.x, y, position.z + depth * 0.5f), new Vector3(width, 0.065f, 0.06f), cyan);
                CreateBox("Rack Shelf", root.transform, new Vector3(position.x, y + 0.03f, position.z), new Vector3(width - 0.18f, 0.045f, depth - 0.18f), steel);
            }

            Material[] cargo = { pallet, boxBlue, yellow, boxWhite, boxOrange };
            for (int column = 0; column < columns; column++)
            {
                for (int level = 0; level < levels; level++)
                {
                    if ((column * 3 + level * 5) % 7 < 2)
                    {
                        continue;
                    }
                    float x = position.x + (column - (columns - 1) * 0.5f) * pitch;
                    float y = 0.79f + level * 0.93f;
                    CreateBox("Stored Tote", root.transform, new Vector3(x, y, position.z - 0.03f), new Vector3(0.72f, 0.58f, 0.72f), cargo[(column + level) % cargo.Length]);
                }
            }

            TextMesh label = CreateWorldLabel("LINE-SIDE ASRS  /  60 SLOTS", position + new Vector3(0f, 4.82f, -0.2f), 0.075f, new Color(0.10f, 0.22f, 0.25f));
            label.transform.SetParent(root.transform, true);
            return root;
        }

        private static void CreateStackerCrane(Transform parent, Vector3 position)
        {
            GameObject root = new GameObject("ASRS STACKER CRANE");
            root.transform.SetParent(parent);
            CreateBox("Crane Base", root.transform, position + new Vector3(0f, 0.12f, 0f), new Vector3(1.15f, 0.24f, 0.86f), darkMetal);
            CreateBox("Crane Mast", root.transform, position + new Vector3(0f, 2.15f, 0.22f), new Vector3(0.12f, 4.15f, 0.12f), rackSteel);
            CreateBox("Crane Carriage", root.transform, position + new Vector3(0f, 1.65f, -0.06f), new Vector3(0.70f, 0.36f, 0.64f), yellow);
            CreateBox("Fork", root.transform, position + new Vector3(-0.19f, 1.38f, -0.48f), new Vector3(0.08f, 0.06f, 0.84f), steel);
            CreateBox("Fork", root.transform, position + new Vector3(0.19f, 1.38f, -0.48f), new Vector3(0.08f, 0.06f, 0.84f), steel);
            CreateCylinder("Crane Beacon", root.transform, position + new Vector3(0f, 1.96f, -0.05f), new Vector3(0.065f, 0.08f, 0.065f), orange);
        }

        private static void CreateHumanWorkstation(Transform parent, Vector3 position, string name)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent);
            Material uniform = machine;

            CreateCylinder("Left Leg", root.transform, position + new Vector3(-0.12f, 0.36f, 0f), new Vector3(0.10f, 0.34f, 0.10f), darkMetal);
            CreateCylinder("Right Leg", root.transform, position + new Vector3(0.12f, 0.36f, 0f), new Vector3(0.10f, 0.34f, 0.10f), darkMetal);
            CreateCylinder("Torso", root.transform, position + new Vector3(0f, 1.03f, 0f), new Vector3(0.25f, 0.38f, 0.25f), uniform);
            CreateSphere("Head", root.transform, position + new Vector3(0f, 1.63f, 0f), new Vector3(0.21f, 0.23f, 0.21f), pallet);
            CreateBox("Safety Cap", root.transform, position + new Vector3(0f, 1.82f, 0f), new Vector3(0.46f, 0.08f, 0.42f), white);
            GameObject leftArm = CreateCylinder("Left Arm", root.transform, position + new Vector3(0.24f, 1.13f, -0.18f), new Vector3(0.075f, 0.30f, 0.075f), uniform);
            leftArm.transform.rotation = Quaternion.Euler(0f, 0f, -56f);
            GameObject rightArm = CreateCylinder("Right Arm", root.transform, position + new Vector3(0.24f, 1.13f, 0.18f), new Vector3(0.075f, 0.30f, 0.075f), uniform);
            rightArm.transform.rotation = Quaternion.Euler(0f, 0f, -56f);

            Vector3 desk = position + new Vector3(0.95f, 0f, 0f);
            CreateBox("Inspection Desk", root.transform, desk + new Vector3(0f, 0.82f, 0f), new Vector3(1.08f, 0.10f, 1.55f), white);
            for (int sideX = -1; sideX <= 1; sideX += 2)
            {
                for (int sideZ = -1; sideZ <= 1; sideZ += 2)
                {
                    CreateBox("Desk Leg", root.transform, desk + new Vector3(sideX * 0.43f, 0.40f, sideZ * 0.62f), new Vector3(0.075f, 0.80f, 0.075f), steel);
                }
            }
            CreateBox("Monitor", root.transform, desk + new Vector3(0.16f, 1.27f, -0.38f), new Vector3(0.08f, 0.42f, 0.58f), glass);
            CreateBox("Scanner", root.transform, desk + new Vector3(-0.18f, 0.99f, 0.25f), new Vector3(0.10f, 0.25f, 0.10f), yellow).transform.rotation = Quaternion.Euler(0f, 0f, -18f);
            TextMesh label = CreateWorldLabel(name, position + new Vector3(0.45f, 2.08f, 0f), 0.045f, new Color(0.10f, 0.18f, 0.20f));
            label.transform.SetParent(root.transform, true);
        }

        private static void CreateTransferTable(Transform parent, Vector3 position)
        {
            GameObject root = new GameObject("ASRS TRANSFER CONVEYOR");
            root.transform.SetParent(parent);
            CreateBox("Transfer Chassis", root.transform, position + new Vector3(0f, 0.50f, 0f), new Vector3(2.15f, 0.18f, 1.18f), steel);
            for (int i = -4; i <= 4; i++)
            {
                GameObject transferRoller = CreateCylinder("Transfer Roller", root.transform, position + new Vector3(i * 0.23f, 0.65f, 0f), new Vector3(0.055f, 0.54f, 0.055f), roller);
                transferRoller.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
            CreateBox("Transfer Stop", root.transform, position + new Vector3(1.05f, 0.73f, 0f), new Vector3(0.05f, 0.22f, 1.10f), orange);
        }

        private static void CreateWarehouseRack(Transform parent, Vector3 position)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/RoboticsWarehouse/Models/ShelvingRackA.fbx");
            if (model == null)
            {
                return;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(model) as GameObject;
            if (instance == null)
            {
                return;
            }
            instance.name = "Robotics-Warehouse Rack";
            instance.transform.SetParent(parent);
            instance.transform.position = position;
            instance.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            instance.transform.localScale = Vector3.one;

            Material[] palette = { boxBlue, boxWhite, boxOrange, rackSteel, steel, pallet };
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            {
                int slotCount = Mathf.Max(1, renderer.sharedMaterials.Length);
                Material[] materials = new Material[slotCount];
                for (int slot = 0; slot < slotCount; slot++)
                {
                    materials[slot] = palette[Mathf.Min(slot, palette.Length - 1)];
                }
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        private static void BuildFleet()
        {
            GameObject fleet = new GameObject("RAIL-MOUNTED ROBOT FLEET");
            for (int lineIndex = 0; lineIndex < 3; lineIndex++)
            {
                string lineId = "line" + (lineIndex + 1);
                CreateAgv(fleet.transform, lineId, "AGV_1", new Vector3(10f * CoordinateScale, 0.34f, (10f + lineIndex * 40f) * CoordinateScale));
                CreateAgv(fleet.transform, lineId, "AGV_2", new Vector3(10f * CoordinateScale, 0.34f, (30f + lineIndex * 40f) * CoordinateScale));
            }
        }

        private static void CreateAgv(Transform parent, string lineId, string agvId, Vector3 position)
        {
            GameObject root = new GameObject(lineId + "_" + agvId);
            root.transform.SetParent(parent);
            root.transform.position = position;

            GameObject carriage = CreateBox("Rail Carriage", root.transform, position + new Vector3(0f, 0.20f, 0f), new Vector3(1.52f, 0.40f, 0.92f), darkMetal);
            carriage.transform.localPosition = new Vector3(0f, 0.20f, 0f);
            GameObject shell = CreateBox("Status Band", root.transform, position + new Vector3(0f, 0.37f, -0.48f), new Vector3(1.30f, 0.12f, 0.055f), agvShell);
            shell.transform.localPosition = new Vector3(0f, 0.37f, -0.48f);
            GameObject beacon = CreateCylinder("Robot Beacon", root.transform, position + new Vector3(-0.55f, 0.54f, 0.27f), new Vector3(0.06f, 0.06f, 0.06f), cyanEmissive);
            beacon.transform.localPosition = new Vector3(-0.55f, 0.54f, 0.27f);

            GameObject baseColumn = CreateCylinder("Robot Base", root.transform, position + new Vector3(0f, 0.64f, 0f), new Vector3(0.30f, 0.28f, 0.30f), agvShell);
            baseColumn.transform.localPosition = new Vector3(0f, 0.64f, 0f);

            GameObject shoulderObject = new GameObject("Shoulder Joint");
            shoulderObject.transform.SetParent(root.transform, false);
            shoulderObject.transform.localPosition = new Vector3(0f, 0.94f, 0f);
            Transform shoulder = shoulderObject.transform;
            CreateSphere("Shoulder Housing", shoulder, shoulder.position, new Vector3(0.28f, 0.28f, 0.28f), agvShell).transform.localPosition = Vector3.zero;
            CreateBox("Upper Arm", shoulder, shoulder.position, new Vector3(0.24f, 1.05f, 0.24f), agvShell).transform.localPosition = new Vector3(0f, 0.51f, 0f);

            GameObject elbowObject = new GameObject("Elbow Joint");
            elbowObject.transform.SetParent(shoulder, false);
            elbowObject.transform.localPosition = new Vector3(0f, 1.02f, 0f);
            Transform elbow = elbowObject.transform;
            CreateSphere("Elbow Housing", elbow, elbow.position, new Vector3(0.23f, 0.23f, 0.23f), yellow).transform.localPosition = Vector3.zero;
            CreateBox("Forearm", elbow, elbow.position, new Vector3(0.20f, 0.88f, 0.20f), agvShell).transform.localPosition = new Vector3(0f, 0.42f, 0f);

            GameObject wristObject = new GameObject("Wrist Joint");
            wristObject.transform.SetParent(elbow, false);
            wristObject.transform.localPosition = new Vector3(0f, 0.84f, 0f);
            Transform wrist = wristObject.transform;
            CreateSphere("Wrist Housing", wrist, wrist.position, new Vector3(0.17f, 0.17f, 0.17f), yellow).transform.localPosition = Vector3.zero;
            CreateCylinder("Tool Flange", wrist, wrist.position, new Vector3(0.13f, 0.18f, 0.13f), darkMetal).transform.localPosition = new Vector3(0f, 0.25f, 0f);
            CreateBox("Gripper", wrist, wrist.position, new Vector3(0.36f, 0.10f, 0.22f), darkMetal).transform.localPosition = new Vector3(0f, 0.47f, 0f);

            TextMesh label = CreateWorldLabel(agvId.Replace("AGV_", "R"), position + Vector3.up * 2.62f, 0.052f, new Color(0.10f, 0.18f, 0.20f));
            label.transform.SetParent(root.transform, true);
            AgvView view = root.AddComponent<AgvView>();
            view.Configure(lineId, agvId, label, new[] { shell.GetComponent<Renderer>(), beacon.GetComponent<Renderer>() });
            root.AddComponent<RobotArmAnimator>().Configure(shoulder, elbow, wrist);
        }

        private static void BuildCameraAndLighting()
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = false;
            camera.fieldOfView = 43f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 150f;
            camera.allowHDR = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.89f, 0.92f, 0.93f);
            UniversalAdditionalCameraData cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<FactoryCameraController>().Configure(
                new Vector3(9.7f, 0.65f, 15f), 45f, -42f, 58f,
                new Vector2(-1f, 26f), new Vector2(-1f, 31f));

            GameObject keyLightObject = new GameObject("Sun / Key Light");
            Light keyLight = keyLightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.28f;
            keyLight.color = new Color(1f, 0.965f, 0.90f);
            keyLight.shadows = LightShadows.Soft;
            keyLight.shadowStrength = 0.58f;
            keyLight.shadowBias = 0.025f;
            keyLightObject.transform.rotation = Quaternion.Euler(68f, -32f, 0f);

            for (int i = 0; i < 3; i++)
            {
                GameObject fillObject = new GameObject("Production Hall Light " + (i + 1));
                fillObject.transform.position = new Vector3(11f, 7f, 5f + i * 10f);
                fillObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                Light fill = fillObject.AddComponent<Light>();
                fill.type = LightType.Spot;
                fill.intensity = 1.05f;
                fill.range = 18f;
                fill.spotAngle = 88f;
                fill.color = new Color(0.86f, 0.94f, 1f);
                fill.shadows = LightShadows.None;
            }

            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            profile.components.Clear();
            Bloom bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.10f);
            bloom.scatter.Override(0.6f);
            Tonemapping tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);
            ColorAdjustments grading = profile.Add<ColorAdjustments>(true);
            grading.postExposure.Override(-0.05f);
            grading.contrast.Override(3f);
            grading.saturation.Override(-2f);
            Vignette vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.035f);
            vignette.smoothness.Override(0.55f);
            EditorUtility.SetDirty(profile);

            GameObject volumeObject = new GameObject("Global Look Development");
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = profile;
        }

        private static void BuildHud()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GameObject canvasObject = new GameObject("DIGITAL TWIN HUD");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            Color panel = new Color(0.018f, 0.033f, 0.055f, 0.96f);
            Color card = new Color(0.045f, 0.075f, 0.11f, 0.96f);
            Color muted = new Color(0.48f, 0.6f, 0.7f);
            Color bright = new Color(0.87f, 0.94f, 1f);
            Color uiCyan = new Color(0.05f, 0.76f, 1f);

            Image sidebar = CreateUiImage("Operations Sidebar", canvasObject.transform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(360f, 0f), Vector2.zero, panel);
            CreateUiImage("Sidebar Accent", sidebar.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(3f, 0f), Vector2.zero, uiCyan);
            CreateUiImage("Brand Mark", sidebar.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(6f, 54f), new Vector2(20f, -24f), uiCyan);
            CreateUiText("Brand", sidebar.transform, font, "SUPCON  /  DIGITAL TWIN", 22, FontStyle.Bold, bright, new Vector2(292f, 32f), new Vector2(42f, -22f), TextAnchor.UpperLeft);
            CreateUiText("Subbrand", sidebar.transform, font, "SMART FACTORY COMMAND CENTER", 11, FontStyle.Normal, muted, new Vector2(292f, 22f), new Vector2(42f, -56f), TextAnchor.UpperLeft);

            Image connectionCard = CreateHudCard(sidebar.transform, "Connection", new Vector2(20f, -106f), new Vector2(320f, 70f), card);
            Image connectionDot = CreateUiImage("Connection Dot", connectionCard.transform, Vector2.up, Vector2.up, new Vector2(0f, 1f), new Vector2(9f, 9f), new Vector2(16f, -19f), new Color(0.18f, 0.93f, 0.64f));
            Text connectionText = CreateUiText("Connection State", connectionCard.transform, font, "CONNECTING", 14, FontStyle.Bold, bright, new Vector2(205f, 22f), new Vector2(34f, -12f), TextAnchor.UpperLeft);
            Text connectionMeta = CreateUiText("Connection Meta", connectionCard.transform, font, "127.0.0.1:1883", 11, FontStyle.Normal, muted, new Vector2(270f, 22f), new Vector2(16f, -40f), TextAnchor.UpperLeft);
            Text messageText = CreateUiText("Message Count", connectionCard.transform, font, "0  LIVE MESSAGES", 10, FontStyle.Bold, uiCyan, new Vector2(142f, 20f), new Vector2(162f, -43f), TextAnchor.UpperRight);

            CreateUiText("KPI Header", sidebar.transform, font, "PRODUCTION KPI", 11, FontStyle.Bold, muted, new Vector2(320f, 20f), new Vector2(20f, -202f), TextAnchor.UpperLeft);
            Text ordersText = CreateKpiCard(sidebar.transform, font, "ORDERS", "--", new Vector2(20f, -228f), card, bright);
            Text completionText = CreateKpiCard(sidebar.transform, font, "COMPLETION", "--%", new Vector2(186f, -228f), card, uiCyan);
            Text utilizationText = CreateKpiCard(sidebar.transform, font, "UTILIZATION", "--%", new Vector2(20f, -326f), card, uiCyan);
            Text qualityText = CreateKpiCard(sidebar.transform, font, "FIRST PASS", "--%", new Vector2(186f, -326f), card, new Color(0.18f, 0.93f, 0.64f));
            Text faultsText = CreateKpiCard(sidebar.transform, font, "ACTIVE FAULTS", "--", new Vector2(20f, -424f), card, new Color(0.18f, 0.93f, 0.64f));
            CreateKpiCard(sidebar.transform, font, "LINES ONLINE", "03 / 03", new Vector2(186f, -424f), card, bright);

            CreateUiText("Fleet Header", sidebar.transform, font, "AMR FLEET", 11, FontStyle.Bold, muted, new Vector2(320f, 20f), new Vector2(20f, -536f), TextAnchor.UpperLeft);
            Image fleetCard = CreateHudCard(sidebar.transform, "Fleet", new Vector2(20f, -562f), new Vector2(320f, 278f), card);
            var fleetNames = new Text[6];
            var fleetStatuses = new Text[6];
            var fleetBatteries = new Text[6];
            var fleetDots = new Image[6];
            for (int i = 0; i < 6; i++)
            {
                float y = -18f - i * 43f;
                fleetDots[i] = CreateUiImage("Fleet Dot " + i, fleetCard.transform, Vector2.up, Vector2.up, new Vector2(0f, 1f), new Vector2(7f, 7f), new Vector2(15f, y - 5f), muted);
                fleetNames[i] = CreateUiText("Fleet Name " + i, fleetCard.transform, font, "--", 12, FontStyle.Bold, bright, new Vector2(112f, 22f), new Vector2(31f, y), TextAnchor.UpperLeft);
                fleetStatuses[i] = CreateUiText("Fleet Status " + i, fleetCard.transform, font, "WAITING", 10, FontStyle.Bold, muted, new Vector2(92f, 20f), new Vector2(155f, y + 1f), TextAnchor.UpperCenter);
                fleetBatteries[i] = CreateUiText("Fleet Battery " + i, fleetCard.transform, font, "--%", 11, FontStyle.Bold, uiCyan, new Vector2(48f, 20f), new Vector2(258f, y), TextAnchor.UpperRight);
                if (i < 5)
                {
                    CreateUiImage("Fleet Divider " + i, fleetCard.transform, Vector2.up, Vector2.up, new Vector2(0f, 1f), new Vector2(290f, 1f), new Vector2(15f, y - 31f), new Color(0.13f, 0.19f, 0.25f, 0.8f));
                }
            }

            CreateUiText("Events Header", sidebar.transform, font, "EVENT STREAM", 11, FontStyle.Bold, muted, new Vector2(320f, 20f), new Vector2(20f, -870f), TextAnchor.UpperLeft);
            Image eventsCard = CreateHudCard(sidebar.transform, "Events", new Vector2(20f, -896f), new Vector2(320f, 150f), card);
            Text eventsText = CreateUiText("Events Text", eventsCard.transform, font, "›  Waiting for live events…", 10, FontStyle.Normal, muted, new Vector2(288f, 120f), new Vector2(16f, -14f), TextAnchor.UpperLeft);
            eventsText.horizontalOverflow = HorizontalWrapMode.Wrap;
            eventsText.verticalOverflow = VerticalWrapMode.Truncate;

            Image topBar = CreateUiImage("Scene Header", canvasObject.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(-418f, 72f), new Vector2(181f, -18f), new Color(0.018f, 0.033f, 0.055f, 0.88f));
            CreateUiText("Scene Eyebrow", topBar.transform, font, "LIVE OPERATIONS  /  FLEXIBLE MACHINING", 11, FontStyle.Bold, uiCyan, new Vector2(500f, 18f), new Vector2(22f, -12f), TextAnchor.UpperLeft);
            CreateUiText("Scene Title", topBar.transform, font, "Human-Robot Collaborative Machining Twin", 22, FontStyle.Bold, bright, new Vector2(700f, 28f), new Vector2(22f, -31f), TextAnchor.UpperLeft);
            Text clockText = CreateUiText("Clock", topBar.transform, font, "", 13, FontStyle.Normal, bright, new Vector2(280f, 28f), new Vector2(1160f, -25f), TextAnchor.UpperRight);

            Image legend = CreateUiImage("Scene Legend", canvasObject.transform, Vector2.one, Vector2.one, Vector2.one, new Vector2(250f, 252f), new Vector2(-24f, -112f), new Color(0.018f, 0.033f, 0.055f, 0.9f));
            CreateUiImage("Legend Accent", legend.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(54f, 3f), new Vector2(18f, -18f), uiCyan);
            CreateUiText("Legend Title", legend.transform, font, "SCENE LEGEND", 12, FontStyle.Bold, bright, new Vector2(210f, 20f), new Vector2(18f, -34f), TextAnchor.UpperLeft);
            CreateLegendRow(legend.transform, font, "ROBOT RAIL", "TEAL", uiCyan, -76f, bright, muted);
            CreateLegendRow(legend.transform, font, "ROBOT ARM", "ORANGE", new Color(1f, 0.64f, 0.04f), -119f, bright, muted);
            CreateLegendRow(legend.transform, font, "ONLINE EQUIPMENT", "GREEN", new Color(0.18f, 0.93f, 0.64f), -162f, bright, muted);
            CreateUiImage("Legend Divider", legend.transform, Vector2.up, Vector2.up, new Vector2(0f, 1f), new Vector2(214f, 1f), new Vector2(18f, -202f), new Color(0.14f, 0.2f, 0.27f));
            CreateUiText("Legend View", legend.transform, font, "URP 14  /  PERSPECTIVE LIVE VIEW", 10, FontStyle.Bold, muted, new Vector2(214f, 20f), new Vector2(18f, -218f), TextAnchor.UpperLeft);

            Image architecture = CreateUiImage("Architecture", canvasObject.transform, Vector2.one, Vector2.one, Vector2.one, new Vector2(250f, 174f), new Vector2(-24f, -386f), new Color(0.018f, 0.033f, 0.055f, 0.9f));
            CreateUiText("Architecture Label", architecture.transform, font, "CONTROL ARCHITECTURE", 10, FontStyle.Bold, muted, new Vector2(214f, 18f), new Vector2(18f, -16f), TextAnchor.UpperLeft);
            CreateUiText("Architecture Backend", architecture.transform, font, "PYTHON / SIMPY", 15, FontStyle.Bold, bright, new Vector2(214f, 22f), new Vector2(18f, -46f), TextAnchor.UpperLeft);
            CreateUiText("Architecture Link", architecture.transform, font, "MQTT  ↕  STATE + COMMAND", 10, FontStyle.Bold, uiCyan, new Vector2(214f, 20f), new Vector2(18f, -78f), TextAnchor.UpperLeft);
            CreateUiText("Architecture Frontend", architecture.transform, font, "UNITY / URP", 15, FontStyle.Bold, bright, new Vector2(214f, 22f), new Vector2(18f, -110f), TextAnchor.UpperLeft);
            CreateUiText("Architecture Note", architecture.transform, font, "REAL LOGIC  →  VISUAL TWIN", 9, FontStyle.Bold, new Color(0.18f, 0.93f, 0.64f), new Vector2(214f, 18f), new Vector2(18f, -142f), TextAnchor.UpperLeft);
            CreateUiText("Camera Help", canvasObject.transform, font, "RMB ORBIT   •   W A S D PAN   •   WHEEL ZOOM", 10, FontStyle.Bold, muted, new Vector2(420f, 24f), new Vector2(-28f, 24f), TextAnchor.LowerRight, new Vector2(1f, 0f));

            FactoryHudView hud = canvasObject.AddComponent<FactoryHudView>();
            hud.Configure(connectionDot, connectionText, connectionMeta, clockText, messageText, ordersText, completionText, utilizationText, qualityText, faultsText, fleetNames, fleetStatuses, fleetBatteries, fleetDots, eventsText);
        }

        private static Image CreateHudCard(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            return CreateUiImage(name, parent, Vector2.up, Vector2.up, new Vector2(0f, 1f), size, anchoredPosition, color);
        }

        private static Text CreateKpiCard(Transform parent, Font font, string label, string value, Vector2 position, Color cardColor, Color valueColor)
        {
            Image card = CreateHudCard(parent, label + " Card", position, new Vector2(154f, 82f), cardColor);
            CreateUiText(label + " Label", card.transform, font, label, 10, FontStyle.Bold, new Color(0.48f, 0.6f, 0.7f), new Vector2(126f, 18f), new Vector2(14f, -12f), TextAnchor.UpperLeft);
            return CreateUiText(label + " Value", card.transform, font, value, 23, FontStyle.Bold, valueColor, new Vector2(126f, 34f), new Vector2(14f, -37f), TextAnchor.UpperLeft);
        }

        private static void CreateLegendRow(Transform parent, Font font, string label, string value, Color dotColor, float y, Color bright, Color muted)
        {
            CreateUiImage(label + " Dot", parent, Vector2.up, Vector2.up, new Vector2(0f, 1f), new Vector2(7f, 7f), new Vector2(18f, y - 4f), dotColor);
            CreateUiText(label, parent, font, label, 10, FontStyle.Bold, bright, new Vector2(150f, 20f), new Vector2(34f, y), TextAnchor.UpperLeft);
            CreateUiText(value, parent, font, value, 9, FontStyle.Bold, muted, new Vector2(60f, 20f), new Vector2(172f, y + 1f), TextAnchor.UpperRight);
        }

        private static Image CreateUiImage(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Vector2 position, Color color)
        {
            GameObject item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            item.transform.SetParent(parent, false);
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Image image = item.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text CreateUiText(string name, Transform parent, Font font, string value, int fontSize, FontStyle style, Color color, Vector2 size, Vector2 position, TextAnchor alignment, Vector2? anchor = null)
        {
            Vector2 resolvedAnchor = anchor ?? Vector2.up;
            GameObject item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            item.transform.SetParent(parent, false);
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.anchorMin = resolvedAnchor;
            rect.anchorMax = resolvedAnchor;
            rect.pivot = resolvedAnchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Text text = item.GetComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        private static GameObject CreateBox(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.position = position;
            box.transform.localScale = scale;
            box.transform.SetParent(parent, true);
            Renderer renderer = box.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = scale.y > 0.02f ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            Object.DestroyImmediate(box.GetComponent<Collider>());
            return box;
        }

        private static GameObject CreateCylinder(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = name;
            cylinder.transform.position = position;
            cylinder.transform.localScale = scale;
            cylinder.transform.SetParent(parent, true);
            Renderer renderer = cylinder.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            Object.DestroyImmediate(cylinder.GetComponent<Collider>());
            return cylinder;
        }

        private static GameObject CreateSphere(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;
            sphere.transform.position = position;
            sphere.transform.localScale = scale;
            sphere.transform.SetParent(parent, true);
            Renderer renderer = sphere.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            Object.DestroyImmediate(sphere.GetComponent<Collider>());
            return sphere;
        }

        private static TextMesh CreateWorldLabel(string text, Vector3 position, float characterSize, Color color)
        {
            GameObject labelObject = new GameObject(text + " Label");
            labelObject.transform.position = position;
            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.text = text;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = characterSize;
            label.fontSize = 64;
            label.color = color;
            labelObject.AddComponent<BillboardLabel>();
            return label;
        }

        private static Material GetMaterial(string name, Color color, float metallic, float smoothness, bool emission = false, string texturePath = null)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            material.SetColor("_BaseColor", color);
            material.color = color;
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            Texture2D texture = string.IsNullOrEmpty(texturePath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture != null)
            {
                material.SetTexture("_BaseMap", texture);
                material.SetTexture("_MainTex", texture);
            }
            else
            {
                material.SetTexture("_BaseMap", null);
                material.SetTexture("_MainTex", null);
            }
            if (emission)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 2.5f);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
            }
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
