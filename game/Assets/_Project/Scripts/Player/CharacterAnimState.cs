namespace Worktown.Game.Player
{
    /// <summary>Spec 024: the base animation a character plays (one clip each; spec 025: retargeted Humanoid clips).</summary>
    public enum CharacterAnimState : byte
    {
        Idle,
        Walk,
        Run,
        Crouch
    }
}
