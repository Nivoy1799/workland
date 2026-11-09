using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 032: the register's synced state for every client: the head payer (<see cref="Payment"/>, one short:
    /// <see cref="RegisterRules.PackPayment"/>) and the change built so far (<see cref="Tray"/>, one byte, at most
    /// CashRules.MaxTray). Two small presentation fields, no RPC: why they exist is that the register display, the tray's coins
    /// and the prompts must show the payment to the cook and to everyone watching, and nothing else carries it. The server decides
    /// everything (RegisterDesk); the money itself never leaves the server.
    /// </summary>
    public sealed class Register : NetworkBehaviour
    {
        public readonly SyncVar<ushort> Payment = new SyncVar<ushort>(RegisterRules.NoPayer);
        public readonly SyncVar<byte> Tray = new SyncVar<byte>(0);

        /// <summary>Server: the head payer and its tray; only changes are sent.</summary>
        internal void ServerSet(ushort payment, int tray)
        {
            byte t = (byte)(tray < 0 ? 0 : tray > 255 ? 255 : tray);
            if (Payment.Value != payment) Payment.Value = payment;
            if (Tray.Value != t) Tray.Value = t;
        }
    }
}
