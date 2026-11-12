namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 054b (D48): how players meet each other. The host decides; it is the session's one mode byte
    /// (<c>SessionPhysics.PlayerCollision</c>), so the server and every owner's prediction use the same rule. The world, stations
    /// and NPCs are rigid in every mode. Release builds always use <see cref="Soft"/>; <c>-player-collision</c> is the dev A/B.
    /// </summary>
    public enum PlayerCollisionMode : byte
    {
        /// <summary>Players walk through each other (the 053 review's pass-through).</summary>
        Off = 0,
        /// <summary>Rigid capsules (the physics layer matrix, Player against Player): spec 054's 224 corrections per minute.</summary>
        Hard = 1,
        /// <summary>The default: no rigid contact, <see cref="PlayerSeparation"/> pushes overlapping players apart.</summary>
        Soft = 2
    }
}
