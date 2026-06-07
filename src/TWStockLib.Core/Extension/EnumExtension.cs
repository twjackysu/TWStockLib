using TWStockLib.Models;
using System.Reflection;

namespace TWStockLib.EnumExtension
{
    public static class EnumExtension
    {
        public static string ToKey(this Enum @enum, string stockNo)
        {
            Type enumType = @enum.GetType();
            MemberInfo[] memberInfo = enumType.GetMember(@enum.ToString());

            var attribute = memberInfo.FirstOrDefault()?
                .GetCustomAttributes(typeof(MarketKeyAttribute), false)
                .FirstOrDefault() as MarketKeyAttribute;

            return attribute?.Key.Replace("{StockNo}", stockNo);
        }
    }
}
