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
        Take,
        /// <summary>Spec 029: a fryer basket goes into or out of the oil; the snapshot's VesselCount is 1 while it is lowered.</summary>
        Basket,
        /// <summary>Spec 030: a flip on a griddle spot; the snapshot's VesselCount is the spot's flip counter (mod 4).</summary>
        Flip,
        /// <summary>Spec 031: a fold or a packaging on the assembly board; the snapshot's VesselCount is the build's close and pack bits.</summary>
        BuildStep
    }
}
