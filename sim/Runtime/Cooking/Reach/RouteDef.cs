using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 056a: how one dish of the game's menu (or one drink) is made, for the offline name list. <see cref="TemplateId"/>
    /// is the <see cref="RecipeTemplates"/> id of a <see cref="RouteKind.Build"/> route, else null.
    /// </summary>
    public sealed record RouteDef(string Id, string Name, RouteKind Kind, IReadOnlyList<RouteChain> Chains, string? TemplateId);
}
