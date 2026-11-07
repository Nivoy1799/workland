namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 016 (changed by the human on 2026-11-07): the on/off switch of one burner, and its fry/saute mode for a
    /// pan on it (spec 021: one per burner). The pan has one fixed heat, so players never see a temperature (D21).
    /// </summary>
    public sealed class StoveSettings
    {
        /// <summary>Fixed pan heat in Celsius: the old "golden" preset, "hot" bucket ("crispy").</summary>
        public const int PanTempC = 190;

        /// <summary>New burners start switched off.</summary>
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

        /// <summary>The sim op food in this cookware gets on this burner: boil in the pot, fry or saute in the pan.</summary>
        public string OperationIdFor(VesselKind kind)
        {
            if (kind == VesselKind.Pot) return "boil";
            return Saute ? "saute" : "fry";
        }

        /// <summary>Text: "on · fry", "off · saute".</summary>
        public string Describe()
        {
            return (IsOn ? "on" : "off") + " · " + (Saute ? "saute" : "fry");
        }
    }
}
