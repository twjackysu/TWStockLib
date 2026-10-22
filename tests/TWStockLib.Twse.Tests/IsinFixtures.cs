namespace TWStockLib.Twse.Tests;

/// <summary>
/// ISIN 股票清單頁的節錄（2026/10/22 實際頁面，Big5 解碼後），保留原始標記：未加引號的屬性、
/// 標題列、跨 7 欄的分段標題，以及頁面開頭被瀏覽器移出表格的 h2。各分段各取幾列具代表性的資料。
/// </summary>
internal static class IsinFixtures
{
    /// <summary>上市（strMode=2）：股票、權證（認購 / 認售 / 牛熊證）、特別股、創新板、ETF、ETN、TDR、REIT。</summary>
    public const string Tse = """
      <link rel="stylesheet" href="http://isin.twse.com.tw/isin/style1.css" type="text/css">
      <body><table  align=center><h2><strong><font class='h1'>本國上市證券國際證券辨識號碼一覽表</font></strong></h2><h2><strong><font class='h1'><center>最近更新日期:2026/10/22  </center> </font></strong></h2><h2><font color='red'><center>掛牌日以正式公告為準</center></font></h2></table><TABLE class='h4' align=center cellSpacing=3 cellPadding=2 width=750 border=0><tr align=center><td bgcolor=#D5FFD5>有價證券代號及名稱 </td><td bgcolor=#D5FFD5>國際證券辨識號碼(ISIN Code)</td><td bgcolor=#D5FFD5>上市日</td><td bgcolor=#D5FFD5>市場別</td><td bgcolor=#D5FFD5>產業別</td><td bgcolor=#D5FFD5>CFICode</td><td bgcolor=#D5FFD5>備註</td></tr>
      <tr><td bgcolor=#FAFAD2 colspan=7 ><B> 股票 <B> </td></tr>
      <tr><td bgcolor=#FAFAD2>1101　台泥</td><td bgcolor=#FAFAD2>TW0001101004</td><td bgcolor=#FAFAD2>1962/02/09</td><td bgcolor=#FAFAD2>上市</td><td bgcolor=#FAFAD2>水泥工業</td><td bgcolor=#FAFAD2>ESVUFR</td><td bgcolor=#FAFAD2></td></tr>
      <tr><td bgcolor=#FAFAD2>2330　台積電</td><td bgcolor=#FAFAD2>TW0002330008</td><td bgcolor=#FAFAD2>1994/09/05</td><td bgcolor=#FAFAD2>上市</td><td bgcolor=#FAFAD2>半導體業</td><td bgcolor=#FAFAD2>ESVUFR</td><td bgcolor=#FAFAD2></td></tr>
      <tr><td bgcolor=#FAFAD2>3673　TPK-KY</td><td bgcolor=#FAFAD2>KYG898431096</td><td bgcolor=#FAFAD2>2010/10/29</td><td bgcolor=#FAFAD2>上市</td><td bgcolor=#FAFAD2>光電業</td><td bgcolor=#FAFAD2>ESVTFR</td><td bgcolor=#FAFAD2></td></tr>
      <tr><td bgcolor=#FAFAD2 colspan=7 ><B> 上市認購(售)權證 <B> </td></tr>
      <tr><td bgcolor=#FAFAD2>030001　友達台新6A購01</td><td bgcolor=#FAFAD2>TW26Z0300015</td><td bgcolor=#FAFAD2>2026/10/05</td><td bgcolor=#FAFAD2>上市</td><td bgcolor=#FAFAD2></td><td bgcolor=#FAFAD2>RWSCCA</td><td bgcolor=#FAFAD2></td></tr>
      <tr><td bgcolor=#FAFAD2>03004T　臺股指群益5C售05</td><td bgcolor=#FAFAD2>TW26Z03004T6</td><td bgcolor=#FAFAD2>2026/04/17</td><td bgcolor=#FAFAD2>上市</td><td bgcolor=#FAFAD2></td><td bgcolor=#FAFAD2>RWICPE</td><td bgcolor=#FAFAD2></td></tr>
      <tr><td bgcolor=#FAFAD2>03726B　台積電富邦64熊01</td><td bgcolor=#FAFAD2>TW26Z03726B2</td><td bgcolor=#FAFAD2>2026/04/30</td><td bgcolor=#FAFAD2>上市</td><td bgcolor=#FAFAD2></td><td bgcolor=#FAFAD2>RWSCPE</td><td bgcolor=#FAFAD2></td></tr>
      <tr><td bgcolor=#FAFAD2>03989C　臺股指凱基5A牛04</td><td bgcolor=#FAFAD2>TW25Z03989C5</td><td bgcolor=#FAFAD2>2025/09/26</td><td bgcolor=#FAFAD2>上市</td><td bgcolor=#FAFAD2></td><td bgcolor=#FAFAD2>RWICCE</td><td bgcolor=#FAFAD2></td></tr>
      <tr><td bgcolor=#FAFAD2 colspan=7 ><B> 特別股 <B> </td></tr>
      <tr><td bgcolor=#FAFAD2>2887Z1　台新新光己特</td><td bgcolor=#FAFAD2>TW0002887Z13</td><td bgcolor=#FAFAD2>2022/07/26</td><td bgcolor=#FAFAD2>上市</td><td bgcolor=#FAFAD2>金融保險業</td><td bgcolor=#FAFAD2>EPNCAR</td><td bgcolor=#FAFAD2></td></tr>
      <tr><td bgcolor=#FAFAD2>2897B　王道銀乙特</td><td bgcolor=#FAFAD2>TW0002897B01</td><td bgcolor=#FAFAD2>2024/09/30</td><td bgcolor=#FAFAD2>上市</td><td bgcolor=#FAFAD2>金融保險業</td><td bgcolor=#FAFAD2>EFNRAR</td><td bgcolor=#FAFAD2></td></tr>
      <tr><td bgcolor=#FAFAD2 colspan=7 ><B> 創新板 <B> </td></tr>
      <tr><td bgcolor=#FAFAD2>2237　華德動能-創</td><td bgcolor=#FAFAD2>TW0002237005</td><td bgcolor=#FAFAD2>2026/08/10</td><td bgcolor=#FAFAD2>上市臺灣創新板</td><td bgcolor=#FAFAD2>汽車工業</td><td bgcolor=#FAFAD2>ESVUFR</td><td bgcolor=#FAFAD2></td></tr>
      <tr><td bgcolor=#FAFAD2 colspan=7 ><B> ETF <B> </td></tr>
      <tr><td bgcolor=#FAFAD2>0050　元大台灣50</td><td bgcolor=#FAFAD2>TW0000050004</td><td bgcolor=#FAFAD2>2003/06/30</td><td bgcolor=#FAFAD2>上市</td><td bgcolor=#FAFAD2></td><td bgcolor=#FAFAD2>CEOGEU</td><td bgcolor=#FAFAD2></td></tr>
      <tr><td bgcolor=#FAFAD2>00631L　元大台灣50正2</td><td bgcolor=#FAFAD2>TW00000631L0</td><td bgcolor=#FAFAD2>2014/10/31</td><td bgcolor=#FAFAD2>上市</td><td bgcolor=#FAFAD2></td><td bgcolor=#FAFAD2>CEOGDU</td><td bgcolor=#FAFAD2></td></tr>
      <tr><td bgcolor=#FAFAD2 colspan=7 ><B> ETN <B> </td></tr>
      <tr><td bgcolor=#FAFAD2>020000　富邦特選蘋果N</td><td bgcolor=#FAFAD2>TW0000200005</td><td bgcolor=#FAFAD2>2019/04/30</td><td bgcolor=#FAFAD2>上市</td><td bgcolor=#FAFAD2></td><td bgcolor=#FAFAD2>CMXXXU</td><td bgcolor=#FAFAD2></td></tr>
      <tr><td bgcolor=#FAFAD2 colspan=7 ><B> 臺灣存託憑證(TDR) <B> </td></tr>
      <tr><td bgcolor=#FAFAD2>910322　康師傅-DR</td><td bgcolor=#FAFAD2>TW0009103226</td><td bgcolor=#FAFAD2>2009/12/16</td><td bgcolor=#FAFAD2>上市</td><td bgcolor=#FAFAD2></td><td bgcolor=#FAFAD2>EDSDDR</td><td bgcolor=#FAFAD2></td></tr>
      <tr><td bgcolor=#FAFAD2 colspan=7 ><B> 受益證券-不動產投資信託 <B> </td></tr>
      <tr><td bgcolor=#FAFAD2>01001T　土銀富邦R1</td><td bgcolor=#FAFAD2>TW00001001T8</td><td bgcolor=#FAFAD2>2005/03/10</td><td bgcolor=#FAFAD2>上市</td><td bgcolor=#FAFAD2></td><td bgcolor=#FAFAD2>CBCIXU</td><td bgcolor=#FAFAD2></td></tr>
      </table><font color='red'><center>掛牌日以正式公告為準</center></font>
      """;

