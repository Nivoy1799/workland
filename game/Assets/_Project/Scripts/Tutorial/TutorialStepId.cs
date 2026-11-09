namespace Worktown.Game.Tutorial
{
    /// <summary>Spec 038: the steps of the default first shift, in order (the card shows "n / 14").</summary>
    public enum TutorialStepId : byte
    {
        MoveAndLook,
        TakePotato,
        CutSticks,
        FrySticks,
        PlateFries,
        BuildWrap,
        FoldAndWrap,
        ServeGuest,
        TakePayment,
        FillLemonade,
        WashPlate,
        OrderStock,
        ShaveSpit,
        Done
    }
}
