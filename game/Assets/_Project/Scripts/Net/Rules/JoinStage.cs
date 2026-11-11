namespace Worktown.Game.Net.Rules
{
    /// <summary>Spec 047: the steps of the loading screen, in order (JoinFlow.Progress).</summary>
    public enum JoinStage
    {
        /// <summary>The transport is reaching the host.</summary>
        Connecting,
        /// <summary>Connected: the join request (the build hash) is with the host.</summary>
        CheckingVersion,
        /// <summary>Admitted: the shop loads and the own player spawns.</summary>
        LoadingShop,
        /// <summary>The own player is in the shop.</summary>
        Ready
    }
}
