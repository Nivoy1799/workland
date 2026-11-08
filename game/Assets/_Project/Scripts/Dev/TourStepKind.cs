namespace Worktown.Game.Dev
{
    /// <summary>Spec 027: what one tour step does (see <see cref="TourStep"/>).</summary>
    public enum TourStepKind
    {
        /// <summary>Walk (sprint, crouch) to a point through the move intent.</summary>
        WalkTo,
        /// <summary>Turn the camera to an absolute yaw and pitch (degrees; pitch up positive).</summary>
        Look,
        /// <summary>Turn the camera towards a world point.</summary>
        LookAt,
        /// <summary>Switch to first or third person.</summary>
        View,
        /// <summary>Stand still for some seconds.</summary>
        Wait,
        /// <summary>One screenshot.</summary>
        Shot,
        /// <summary>A frame sequence; it runs alongside the steps after it.</summary>
        Frames,
        /// <summary>A named action from the runner (pick up the pan, the cookware sequence, ...).</summary>
        Act,
        /// <summary>Wait until the observer's player stands at its spot.</summary>
        WaitObserver
    }
}
