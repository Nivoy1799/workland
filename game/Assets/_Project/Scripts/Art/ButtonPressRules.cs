using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 044 (027b feels off): a pressed station button sinks into the station and springs back. Pure: the depth over time and
    /// which button a change of the station's flags byte belongs to, so every viewer sees the press of the button that did it.
    /// </summary>
    public static class ButtonPressRules
    {
        /// <summary>m: how far a button sinks (its cube is 6 cm deep).</summary>
        public const float SinkDepth = 0.012f;
        /// <summary>s: down (fast, like a finger), held, then back up (slower, a spring).</summary>
        public const float DownSeconds = 0.04f;
        public const float HoldSeconds = 0.05f;
        public const float UpSeconds = 0.12f;
        public const float TotalSeconds = DownSeconds + HoldSeconds + UpSeconds;
        /// <summary>s: after our own press, a flags change of the same button within this time is that press (no second sink).</summary>
        public const float OwnPressWindow = 1f;

        /// <summary>Depth in metres <paramref name="t"/> s after the press: 0 before and after, <see cref="SinkDepth"/> while held.</summary>
        public static float Depth(float t)
        {
            if (float.IsNaN(t) || t <= 0f || t >= TotalSeconds) return 0f;
            if (t < DownSeconds) return SinkDepth * (t / DownSeconds);
            if (t < DownSeconds + HoldSeconds) return SinkDepth;
            float u = (t - DownSeconds - HoldSeconds) / UpSeconds;
            float ease = 1f - (1f - u) * (1f - u); // ease out: quick at first, settling at the top
            return SinkDepth * (1f - ease);
        }

        /// <summary>
        /// Whether the station's flags going from <paramref name="before"/> to <paramref name="after"/> is a press of the button with
        /// <paramref name="option"/> on a <paramref name="kind"/> station: the stove's switch (its burner's on bit) or mode button (its
        /// saute bit), the fryer's power (on) or basket button (that basket lowered), the griddle's power, the spit's knob (its level).
        /// </summary>
        public static bool Pressed(StationKind kind, int option, byte before, byte after)
        {
            if (before == after) return false;
            switch (kind)
            {
                case StationKind.Stove:
                    if (!StationRules.TryParseStoveOption(option, out int burner, out int button)) return false;
                    return button == StationRules.StoveSwitch
                        ? StationRules.IsBurnerOn(before, burner) != StationRules.IsBurnerOn(after, burner)
                        : StationRules.IsBurnerSaute(before, burner) != StationRules.IsBurnerSaute(after, burner);
                case StationKind.Fryer:
                    if (option == FryerRules.PowerOption) return FryerRules.IsOn(before) != FryerRules.IsOn(after);
                    return FryerRules.TryParseBasketOption(option, out int basket)
                        && FryerRules.IsLowered(before, basket) != FryerRules.IsLowered(after, basket);
                case StationKind.Griddle:
                    return option == GriddleRules.PowerOption && GriddleRules.IsOn(before) != GriddleRules.IsOn(after);
                case StationKind.Spit:
                    return option == SpitRules.KnobOption && SpitRules.LevelOf(before) != SpitRules.LevelOf(after);
                default:
                    return false;
            }
        }
    }
}
