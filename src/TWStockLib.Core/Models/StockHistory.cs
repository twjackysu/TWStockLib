using System.Diagnostics;

namespace TWStockLib.Models
{
    public class StockHistory
    {
        public StockHistory(List<string> data)
        {
            try
            {
                if (data == null || data.Count < 9)
                {
                    SetDefaultValues();
                    return;
                }
                
                // 處理日期格式
                var dateStr = data[0]?.Replace(",", "").Replace("＊", "").Trim() ?? "";
                List<int> date;
                
                // 處理不同的日期格式
                if (dateStr.Contains('/'))
                {
                    try
                    {
                        // 處理 "112/11/01" 格式
                        date = dateStr.Split('/').Select(x => Convert.ToInt32(x)).ToList();
                        if (date.Count >= 3)
                        {
                            // 如果第一個數字小於 200，假設是民國年
                            if (date[0] < 200)
                            {
                                Date = new DateTime(date[0] + 1911, date[1], date[2]);
                            }
                            else
                            {
                                Date = new DateTime(date[0], date[1], date[2]);
                            }
                        }
                        else
                        {
                            Date = DateTime.Now;
                        }
                    }
                    catch
                    {
                        Date = DateTime.Now;
                    }
                }
                else if (dateStr.Contains('-'))
                {
                    try
                    {
                        // 處理 "2023-11-01" 格式
                        date = dateStr.Split('-').Select(x => Convert.ToInt32(x)).ToList();
                        if (date.Count >= 3)
                        {
                            Date = new DateTime(date[0], date[1], date[2]);
                        }
                        else
                        {
                            Date = DateTime.Now;
                        }
                    }
                    catch
                    {
                        Date = DateTime.Now;
                    }
                }
                else
                {
                    // 默認格式
                    Date = DateTime.Now;
                }
                
                // 處理數值
                TradeVolume = ParseUInt32(data.Count > 1 ? data[1] : null);
                TurnOverInValue = ParseDecimal(data.Count > 2 ? data[2] : null);
                OpeningPrice = ParseDecimal(data.Count > 3 ? data[3] : null);
                HighestPrice = ParseDecimal(data.Count > 4 ? data[4] : null);
                LowestPrice = ParseDecimal(data.Count > 5 ? data[5] : null);
                ClosingPrice = ParseDecimal(data.Count > 6 ? data[6] : null);
                DailyPricing = data.Count > 7 ? data[7] : "0";
                NumberOfDeals = ParseUInt32(data.Count > 8 ? data[8] : null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing stock history: {ex.Message}");
                SetDefaultValues();
            }
        }
        
        private void SetDefaultValues()
        {
            Date = DateTime.Now;
            TradeVolume = 0;
            TurnOverInValue = 0;
            OpeningPrice = 0;
            HighestPrice = 0;
            LowestPrice = 0;
            ClosingPrice = 0;
            DailyPricing = "0";
            NumberOfDeals = 0;
        }
        
        private uint ParseUInt32(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "--" || value == "-")
                return 0;
                
            try
            {
                return Convert.ToUInt32(value.Replace(",", ""));
            }
            catch
            {
                return 0;
            }
        }
        
        private decimal ParseDecimal(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "--" || value == "-")
                return 0;
                
            try
            {
                return Convert.ToDecimal(value.Replace(",", ""));
            }
            catch
            {
                return 0;
            }
        }
        
        /// <summary>日期</summary>
        public DateTime Date { get; set; }
        /// <summary>成交股數</summary>
        public uint TradeVolume { get; set; }
        /// <summary>成交金額</summary>
        public decimal TurnOverInValue { get; set; }
        /// <summary>開盤價</summary>
        public decimal OpeningPrice { get; set; }
        /// <summary>最高價</summary>
        public decimal HighestPrice { get; set; }
        /// <summary>最低價</summary>
        public decimal LowestPrice { get; set; }
        /// <summary>收盤價</summary>
        public decimal ClosingPrice { get; set; }
        /// <summary>漲跌價差</summary>
        public string DailyPricing { get; set; }
        /// <summary>成交筆數</summary>
        public uint NumberOfDeals { get; set; }
    }
} 