using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 019/021: the stove's synced state for its knob and button lights: one byte, two bits per burner (on,
    /// saute), see <see cref="StationRules.BurnerFlags"/>. Display only; the server decides. Spec 021: the contents of
    /// the board, pan, pot, plate and bowl moved to the carried cookware (<see cref="Vessel"/>).
    /// </summary>
    public sealed class StationContents : NetworkBehaviour
    {
        public readonly SyncVar<byte> StoveFlags = new SyncVar<byte>(0);

        /// <summary>Server sets the stove byte; only sends a change.</summary>
        internal void ServerSetStoveFlags(byte flags)
        {
            if (StoveFlags.Value != flags) StoveFlags.Value = flags;
        }
    }
}
