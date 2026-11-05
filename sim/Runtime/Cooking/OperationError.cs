namespace Worktown.Sim.Cooking
{
    public enum OperationError
    {
        None, UnknownBaseItem, UnknownOperation, InvalidParam, ArityViolation,
        ForbiddenInput, DepthLimit, ComponentLimit
    }
}
