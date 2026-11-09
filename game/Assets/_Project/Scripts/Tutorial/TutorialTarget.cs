namespace Worktown.Game.Tutorial
{
    /// <summary>
    /// Spec 038: what the tutorial points at for a step (the Mustard pulse and the edge arrow). The runner finds the scene object for
    /// each value (<see cref="TutorialTargets"/>); <see cref="None"/> points at nothing.
    /// </summary>
    public enum TutorialTarget : byte
    {
        None,
        PotatoSlot,
        CuttingBoard,
        Knife,
        FryerPower,
        FryerBasket,
        FryerButton,
        Plate,
        WrapSlot,
        FalafelSlot,
        LettuceSlot,
        AssemblyBoard,
        Sauce,
        WrapPaper,
        Counter,
        Register,
        CupStack,
        LemonadeTap,
        LidStack,
        DishReturn,
        Sink,
        Terminal,
        SpitKnob,
        SpitCone
    }
}
