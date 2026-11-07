namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 016: the heat knob and fry/saute mode of one pan. Changed only while the pan is empty.
    /// Players see a knob level (1 to 11) and the mode, not degrees (D21).
    /// </summary>
    public sealed class PanSettings
    {
        public const int MinTempC = 120;
        public const int MaxTempC = 220;
        public const int StepC = 10;
        public const int StartTempC = 170;
        public const int Levels = (MaxTempC - MinTempC) / StepC + 1;

        public int TempC { get; private set; } = StartTempC;
        public bool Saute { get; private set; }

        public string OperationId => Saute ? "saute" : "fry";

        /// <summary>Knob position 1..<see cref="Levels"/>.</summary>
        public int Level => (TempC - MinTempC) / StepC + 1;

        /// <summary>One knob step down (-1) or up (+1). Stops at the ends; false if nothing changed.</summary>
        public bool TryStep(int direction)
        {
            int next = TempC + (direction < 0 ? -StepC : StepC);
            if (next < MinTempC || next > MaxTempC) return false;
            TempC = next;
            return true;
        }

        public void ToggleMode()
        {
            Saute = !Saute;
        }

        /// <summary>Label text, e.g. "heat 6/11 · fry".</summary>
        public string Describe()
        {
            return "heat " + Level + "/" + Levels + " · " + (Saute ? "saute" : "fry");
        }
    }
}
