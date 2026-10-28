namespace FactoryTwin
{
    /// <summary>
    /// Owns the status presentation of one inter-station conveyor. The production
    /// orchestrator sequences work through this controller instead of calling
    /// DeviceView.Apply directly.
    /// </summary>
    public sealed class ConveyorFlowController
    {
        private DeviceView view;

        public string Id { get; }

        public ConveyorFlowController(int line, int stationIndex)
        {
            Id = ConveyorId(line, stationIndex);
        }

        public void Bind(DeviceView device)
        {
            view = device;
        }

        public void Receiving(string materialId)
        {
            view?.Apply("blocked", materialId + " receiving");
        }

        public void Transferring(string materialId)
        {
            view?.Apply("working", materialId + " transferring");
        }

        public void HandingOff(string materialId)
        {
            view?.Apply("blocked", materialId + " handoff");
        }

        public static string ConveyorId(int line, int stationIndex)
        {
            return "line" + line + "/" + ConveyorName(stationIndex);
        }

        private static string ConveyorName(int stationIndex)
        {
            switch (stationIndex)
            {
                case 0: return "Conveyor_AB";
                case 1: return "Conveyor_BC";
                default: return "Conveyor_CQ";
            }
        }
    }
}
