namespace Worktown.Sim.Clock
{
    /// <summary>A festival on a day of a season. PlaceHint is a town plan place id.</summary>
    public sealed record FestivalDef(string Id, string Name, Season Season, int Day, string PlaceHint);
}
