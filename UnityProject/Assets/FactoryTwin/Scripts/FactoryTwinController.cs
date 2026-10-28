using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FactoryTwin
{
    public sealed class FactoryTwinController : MonoBehaviour
    {
        [Serializable]
        public sealed class Position2D
        {
            public float x;
            public float y;
        }

        [Serializable]
        public sealed class AgvStatusMessage
        {
            public float timestamp;
            public string source_id;
            public string status;
            public float speed_mps;
            public string current_point;
            public Position2D position;
            public string target_point;
            public Position2D target_position;
            public float estimated_time;
            public string[] payload;
            public float battery_level;
            public string message;
        }

        [Serializable]
        private sealed class DeviceStatusMessage
        {
            public string source_id;
            public string status;
            public string message;
        }

        [Serializable]
        public sealed class KpiMessage
        {
            public float order_completion_rate;
            public float device_utilization;
            public float first_pass_rate;
            public float total_production_cost;
            public float agv_utilization;
            public int active_orders;
            public int completed_orders;
            public int active_faults;
        }

        private readonly struct InboundMessage
        {
            public readonly string Topic;
            public readonly string Payload;

            public InboundMessage(string topic, string payload)
            {
                Topic = topic;
                Payload = payload;
            }
        }

        private sealed class DemoAgvMotion
        {
            public AgvView View;
            public float MinX;
            public float MaxX;
            public float Z;
            public float NextTransition;
            public float Battery;
            public bool TowardMax = true;
            public int Phase;
            public int Index;
        }

        [Header("Local MQTT")]
        [SerializeField] private string brokerHost = "127.0.0.1";
        [SerializeField] private int brokerPort = 1883;
        [SerializeField] private string topicRoot = "NLDF_LOCAL";
        [SerializeField] private float coordinateScale = 0.25f;

        [Header("Offline Preview")]
        [SerializeField] private bool enableOfflineDemo = true;
        [SerializeField] private float offlineDemoDelay = 1.5f;
        [SerializeField] private float offlineTravelSeconds = 7.5f;

        private readonly ConcurrentQueue<InboundMessage> incoming = new ConcurrentQueue<InboundMessage>();
        private readonly Dictionary<string, AgvView> agvs = new Dictionary<string, AgvView>();
        private readonly Dictionary<string, DeviceView> devices = new Dictionary<string, DeviceView>();
        private readonly List<DemoAgvMotion> demoFleet = new List<DemoAgvMotion>();
        private readonly Queue<string> events = new Queue<string>();
        private MqttTcpClient mqtt;
        private KpiMessage latestKpi;
        private FactoryHudView hud;
        private int receivedMessages;
        private float nextHudRefresh;
        private float startupTime;
        private float nextDeviceDemoUpdate;
        private int deviceDemoPhase;
        private bool offlineDemoActive;
        private string lastEvent;
        private FactoryProductionFlowAnimator productionFlow;
        private FactoryFlowIntegrityMonitor integrityMonitor;

        private void Start()
        {
            startupTime = Time.unscaledTime;
            ApplyPresentationLook();
            if (FindObjectOfType<WarehouseFlowAnimator>() == null)
            {
                gameObject.AddComponent<WarehouseFlowAnimator>();
            }
            productionFlow = FindObjectOfType<FactoryProductionFlowAnimator>();
            if (productionFlow == null)
            {
                productionFlow = gameObject.AddComponent<FactoryProductionFlowAnimator>();
            }
            integrityMonitor = FindObjectOfType<FactoryFlowIntegrityMonitor>();
            if (integrityMonitor == null)
            {
                integrityMonitor = gameObject.AddComponent<FactoryFlowIntegrityMonitor>();
            }
            foreach (AgvView view in FindObjectsOfType<AgvView>())
            {
                agvs[view.Key] = view;
            }
            foreach (DeviceView view in FindObjectsOfType<DeviceView>())
            {
                devices[view.Key] = view;
            }
            hud = FindObjectOfType<FactoryHudView>();

            mqtt = new MqttTcpClient();
            mqtt.MessageReceived += (topic, payload) => incoming.Enqueue(new InboundMessage(topic, payload));
            string clientId = "unity_factory_twin_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            mqtt.Start(brokerHost, brokerPort, clientId, new[]
            {
                topicRoot + "/+/agv/+/status",
                topicRoot + "/+/station/+/status",
                topicRoot + "/+/conveyor/+/status",
                topicRoot + "/warehouse/+/status",
                topicRoot + "/kpi/status",
                topicRoot + "/+/alerts",
                topicRoot + "/response/+"
            });

            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (arguments[i] == "-factoryTwinSmokeSeconds" &&
                    float.TryParse(arguments[i + 1], out float seconds))
                {
                    StartCoroutine(QuitAfterSmokeTest(seconds));
                    break;
                }
            }
        }

        private static void ApplyPresentationLook()
        {
            Application.targetFrameRate = 60;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.34f, 0.39f, 0.42f);
            RenderSettings.ambientEquatorColor = new Color(0.25f, 0.29f, 0.31f);
            RenderSettings.ambientGroundColor = new Color(0.14f, 0.17f, 0.18f);
            RenderSettings.fogColor = new Color(0.50f, 0.57f, 0.60f);
            RenderSettings.fogStartDistance = 46f;
            RenderSettings.fogEndDistance = 88f;

            foreach (Light sceneLight in FindObjectsOfType<Light>())
            {
                if (sceneLight.name == "Sun / Key Light")
                {
                    sceneLight.intensity = 0.92f;
                    sceneLight.color = new Color(1f, 0.95f, 0.86f);
                    sceneLight.shadowStrength = 0.72f;
                }
                else if (sceneLight.name.StartsWith("Production Hall Light"))
                {
                    sceneLight.intensity = 0.38f;
                    sceneLight.color = new Color(0.72f, 0.86f, 1f);
                }
            }

            Volume volume = FindObjectOfType<Volume>();
            if (volume != null && volume.sharedProfile != null)
            {
                if (volume.sharedProfile.TryGet(out ColorAdjustments grading))
                {
                    grading.postExposure.Override(-0.62f);
                    grading.contrast.Override(14f);
                    grading.saturation.Override(5f);
                }
                if (volume.sharedProfile.TryGet(out Bloom bloom))
                {
                    bloom.threshold.Override(1.25f);
                    bloom.intensity.Override(0.045f);
                }
                if (volume.sharedProfile.TryGet(out Vignette vignette))
                {
                    vignette.intensity.Override(0.12f);
                    vignette.smoothness.Override(0.62f);
                }
            }
        }

        private void Update()
        {
            int processed = 0;
            while (processed++ < 100 && incoming.TryDequeue(out InboundMessage message))
            {
                ProcessMessage(message.Topic, message.Payload);
            }

            bool liveConnected = mqtt != null && mqtt.IsConnected;
            if (liveConnected && offlineDemoActive)
            {
                StopOfflineDemo();
            }
            else if (!liveConnected && enableOfflineDemo && !offlineDemoActive &&
                     Time.unscaledTime - startupTime >= offlineDemoDelay)
            {
                StartOfflineDemo();
            }

            if (offlineDemoActive)
            {
                UpdateOfflineDemo();
            }

            foreach (AgvView view in agvs.Values)
            {
                view.Tick();
            }

            if (hud != null && Time.unscaledTime >= nextHudRefresh)
            {
                nextHudRefresh = Time.unscaledTime + 0.2f;
                hud.Refresh(
                    liveConnected,
                    offlineDemoActive,
                    offlineDemoActive ? "LOCAL ANIMATION  /  MQTT NOT REQUIRED" : brokerHost + ":" + brokerPort + "  /  " + topicRoot,
                    receivedMessages,
                    latestKpi,
                    agvs.Values,
                    events);
            }
        }

        private void StartOfflineDemo()
        {
            offlineDemoActive = true;
            demoFleet.Clear();
            if (productionFlow != null)
            {
                productionFlow.SetRunning(true);
            }

            latestKpi = new KpiMessage
            {
                order_completion_rate = 72f,
                device_utilization = 81f,
                first_pass_rate = 96f,
                total_production_cost = 12840f,
                agv_utilization = 78f,
                active_orders = 8,
                completed_orders = 21,
                active_faults = 0
            };
            receivedMessages = 0;
            nextDeviceDemoUpdate = 0f;
            AddEvent("Offline demo started — waiting for MQTT backend");
        }

        private void StopOfflineDemo()
        {
            offlineDemoActive = false;
            if (productionFlow != null)
            {
                productionFlow.SetRunning(false);
            }
            foreach (DemoAgvMotion item in demoFleet)
            {
                Vector3 position = item.View.transform.position;
                item.View.Apply(new AgvStatusMessage
                {
                    timestamp = 0f,
                    source_id = item.View.Key,
                    status = "waiting",
                    position = FromWorld(position),
                    battery_level = item.Battery
                }, coordinateScale);
            }
            demoFleet.Clear();
            AddEvent("MQTT connected — switched to live simulation data");
        }

        private void UpdateOfflineDemo()
        {
            float now = Time.unscaledTime;
            if (productionFlow != null)
            {
                latestKpi.order_completion_rate = 72f + Mathf.PingPong(now * 0.35f, 18f);
                latestKpi.device_utilization = 76f + Mathf.PingPong(now * 0.28f, 14f);
                latestKpi.completed_orders = 21 + productionFlow.CompletedJobs;
                receivedMessages = productionFlow.EventCount;
                return;
            }
            foreach (DemoAgvMotion item in demoFleet)
            {
                if (now < item.NextTransition)
                {
                    continue;
                }

                if (item.Phase == 1)
                {
                    BeginDemoInteraction(item, now);
                }
                else
                {
                    BeginDemoTravel(item, now);
                }
            }

            if (now >= nextDeviceDemoUpdate)
            {
                nextDeviceDemoUpdate = now + 4f;
                deviceDemoPhase++;
                int index = 0;
                foreach (DeviceView device in devices.Values)
                {
                    string state = (index + deviceDemoPhase) % 3 == 0 ? "processing" : "idle";
                    device.Apply(state, "Offline preview");
                    index++;
                }
                receivedMessages++;
                latestKpi.order_completion_rate = 72f + Mathf.PingPong(now * 0.35f, 18f);
                latestKpi.device_utilization = 76f + Mathf.PingPong(now * 0.28f, 14f);
                latestKpi.completed_orders = 21 + Mathf.FloorToInt(now / 8f);
            }
        }

        private void BeginDemoTravel(DemoAgvMotion item, float now)
        {
            Vector3 current = item.View.transform.position;
            float targetX = item.TowardMax ? item.MaxX : item.MinX;
            float duration = offlineTravelSeconds + item.Index * 0.28f;
            item.Battery = Mathf.Max(28f, item.Battery - 0.8f);
            item.View.Apply(new AgvStatusMessage
            {
                timestamp = 0f,
                source_id = item.View.Key,
                status = "moving",
                position = FromWorld(current),
                target_position = FromWorld(new Vector3(targetX, current.y, item.Z)),
                estimated_time = duration,
                battery_level = item.Battery
            }, coordinateScale);
            item.Phase = 1;
            item.NextTransition = now + duration;
            receivedMessages++;
        }

        private void BeginDemoInteraction(DemoAgvMotion item, float now)
        {
            Vector3 position = item.View.transform.position;
            item.View.Apply(new AgvStatusMessage
            {
                timestamp = 0f,
                source_id = item.View.Key,
                status = "interacting",
                position = FromWorld(position),
                battery_level = item.Battery
            }, coordinateScale);
            item.TowardMax = !item.TowardMax;
            item.Phase = 2;
            // R3-2 waits for the large ASRS crane and fixed conveyor before grasping.
            bool warehouseLoad = item.View.Key == "line3/AGV_2" && position.x > 18f;
            item.NextTransition = now + (warehouseLoad ? WarehouseFlowAnimator.CycleSeconds + 0.25f : 5.4f);
            receivedMessages++;
        }

        private Position2D FromWorld(Vector3 position)
        {
            return new Position2D
            {
                x = position.x / coordinateScale,
                y = position.z / coordinateScale
            };
        }

        private void ProcessMessage(string topic, string payload)
        {
            receivedMessages++;
            string[] parts = topic.Split('/');
            if (parts.Length < 3 || parts[0] != topicRoot)
            {
                return;
            }

            try
            {
                if (parts.Length == 5 && parts[2] == "agv")
                {
                    AgvStatusMessage state = JsonUtility.FromJson<AgvStatusMessage>(payload);
                    string key = parts[1] + "/" + state.source_id;
                    if (!agvs.TryGetValue(key, out AgvView view))
                    {
                        view = CreateRuntimeAgv(parts[1], state.source_id);
                        agvs[key] = view;
                    }
                    view.Apply(state, coordinateScale);
                }
                else if (parts.Length == 5 && (parts[2] == "station" || parts[2] == "conveyor"))
                {
                    DeviceStatusMessage state = JsonUtility.FromJson<DeviceStatusMessage>(payload);
                    string key = parts[1] + "/" + state.source_id;
                    if (devices.TryGetValue(key, out DeviceView view))
                    {
                        view.Apply(state.status, state.message);
                    }
                }
                else if (parts.Length == 4 && parts[1] == "warehouse")
                {
                    DeviceStatusMessage state = JsonUtility.FromJson<DeviceStatusMessage>(payload);
                    string key = "warehouse/" + parts[2];
                    if (devices.TryGetValue(key, out DeviceView view))
                    {
                        view.Apply(state.status, state.message);
                    }
                    AddEvent(parts[2].ToUpperInvariant() + "  /  " +
                             (string.IsNullOrEmpty(state.status) ? "UPDATED" : state.status.ToUpperInvariant()));
                }
                else if (parts.Length == 3 && parts[1] == "kpi")
                {
                    latestKpi = JsonUtility.FromJson<KpiMessage>(payload);
                }
                else if (topic.Contains("/alerts") || topic.Contains("/response/"))
                {
                    AddEvent(topic + ": " + payload);
                }
            }
            catch (Exception exception)
            {
                AddEvent("JSON error on " + topic + ": " + exception.Message);
            }
        }

        private AgvView CreateRuntimeAgv(string lineId, string agvId)
        {
            var root = new GameObject(lineId + "_" + agvId);
            root.transform.SetParent(transform, false);
            root.transform.position = Vector3.up * 0.35f;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(1.1f, 0.35f, 0.75f);
            Destroy(body.GetComponent<Collider>());

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(root.transform, false);
            labelObject.transform.localPosition = Vector3.up * 1.1f;
            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = 0.12f;
            label.fontSize = 40;
            label.color = Color.white;

            AgvView view = root.AddComponent<AgvView>();
            view.Configure(lineId, agvId, label, new[] { body.GetComponent<Renderer>() });
            return view;
        }

        private void AddEvent(string text)
        {
            if (text == lastEvent)
            {
                return;
            }
            lastEvent = text;
            events.Enqueue(text.Length > 120 ? text.Substring(0, 120) + "..." : text);
            while (events.Count > 5)
            {
                events.Dequeue();
            }
        }

        public void ReportFlowEvent(string text)
        {
            AddEvent(text);
        }

        private void OnDestroy()
        {
            mqtt?.Dispose();
        }

        private IEnumerator QuitAfterSmokeTest(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            bool offlineFlowPassed = productionFlow != null && productionFlow.CompletedJobs > 0;
            bool passed = (mqtt != null && mqtt.IsConnected && receivedMessages > 0 && agvs.Count >= 6) ||
                          offlineFlowPassed;
            Debug.Log("FACTORY_TWIN_SMOKE connected=" + (mqtt != null && mqtt.IsConnected) +
                      " messages=" + receivedMessages + " agvs=" + agvs.Count +
                      " completedJobs=" + (productionFlow == null ? 0 : productionFlow.CompletedJobs) +
                      " result=" + (passed ? "PASS" : "FAIL"));
            Application.Quit(passed ? 0 : 2);
        }
    }
}
