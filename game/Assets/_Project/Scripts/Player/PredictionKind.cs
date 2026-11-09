namespace Worktown.Game.Player
{
    /// <summary>Spec 027b: what a click predicts for an item (the look only; the server decides).</summary>
    public enum PredictionKind : byte
    {
        PickUp,
        Place,
        Drop,
        PutInto
    }
}
