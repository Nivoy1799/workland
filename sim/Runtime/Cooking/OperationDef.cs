using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Conventions: Cut needs a Choice param "shape". Heat needs a Number param "time" (seconds) and either a
    /// Number param "temp" (Celsius) or Heat.FixedTemperatureC.
    /// </summary>
    public sealed record OperationDef(
        string Id, string Station, OperationKind Kind, bool Commutative,
        int MinInputs, int MaxInputs, IReadOnlyList<ParamDef> Params, HeatProfile? Heat,
        IReadOnlyList<string> ForbiddenInputTags, IReadOnlyList<string> AddedTags, string NameTemplate);
}
