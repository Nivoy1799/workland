using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Worktown.Game.Player;
using Worktown.Sim.Layout;

namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062b, the owner's client: build mode. B toggles it (when the shop is closed, the stage allows it and the hands are empty; else
    /// the reason shows); B or Esc leaves (a held piece is let go). In build mode the aim (the camera's centre) targets pieces and the
    /// floor only, cooking inputs do nothing (PickupInteractor idles), and spec 041's buttons mean what BuildControlRules says. The client
    /// keeps its own ShopLayout from the pieces' poses and runs the same BuildSnap and LayoutRules as the server, so the ghost is Mint where
    /// the piece fits and Terracotta with the reason where it does not ("Tight aisle" under 1.2 m). A right click moves the piece at once
    /// (PieceBehaviour.Predict); a refusal puts it back with the "nope" sound. Every decision is the server's (BuildInteractor, BuildService).
    /// The prompt and the refusals are drawn with IMGUI (a graybox stand-in until the HUD pass).
    /// </summary>
    public sealed class BuildModeView : MonoBehaviour
    {
        private const float AimDistance = 14f;
        private const float ToastSeconds = 2.5f;

        private static BuildModeView _local;

        /// <summary>Whether the local player is in build mode now.</summary>
        public static bool LocalActive => _local != null && _local._active;

        /// <summary>The local player's build mode view (null before it spawned).</summary>
        public static BuildModeView Local => _local;

        private sealed class Pending
        {
            public byte Kind;
            public int PieceId;
        }

        private readonly Dictionary<int, Pending> _pending = new Dictionary<int, Pending>();
        private BuildInteractor _net;
        private PickupInteractor _hands;
        private PlayerInputReader _input;
        private BuildGhost _ghost;
        private BuildDesk _check;
        private bool _active;
        private int _claimWanted;
        private int _turns;
        private int _pick;
        private string _toast;
        private float _toastUntil;
        private PieceBehaviour _aimed;
        private Vector3 _aimPoint;
        private bool _aimHit;
        private long _ghostKey = long.MinValue;
        private bool _ghostFits;
        private string _ghostProblem;
        private LayoutPiece _ghostPose;

        // Dev builds: the tours' scripted presses and aim (the same intents a click sends).
        private bool _scriptedLeft, _scriptedRight;
        private int _scriptedTurn;
        private bool _scriptedAim;
        private Vector3 _scriptedPoint;

        public bool Active => _active;
        /// <summary>The piece the server says this player holds (0: none).</summary>
        public int HeldPieceId { get; private set; }
        /// <summary>The last answer's outcome (tours, logs).</summary>
        public BuildOutcome LastOutcome { get; private set; } = BuildOutcome.Ok;
        public int PendingCount => _pending.Count;
        public int AnswerCount { get; private set; }
        public string PromptText { get; private set; } = string.Empty;
        public bool GhostShown => _ghost != null && _ghost.IsShown;
        public bool GhostFits => _ghostFits;

        private void Awake()
        {
            _net = GetComponent<BuildInteractor>();
            _hands = GetComponent<PickupInteractor>();
            if (_net != null) _net.Answered += OnAnswered;
        }

        private void Start()
        {
            _local = this;
            var highlight = GetComponent<InteractionHighlight>();
            _ghost = new BuildGhost(highlight != null ? highlight.MarkerMaterial : null);
            LayoutSpawner spawner = FindFirstObjectByType<LayoutSpawner>();
            _check = new BuildDesk(spawner != null ? spawner.Catalog : PieceCatalog.CreateDefault(), spawner != null ? spawner.Room : RoomDef.CreateDefault());
        }

        private void OnDestroy()
        {
            if (_net != null) _net.Answered -= OnAnswered;
            _ghost?.Destroy();
            if (_local == this) _local = null;
        }

        private void Update()
        {
            if (_input == null) _input = FindFirstObjectByType<PlayerInputReader>();
            bool toggle = _input != null && _input.ConsumeBuildToggle();
            bool undo = _input != null && _input.ConsumeUndo();
            bool leave = _input != null && _input.ConsumeLeave();
            if (!_active)
            {
                _ghost?.Hide();
                if (toggle) TryEnter();
                return;
            }
            if (!ShopDoorRules.AllowsBuild(ShopDoor.Now))
            {
                Leave();
                Toast("The shop is open");
                return;
            }
            if (toggle || leave)
            {
                Leave();
                return;
            }
            HeldPieceId = HeldNow();
            Aim();
            bool holding = HeldPieceId != 0 || _claimWanted != 0;
            bool onPallet = StoragePallet.Is(_aimed);
            bool onPiece = _aimed != null && !onPallet;
            Mouse mouse = Mouse.current;
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            bool left = _scriptedLeft || (mouse != null && locked && mouse.leftButton.wasPressedThisFrame);
            bool right = _scriptedRight || (mouse != null && locked && mouse.rightButton.wasPressedThisFrame);
            float scroll = mouse != null ? mouse.scroll.ReadValue().y : 0f;
            int wheel = _scriptedTurn != 0 ? _scriptedTurn : scroll > 0.01f ? 1 : scroll < -0.01f ? -1 : 0;
            _scriptedLeft = _scriptedRight = false;
            _scriptedTurn = 0;

            if (undo) Do(BuildControlRules.ActionFor(BuildInput.Undo, holding, onPiece, onPallet), 0);
            if (left) Do(BuildControlRules.ActionFor(BuildInput.LeftClick, holding, onPiece, onPallet), 0);
            if (right) Do(BuildControlRules.ActionFor(BuildInput.RightClick, holding, onPiece, onPallet), 0);
            if (wheel != 0) Do(BuildControlRules.ActionFor(BuildInput.Wheel, holding, onPiece, onPallet), wheel);
            UpdateGhost();
            PromptText = BuildControlRules.Prompt(holding, onPiece ? BuildControlRules.NameOf(_aimed.TypeId) : null, onPallet, _ghostFits, _ghostProblem);
        }

        private void TryEnter()
        {
            var stage = Worktown.Game.Climb.ShopStage.Find();
            byte stageNow = stage != null ? stage.StageNow : Worktown.Game.Climb.ShopStageRules.OpenKitchen;
            bool handsEmpty = _hands == null || _hands.FindHeldPickup() == null;
            string refusal = BuildControlRules.EntryRefusal(ShopDoor.Now, stageNow, handsEmpty);
            if (refusal != null)
            {
                Toast(refusal);
                return;
            }
            _active = true;
            _turns = 0;
            Debug.Log("[Build] build mode on");
        }

        private void Leave()
        {
            if (!_active) return;
            _active = false;
            if (HeldPieceId != 0 || _claimWanted != 0) Send(BuildIntent.Release, 0, 0L);
            _claimWanted = 0;
            _ghost?.Hide();
            _ghostKey = long.MinValue;
            Debug.Log("[Build] build mode off");
        }

        private void Do(BuildAction action, int wheel)
        {
            switch (action)
            {
                case BuildAction.PickUp:
                    if (_aimed == null) return;
                    _claimWanted = _aimed.PieceId;
                    _turns = _aimed.Turns;
                    Send(BuildIntent.Claim, _aimed.PieceId, 0L);
                    break;
                case BuildAction.TakeOut:
                    Send(BuildIntent.TakeOut, 0, _pick);
                    break;
                case BuildAction.NextStored:
                    _pick = Mathf.Max(0, _pick + wheel);
                    Toast("Next out: stored piece " + (_pick + 1));
                    break;
                case BuildAction.Turn:
                    _turns = BuildSnap.Turns(_turns + (wheel > 0 ? 1 : -1));
                    _ghostKey = long.MinValue;
                    break;
                case BuildAction.Place:
                    Place();
                    break;
                case BuildAction.Store:
                    Send(BuildIntent.Store, HeldPieceId, 0L);
                    break;
                case BuildAction.Undo:
                    Send(BuildIntent.Undo, 0, 0L);
                    break;
                case BuildAction.Leave:
                    Leave();
                    break;
            }
        }

        private void Place()
        {
            PieceBehaviour held = LayoutRegistry.Piece(HeldPieceId);
            if (held == null || _ghostPose == null) return;
            int x = Mathf.RoundToInt(_aimPoint.x * 1000f), z = Mathf.RoundToInt(_aimPoint.z * 1000f);
            if (_ghostFits) held.Predict(_ghostPose.XMm, _ghostPose.ZMm, _ghostPose.Turns); // feel.md 1: it moves at the click
            Send(BuildIntent.Move, HeldPieceId, PosePacking.Pack(x, z, _turns, 0));
        }

        private int Send(byte kind, int pieceId, long pose)
        {
            if (_net == null) return 0;
            int request = _net.Send(new BuildIntent(kind, pieceId, pose));
            if (request != 0) _pending[request] = new Pending { Kind = kind, PieceId = pieceId };
            return request;
        }

        private void OnAnswered(int request, byte answer)
        {
            AnswerCount++;
            BuildOutcome outcome = BuildDesk.Unpack(answer, out LayoutProblemKind? kind);
            LastOutcome = outcome;
            _pending.TryGetValue(request, out Pending sent);
            _pending.Remove(request);
            if (sent != null && sent.Kind == BuildIntent.Claim && _claimWanted == sent.PieceId) _claimWanted = 0;
            if (outcome == BuildOutcome.Ok) return;
            if (sent != null && sent.Kind == BuildIntent.Move)
            {
                PieceBehaviour piece = LayoutRegistry.Piece(sent.PieceId);
                if (piece != null)
                {
                    piece.EndPrediction(); // it slides back (a jump in the graybox)
                    var sounds = FindFirstObjectByType<Worktown.Game.Audio.KitchenSounds>();
                    if (sounds != null) sounds.Play(Worktown.Game.Audio.SoundKey.Nope, piece.transform.position);
                }
            }
            string text = BuildControlRules.RefusalText(outcome, kind);
            if (text != null) Toast(text);
            Debug.Log("[Build] " + (sent != null ? BuildIntent.KindName(sent.Kind) : "request " + request) + " refused: " + outcome
                + (kind.HasValue ? " (" + kind.Value + ")" : string.Empty));
        }

        private int HeldNow()
        {
            byte mine = _net != null ? _net.Slot : (byte)0;
            if (mine == 0) return 0;
            foreach (PieceBehaviour p in LayoutRegistry.Pieces)
                if (p != null && p.ClaimedBySlot == mine) return p.PieceId;
            return 0;
        }

        private void Aim()
        {
            _aimed = null;
            _aimHit = false;
            if (_scriptedAim)
            {
                _aimPoint = _scriptedPoint;
                _aimHit = true;
                _aimed = PieceAt(_scriptedPoint);
                return;
            }
            Camera cam = Camera.main;
            if (cam == null) return;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int mask = Physics.DefaultRaycastLayers & ~(1 << PhysicsLayers.Player) & ~PhysicsLayers.ItemsMask;
            if (Physics.Raycast(ray, out RaycastHit hit, AimDistance, mask, QueryTriggerInteraction.Ignore))
            {
                _aimPoint = hit.point;
                _aimHit = true;
                _aimed = hit.collider != null ? hit.collider.GetComponentInParent<PieceBehaviour>() : null;
                if (_aimed != null && _aimed.PieceId == HeldPieceId) _aimed = null; // aiming through the held piece: its old place
                return;
            }
            if (Mathf.Abs(ray.direction.y) > 0.0001f)
            {
                float t = -ray.origin.y / ray.direction.y;
                if (t > 0f && t < AimDistance)
                {
                    _aimPoint = ray.origin + ray.direction * t;
                    _aimHit = true;
                }
            }
        }

        private static PieceBehaviour PieceAt(Vector3 point)
        {
            PieceCatalog catalog = PieceCatalog.CreateDefault();
            int x = Mathf.RoundToInt(point.x * 1000f), z = Mathf.RoundToInt(point.z * 1000f);
            foreach (PieceBehaviour p in LayoutRegistry.Pieces)
            {
                if (p == null || !catalog.TryGet(p.TypeId, out PieceDef def)) continue;
                if (def.Footprint(Record(p)).Contains(x, z)) return p;
            }
            return null;
        }

        private void UpdateGhost()
        {
            PieceBehaviour held = LayoutRegistry.Piece(HeldPieceId);
            if (held == null || !_aimHit)
            {
                _ghost?.Hide();
                _ghostPose = null;
                _ghostFits = false;
                _ghostProblem = null;
                return;
            }
            ShopLayout layout = ClientLayout(out bool fromStorage);
            LayoutPiece piece = Record(held);
            LayoutPiece at = BuildSnap.Pose(layout, _check.Catalog, _check.Room, piece, Mathf.RoundToInt(_aimPoint.x * 1000f),
                Mathf.RoundToInt(_aimPoint.z * 1000f), _turns);
            long key = PosePacking.Pack(at.XMm, at.ZMm, at.Turns, 0) ^ ((long)layout.Pieces.Count << 60);
            if (key != _ghostKey)
            {
                _ghostKey = key;
                _ghostPose = at;
                LayoutEdit edit = fromStorage ? LayoutEdit.TakeOut(piece.Id, at.XMm, at.ZMm, at.Turns) : LayoutEdit.Move(piece.Id, at.XMm, at.ZMm, at.Turns);
                ShopLayout before = fromStorage ? layout.With(layout.Pieces, new[] { piece.Id }, new[] { piece }) : layout;
                LayoutProblem problem = null;
                bool applied = edit.TryApply(before, out ShopLayout after, out _);
                if (applied) problem = _check.FirstNew(before, after);
                _ghostFits = applied && problem == null;
                _ghostProblem = !applied ? "Not here" : problem != null ? problem.Text : TightAisle(after, piece.Id) ? BuildControlRules.TightAisleText : null;
            }
            _ghost?.Show(held.transform, _ghostPose.XMm, _ghostPose.ZMm, _ghostPose.Turns, _ghostFits);
        }

        /// <summary>feel.md 6: the soft note under 1.2 m (the hard rule, 0.9 m, holds).</summary>
        private bool TightAisle(ShopLayout layout, int pieceId)
        {
            LayoutPiece piece = layout.Piece(pieceId);
            if (piece == null || !_check.Catalog.TryGet(piece.TypeId, out PieceDef def) || LayoutRules.Band(def.Mount) != 0) return false;
            RectMm mine = def.Footprint(piece);
            foreach (LayoutPiece other in layout.Pieces)
            {
                if (other.Id == pieceId || !_check.Catalog.TryGet(other.TypeId, out PieceDef od) || LayoutRules.Band(od.Mount) != 0) continue;
                RectMm r = od.Footprint(other);
                int gapX = System.Math.Max(r.MinX - mine.MaxX, mine.MinX - r.MaxX), gapZ = System.Math.Max(r.MinZ - mine.MaxZ, mine.MinZ - r.MaxZ);
                bool facing = (gapX < 0 && gapZ > 0) || (gapZ < 0 && gapX > 0);
                int gap = System.Math.Max(gapX, gapZ);
                if (facing && gap >= LayoutRules.AisleMm && gap < BuildControlRules.ComfortAisleMm) return true;
            }
            return false;
        }

        /// <summary>The layout as this client sees it: every spawned piece at its synced pose, except a held piece from storage (on the pallet).</summary>
        private ShopLayout ClientLayout(out bool heldFromStorage)
        {
            heldFromStorage = false;
            var pieces = new List<LayoutPiece>();
            var pallets = new List<RectMm>();
            foreach (PieceBehaviour p in LayoutRegistry.Pieces)
                if (StoragePallet.Is(p) && _check.Catalog.TryGet(p.TypeId, out PieceDef pd)) pallets.Add(pd.Footprint(Record(p)));
            foreach (PieceBehaviour p in LayoutRegistry.Pieces)
            {
                if (p == null || p.PieceId <= 0) continue;
                LayoutPiece record = Record(p);
                if (!StoragePallet.Is(p) && _check.Catalog.TryGet(p.TypeId, out PieceDef def) && pallets.Exists(r => r.Intersects(def.Footprint(record))))
                {
                    if (p.PieceId == HeldPieceId) heldFromStorage = true;
                    continue; // taken out of storage, waiting on the pallet
                }
                pieces.Add(record);
            }
            return new ShopLayout(ShopLayout.CurrentVersion, pieces, null, null, 1);
        }

        private static LayoutPiece Record(PieceBehaviour p) => new LayoutPiece(p.PieceId, p.TypeId, p.XMm, p.ZMm, p.Turns, p.LengthMm, 1, 0);

        private void Toast(string text)
        {
            _toast = text;
            _toastUntil = Time.unscaledTime + ToastSeconds;
        }

        private GUIStyle _style;

        private void OnGUI()
        {
            bool toast = _toast != null && Time.unscaledTime < _toastUntil;
            if (!_active && !toast) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                _style.normal.textColor = Worktown.Game.Art.Palette.Cream;
            }
            float w = 760f, h = 40f, x = (Screen.width - w) * 0.5f;
            if (_active) GUI.Box(new Rect(x, Screen.height - 150f, w, h), "Build mode   " + PromptText, _style);
            if (toast) GUI.Box(new Rect(x, Screen.height - 200f, w, h), _toast, _style);
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        // ---- Spec 062b: hooks for the build tour (development builds only): the same intents a click sends ----

        public bool DevEnter()
        {
            TryEnter();
            return _active;
        }

        public void DevLeave() => Leave();

        /// <summary>Aims at a world point (the floor or a piece there) instead of the camera's centre; null goes back to the camera.</summary>
        public void DevAim(Vector3? point)
        {
            _scriptedAim = point.HasValue;
            _scriptedPoint = point ?? Vector3.zero;
            _ghostKey = long.MinValue;
        }

        public void DevLeftClick() => _scriptedLeft = true;
        public void DevRightClick() => _scriptedRight = true;
        public void DevTurn(int steps) => _scriptedTurn = steps;
        public void DevUndo() => Send(BuildIntent.Undo, 0, 0L);
#endif
    }
}
