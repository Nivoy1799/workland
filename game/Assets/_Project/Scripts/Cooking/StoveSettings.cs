namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 016 (changed by the human on 2026-11-07): the on/off switch of one pan or pot, and the pan's
    /// fry/saute mode. The pan has one fixed heat, so players never see a temperature (D21).
    /// </summary>
    public sealed class StoveSettings
    {
        /// <summary>Fixed pan heat in Celsius: the old "golden" preset, "hot" bucket ("crispy").</summary>
        public const int PanTempC = 190;

        /// <summary>New stoves start switched off.</summary>
        public bool IsOn { get; private set; }
        public bool Saute { get; private set; }

        public void Toggle()
        {
            IsOn = !IsOn;
        }

        public void ToggleMode()
        {
            Saute = !Saute;
        }

        /// <summary>The sim op this station applies: fry or saute on the pan, boil in the pot.</summary>
        public string OperationIdFor(StationKind kind)
        {
            if (kind == StationKind.Pot) return "boil";
            return Saute ? "saute" : "fry";
        }

        /// <summary>Label text: "on · fry", "off · saute" for the pan; "on" or "off" for the pot.</summary>
        public string Describe(StationKind kind)
        {
            string power = IsOn ? "on" : "off";
            return kind == StationKind.Pan ? power + " · " + (Saute ? "saute" : "fry") : power;
        }
    }
}
