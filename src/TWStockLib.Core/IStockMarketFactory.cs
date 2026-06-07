using TWStockLib.Observer;
using TWStockLib.Strategy;

namespace TWStockLib.Factory
{
    public interface IStockMarketFactory
    {
        IDataFetchStrategy CreateDataFetchStrategy();
    }
} 