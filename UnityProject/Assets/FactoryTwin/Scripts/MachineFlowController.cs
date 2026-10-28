namespace FactoryTwin
{
    /// <summary>
    /// Owns the status presentation of one machining station. The production
    /// orchestrator sequences work through this controller instead of calling
    /// DeviceView.Apply directly.
    /// </summary>
    public sealed class MachineFlowController
    {
        private DeviceView view;

        public string Id { get; }

        public MachineFlowController(int line, int stationIndex)
        {
            Id = MachineId(line, stationIndex);
        }

        public void Bind(DeviceView device)
        {
            view = device;
        }

        public void Loading(string materialId)
        {
            view?.Apply("blocked", materialId + " loading");
        }

        public void Processing(string materialId)
        {
            view?.Apply("processing", materialId + " machining");
        }

        public void Unloading(string materialId)
        {
            view?.Apply("blocked", materialId + " unloading");
        }

        public void Idle()
        {
            view?.Apply("idle", "Cycle complete");
        }

        public static string MachineId(int line, int stationIndex)
        {
            return "machine_m" + (((line - 1) * 4) + stationIndex + 1).ToString("D2");
        }
    }
}
