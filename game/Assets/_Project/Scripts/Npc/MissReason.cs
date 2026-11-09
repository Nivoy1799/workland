namespace Worktown.Game.Npc
{
    /// <summary>Spec 032: why a guest left without its order (the shift summary counts each).</summary>
    public enum MissReason : byte
    {
        /// <summary>Its patience ran out in the queue.</summary>
        Patience = 0,
        /// <summary>The town's demand for its market went (the spec 014 reconcile): it changed its mind.</summary>
        Gone = 1,
        /// <summary>The kitchen closed at 14:00 while it waited.</summary>
        Closed = 2,
    }
}
