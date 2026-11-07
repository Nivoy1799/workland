using System.Collections.Generic;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Cooking
{
    /// <summary>One sim operation with fixed parameters, chosen by a station option key.</summary>
    public sealed class StationAction
    {
        public StationAction(string operationId, IReadOnlyDictionary<string, ParamValue> parameters, string label)
        {
            OperationId = operationId;
            Parameters = parameters;
            Label = label;
        }

        public string OperationId { get; }
        public IReadOnlyDictionary<string, ParamValue> Parameters { get; }
        /// <summary>Short text for the prompt, e.g. "golden".</summary>
        public string Label { get; }
    }
}
