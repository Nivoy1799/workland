namespace Worktown.Sim.Cooking
{
    /// <summary>A player-supplied parameter: a number or the id of a choice bucket.</summary>
    public readonly struct ParamValue
    {
        private ParamValue(ParamKind kind, float number, string? choiceId)
        {
            Kind = kind;
            NumberValue = number;
            ChoiceId = choiceId;
        }

        public ParamKind Kind { get; }
        public float NumberValue { get; }
        public string? ChoiceId { get; }

        public static ParamValue Number(float v) => new ParamValue(ParamKind.Number, v, null);
        public static ParamValue Choice(string id) => new ParamValue(ParamKind.Choice, 0f, id);
    }
}
