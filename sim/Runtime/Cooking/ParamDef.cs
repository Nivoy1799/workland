using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    public sealed record ParamDef(string Name, ParamKind Kind, float Min, float Max, IReadOnlyList<BucketDef> Buckets);
}
