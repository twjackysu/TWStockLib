namespace TWStockLib.Twse.Tests;

/// <summary>
/// 證交所與櫃買 OpenAPI 股票清單的節錄（2026/10/22 實際回應，未改動欄位，只挑列；權證的長「備註」截短）。
/// </summary>
internal static class OpenApiFixtures
{
    /// <summary>上市個股日成交資訊（STOCK_DAY_ALL）：股票、ETF、槓桿 ETF、REIT、ETN、特別股、TDR，沒有權證。</summary>
    public const string TwseStockDayAll = """
        [{"Date":"1151021","Code":"0050","Name":"元大台灣50","TradeVolume":"83301839","TradeValue":"9028161400","OpeningPrice":"108.80","HighestPrice":"109.20","LowestPrice":"107.60","ClosingPrice":"108.15","Change":"0.9000","Transaction":"97821"},
         {"Date":"1151021","Code":"00631L","Name":"元大台灣50正2","TradeVolume":"144974199","TradeValue":"5334109132","OpeningPrice":"36.89","HighestPrice":"37.19","LowestPrice":"36.36","ClosingPrice":"36.61","Change":"0.7400","Transaction":"48098"},
         {"Date":"1151021","Code":"01001T","Name":"土銀富邦R1","TradeVolume":"30000","TradeValue":"331200","OpeningPrice":"11.04","HighestPrice":"11.04","LowestPrice":"11.04","ClosingPrice":"11.04","Change":"0.0000","Transaction":"6"},
         {"Date":"1151021","Code":"020000","Name":"富邦特選蘋果N","TradeVolume":"55000","TradeValue":"1745910","OpeningPrice":"31.77","HighestPrice":"31.91","LowestPrice":"31.47","ClosingPrice":"31.75","Change":"0.2900","Transaction":"18"},
         {"Date":"1151021","Code":"1101","Name":"台泥","TradeVolume":"29843641","TradeValue":"799820135","OpeningPrice":"27.10","HighestPrice":"27.15","LowestPrice":"26.55","ClosingPrice":"26.80","Change":"0.0000","Transaction":"8185"},
         {"Date":"1151021","Code":"2330","Name":"台積電","TradeVolume":"27095005","TradeValue":"65515856247","OpeningPrice":"2425.00","HighestPrice":"2435.00","LowestPrice":"2400.00","ClosingPrice":"2420.00","Change":"15.0000","Transaction":"101401"},
         {"Date":"1151021","Code":"2887Z1","Name":"台新新光己特","TradeVolume":"209721","TradeValue":"4821058","OpeningPrice":"22.70","HighestPrice":"23.40","LowestPrice":"22.65","ClosingPrice":"23.35","Change":"0.4000","Transaction":"53"},
         {"Date":"1151021","Code":"910322","Name":"康師傅-DR","TradeVolume":"5025","TradeValue":"125631","OpeningPrice":"25.50","HighestPrice":"25.50","LowestPrice":"24.40","ClosingPrice":"25.20","Change":"-0.1000","Transaction":"9"}]
        """;

    /// <summary>
    /// 上市權證基本資料彙總表（t187ap37_L），以 2026/10/22 為今天：030001、03004T、03726B（熊證）尚未到期，
    /// 03080T 今天是最後交易日，03001T 已在 10/14 到期。
    /// </summary>
    public const string TwseWarrants = """
        [{"出表日期":"1151021","權證代號":"030001","權證簡稱":"友達台新6A購01","權證類型":"認購","類別":"一般型","流動量提供者報價方式":"主動報價","履約開始日":"1151005","最後交易日":"1160930","履約截止日":"1161004","發行單位數量(仟單位)":"5000","結算方式(詳附註編號說明)":"5","標的證券/指數":"友達","最新標的履約配發數量(每仟單位權證)":"65.00","原始履約價格(元)/履約指數":"53.0000","原始上限價格(元)/上限指數":"0.0000","原始下限價格(元)/下限指數":"0.0000","最新履約價格(元)/履約指數":"53.0000","最新上限價格(元)/上限指數":"0.0000","最新下限價格(元)/下限指數":"0.0000","備註":""},
         {"出表日期":"1151021","權證代號":"03004T","權證簡稱":"臺股指群益5C售05","權證類型":"認售","類別":"一般型","流動量提供者報價方式":"主動報價","履約開始日":"1151216","最後交易日":"1151214","履約截止日":"1151216","發行單位數量(仟單位)":"5000","結算方式(詳附註編號說明)":"2","標的證券/指數":"臺股指數","最新標的履約配發數量(每仟單位權證)":"0.30","原始履約價格(元)/履約指數":"31488.0000","原始上限價格(元)/上限指數":"0.0000","原始下限價格(元)/下限指數":"0.0000","最新履約價格(元)/履約指數":"31488.0000","最新上限價格(元)/上限指數":"0.0000","最新下限價格(元)/下限指數":"0.0000","備註":"◎2026/05/21申請自行註銷權證2,995仟單位，剩餘權證2,005仟單位。\r\n◎2026/05/25申請自行註銷權證425仟單位，剩餘權證1,580仟單位。"},
         {"出表日期":"1151021","權證代號":"03726B","權證簡稱":"台積電富邦64熊01","權證類型":"認售","類別":"上限型；重設型-單一多價","流動量提供者報價方式":"主動報價","履約開始日":"1160429","最後交易日":"1160427","履約截止日":"1160429","發行單位數量(仟單位)":"10000","結算方式(詳附註編號說明)":"2","標的證券/指數":"台積電","最新標的履約配發數量(每仟單位權證)":"2.00","原始履約價格(元)/履約指數":"2831.2500","原始上限價格(元)/上限指數":"2718.0000","原始下限價格(元)/下限指數":"0.0000","最新履約價格(元)/履約指數":"2731.5300","最新上限價格(元)/上限指數":"2601.3800","最新下限價格(元)/下限指數":"0.0000","備註":"重設條件：履約價格重設為上市日前一交易日及前二交易日標的證券\r\n收盤價格二日均價之125%"},
         {"出表日期":"1151021","權證代號":"03080T","權證簡稱":"台積電群益5A售16","權證類型":"認售","類別":"一般型","流動量提供者報價方式":"主動報價","履約開始日":"1151027","最後交易日":"1151022","履約截止日":"1151027","發行單位數量(仟單位)":"5000","結算方式(詳附註編號說明)":"2","標的證券/指數":"台積電","最新標的履約配發數量(每仟單位權證)":"25.00","原始履約價格(元)/履約指數":"1640.0000","原始上限價格(元)/上限指數":"0.0000","原始下限價格(元)/下限指數":"0.0000","最新履約價格(元)/履約指數":"1630.8400","最新上限價格(元)/上限指數":"0.0000","最新下限價格(元)/下限指數":"0.0000","備註":""},
         {"出表日期":"1151021","權證代號":"03001T","權證簡稱":"啟碁台新5A售02","權證類型":"認售","類別":"一般型","流動量提供者報價方式":"主動報價","履約開始日":"1151016","最後交易日":"1151014","履約截止日":"1151016","發行單位數量(仟單位)":"5000","結算方式(詳附註編號說明)":"2","標的證券/指數":"啟碁","最新標的履約配發數量(每仟單位權證)":"51.00","原始履約價格(元)/履約指數":"210.0000","原始上限價格(元)/上限指數":"0.0000","原始下限價格(元)/下限指數":"0.0000","最新履約價格(元)/履約指數":"206.5200","最新上限價格(元)/上限指數":"0.0000","最新下限價格(元)/下限指數":"0.0000","備註":"◎2026/05/19申請自行註銷權證2,550仟單位，剩餘權證2,450仟單位"}]
        """;

