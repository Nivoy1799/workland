namespace Worktown.Game.Save
{
    /// <summary>Spec 053: how the host's next game starts (NetBootstrap.HostStart; SaveService applies it when the server starts).</summary>
    public enum ShopStart : byte
    {
        /// <summary><c>-host</c>, <c>-server</c>, the tours and tests: every station, no save (dev and tests only).</summary>
        OpenKitchen,
        /// <summary>New shop: a new food corner with its own new save file (never overwrites one).</summary>
        NewShop,
        /// <summary>Continue: the newest save.</summary>
        Continue
    }
}
