namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 024: the base animation a character plays. Spec 025: retargeted Humanoid clips; walk and run both play the
    /// phase-synced walk / jog blend (<see cref="CharacterAnimRules.Locomotion"/>), crouch stands, crouch walk moves.
    /// </summary>
    public enum CharacterAnimState : byte
    {
        Idle,
        Walk,
        Run,
        Crouch,
        CrouchWalk
    }
}
