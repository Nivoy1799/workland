namespace Worktown.Game.Player
{
    /// <summary>Spec 027b: what a click predicts for an item (the look only; the server decides).</summary>
    public enum PredictionKind : byte
    {
        PickUp,
        Place,
        Drop,
        PutInto,
        /// <summary>Spec 028b: take an ingredient from a stock slot; the snapshot is your hands (an item appears in them).</summary>
        Take
    }
}
