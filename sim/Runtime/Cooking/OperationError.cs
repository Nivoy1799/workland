namespace Worktown.Sim.Cooking
{
    public enum OperationError
    {
        None, UnknownBaseItem, UnknownOperation, InvalidParam, ArityViolation,
        ForbiddenInput, DepthLimit, ComponentLimit,
        /// <summary>Spec 031: a close or pack step out of order (fold a wrap before it goes in paper or a bag; only bread folds).</summary>
        StepOrder
    }
}