    /// <summary>上櫃（strMode=4）：權證、ETF、ETN、股票、特別股、資產基礎證券。</summary>
    public const string Otc = """
      <link rel="stylesheet" href="http://isin.twse.com.tw/isin/style1.css" type="text/css">
      <body><table  align=center><h2><strong><font class='h1'>本國上櫃證券國際證券辨識號碼一覽表</font></strong></h2><h2><strong><font class='h1'><center>最近更新日期:2026/10/22  </center> </font></strong></h2><h2><font color='red'><center>掛牌日以正式公告為準</center></font></h2></table><TABLE class='h4' align=center cellSpacing=3 cellPadding=2 width=750 border=0><tr align=center><td bgcolor=#D5FFD5>有價證券代號及名稱 </td><td bgcolor=#D5FFD5>國際證券辨識號碼(ISIN Code)</td><td bgcolor=#D5FFD5>上市日</td><td bgcolor=#D5FFD5>市場別</td><td bgcolor=#D5FFD5>產業別</td><td bgcolor=#D5FFD5>CFICode</td><td bgcolor=#D5FFD5>備註</td></tr>
      <tr><td bgcolor=#FAFAD2 colspan=7 ><B> 上櫃認購(售)權證 <B> </td></tr>
      <tr><td bgcolor=#FAFAD2>700019　宏捷科統一5C購01</td><td bgcolor=#FAFAD2>TW25Z7000198</td><td bgcolor=#FAFAD2>2025/10/01</td><td bgcolor=#FAFAD2>上櫃</td><td bgcolor=#FAFAD2></td><td bgcolor=#FAFAD2>RWSCCA</td><td bgcolor=#FAFAD2></td></tr>
      <tr><td bgcolor=#FAFAD2>72479U　擎亞群益5B售01</td><td bgcolor=#FAFAD2>TW25Z72479U5</td><td bgcolor=#FAFAD2>2025/11/05</td><td bgcolor=#FAFAD2>上櫃</td><td bgcolor=#FAFAD2></td><td bgcolor=#FAFAD2>RWSCPE</td><td bgcolor=#FAFAD2></td></tr>
      <tr><td bgcolor=#FAFAD2 colspan=7 ><B> ETF <B> </td></tr>
      <tr><td bgcolor=#FAFAD2>00679B　元大美債20年</td><td bgcolor=#FAFAD2>TW00000679B0</td><td bgcolor=#FAFAD2>2017/01/17</td><td bgcolor=#FAFAD2>上櫃</td><td bgcolor=#FAFAD2></td><td bgcolor=#FAFAD2>CEOIBU</td><td bgcolor=#FAFAD2></td></tr>
      <tr><td bgcolor=#FAFAD2 colspan=7 ><B> ETN <B> </td></tr>
      <tr><td bgcolor=#FAFAD2>020001　富邦存股雙十N</td><td bgcolor=#FAFAD2>TW0000200013</td><td bgcolor=#FAFAD2>2019/04/30</td><td bgcolor=#FAFAD2>上櫃</td><td bgcolor=#FAFAD2></td><td bgcolor=#FAFAD2>CMXXXU</td><td bgcolor=#FAFAD2></td></tr>
      <tr><td bgcolor=#FAFAD2 colspan=7 ><B> 股票 <B> </td></tr>
      <tr><td bgcolor=#FAFAD2>6510　精測</td><td bgcolor=#FAFAD2>TW0006510001</td><td bgcolor=#FAFAD2>2016/03/24</td><td bgcolor=#FAFAD2>上櫃</td><td bgcolor=#FAFAD2>半導體業</td><td bgcolor=#FAFAD2>ESVUFR</td><td bgcolor=#FAFAD2></td></tr>
      <tr><td bgcolor=#FAFAD2 colspan=7 ><B> 特別股 <B> </td></tr>
      <tr><td bgcolor=#FAFAD2>8349A　恒耀甲特</td><td bgcolor=#FAFAD2>TW0008349A03</td><td bgcolor=#FAFAD2>2020/02/24</td><td bgcolor=#FAFAD2>上櫃</td><td bgcolor=#FAFAD2></td><td bgcolor=#FAFAD2>EPNRAR</td><td bgcolor=#FAFAD2></td></tr>
      <tr><td bgcolor=#FAFAD2 colspan=7 ><B> 受益證券-資產基礎證券 <B> </td></tr>
      <tr><td bgcolor=#FAFAD2>01111S　081中租賃A</td><td bgcolor=#FAFAD2>TW00001111S7</td><td bgcolor=#FAFAD2>2019/12/11</td><td bgcolor=#FAFAD2>上櫃</td><td bgcolor=#FAFAD2></td><td bgcolor=#FAFAD2>DAFUFR</td><td bgcolor=#FAFAD2></td></tr>
      </table><font color='red'><center>掛牌日以正式公告為準</center></font>
      """;
}
