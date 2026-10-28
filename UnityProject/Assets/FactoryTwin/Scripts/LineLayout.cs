namespace FactoryTwin
{
    /// <summary>
    /// Shared workshop geometry used by the production orchestrator and the
    /// resource controllers. Keeps the layout constants in one place.
    /// </summary>
    public static class LineLayout
    {
        public static float LineCenterZ(int line) => 5f + (line - 1) * 10f;
        public static float MachineX(int stationIndex) => 4f + stationIndex * 4.75f;
        public static float ConveyorX(int stationIndex) => 6.38f + stationIndex * 4.75f;
    }
}
