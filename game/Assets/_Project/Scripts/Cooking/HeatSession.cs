using Worktown.Sim.Cooking;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 016: one food item cooking in a pan or pot. Server only, immutable. Temperature and start
    /// tick never leave the server (D21); clients see bucket labels.
    /// </summary>
    public sealed class HeatSession
    {
        public HeatSession(Item food, byte color, string operationId, int? tempC, uint startTick)
        {
            Food = food;
            Color = color;
            OperationId = operationId;
            TempC = tempC;
            StartTick = startTick;
        }

        public Item Food { get; }
        /// <summary>Display color of the food (ingredient index), kept for the result.</summary>
        public byte Color { get; }
        /// <summary>"fry", "saute" or "boil".</summary>
        public string OperationId { get; }
        /// <summary>Pan temperature in Celsius; null for the pot (boil has a fixed temperature).</summary>
        public int? TempC { get; }
        public uint StartTick { get; }
    }
}
