namespace Worktown.Sim.Cooking
{
    public static class CookingLimits
    {
        public const int MaxDepth = 8;
        public const int MaxComponents = 16;
        public const int MaxMixInputs = 6;
        public const int MaxPlateInputs = 4;
        /// <summary>Spec 031: the most layers on one build (base, fillings, salads, sauces).</summary>
        public const int MaxBuildLayers = 10;
    }
}
