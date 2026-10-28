using UnityEngine;

namespace FactoryTwin
{
    public sealed class DeviceView : MonoBehaviour
    {
        [SerializeField] private string key;
        [SerializeField] private TextMesh label;
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private MachineDoorAnimator doorAnimator;
        private MaterialPropertyBlock colorBlock;

        public string Key => key;
        public string Status { get; private set; } = "idle";

        private void Awake()
        {
            colorBlock = new MaterialPropertyBlock();
        }

        public void Configure(string newKey, TextMesh newLabel, Renderer newRenderer, MachineDoorAnimator newDoorAnimator = null)
        {
            key = newKey;
            label = newLabel;
            statusRenderer = newRenderer;
            doorAnimator = newDoorAnimator;
        }

        public void Apply(string status, string message)
        {
            Status = string.IsNullOrEmpty(status) ? "idle" : status;
            Color color;
            switch (Status)
            {
                case "processing":
                case "working": color = new Color(0.1f, 0.75f, 1f); break;
                case "blocked": color = new Color(1f, 0.55f, 0.1f); break;
                case "fault": color = new Color(1f, 0.15f, 0.1f); break;
                default: color = new Color(0.2f, 0.55f, 0.95f); break;
            }

            colorBlock.SetColor("_Color", color);
            colorBlock.SetColor("_BaseColor", color);
            colorBlock.SetColor("_EmissionColor", color * 3f);
            statusRenderer?.SetPropertyBlock(colorBlock);
            doorAnimator?.ApplyStatus(Status);
            if (label != null)
            {
                label.text = label.gameObject.name.Replace(" Label", string.Empty) + "\n" + Status.ToUpperInvariant();
            }
        }
    }
}
