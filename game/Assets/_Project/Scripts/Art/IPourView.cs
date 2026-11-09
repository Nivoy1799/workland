namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 033: a dispenser the owner holds to pour (the 031 sauce dispenser, a drink tap): its look follows the button at once (on
    /// at the press, off at the release), before the server answers; others see it from the station's synced byte.
    /// </summary>
    public interface IPourView
    {
        void Predict(bool on);
    }
}
