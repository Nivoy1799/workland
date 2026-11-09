namespace Worktown.Game.Player
{
    /// <summary>Spec 027b review: which server answer arrived (the reports the server already sends; no new RPCs).</summary>
    public enum AnswerKind : byte
    {
        /// <summary>ReportResult for a pick-up request.</summary>
        Pickup,
        /// <summary>ReportResult for a drop request.</summary>
        Drop,
        /// <summary>ReportPlace.</summary>
        Place,
        /// <summary>ReportCook: a station request or a use of cookware (put into, pour, take, a knife stroke).</summary>
        Cook
    }
}
