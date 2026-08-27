using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace StockMonitor
{
    // ================================================================ 数据模型

    public class Bar
    {
        public string Date;
        public double Open, Close, High, Low, Volume;
    }

    public class TrendPoint
    {
        public string Time;
        public double Price;
        public double Avg;
        public double Volume;   // 手(部分接口提供)
    }

    public class Quote
    {
        public string Name = "";
        public string Code = "";
        public double Price, PrevClose, Open, High, Low, Change, ChangePct, Volume, Amount, Turnover;
        public string Time = "";
        public bool Ok;
        public string Error = "";
    }

    public class KlineData
    {
        public string Name = "";
        public string Code = "";
        public List<Bar> Bars = new List<Bar>();
    }

    // ================================================================ 指标计算

    public static class Indicators
    {
        public static List<double> MA(List<double> src, int n)
        {
            List<double> r = new List<double>();
            double sum = 0;
            for (int i = 0; i < src.Count; i++)
            {
                sum += src[i];
                if (i >= n) sum -= src[i - n];
                r.Add(i >= n - 1 ? sum / n : double.NaN);
            }
            return r;
        }

        public static List<double> EMA(List<double> src, int n)
        {
            List<double> r = new List<double>();
            double k = 2.0 / (n + 1);
            double prev = double.NaN;
            for (int i = 0; i < src.Count; i++)
            {
                prev = double.IsNaN(prev) ? src[i] : src[i] * k + prev * (1 - k);
                r.Add(prev);
            }
            return r;
        }

        public static void MACD(List<double> close, int fast, int slow, int sig,
                                out List<double> dif, out List<double> dea, out List<double> hist)
        {
            List<double> e1 = EMA(close, fast);
            List<double> e2 = EMA(close, slow);
            dif = new List<double>();
            for (int i = 0; i < close.Count; i++) dif.Add(e1[i] - e2[i]);
            dea = EMA(dif, sig);
            hist = new List<double>();
            for (int i = 0; i < close.Count; i++) hist.Add(2 * (dif[i] - dea[i]));
        }

        public static void KDJ(List<double> high, List<double> low, List<double> close, int n, int kp, int dp,
                               out List<double> k, out List<double> d, out List<double> j)
        {
            k = new List<double>(); d = new List<double>(); j = new List<double>();
            double pk = 50, pd = 50;
            for (int i = 0; i < close.Count; i++)
            {
                int s = Math.Max(0, i - n + 1);
                double hn = double.MinValue, ln = double.MaxValue;
                for (int x = s; x <= i; x++) { if (high[x] > hn) hn = high[x]; if (low[x] < ln) ln = low[x]; }
                double rsv = (hn - ln) < 1e-9 ? 50 : (close[i] - ln) / (hn - ln) * 100;
                double ck = (pk * (kp - 1) + rsv) / kp;
                double cd = (pd * (dp - 1) + ck) / dp;
                pk = ck; pd = cd;
                k.Add(ck); d.Add(cd); j.Add(3 * ck - 2 * cd);
            }
        }

        public static List<double> RSI(List<double> close, int n)
        {
            List<double> r = new List<double>();
            double gain = 0, loss = 0;
            for (int i = 0; i < close.Count; i++)
            {
                if (i > 0)
                {
                    double ch = close[i] - close[i - 1];
                    double g = ch > 0 ? ch : 0;
                    double l = ch < 0 ? -ch : 0;
                    if (i <= n) { gain += g; loss += l; }
                    else { gain = (gain * (n - 1) + g) / n; loss = (loss * (n - 1) + l) / n; }
                    if (i >= n)
                    {
                        double rs = loss < 1e-9 ? 100 : gain / loss;
                        r.Add(100 - 100 / (1 + rs));
                    }
                    else r.Add(double.NaN);
                }
                else r.Add(double.NaN);
            }
            return r;
        }

        public static void BOLL(List<double> close, int n, double mult,
                                out List<double> mid, out List<double> up, out List<double> low)
        {
            mid = MA(close, n);
            up = new List<double>(); low = new List<double>();
            for (int i = 0; i < close.Count; i++)
            {
                if (i >= n - 1)
                {
                    double m = mid[i], sq = 0;
                    for (int x = i - n + 1; x <= i; x++) { double dd = close[x] - m; sq += dd * dd; }
                    double sd = Math.Sqrt(sq / n);
                    up.Add(m + mult * sd); low.Add(m - mult * sd);
                }
                else { up.Add(double.NaN); low.Add(double.NaN); }
            }
        }

        public static List<double> WR(List<double> high, List<double> low, List<double> close, int n)
        {
            List<double> r = new List<double>();
            for (int i = 0; i < close.Count; i++)
            {
                int s = Math.Max(0, i - n + 1);
                double hn = double.MinValue, ln = double.MaxValue;
                for (int x = s; x <= i; x++) { if (high[x] > hn) hn = high[x]; if (low[x] < ln) ln = low[x]; }
                r.Add((hn - ln) < 1e-9 ? 0 : (hn - close[i]) / (hn - ln) * 100);
            }
            return r;
        }

        public static List<double> BIAS(List<double> close, int n)
        {
            List<double> ma = MA(close, n);
            List<double> r = new List<double>();
            for (int i = 0; i < close.Count; i++)
                r.Add(!double.IsNaN(ma[i]) && Math.Abs(ma[i]) > 1e-9 ? (close[i] - ma[i]) / ma[i] * 100 : double.NaN);
            return r;
        }

        public static double Last(List<double> v) { return v.Count > 0 ? v[v.Count - 1] : double.NaN; }
        public static double Prev(List<double> v) { return v.Count > 1 ? v[v.Count - 2] : double.NaN; }

        public static double Safe(double v) { return double.IsNaN(v) ? 0 : v; }
    }

    // ================================================================ 配置

    [DataContract]
    public class AlertRule
    {
        [DataMember] public string Stock = "";          // "" = 所有自选股
        [DataMember] public string Indicator = "PRICE"; // PRICE/MA5/MA10/MA20/MACD_DIF/MACD_DEA/MACD_HIST/KDJ_K/KDJ_D/KDJ_J/RSI6/RSI12/RSI24/BOLL_UP/BOLL_MID/BOLL_LOW/WR14/BIAS6/BIAS12
        [DataMember] public string Condition = ">=";    // >= / <= / CROSS_UP / CROSS_DOWN
        [DataMember] public double Value = 0;
        [DataMember] public bool Enabled = true;
        [DataMember] public string Note = "";
    }

    [DataContract]
    public class AppConfig
    {
        [DataMember] public List<string> Watch = new List<string>();
        [DataMember] public List<string> ShowIndicators = new List<string> { "KDJ", "MACD", "RSI6", "BOLL" };
        [DataMember] public List<AlertRule> Alerts = new List<AlertRule>();
        [DataMember] public bool FlashAlert = true;
        [DataMember] public int FlashSeconds = 5;
        [DataMember] public bool SoundAlert = true;
        [DataMember] public int RefreshSeconds = 3;
        [DataMember] public int WinX = -1;
        [DataMember] public int WinY = -1;
        [DataMember] public int WinW = 760;
        [DataMember] public int WinH = 560;
        [DataMember] public string ChartLayout = "grid";   // grid=格子排列 rows=行排列(叠放)
        [DataMember] public string HideHotkey = "Ctrl+Shift+H";  // 一键隐藏/还原快捷键,空=禁用
    }

    public static class ConfigIO
    {
        private static string PathFile { get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json"); } }

        public static AppConfig Load()
        {
            AppConfig cfg = new AppConfig();
            try
            {
                if (File.Exists(PathFile))
                {
                    using (FileStream fs = File.OpenRead(PathFile))
                    {
                        DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(AppConfig));
                        cfg = (AppConfig)ser.ReadObject(fs);
                    }
                }
                else if (File.Exists(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "watch.txt")))
                {
                    foreach (string line in File.ReadAllLines(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "watch.txt")))
                    {
                        string c = StockApi.NormalizeCode(line.Trim());
                        if (c.Length > 0 && !cfg.Watch.Contains(c)) cfg.Watch.Add(c);
                    }
                }
            }
            catch (Exception ex) { Log.Error("ConfigIO.Load", ex); }
            if (cfg.Watch == null) cfg.Watch = new List<string>();
            if (cfg.ShowIndicators == null) cfg.ShowIndicators = new List<string>();
            if (cfg.Alerts == null) cfg.Alerts = new List<AlertRule>();
            if (cfg.Watch.Count == 0) cfg.Watch.Add("sh600519");
            if (cfg.ShowIndicators.Count == 0) cfg.ShowIndicators = new List<string> { "KDJ", "MACD" };
            return cfg;
        }

        public static void Save(AppConfig cfg)
        {
            try
            {
                using (FileStream fs = File.Create(PathFile))
                {
                    DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(AppConfig));
                    ser.WriteObject(fs, cfg);
                }
            }
            catch (Exception ex) { Log.Error("ConfigIO.Save", ex); }
        }
    }

    // ================================================================ 数据源

    public static class StockApi
    {
        static StockApi()
        {
            ServicePointManager.SecurityProtocol =
                (SecurityProtocolType)3072 | (SecurityProtocolType)768 | (SecurityProtocolType)192;
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            try { ServicePointManager.DefaultConnectionLimit = 12; } catch { }
        }

        public static string NormalizeCode(string raw)
        {
            raw = (raw ?? "").Trim().ToLowerInvariant();
            if (raw.Length == 0) return "";
            if (raw.Length >= 2 && char.IsLetter(raw[0]))
            {
                if (raw.StartsWith("sh") || raw.StartsWith("sz") || raw.StartsWith("bj") ||
                    raw.StartsWith("hk") || raw.StartsWith("us")) return raw;
                return raw;
            }
            if (raw.Length == 6 && IsDigits(raw))
            {
                char head = raw[0];
                if (head == '6' || head == '9' || head == '5') return "sh" + raw;
                if (head == '4' || head == '8') return "bj" + raw;
                return "sz" + raw;
            }
            if (raw.Length == 5 && IsDigits(raw)) return "hk" + raw;
            return raw;
        }

        private static bool IsDigits(string s)
        {
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        private static byte[] HttpGetBytes(string url, string referer)
        {
            Exception last = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "GET";
                    req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                    req.Accept = "*/*";
                    req.Referer = referer;
                    req.Timeout = 12000;
                    req.ReadWriteTimeout = 12000;
                    req.KeepAlive = false;
                    using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                    using (Stream st = res.GetResponseStream())
                    using (MemoryStream ms = new MemoryStream())
                    {
                        byte[] buf = new byte[8192];
                        int n;
                        while ((n = st.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                        return ms.ToArray();
                    }
                }
                catch (Exception ex) { last = ex; Thread.Sleep(300 * (attempt + 1)); }
            }
            throw last;
        }

        private static string DecodeGbk(byte[] bytes)
        {
            try { return Encoding.GetEncoding(936).GetString(bytes); }
            catch { return Encoding.Default.GetString(bytes); }
        }

        private static double Num(string[] f, int idx)
        {
            double d;
            if (idx < 0 || idx >= f.Length) return 0;
            return double.TryParse(f[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0;
        }

        public static Quote GetQuote(string code, out string error)
        {
            error = "";
            try
            {
                byte[] bytes = HttpGetBytes("https://qt.gtimg.cn/q=" + code, "https://gu.qq.com/");
                string text = DecodeGbk(bytes);
                int q1 = text.IndexOf('"');
                int q2 = text.LastIndexOf('"');
                if (q1 < 0 || q2 <= q1) { error = "行情返回格式异常"; return null; }
                string[] f = text.Substring(q1 + 1, q2 - q1 - 1).Split('~');
                if (f.Length < 40) { error = "行情字段不足"; return null; }
                Quote q = new Quote();
                q.Name = f.Length > 1 ? f[1] : code;
                q.Code = f.Length > 2 ? f[2] : code;
                q.Price = Num(f, 3);
                q.PrevClose = Num(f, 4);
                q.Open = Num(f, 5);
                q.Volume = Num(f, 6);
                q.Change = Num(f, 31);
                q.ChangePct = Num(f, 32);
                q.High = Num(f, 33);
                q.Low = Num(f, 34);
                q.Amount = Num(f, 37);
                q.Turnover = Num(f, 38);
                string ts = f.Length > 30 ? f[30] : "";
                if (ts.Length >= 14)
                    q.Time = ts.Substring(0, 4) + "-" + ts.Substring(4, 2) + "-" + ts.Substring(6, 2) +
                             " " + ts.Substring(8, 2) + ":" + ts.Substring(10, 2) + ":" + ts.Substring(12, 2);
                q.Ok = true;
                return q;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        public static List<TrendPoint> GetTrend(string code, out string error)
        {
            error = "";
            string secid = ToEastmoneySecid(code);
            if (secid == null) { error = "该代码暂不支持走势图"; return null; }
            try
            {
                string url = "https://push2his.eastmoney.com/api/qt/stock/trends2/get?secid=" + secid +
                             "&fields1=f1,f2,f3,f4,f5,f6,f7,f8,f9,f10,f11,f12,f13" +
                             "&fields2=f51,f52,f53,f54,f55,f56,f57,f58&ndays=1&iscr=0";
                string json = Encoding.UTF8.GetString(HttpGetBytes(url, "https://quote.eastmoney.com/"));
                Match m = Regex.Match(json, "\"trends\"\\s*:\\s*\\[(.*?)\\]");
                if (!m.Success) { error = "走势数据为空"; return null; }
                string[] rows = m.Groups[1].Value.Split(new[] { "\",\"" }, StringSplitOptions.None);
                List<TrendPoint> list = new List<TrendPoint>();
                for (int i = 0; i < rows.Length; i++)
                {
                    string[] p = rows[i].Trim().Trim('"').Split(',');
                    if (p.Length < 3) continue;
                    double price, avg, vol = 0;
                    if (!double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out price)) continue;
                    double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out avg);
                    if (p.Length > 5) double.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out vol);
                    list.Add(new TrendPoint { Time = p[0], Price = price, Avg = avg, Volume = vol });
                }
                if (list.Count > 0) return list;
                error = "走势数据为空";
            }
            catch (Exception ex) { error = ex.Message; }
            return GetTrendTencent(code, out error);
        }

        private static List<TrendPoint> GetTrendTencent(string code, out string error)
        {
            error = "";
            try
            {
                string url = "https://web.ifzq.gtimg.cn/appstock/app/minute/query?code=" + code;
                string json = Encoding.UTF8.GetString(HttpGetBytes(url, "https://gu.qq.com/"));
                MatchCollection ms = Regex.Matches(json, "\\[\"(\\d{8,14})\",\"([\\d.]+)\",\"([\\d.]+)\"(?:,\"([\\d.]+)\")?");
                List<TrendPoint> list = new List<TrendPoint>();
                foreach (Match m in ms)
                {
                    TrendPoint tp = new TrendPoint();
                    string t = m.Groups[1].Value;
                    if (t.Length == 14) tp.Time = t.Substring(0, 4) + "-" + t.Substring(4, 2) + "-" + t.Substring(6, 2) +
                                                " " + t.Substring(8, 2) + ":" + t.Substring(10, 2);
                    else tp.Time = t;
                    double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out tp.Price);
                    double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out tp.Avg);
                    if (m.Groups[4].Success)
                        double.TryParse(m.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out tp.Volume);
                    list.Add(tp);
                }
                if (list.Count > 0) return list;
                error = "走势数据为空";
            }
            catch (Exception ex) { error = ex.Message; }
            return null;
        }

        /// <summary>日K线(指标计算用)。腾讯为主,东方财富为备。</summary>
        public static KlineData GetKline(string code, out string error)
        {
            error = "";
            KlineData kd = GetKlineTencent(code, out error);
            if (kd != null) return kd;
            string emErr;
            kd = GetKlineEastmoney(code, out emErr);
            if (kd != null) { error = ""; return kd; }
            if (error.Length == 0) error = emErr;
            return null;
        }

        private static KlineData GetKlineTencent(string code, out string error)
        {
            error = "";
            try
            {
                string url = "https://web.ifzq.gtimg.cn/appstock/app/fqkline/get?param=" + code + ",day,,,150,qfq";
                string json = Encoding.UTF8.GetString(HttpGetBytes(url, "https://gu.qq.com/"));
                MatchCollection ms = Regex.Matches(json, "\\[\"(\\d{4}-\\d{2}-\\d{2})\",\"([\\d.]+)\",\"([\\d.]+)\",\"([\\d.]+)\",\"([\\d.]+)\",\"([\\d.]+)\"");
                if (ms.Count == 0) { error = "K线数据为空"; return null; }
                KlineData kd = new KlineData { Code = code, Name = code };
                foreach (Match m in ms)
                {
                    Bar b = new Bar();
                    b.Date = m.Groups[1].Value;
                    double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out b.Open);
                    double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out b.Close);
                    double.TryParse(m.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out b.High);
                    double.TryParse(m.Groups[5].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out b.Low);
                    double.TryParse(m.Groups[6].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out b.Volume);
                    kd.Bars.Add(b);
                }
                if (kd.Bars.Count > 0) return kd;
                error = "K线数据为空";
            }
            catch (Exception ex) { error = ex.Message; }
            return null;
        }

        private static KlineData GetKlineEastmoney(string code, out string error)
        {
            error = "";
            string secid = ToEastmoneySecid(code);
            if (secid == null) { error = "该代码暂不支持K线"; return null; }
            try
            {
                string url = "https://push2his.eastmoney.com/api/qt/stock/kline/get?secid=" + secid +
                             "&fields1=f1,f2,f3,f4,f5,f6&fields2=f51,f52,f53,f54,f55,f56,f57,f58,f59,f60,f61" +
                             "&klt=101&fqt=1&end=20500101&lmt=150";
                string json = Encoding.UTF8.GetString(HttpGetBytes(url, "https://quote.eastmoney.com/"));
                Match mName = Regex.Match(json, "\"name\":\"([^\"]*)\"");
                Match mK = Regex.Match(json, "\"klines\":\\s*\\[(.*?)\\]");
                if (!mK.Success) { error = "K线数据为空"; return null; }
                KlineData kd = new KlineData();
                kd.Name = mName.Success ? UnescapeJson(mName.Groups[1].Value) : code;
                kd.Code = code;
                string[] rows = mK.Groups[1].Value.Split(new[] { "\",\"" }, StringSplitOptions.None);
                foreach (string row in rows)
                {
                    string[] p = row.Trim().Trim('"').Split(',');
                    if (p.Length < 6) continue;
                    Bar b = new Bar();
                    b.Date = p[0];
                    double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out b.Open);
                    double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out b.Close);
                    double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out b.High);
                    double.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out b.Low);
                    double.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out b.Volume);
                    kd.Bars.Add(b);
                }
                if (kd.Bars.Count > 0) return kd;
                error = "K线数据为空";
            }
            catch (Exception ex) { error = ex.Message; }
            return null;
        }

        /// <summary>腾讯智能搜索:按名称/代码关键字找股票。</summary>
        public static List<Quote> Search(string keyword, out string error)
        {
            error = "";
            List<Quote> list = new List<Quote>();
            try
            {
                string url = "https://smartbox.gtimg.cn/s3/?v=2&q=" + Uri.EscapeDataString(keyword) + "&t=all";
                byte[] bytes = HttpGetBytes(url, "https://gu.qq.com/");
                string text = DecodeGbk(bytes);
                int q1 = text.IndexOf('"');
                int q2 = text.LastIndexOf('"');
                if (q1 < 0 || q2 <= q1) return list;
                string body = text.Substring(q1 + 1, q2 - q1 - 1);
                string[] items = body.Split('^');
                for (int i = 0; i < items.Length; i++)
                {
                    if (i == 0 && items[0].IndexOf('~') < 0) continue; // 首段可能是数量
                    string[] f = items[i].Split('~');
                    if (f.Length < 5) continue;
                    string market = f[0], code = f[1], name = UnescapeUnicode(f[2]), type = f[4];
                    if (!(type == "GP-A" || type == "GP-B" || type == "GP")) continue;
                    string full = (market == "sh" || market == "sz" || market == "bj" ? market : (market == "hk" ? "hk" : "us")) + code;
                    Quote q = new Quote { Name = name, Code = full, Ok = true };
                    list.Add(q);
                }
            }
            catch (Exception ex) { error = ex.Message; }
            return list;
        }

        private static string UnescapeJson(string s) { return s.Replace("\\\"", "\""); }

        private static string UnescapeUnicode(string s)
        {
            return Regex.Replace(s, @"\\u([0-9a-fA-F]{4})", m =>
            {
                int cp;
                return int.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out cp)
                    ? ((char)cp).ToString() : m.Value;
            });
        }

        private static string ToEastmoneySecid(string code)
        {
            code = code.Trim().ToLowerInvariant();
            if (code.StartsWith("sh")) return "1." + code.Substring(2);
            if (code.StartsWith("sz") || code.StartsWith("bj")) return "0." + code.Substring(2);
            if (code.StartsWith("hk")) return "116." + code.Substring(2);
            if (code.StartsWith("us")) return "105." + code.Substring(2).ToUpperInvariant();
            return null;
        }
    }

    // ================================================================ 指标计算包装

    public class StockIndicators
    {
        public double MA5, MA10, MA20;
        public double Dif, Dea, Hist;
        public double K, D, J;
        public double Rsi6, Rsi12, Rsi24;
        public double BollUp, BollMid, BollLow;
        public double Wr14;
        public double Bias6, Bias12;
        public double VolMa5;
        public bool HasBars;

        public static StockIndicators Compute(KlineData kd)
        {
            StockIndicators r = new StockIndicators();
            if (kd == null || kd.Bars.Count < 30) return r;
            List<double> close = new List<double>(), high = new List<double>(), low = new List<double>(), vol = new List<double>();
            foreach (Bar b in kd.Bars) { close.Add(b.Close); high.Add(b.High); low.Add(b.Low); vol.Add(b.Volume); }
            r.HasBars = true;

            List<double> ma5 = Indicators.MA(close, 5), ma10 = Indicators.MA(close, 10), ma20 = Indicators.MA(close, 20);
            r.MA5 = Indicators.Safe(Indicators.Last(ma5));
            r.MA10 = Indicators.Safe(Indicators.Last(ma10));
            r.MA20 = Indicators.Safe(Indicators.Last(ma20));

            List<double> dif, dea, hist;
            Indicators.MACD(close, 12, 26, 9, out dif, out dea, out hist);
            r.Dif = Indicators.Safe(Indicators.Last(dif));
            r.Dea = Indicators.Safe(Indicators.Last(dea));
            r.Hist = Indicators.Safe(Indicators.Last(hist));

            List<double> k, d, j;
            Indicators.KDJ(high, low, close, 9, 3, 3, out k, out d, out j);
            r.K = Indicators.Safe(Indicators.Last(k));
            r.D = Indicators.Safe(Indicators.Last(d));
            r.J = Indicators.Safe(Indicators.Last(j));

            r.Rsi6 = Indicators.Safe(Indicators.Last(Indicators.RSI(close, 6)));
            r.Rsi12 = Indicators.Safe(Indicators.Last(Indicators.RSI(close, 12)));
            r.Rsi24 = Indicators.Safe(Indicators.Last(Indicators.RSI(close, 24)));

            List<double> mid, up, lowB;
            Indicators.BOLL(close, 20, 2, out mid, out up, out lowB);
            r.BollUp = Indicators.Safe(Indicators.Last(up));
            r.BollMid = Indicators.Safe(Indicators.Last(mid));
            r.BollLow = Indicators.Safe(Indicators.Last(lowB));

            r.Wr14 = Indicators.Safe(Indicators.Last(Indicators.WR(high, low, close, 14)));
            r.Bias6 = Indicators.Safe(Indicators.Last(Indicators.BIAS(close, 6)));
            r.Bias12 = Indicators.Safe(Indicators.Last(Indicators.BIAS(close, 12)));
            List<double> vma = Indicators.MA(vol, 5);
            r.VolMa5 = Indicators.Safe(Indicators.Last(vma));
            return r;
        }

        /// <summary>取某指标当前值;cross 时返回 0=无信号 1=上穿 2=下穿。</summary>
        public static double ValueOf(StockIndicators si, string name)
        {
            switch (name)
            {
                case "PRICE": return 0; // 特殊处理
                case "MA5": return si.MA5;
                case "MA10": return si.MA10;
                case "MA20": return si.MA20;
                case "MACD_DIF": return si.Dif;
                case "MACD_DEA": return si.Dea;
                case "MACD_HIST": return si.Hist;
                case "KDJ_K": return si.K;
                case "KDJ_D": return si.D;
                case "KDJ_J": return si.J;
                case "RSI6": return si.Rsi6;
                case "RSI12": return si.Rsi12;
                case "RSI24": return si.Rsi24;
                case "BOLL_UP": return si.BollUp;
                case "BOLL_MID": return si.BollMid;
                case "BOLL_LOW": return si.BollLow;
                case "WR14": return si.Wr14;
                case "BIAS6": return si.Bias6;
                case "BIAS12": return si.Bias12;
                default: return 0;
            }
        }
    }

    // ================================================================ 提醒引擎

    public class AlertEngine
    {
        private readonly Dictionary<string, DateTime> fired = new Dictionary<string, DateTime>();

        /// <summary>检查所有规则,返回需要触发的描述列表。</summary>
        public List<string> Check(AppConfig cfg, string stockCode, string stockName, Quote q, KlineData kd)
        {
            List<string> triggers = new List<string>();
            if (cfg.Alerts == null || q == null || !q.Ok) return triggers;
            StockIndicators si = StockIndicators.Compute(kd);
            StockIndicators siPrev = null;
            if (kd != null && kd.Bars.Count > 1)
            {
                KlineData kd2 = new KlineData { Name = kd.Name, Code = kd.Code };
                for (int i = 0; i < kd.Bars.Count - 1; i++) kd2.Bars.Add(kd.Bars[i]);
                siPrev = StockIndicators.Compute(kd2);
            }
            foreach (AlertRule rule in cfg.Alerts)
            {
                if (rule == null || !rule.Enabled) continue;
                if (rule.Stock.Length > 0 && rule.Stock != stockCode) continue;
                double val = 0;
                bool ok = false;
                if (rule.Indicator == "PRICE") { val = q.Price; ok = true; }
                else if (si != null && si.HasBars) { val = StockIndicators.ValueOf(si, rule.Indicator); ok = true; }
                if (!ok) continue;

                bool hit = false;
                string cond = rule.Condition;
                if (cond == ">=") hit = val >= rule.Value;
                else if (cond == "<=") hit = val <= rule.Value;
                else if (cond == "CROSS_UP" || cond == "CROSS_DOWN")
                {
                    double pv = 0;
                    if (rule.Indicator == "PRICE") pv = q.PrevClose;
                    else if (siPrev != null && siPrev.HasBars) pv = StockIndicators.ValueOf(siPrev, rule.Indicator);
                    if (cond == "CROSS_UP") hit = pv < rule.Value && val >= rule.Value;
                    else hit = pv > rule.Value && val <= rule.Value;
                }
                if (!hit) continue;

                string key = stockCode + "|" + rule.Indicator + rule.Condition + rule.Value;
                DateTime now = DateTime.Now;
                DateTime last;
                if (fired.TryGetValue(key, out last) && (now - last).TotalSeconds < 60) continue; // 冷却60秒
                fired[key] = now;
                triggers.Add(stockName + "(" + stockCode + ") " + DisplayIndicator(rule.Indicator) + " " +
                             RuleCondText(cond) + " " + rule.Value.ToString("F2") + " 当前 " + val.ToString("F2"));
            }
            return triggers;
        }

        public static string DisplayIndicator(string name)
        {
            switch (name)
            {
                case "PRICE": return "价格";
                case "MA5": return "MA5";
                case "MA10": return "MA10";
                case "MA20": return "MA20";
                case "MACD_DIF": return "MACD-DIF";
                case "MACD_DEA": return "MACD-DEA";
                case "MACD_HIST": return "MACD柱";
                case "KDJ_K": return "KDJ-K";
                case "KDJ_D": return "KDJ-D";
                case "KDJ_J": return "KDJ-J";
                case "RSI6": return "RSI6";
                case "RSI12": return "RSI12";
                case "RSI24": return "RSI24";
                case "BOLL_UP": return "布林上轨";
                case "BOLL_MID": return "布林中轨";
                case "BOLL_LOW": return "布林下轨";
                case "WR14": return "WR14";
                case "BIAS6": return "BIAS6";
                case "BIAS12": return "BIAS12";
                default: return name;
            }
        }

        private static string RuleCondText(string c)
        {
            if (c == ">=") return "≥";
            if (c == "<=") return "≤";
            if (c == "CROSS_UP") return "上穿";
            if (c == "CROSS_DOWN") return "下穿";
            return c;
        }
    }

    // ================================================================ 全屏红色闪烁提醒

    public class AlertForm : Form
    {
        private readonly System.Windows.Forms.Timer flash;
        private readonly int totalMs;
        private readonly DateTime start;

        public AlertForm(int seconds, bool sound)
        {
            totalMs = Math.Max(1, seconds) * 1000;
            start = DateTime.Now;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.Bounds;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Red;
            Opacity = 0.32;
            Cursor = Cursors.Hand;
            Text = "行情提醒";
            if (sound)
            {
                try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
                try { Console.Beep(900, 250); Console.Beep(1200, 250); } catch { }
            }
            flash = new System.Windows.Forms.Timer { Interval = 280 };
            flash.Tick += delegate
            {
                Opacity = Opacity > 0.4 ? 0.22 : 0.5;
                if ((DateTime.Now - start).TotalMilliseconds > totalMs) Close();
            };
            flash.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Font f = new Font("Microsoft YaHei UI", 40f, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(Color.White))
            {
                string msg = "⚠ 行情提醒 ⚠";
                SizeF sz = e.Graphics.MeasureString(msg, f);
                e.Graphics.DrawString(msg, f, b, (ClientSize.Width - sz.Width) / 2, (ClientSize.Height - sz.Height) / 2 - 30);
            }
            using (Font f = new Font("Microsoft YaHei UI", 14f))
            using (SolidBrush b = new SolidBrush(Color.White))
            {
                string msg = "点击任意处关闭";
                SizeF sz = e.Graphics.MeasureString(msg, f);
                e.Graphics.DrawString(msg, f, b, (ClientSize.Width - sz.Width) / 2, ClientSize.Height / 2 + 30);
            }
        }

        protected override void OnClick(EventArgs e) { Close(); base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { Close(); base.OnKeyDown(e); }
    }

    // ================================================================ 日志

    public static class Log
    {
        private static readonly object Sync = new object();
        private static string FilePath { get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log"); } }

        public static void Error(string where, Exception ex)
        {
            try
            {
                lock (Sync)
                {
                    File.AppendAllText(FilePath,
                        "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + where + "\r\n" + ex + "\r\n\r\n",
                        Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
