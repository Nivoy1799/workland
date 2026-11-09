namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 031 appended Portion (a measured squeeze of sauce), Assemble (ordered layers of a build), Close (fold, wrap in paper)
    /// and Pack (box, bag). Item hashes use the op id, never this number.
    /// </summary>
    public enum OperationKind { Cut, Heat, Mix, Plate, Portion, Assemble, Close, Pack }
}
