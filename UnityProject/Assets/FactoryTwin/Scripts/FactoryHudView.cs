using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FactoryTwin
{
    public sealed class FactoryHudView : MonoBehaviour
    {
        [SerializeField] private Image connectionDot;
        [SerializeField] private Text connectionText;
        [SerializeField] private Text connectionMetaText;
        [SerializeField] private Text clockText;
        [SerializeField] private Text messageText;
        [SerializeField] private Text ordersText;
        [SerializeField] private Text completionText;
        [SerializeField] private Text utilizationText;
        [SerializeField] private Text qualityText;
        [SerializeField] private Text faultsText;
        [SerializeField] private Text[] fleetNameTexts;
        [SerializeField] private Text[] fleetStatusTexts;
        [SerializeField] private Text[] fleetBatteryTexts;
        [SerializeField] private Image[] fleetStatusDots;
        [SerializeField] private Text eventsText;

        private readonly List<AgvView> sortedFleet = new List<AgvView>();

        public void Configure(
            Image newConnectionDot,
            Text newConnectionText,
            Text newConnectionMetaText,
            Text newClockText,
            Text newMessageText,
            Text newOrdersText,
            Text newCompletionText,
            Text newUtilizationText,
            Text newQualityText,
            Text newFaultsText,
            Text[] newFleetNameTexts,
            Text[] newFleetStatusTexts,
            Text[] newFleetBatteryTexts,
            Image[] newFleetStatusDots,
            Text newEventsText)
        {
            connectionDot = newConnectionDot;
            connectionText = newConnectionText;
            connectionMetaText = newConnectionMetaText;
            clockText = newClockText;
            messageText = newMessageText;
            ordersText = newOrdersText;
            completionText = newCompletionText;
            utilizationText = newUtilizationText;
            qualityText = newQualityText;
            faultsText = newFaultsText;
            fleetNameTexts = newFleetNameTexts;
            fleetStatusTexts = newFleetStatusTexts;
            fleetBatteryTexts = newFleetBatteryTexts;
            fleetStatusDots = newFleetStatusDots;
            eventsText = newEventsText;
        }

        public void Refresh(
            bool connected,
            bool demoMode,
            string endpoint,
            int receivedMessages,
            FactoryTwinController.KpiMessage kpi,
            IEnumerable<AgvView> fleet,
            IEnumerable<string> recentEvents)
        {
            Color online = new Color(0.18f, 0.93f, 0.64f);
            Color offline = new Color(1f, 0.42f, 0.32f);
            Color demo = new Color(1f, 0.72f, 0.18f);
            if (connectionDot != null)
            {
                connectionDot.color = connected ? online : (demoMode ? demo : offline);
            }
            Set(connectionText, connected ? "SYSTEM ONLINE" : (demoMode ? "OFFLINE DEMO" : "CONNECTING"));
            if (connectionText != null)
            {
                connectionText.color = connected ? online : (demoMode ? demo : offline);
            }
            Set(connectionMetaText, endpoint);
            Set(clockText, DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss"));
            Set(messageText, receivedMessages.ToString("N0") + (demoMode ? "  DEMO EVENTS" : "  LIVE MESSAGES"));

            if (kpi == null)
            {
                Set(ordersText, "--");
                Set(completionText, "--%");
                Set(utilizationText, "--%");
                Set(qualityText, "--%");
                Set(faultsText, "--");
            }
            else
            {
                Set(ordersText, kpi.completed_orders + " / " + (kpi.completed_orders + kpi.active_orders));
                Set(completionText, kpi.order_completion_rate.ToString("0.0") + "%");
                Set(utilizationText, kpi.device_utilization.ToString("0.0") + "%");
                Set(qualityText, kpi.first_pass_rate.ToString("0.0") + "%");
                Set(faultsText, kpi.active_faults.ToString());
                if (faultsText != null)
                {
                    faultsText.color = kpi.active_faults > 0 ? offline : online;
                }
            }

            sortedFleet.Clear();
            if (fleet != null)
            {
                sortedFleet.AddRange(fleet);
                sortedFleet.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
            }

            int rowCount = fleetNameTexts == null ? 0 : fleetNameTexts.Length;
            for (int i = 0; i < rowCount; i++)
            {
                bool visible = i < sortedFleet.Count;
                AgvView view = visible ? sortedFleet[i] : null;
                Set(fleetNameTexts[i], visible ? ShortFleetName(view.Key) : "--");
                Set(fleetStatusTexts[i], visible ? view.Status.ToUpperInvariant() : "WAITING");
                Set(fleetBatteryTexts[i], visible ? view.Battery.ToString("0") + "%" : "--%");
                if (fleetStatusDots != null && i < fleetStatusDots.Length && fleetStatusDots[i] != null)
                {
                    fleetStatusDots[i].color = visible ? StatusColor(view.Status) : new Color(0.28f, 0.34f, 0.42f);
                }
            }

            if (eventsText != null)
            {
                string content = string.Empty;
                if (recentEvents != null)
                {
                    foreach (string item in recentEvents)
                    {
                        if (content.Length > 0)
                        {
                            content += "\n";
                        }
                        content += "›  " + item;
                    }
                }
                eventsText.text = string.IsNullOrEmpty(content) ? "›  Waiting for live events…" : content;
            }
        }

        private static void Set(Text target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }

        private static string ShortFleetName(string key)
        {
            string[] parts = key.Split('/');
            if (parts.Length != 2)
            {
                return key;
            }
            string line = parts[0].Replace("line", "L").ToUpperInvariant();
            string agv = parts[1].Replace("AGV_", "AGV-");
            return line + "  /  " + agv;
        }

        private static Color StatusColor(string status)
        {
            switch (status)
            {
                case "moving": return new Color(0.12f, 0.76f, 1f);
                case "charging": return new Color(1f, 0.72f, 0.18f);
                case "fault": return new Color(1f, 0.3f, 0.24f);
                case "interacting": return new Color(0.75f, 0.42f, 1f);
                default: return new Color(0.18f, 0.93f, 0.64f);
            }
        }
    }
}
