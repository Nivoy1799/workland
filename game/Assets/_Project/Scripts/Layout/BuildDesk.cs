using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Layout;

namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062b, server, pure (no network, no world): the build mode's book. It holds the layout the server decides on, who holds which
    /// piece (a claim: one per player, by the player's slot), each player's undo history (UndoHistory, the last 10) and the stored pieces
    /// taken out of the pallet. Every action is checked here against the current layout: a move is snapped (BuildSnap.Pose) and the new layout must
    /// keep every rule (LayoutRules.Validate); a required piece (or the door sign) cannot be stored; an
    /// undo applies its inverse edit only if that fits now. BuildService applies what it decides to the world (spawn, move, despawn, the
    /// items that travel) and saves. Deterministic.
    /// </summary>
    public sealed class BuildDesk
    {
        /// <summary>A claim lapses after this long without input from its player (question 7: 60 s).</summary>
        public const double ClaimTimeoutSeconds = 60.0;

        /// <summary>
        /// 062b review: types that may move but never go into storage, though the catalog does not require them: the door sign (stored, the
        /// shop could never be closed again, and build mode needs a closed shop).
        /// </summary>
        public static readonly IReadOnlyList<string> NeverStored = new[] { PieceCatalog.DoorSign };

        private sealed class Claim
        {
            public int PieceId;
            public bool FromStorage;
            public double LastInput;
        }

        private readonly PieceCatalog _catalog;
        private readonly RoomDef _room;
        private readonly Dictionary<byte, Claim> _claims = new Dictionary<byte, Claim>();
        private readonly Dictionary<byte, UndoHistory> _histories = new Dictionary<byte, UndoHistory>();

        public BuildDesk(PieceCatalog catalog, RoomDef room)
        {
            _catalog = catalog ?? PieceCatalog.CreateDefault();
            _room = room ?? RoomDef.CreateDefault();
            Layout = new ShopLayout(ShopLayout.CurrentVersion, null, null, null, 1);
        }

        /// <summary>The layout as the server decided it.</summary>
        public ShopLayout Layout { get; private set; }
        public PieceCatalog Catalog => _catalog;
        public RoomDef Room => _room;

        /// <summary>A new layout (spawned): every claim and every history is forgotten.</summary>
        public void Reset(ShopLayout layout)
        {
            Layout = layout ?? new ShopLayout(ShopLayout.CurrentVersion, null, null, null, 1);
            _claims.Clear();
            _histories.Clear();
        }

        /// <summary>The slot that holds <paramref name="pieceId"/> (0: nobody).</summary>
        public byte HolderOf(int pieceId)
        {
            foreach (KeyValuePair<byte, Claim> kv in _claims)
                if (kv.Value.PieceId == pieceId) return kv.Key;
            return 0;
        }

        /// <summary>The piece <paramref name="slot"/> holds (0: none); <paramref name="fromStorage"/>: taken out of the pallet, not placed.</summary>
        public int HeldBy(byte slot, out bool fromStorage)
        {
            fromStorage = false;
            if (!_claims.TryGetValue(slot, out Claim c)) return 0;
            fromStorage = c.FromStorage;
            return c.PieceId;
        }

        public int HistoryCount(byte slot) => _histories.TryGetValue(slot, out UndoHistory h) ? h.Count : 0;

        /// <summary>
        /// Spec 062b: the checks before any intent, in order: too fast (Busy), the shop not closed (NotClosed), the stage without build
        /// mode or something in the hands (NotBuilding); null when the intent may go on.
        /// </summary>
        public static BuildOutcome? Gate(bool tooFast, ShopDoorState door, bool stageAllows, bool handsEmpty)
        {
            if (tooFast) return BuildOutcome.Busy;
            if (!ShopDoorRules.AllowsBuild(door)) return BuildOutcome.NotClosed;
            if (!stageAllows || !handsEmpty) return BuildOutcome.NotBuilding;
            return null;
        }

        /// <summary>
        /// <paramref name="slot"/> picks up placed piece <paramref name="pieceId"/>. Refused when it is unknown, fixed, held by someone else
        /// or its station is on (<paramref name="stationOn"/>). A piece the player held before is let go first (<paramref name="released"/>).
        /// </summary>
        public BuildOutcome TryClaim(byte slot, int pieceId, bool stationOn, double now, out int released, out bool releasedFromStorage)
        {
            released = 0;
            releasedFromStorage = false;
            LayoutPiece piece = Layout.Piece(pieceId);
            if (piece == null || !_catalog.TryGet(piece.TypeId, out PieceDef def)) return BuildOutcome.Unknown;
            if (!def.Movable) return BuildOutcome.NotMovable;
            byte holder = HolderOf(pieceId);
            if (holder == slot)
            {
                _claims[slot].LastInput = now;
                return BuildOutcome.Ok;
            }
            if (holder != 0) return BuildOutcome.Claimed;
            if (stationOn) return BuildOutcome.StationOn;
            released = Release(slot, out releasedFromStorage);
            _claims[slot] = new Claim { PieceId = pieceId, FromStorage = false, LastInput = now };
            return BuildOutcome.Ok;
        }

        /// <summary>Lets go of what <paramref name="slot"/> holds; returns the piece (0: none). A piece from the pallet goes back to it.</summary>
        public int Release(byte slot, out bool fromStorage)
        {
            fromStorage = false;
            if (!_claims.TryGetValue(slot, out Claim c)) return 0;
            _claims.Remove(slot);
            fromStorage = c.FromStorage;
            return c.PieceId;
        }

        /// <summary>
        /// <paramref name="slot"/> places the piece it holds where it aims (<paramref name="aimXMm"/>, <paramref name="aimZMm"/>, with
        /// <paramref name="turns"/>): the pose is snapped (BuildSnap.Pose); the new layout must add no problem, else Invalid with the first
        /// new one in <paramref name="problem"/>. On Ok the layout changes, the inverse goes on the player's history, the claim ends and
        /// <paramref name="applied"/> is the edit done (a Move, or a TakeOut for a piece from the pallet).
        /// </summary>
        public BuildOutcome TryMove(byte slot, int aimXMm, int aimZMm, int turns, double now, out LayoutEdit applied, out LayoutProblem problem) =>
            TryMove(slot, aimXMm, aimZMm, turns, false, now, out applied, out problem);

        /// <summary>062b review: as above; <paramref name="stationOn"/>: its heat was turned on since the claim (refused: StationOn).</summary>
        public BuildOutcome TryMove(byte slot, int aimXMm, int aimZMm, int turns, bool stationOn, double now, out LayoutEdit applied,
            out LayoutProblem problem)
        {
            applied = null;
            problem = null;
            if (!_claims.TryGetValue(slot, out Claim c)) return BuildOutcome.NotHeld;
            c.LastInput = now;
            if (stationOn) return BuildOutcome.StationOn;
            LayoutPiece piece = c.FromStorage ? Layout.StoredPiece(c.PieceId) : Layout.Piece(c.PieceId);
            if (piece == null)
            {
                _claims.Remove(slot);
                return BuildOutcome.Unknown;
            }
            LayoutPiece at = BuildSnap.Pose(Layout, _catalog, _room, piece, aimXMm, aimZMm, turns);
            LayoutEdit edit = c.FromStorage ? LayoutEdit.TakeOut(piece.Id, at.XMm, at.ZMm, at.Turns) : LayoutEdit.Move(piece.Id, at.XMm, at.ZMm, at.Turns);
            BuildOutcome outcome = Apply(slot, edit, out problem);
            if (outcome != BuildOutcome.Ok) return outcome;
            _claims.Remove(slot);
            applied = edit;
            return BuildOutcome.Ok;
        }

        /// <summary>
        /// <paramref name="slot"/> puts the piece it holds on the storage pallet. A placed piece leaves the layout (refused for a required one
        /// or when its going adds a problem); a piece from the pallet just goes back (Ok, <paramref name="applied"/> null).
        /// </summary>
        public BuildOutcome TryStore(byte slot, double now, out LayoutEdit applied, out LayoutProblem problem) =>
            TryStore(slot, now, null, out applied, out problem);

        /// <summary>
        /// 062b review: as above; <paramref name="target"/> gives each item home on the stored piece its new home (a free spot on another
        /// worktop: <see cref="Rehome"/>), so the tools come back on the next load; an undo restores the old homes.
        /// </summary>
        public BuildOutcome TryStore(byte slot, double now, System.Func<ItemHome, ItemHome> target, out LayoutEdit applied, out LayoutProblem problem)
        {
            applied = null;
            problem = null;
            if (!_claims.TryGetValue(slot, out Claim c)) return BuildOutcome.NotHeld;
            c.LastInput = now;
            if (c.FromStorage)
            {
                _claims.Remove(slot);
                return BuildOutcome.Ok;
            }
            LayoutPiece piece = Layout.Piece(c.PieceId);
            if (piece == null) return BuildOutcome.Unknown;
            if ((_catalog.TryGet(piece.TypeId, out PieceDef def) && def.Required) || NeverStored.Contains(piece.TypeId)) return BuildOutcome.Required;
            LayoutEdit edit = LayoutEdit.Store(piece.Id);
            IReadOnlyList<ItemHome> homes = target != null ? Rehome(Layout, piece.Id, target) : null;
            if (homes != null) edit = edit with { Homes = homes };
            BuildOutcome outcome = Apply(slot, edit, out problem);
            if (outcome != BuildOutcome.Ok) return outcome;
            _claims.Remove(slot);
            applied = edit;
            return BuildOutcome.Ok;
        }

        /// <summary>
        /// <paramref name="slot"/> takes a stored piece from the pallet into its hands: the <paramref name="pick"/>-th (wrapping) of the stored
        /// pieces with a record that nobody holds. The layout does not change until it is placed (TryMove). <paramref name="pieceId"/>: the one.
        /// </summary>
        public BuildOutcome TryTakeOut(byte slot, int pick, double now, out int pieceId, out int released, out bool releasedFromStorage)
        {
            pieceId = 0;
            released = 0;
            releasedFromStorage = false;
            List<LayoutPiece> free = Layout.StoredPieces
                .Where(p => HolderOf(p.Id) == 0 && _catalog.TryGet(p.TypeId, out PieceDef d) && d.Movable).ToList(); // 062b review: movable only
            if (free.Count == 0) return BuildOutcome.NothingStored;
            LayoutPiece chosen = free[((pick % free.Count) + free.Count) % free.Count];
            released = Release(slot, out releasedFromStorage);
            _claims[slot] = new Claim { PieceId = chosen.Id, FromStorage = true, LastInput = now };
            pieceId = chosen.Id;
            return BuildOutcome.Ok;
        }

        /// <summary>
        /// <paramref name="slot"/> undoes its last action: its inverse edit, when it fits now (else Blocked; it stays on the history for a
        /// later try) and nobody else holds the piece (Claimed). The player's own claim on that piece is let go first.
        /// </summary>
        public BuildOutcome TryUndo(byte slot, double now, out LayoutEdit applied, out LayoutProblem problem, out int released, out bool releasedFromStorage) =>
            TryUndo(slot, now, null, out applied, out problem, out released, out releasedFromStorage);

        /// <summary>The edit an undo of <paramref name="slot"/> would apply (null: nothing to undo).</summary>
        public LayoutEdit PeekUndo(byte slot) => _histories.TryGetValue(slot, out UndoHistory h) && h.TryPeek(out LayoutEdit e) ? e : null;

        /// <summary>062b review: as above; <paramref name="stationOn"/> says whether a piece's heat is on (an undo never moves a hot piece: StationOn).</summary>
        public BuildOutcome TryUndo(byte slot, double now, System.Func<int, bool> stationOn, out LayoutEdit applied, out LayoutProblem problem,
            out int released, out bool releasedFromStorage)
        {
            applied = null;
            problem = null;
            released = 0;
            releasedFromStorage = false;
            if (!_histories.TryGetValue(slot, out UndoHistory history) || !history.TryPeek(out LayoutEdit inverse)) return BuildOutcome.NothingToUndo;
            byte holder = HolderOf(inverse.PieceId);
            if (holder != 0 && holder != slot) return BuildOutcome.Claimed;
            if (stationOn != null && Layout.Piece(inverse.PieceId) != null && stationOn(inverse.PieceId)) return BuildOutcome.StationOn;
            if (!inverse.TryApply(Layout, out ShopLayout result, out _)) return BuildOutcome.Blocked;
            problem = FirstProblem(result);
            if (problem != null) return BuildOutcome.Blocked;
            if (holder == slot) released = Release(slot, out releasedFromStorage);
            history.TryPop(out _);
            Layout = result;
            applied = inverse;
            return BuildOutcome.Ok;
        }

        /// <summary>Records input from <paramref name="slot"/> (its claim does not lapse).</summary>
        public void Touch(byte slot, double now)
        {
            if (_claims.TryGetValue(slot, out Claim c)) c.LastInput = now;
        }

        /// <summary>The slots whose claim has had no input for <see cref="ClaimTimeoutSeconds"/> (ascending).</summary>
        public List<byte> Lapsed(double now)
        {
            var slots = new List<byte>();
            foreach (KeyValuePair<byte, Claim> kv in _claims)
                if (now - kv.Value.LastInput >= ClaimTimeoutSeconds) slots.Add(kv.Key);
            slots.Sort();
            return slots;
        }

        /// <summary>Every slot that holds a piece (ascending).</summary>
        public List<byte> Holders()
        {
            var slots = new List<byte>(_claims.Keys);
            slots.Sort();
            return slots;
        }

        /// <summary>Question 4: the histories are cleared when the shop opens.</summary>
        public void ClearHistories() => _histories.Clear();

        /// <summary>
        /// 062b review: a player left: its claim ends (returned: the piece, and whether it came from storage) and its history goes (a later
        /// player may get the same slot).
        /// </summary>
        public int ForgetSlot(byte slot, out bool fromStorage)
        {
            _histories.Remove(slot);
            return Release(slot, out fromStorage);
        }

        /// <summary>
        /// 062b review: the item homes of <paramref name="layout"/> after <paramref name="pieceId"/> is stored: each home on it becomes
        /// <paramref name="target"/>'s answer (a home on another piece; null keeps it); null when no home lies on the piece.
        /// </summary>
        public static IReadOnlyList<ItemHome> Rehome(ShopLayout layout, int pieceId, System.Func<ItemHome, ItemHome> target)
        {
            if (layout == null || target == null || !layout.Homes.Any(h => h.PieceId == pieceId)) return null;
            var homes = new List<ItemHome>();
            foreach (ItemHome h in layout.Homes)
                homes.Add(h.PieceId == pieceId ? target(h) ?? h : h);
            return homes.AsReadOnly();
        }

        private BuildOutcome Apply(byte slot, LayoutEdit edit, out LayoutProblem problem)
        {
            problem = null;
            if (!edit.TryApply(Layout, out ShopLayout result, out LayoutEdit inverse)) return BuildOutcome.Unknown;
            problem = FirstProblem(result);
            if (problem != null) return BuildOutcome.Invalid;
            Layout = result;
            if (!_histories.TryGetValue(slot, out UndoHistory history)) _histories[slot] = history = new UndoHistory();
            history.Push(inverse);
            return BuildOutcome.Ok;
        }

        /// <summary>The first problem of <paramref name="layout"/> (LayoutRules.Validate), or null when it keeps every rule.</summary>
        public LayoutProblem FirstProblem(ShopLayout layout)
        {
            LayoutReport report = LayoutRules.Validate(layout, _room, _catalog);
            return report.IsValid ? null : report.Problems[0];
        }

        /// <summary>The answer byte: the outcome in the low 4 bits, a refused move's problem kind + 1 in the high 4 (0: none).</summary>
        public static byte Pack(BuildOutcome outcome, LayoutProblem problem)
        {
            int kind = problem != null ? System.Math.Min(14, (int)problem.Kind) + 1 : 0;
            return (byte)(((int)outcome & 0x0F) | (kind << 4));
        }

        public static BuildOutcome Unpack(byte answer, out LayoutProblemKind? kind)
        {
            int k = answer >> 4;
            kind = k == 0 ? (LayoutProblemKind?)null : (LayoutProblemKind)(k - 1);
            return (BuildOutcome)(answer & 0x0F);
        }
    }
}
