namespace Worktown.Sim.Economy
{
    public enum EconomyError
    {
        None, InvalidAccountId, AccountExists, UnknownAccount, InvalidInstanceId,
        AlreadyConsumed, NoMarket, QualityTooLow, UnknownGood, InvalidQuantity, InsufficientGoods,
        InsufficientFunds, BalanceLimit, GoodsLimit, UnknownBaseItem, WholesaleRefused
    }
}
