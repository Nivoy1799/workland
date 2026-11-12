namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062b: what a player asks in build mode (BuildInteractor.RequestBuild): <see cref="Kind"/> (<see cref="Claim"/> a piece,
    /// <see cref="Release"/> it, <see cref="Move"/> the held piece, <see cref="Store"/> it, <see cref="TakeOut"/> a stored one,
    /// <see cref="Undo"/>), the piece (<see cref="PieceId"/>: Claim only) and <see cref="Pose"/>: for Move the aim point and the turns
    /// (PosePacking, the length unused: the server snaps it itself, BuildSnap), for TakeOut the wheel's pick among the stored pieces in its
    /// low bits. An intent only: the server checks and decides everything (BuildService).
    /// </summary>
    public struct BuildIntent
    {
        public const byte Claim = 0;
        public const byte Release = 1;
        public const byte Move = 2;
        public const byte Store = 3;
        public const byte TakeOut = 4;
        public const byte Undo = 5;

        public byte Kind;
        public int PieceId;
        public long Pose;

        public BuildIntent(byte kind, int pieceId, long pose)
        {
            Kind = kind;
            PieceId = pieceId;
            Pose = pose;
        }

        public override string ToString() => KindName(Kind) + (PieceId != 0 ? " " + PieceId : string.Empty);

        public static string KindName(byte kind)
        {
            switch (kind)
            {
                case Claim: return "claim";
                case Release: return "release";
                case Move: return "move";
                case Store: return "store";
                case TakeOut: return "take out";
                case Undo: return "undo";
                default: return "kind " + kind;
            }
        }
    }
}
