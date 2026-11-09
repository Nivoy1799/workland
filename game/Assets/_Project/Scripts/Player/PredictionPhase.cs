namespace Worktown.Game.Player
{
    /// <summary>Spec 027b: the state of a predicted visual (<see cref="FeelPrediction"/>).</summary>
    public enum PredictionPhase : byte
    {
        /// <summary>Waiting for the server's state change.</summary>
        Pending,
        /// <summary>The synced state shows the expected change: the visual stays.</summary>
        Confirmed,
        /// <summary>Refused or timed out: the visual flies back and "nope" plays.</summary>
        Denied
    }
}
