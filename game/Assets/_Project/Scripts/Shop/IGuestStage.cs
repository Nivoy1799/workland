namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 032 (review): what the register's flow shows of a guest, the server's presentation side (CustomerService implements it
    /// with the guests' network objects, the feed and the reward moment; tests record the calls). Nothing here moves money.
    /// </summary>
    public interface IGuestStage
    {
        /// <summary>The guest took its food and stands <paramref name="lineIndex"/> in the register's line, saying <paramref name="bubble"/>.</summary>
        void Paying(int guestId, string bubble, int lineIndex);

        /// <summary>The guest walks out with <paramref name="bubble"/> in mood <paramref name="mood"/>.</summary>
        void Leave(int guestId, string bubble, byte mood);

        /// <summary>The paying guests moved up in the line.</summary>
        void LineMoved();

        /// <summary>A line in the serve feed.</summary>
        void Feed(string line);

        /// <summary>The reward moment over the register (stars of the review, what was paid, the tip in it).</summary>
        void Reward(int stars, long paid, long tip);

        void Log(string line);
    }
}
