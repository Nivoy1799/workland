using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;

namespace Worktown.Game.Climb
{
    /// <summary>
    /// Spec 053: the shop's stage of the D34 climb and its goal, as every client sees them (a global scene object, like the stock counts):
    /// <see cref="Stage"/> (0 the open kitchen, 1 the food corner), <see cref="GoalMoney"/> (the rent goal) and <see cref="GoalReached"/>.
    /// No RPC. The server sets them when a game starts (SaveService) and runs stage 1's two service moments here, through the pure
    /// <see cref="FoodCornerRules"/>: at a service start the supplier's fixed drop fills the potato slot (paid once from the shop wallet
    /// at the bulk price, only what the money covers); at a service end the pitch fee is paid (the wallet clamped at 0), then the goal
    /// is checked and flips once. ClosedTargets.IsClosed is the one test of a closed station, slot or cookware that the server's
    /// refusal and the client's prompt share.
    /// </summary>
    public sealed class ShopStage : NetworkBehaviour
    {
        public readonly SyncVar<byte> Stage = new SyncVar<byte>(ShopStageRules.OpenKitchen);
        public readonly SyncVar<long> GoalMoney = new SyncVar<long>(0L);
        public readonly SyncVar<bool> GoalReached = new SyncVar<bool>(false);

        [SerializeField] private EconomyService _economy;
        [SerializeField] private Worktown.Game.Shop.SupplyService _supply;
        [SerializeField] private ServeFeed _feed;

        // Server: the game's values (kept while the object is not spawned yet; pushed to the synced fields when it is).
        private byte _stage = ShopStageRules.OpenKitchen;
        private long _goal;
        private long _pitchFee;
        private bool _reached;

        /// <summary>Server: the stage of the running game.</summary>
        public byte ServerStage => _stage;
        public long ServerGoal => _goal;
        public bool ServerReached => _reached;
        public long PitchFee => _pitchFee;

        /// <summary>The stage this process sees now: the server's value, a client's synced one, else the open kitchen.</summary>
        public byte StageNow => IsServerInitialized ? _stage : IsClientInitialized ? Stage.Value : _stage;

        public static ShopStage Find() => FindFirstObjectByType<ShopStage>();

        /// <summary>Server: a game starts at <paramref name="stage"/> with its goal, pitch fee and whether the goal was reached already.</summary>
        public void ServerBegin(byte stage, long goal, long pitchFee, bool reached)
        {
            _stage = stage;
            _goal = goal;
            _pitchFee = pitchFee;
            _reached = reached;
            Push();
            Debug.Log("[Stage] " + (stage == ShopStageRules.OpenKitchen ? "the open kitchen (every station)"
                : "stage " + stage + ", the food corner: goal " + EconomyRules.Money(goal) + (reached ? " (reached)" : string.Empty)
                  + ", pitch fee " + EconomyRules.Money(pitchFee)));
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            Push();
        }

        private void Push()
        {
            if (!IsServerInitialized) return;
            if (Stage.Value != _stage) Stage.Value = _stage;
            if (GoalMoney.Value != _goal) GoalMoney.Value = _goal;
            if (GoalReached.Value != _reached) GoalReached.Value = _reached;
        }

        /// <summary>Server, a service opens: stage 1's fixed drop fills the potato slot. Returns the units dropped.</summary>
        public int ServerServiceOpened()
        {
            if (_stage != ShopStageRules.FoodCorner || _supply == null) return 0;
            int units = _supply.DropStock(FoodCornerRules.StockGood, out long cost);
            string text = FoodCornerRules.DeliveryText(units, cost); // follow-up 2026-11-11 evening: "Potatoes delivered -$96"
            Debug.Log("[Stage] " + text);
            if (_feed != null) _feed.ServerAdd(text);
            return units;
        }

        /// <summary>
        /// Server, a service ended (after its summary numbers): stage 1 pays the pitch fee, then the goal flips once when the balance
        /// reached it. True when the goal was reached just now.
        /// </summary>
        public bool ServerServiceEnded()
        {
            if (_stage != ShopStageRules.FoodCorner || _economy == null) return false;
            if (_pitchFee > 0)
            {
                long paid = _economy.PayFee(_pitchFee);
                string fee = "Pitch fee for the corner: " + EconomyRules.Money(paid);
                Debug.Log("[Stage] " + fee + ", the shop has " + EconomyRules.Money(_economy.ShopBalance));
                if (_feed != null) _feed.ServerAdd(fee);
            }
            if (!FoodCornerRules.ReachesGoal(_reached, _economy.ShopBalance, _goal)) return false;
            _reached = true;
            Push();
            Debug.Log("[Stage] " + FoodCornerRules.GoalReachedText);
            if (_feed != null) _feed.ServerAdd(FoodCornerRules.GoalReachedText);
            return true;
        }
    }
}