    /// <summary>上櫃股票行情（tpex_mainboard_daily_close_quotes）：ETF、ETN、股票、權證（認購、認售）、特別股。</summary>
    public const string TpexDailyClose = """
        [{"Date":"1151022","SecuritiesCompanyCode":"00679B","CompanyName":"元大美債20年","Close":"26.01","Change":"+0.24","Open":"25.86","High":"26.05","Low":"25.82","Average":"25.97","TradingShares":"32614019","TransactionAmount":"846905001","TransactionNumber":"5643","LatestBidPrice":"26.01","LatesAskPrice":"26.02","Capitals":"6170692000","NextReferencePrice":"26.01","NextLimitUp":"9999.95","NextLimitDown":"0.01"},
         {"Date":"1151022","SecuritiesCompanyCode":"020001","CompanyName":"富邦存股雙十N","Close":" ---","Change":"--- ","Open":"---","High":"---","Low":"---","Average":"28.93","TradingShares":"0","TransactionAmount":"0","TransactionNumber":"0","LatestBidPrice":"26.76","LatesAskPrice":"26.85","Capitals":"40000000","NextReferencePrice":"26.85","NextLimitUp":"29.53","NextLimitDown":"24.17"},
         {"Date":"1151022","SecuritiesCompanyCode":"6510","CompanyName":"精測","Close":"3065.00","Change":"-165.00 ","Open":"3280.00","High":"3345.00","Low":"3065.00","Average":"3202.81","TradingShares":"731590","TransactionAmount":"2343141715","TransactionNumber":"3539","LatestBidPrice":"3065.00","LatesAskPrice":"3090.00","Capitals":"32929138","NextReferencePrice":"3065.00","NextLimitUp":"3370.00","NextLimitDown":"2760.00"},
         {"Date":"1151022","SecuritiesCompanyCode":"700019","CompanyName":"宏捷科統一5C購01","Close":" ---","Change":"--- ","Open":"---","High":"---","Low":"---","Average":"0.33","TradingShares":"0","TransactionAmount":"0","TransactionNumber":"0","LatestBidPrice":"0.07","LatesAskPrice":"0.17","Capitals":"5000000","NextReferencePrice":"0.17","NextLimitUp":"0.32","NextLimitDown":"0.02"},
         {"Date":"1151022","SecuritiesCompanyCode":"72479U","CompanyName":"擎亞群益5B售01","Close":" ---","Change":"--- ","Open":"---","High":"---","Low":"---","Average":"0.01","TradingShares":"0","TransactionAmount":"0","TransactionNumber":"0","LatestBidPrice":"0.00","LatesAskPrice":"0.09","Capitals":"5000000","NextReferencePrice":"0.01","NextLimitUp":"0.64","NextLimitDown":"0.01"},
         {"Date":"1151022","SecuritiesCompanyCode":"8349A","CompanyName":"恒耀甲特","Close":"45.20","Change":"0.00 ","Open":"45.20","High":"45.20","Low":"45.20","Average":"45.20","TradingShares":"2000","TransactionAmount":"90400","TransactionNumber":"2","LatestBidPrice":"45.15","LatesAskPrice":"45.55","Capitals":"10000000","NextReferencePrice":"45.20","NextLimitUp":"49.70","NextLimitDown":"40.70"}]
        """;
}
