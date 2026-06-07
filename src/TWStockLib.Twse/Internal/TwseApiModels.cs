namespace TWStockLib.Twse.Internal
{
    /// <summary>櫃買中心（TPEX）盤後個股日成交資訊 API 回應模型。</summary>
    internal sealed class TPEXAPIModel
    {
        public List<Table> tables { get; set; }
        public string date { get; set; }
        public string code { get; set; }
        public string name { get; set; }
        public bool showListPriceNote { get; set; }
        public bool showListPriceLink { get; set; }
        public string stat { get; set; }

        internal sealed class Table
        {
            public string title { get; set; }
            public string subtitle { get; set; }
            public string date { get; set; }
            public List<List<string>> data { get; set; }
            public List<string> fields { get; set; }
            public List<string> notes { get; set; }
            public int totalCount { get; set; }
            public List<string> summary { get; set; }
        }
    }
}
