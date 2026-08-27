using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace StockMonitor
{
    /// <summary>从分时数据计算分钟级指标序列(与分时主图时间轴对齐)。</summary>
    public static class MinuteSeries
    {
        public static List<IndicatorSeries> Build(List<TrendPoint> pts, List<string> keys)
        {
            List<IndicatorSeries> list = new List<IndicatorSeries>();
            if (pts == null || pts.Count < 15) return list;
            List<double> close = new List<double>(), vol = new List<double>();
            List<string> dates = new List<string>();
            foreach (TrendPoint p in pts) { close.Add(p.Price); vol.Add(p.Volume); dates.Add(p.Time); }

            foreach (string key in keys)
            {
                IndicatorSeries s = BuildOne(key, dates, close, vol);
                if (s != null) list.Add(s);
            }
            return list;
        }

        private static IndicatorSeries BuildOne(string key, List<string> dates, List<double> close, List<double> vol)
        {
            IndicatorSeries s = new IndicatorSeries { Key = key, Dates = dates };
            switch (key)
            {
                case "MACD":
                {
                    s.Title = "MACD(分)";
                    s.Desc = "分钟级 MACD:红绿柱 + DIF/DEA。柱由负转正、DIF上穿DEA = 短线转强。";
                    List<double> dif, dea, hist;
                    Indicators.MACD(close, 12, 26, 9, out dif, out dea, out hist);
                    s.Lines.Add(dif.ToArray()); s.LineNames.Add("DIF"); s.LineColors.Add(Color.FromArgb(255, 120, 90));
                    s.Lines.Add(dea.ToArray()); s.LineNames.Add("DEA"); s.LineColors.Add(Color.FromArgb(255, 200, 60));
                    s.Hist = hist;
                    return s;
                }
                case "KDJ":
                {
                    s.Title = "KDJ(分)";
                    s.Desc = "分钟级 KDJ(以分钟收盘价近似高低点)。J>100超买、J<0超卖;K上穿D=金叉。";
                    // 用滚动窗口内收盘价的最大/最小近似高低点
                    List<double> high = RollingMax(close, 9), low = RollingMin(close, 9);
                    List<double> k, d, j;
                    Indicators.KDJ(high, low, close, 9, 3, 3, out k, out d, out j);
                    s.Lines.Add(k.ToArray()); s.LineNames.Add("K"); s.LineColors.Add(Color.FromArgb(90, 170, 255));
                    s.Lines.Add(d.ToArray()); s.LineNames.Add("D"); s.LineColors.Add(Color.FromArgb(255, 200, 60));
                    s.Lines.Add(j.ToArray()); s.LineNames.Add("J"); s.LineColors.Add(Color.FromArgb(255, 120, 200));
                    s.Bands = new[] { 80.0, 20.0 }; s.BandNames = new[] { "80", "20" };
                    return s;
                }
                case "RSI6":
                {
                    s.Title = "RSI6(分)";
                    s.Desc = "分钟级 RSI6:>70超买、<30超卖。";
                    List<double> rsi = Indicators.RSI(close, 6);
                    s.Lines.Add(rsi.ToArray()); s.LineNames.Add("RSI6"); s.LineColors.Add(Color.FromArgb(255, 150, 90));
                    s.Bands = new[] { 70.0, 30.0 }; s.BandNames = new[] { "70", "30" };
                    return s;
                }
                case "BOLL":
                {
                    s.Title = "BOLL(分)";
                    s.Desc = "分钟级布林带(中轨=MA20分钟)。触上轨偏超买、触下轨偏超卖。";
                    List<double> mid, up, lowB;
                    Indicators.BOLL(close, 20, 2, out mid, out up, out lowB);
                    s.Lines.Add(up.ToArray()); s.LineNames.Add("上"); s.LineColors.Add(Color.FromArgb(120, 200, 255));
                    s.Lines.Add(mid.ToArray()); s.LineNames.Add("中"); s.LineColors.Add(Color.FromArgb(255, 200, 60));
                    s.Lines.Add(lowB.ToArray()); s.LineNames.Add("下"); s.LineColors.Add(Color.FromArgb(120, 200, 255));
                    s.Lines.Add(close.ToArray()); s.LineNames.Add("价"); s.LineColors.Add(Color.FromArgb(235, 235, 240));
                    s.ShowZero = false;
                    return s;
                }
                case "WR14":
                {
                    s.Title = "WR14(分)";
                    s.Desc = "分钟级威廉指标(收盘价近似)。>80超卖反弹、<20超买回调。";
                    List<double> high = RollingMax(close, 14), low = RollingMin(close, 14);
                    List<double> wr = Indicators.WR(high, low, close, 14);
                    s.Lines.Add(wr.ToArray()); s.LineNames.Add("WR14"); s.LineColors.Add(Color.FromArgb(120, 200, 255));
                    s.Bands = new[] { 80.0, 20.0 }; s.BandNames = new[] { "80", "20" };
                    return s;
                }
                case "BIAS6":
                {
                    s.Title = "BIAS6(分)";
                    s.Desc = "分钟级乖离率:价格偏离6分钟均线幅度。";
                    List<double> bias = Indicators.BIAS(close, 6);
                    s.Lines.Add(bias.ToArray()); s.LineNames.Add("BIAS6"); s.LineColors.Add(Color.FromArgb(255, 150, 90));
                    return s;
                }
                case "VOL":
                {
                    s.Title = "量能(分)";
                    s.Desc = "每分钟成交量柱 + 5分钟均量线。";
                    s.Hist = new List<double>(vol);
                    List<double> vma = Indicators.MA(vol, 5);
                    s.Lines.Add(vma.ToArray()); s.LineNames.Add("量MA5"); s.LineColors.Add(Color.FromArgb(255, 200, 60));
                    s.ShowZero = false;
                    return s;
                }
                default:
                    return null;
            }
        }

        private static List<double> RollingMax(List<double> v, int n)
        {
            List<double> r = new List<double>();
            for (int i = 0; i < v.Count; i++)
            {
                double m = double.MinValue;
                for (int x = Math.Max(0, i - n + 1); x <= i; x++) if (v[x] > m) m = v[x];
                r.Add(m);
            }
            return r;
        }

        private static List<double> RollingMin(List<double> v, int n)
        {
            List<double> r = new List<double>();
            for (int i = 0; i < v.Count; i++)
            {
                double m = double.MaxValue;
                for (int x = Math.Max(0, i - n + 1); x <= i; x++) if (v[x] < m) m = v[x];
                r.Add(m);
            }
            return r;
        }

        /// <summary>分时主图叠加的分钟均线(MA5/MA10/MA20)。</summary>
        public static List<KeyValuePair<string, double[]>> MaOverlays(List<TrendPoint> pts, List<string> keys)
        {
            List<KeyValuePair<string, double[]>> r = new List<KeyValuePair<string, double[]>>();
            if (pts == null || pts.Count < 5) return r;
            List<double> close = new List<double>();
            foreach (TrendPoint p in pts) close.Add(p.Price);
            foreach (string key in keys)
                if (key == "MA5" || key == "MA10" || key == "MA20")
                {
                    List<double> ma = Indicators.MA(close, int.Parse(key.Substring(2)));
                    r.Add(new KeyValuePair<string, double[]>(key, ma.ToArray()));
                }
            return r;
        }
    }

    /// <summary>一组可绘制的指标序列。</summary>
    public class IndicatorSeries
    {
        public string Key;                 // 配置键
        public string Title;               // 显示标题
        public string Desc;                // 说明
        public List<string> Dates = new List<string>();
        public List<double[]> Lines = new List<double[]>();   // 每条线
        public List<string> LineNames = new List<string>();
        public List<Color> LineColors = new List<Color>();
        public List<double> Hist = new List<double>();        // 可选:柱状(如MACD柱)
        public double[] Bands;             // 可选:参考线(如 RSI 70/30)
        public string[] BandNames;
        public bool ShowZero = true;

        public double LastValue(string name)
        {
            for (int i = 0; i < LineNames.Count; i++)
                if (LineNames[i] == name && Lines[i].Length > 0)
                    return Lines[i][Lines[i].Length - 1];
            return 0;
        }
    }

    /// <summary>从 K 线计算全部指标序列。</summary>
    public static class IndicatorFactory
    {
        public static List<IndicatorSeries> ComputeAll(KlineData kd, List<string> keys)
        {
            List<IndicatorSeries> list = new List<IndicatorSeries>();
            if (kd == null || kd.Bars.Count < 30) return list;
            List<double> close = new List<double>(), high = new List<double>(), low = new List<double>(), vol = new List<double>();
            List<string> dates = new List<string>();
            foreach (Bar b in kd.Bars) { close.Add(b.Close); high.Add(b.High); low.Add(b.Low); vol.Add(b.Volume); dates.Add(b.Date); }

            foreach (string key in keys)
            {
                IndicatorSeries s = Build(key, dates, close, high, low, vol);
                if (s != null) list.Add(s);
            }
            return list;
        }

        private static IndicatorSeries Build(string key, List<string> dates, List<double> close, List<double> high, List<double> low, List<double> vol)
        {
            IndicatorSeries s = new IndicatorSeries { Key = key, Dates = dates };
            switch (key)
            {
                case "MA5":
                case "MA10":
                case "MA20":
                {
                    s.Title = key + " 均线";
                    s.Desc = "收盘价 " + key.Substring(2) + " 日均值,判断趋势。多头排列(MA5>MA10>MA20)看涨,空头排列看跌。";
                    s.Lines.Add(close.ToArray()); s.LineNames.Add("收盘"); s.LineColors.Add(Color.FromArgb(90, 170, 255));
                    List<double> ma = Indicators.MA(close, int.Parse(key.Substring(2)));
                    s.Lines.Add(ToArr(ma)); s.LineNames.Add(key); s.LineColors.Add(Color.FromArgb(255, 200, 60));
                    return s;
                }
                case "MACD":
                {
                    s.Title = "MACD (12,26,9)";
                    s.Desc = "DIF=快线(EMA12-EMA26),DEA=慢线(DIF的9日EMA),柱=2×(DIF-DEA)。DIF上穿DEA=金叉看涨;下穿=死叉看跌;柱由负转正=多头转强。";
                    List<double> dif, dea, hist;
                    Indicators.MACD(close, 12, 26, 9, out dif, out dea, out hist);
                    s.Lines.Add(ToArr(dif)); s.LineNames.Add("DIF"); s.LineColors.Add(Color.FromArgb(255, 120, 90));
                    s.Lines.Add(ToArr(dea)); s.LineNames.Add("DEA"); s.LineColors.Add(Color.FromArgb(255, 200, 60));
                    s.Hist = hist;
                    return s;
                }
                case "KDJ":
                {
                    s.Title = "KDJ (9,3,3)";
                    s.Desc = "随机指标。J>100 超买、J<0 超卖;K上穿D=金叉看涨,下穿=死叉看跌;80/20 为超买超卖参考线。";
                    List<double> k, d, j;
                    Indicators.KDJ(high, low, close, 9, 3, 3, out k, out d, out j);
                    s.Lines.Add(ToArr(k)); s.LineNames.Add("K"); s.LineColors.Add(Color.FromArgb(90, 170, 255));
                    s.Lines.Add(ToArr(d)); s.LineNames.Add("D"); s.LineColors.Add(Color.FromArgb(255, 200, 60));
                    s.Lines.Add(ToArr(j)); s.LineNames.Add("J"); s.LineColors.Add(Color.FromArgb(255, 120, 200));
                    s.Bands = new[] { 80.0, 20.0 }; s.BandNames = new[] { "80", "20" };
                    return s;
                }
                case "RSI6":
                case "RSI12":
                case "RSI24":
                {
                    int n = int.Parse(key.Substring(3));
                    s.Title = "RSI" + n + " 相对强弱";
                    s.Desc = "相对强弱指标。>70 超买(回调风险),<30 超卖(反弹机会);50 为强弱分界。";
                    List<double> rsi = Indicators.RSI(close, n);
                    s.Lines.Add(ToArr(rsi)); s.LineNames.Add("RSI" + n); s.LineColors.Add(Color.FromArgb(255, 150, 90));
                    s.Bands = new[] { 70.0, 30.0, 50.0 }; s.BandNames = new[] { "70", "30", "50" };
                    return s;
                }
                case "BOLL":
                {
                    s.Title = "BOLL (20,2) 布林带";
                    s.Desc = "中轨=MA20,上/下轨=±2倍标准差。触上轨偏超买、触下轨偏超卖;开口放大=波动加剧。";
                    List<double> mid, up, lowB;
                    Indicators.BOLL(close, 20, 2, out mid, out up, out lowB);
                    s.Lines.Add(ToArr(up)); s.LineNames.Add("上轨"); s.LineColors.Add(Color.FromArgb(120, 200, 255));
                    s.Lines.Add(ToArr(mid)); s.LineNames.Add("中轨"); s.LineColors.Add(Color.FromArgb(255, 200, 60));
                    s.Lines.Add(ToArr(lowB)); s.LineNames.Add("下轨"); s.LineColors.Add(Color.FromArgb(120, 200, 255));
                    s.Lines.Add(close.ToArray()); s.LineNames.Add("收盘"); s.LineColors.Add(Color.FromArgb(235, 235, 240));
                    s.ShowZero = false;
                    return s;
                }
                case "WR14":
                {
                    s.Title = "WR14 威廉指标";
                    s.Desc = "威廉指标。>80 超卖(反弹信号),<20 超买(回调信号)。与 KDJ 配合使用更佳。";
                    List<double> wr = Indicators.WR(high, low, close, 14);
                    s.Lines.Add(ToArr(wr)); s.LineNames.Add("WR14"); s.LineColors.Add(Color.FromArgb(120, 200, 255));
                    s.Bands = new[] { 80.0, 20.0 }; s.BandNames = new[] { "80", "20" };
                    return s;
                }
                case "BIAS6":
                case "BIAS12":
                {
                    int n = int.Parse(key.Substring(4));
                    s.Title = "BIAS" + n + " 乖离率";
                    s.Desc = "价格偏离均线的百分比。过大(正)超买、过小(负)超卖,有回归均线的需求。";
                    List<double> bias = Indicators.BIAS(close, n);
                    s.Lines.Add(ToArr(bias)); s.LineNames.Add("BIAS" + n); s.LineColors.Add(Color.FromArgb(255, 150, 90));
                    return s;
                }
                case "VOLMA5":
                {
                    s.Title = "成交量 + MA5";
                    s.Desc = "每日成交量柱 + 5日均量线。放量上涨=资金进场,放量下跌=恐慌出货。";
                    s.Hist = new List<double>(vol);
                    List<double> vma = Indicators.MA(vol, 5);
                    s.Lines.Add(ToArr(vma)); s.LineNames.Add("量MA5"); s.LineColors.Add(Color.FromArgb(255, 200, 60));
                    return s;
                }
                default:
                    return null;
            }
        }

        private static double[] ToArr(List<double> v) { return v.ToArray(); }
    }

    /// <summary>通用指标图表绘制。</summary>
    public static class ChartPainter
    {
        /// <summary>计算可视区间。</summary>
        public static int Slice(IndicatorSeries s, int showCount, out int n, out int start)
        {
            n = Math.Min(s.Dates.Count, showCount > 0 ? showCount : s.Dates.Count);
            start = s.Dates.Count - n;
            return n;
        }

        /// <summary>与 Draw 一致的 Y 值范围(含 padding)。</summary>
        public static void GetRange(IndicatorSeries s, int showCount, out double lo, out double hi)
        {
            int n, start;
            Slice(s, showCount, out n, out start);
            lo = double.MaxValue; hi = double.MinValue;
            foreach (double[] line in s.Lines)
                for (int i = start; i < s.Dates.Count; i++)
                    if (!double.IsNaN(line[i])) { if (line[i] < lo) lo = line[i]; if (line[i] > hi) hi = line[i]; }
            if (s.Hist.Count > 0)
                for (int i = start; i < s.Hist.Count; i++)
                { double v = s.Hist[i]; if (v < lo) lo = v; if (v > hi) hi = v; }
            if (s.Bands != null)
                foreach (double b in s.Bands) { if (b < lo) lo = b; if (b > hi) hi = b; }
            if (s.ShowZero && lo > 0) lo = 0;
            if (hi - lo < 1e-9) { lo -= 1; hi += 1; }
            double pad = (hi - lo) * 0.06; lo -= pad; hi += pad;
        }

        public static float MapX(Rectangle rc, int n, int start, int idx)
        {
            return rc.X + (float)(rc.Width * (1.0 * (idx - start) / Math.Max(1, n - 1)));
        }

        public static float MapY(Rectangle rc, double lo, double hi, double v)
        {
            return rc.Y + (float)((hi - v) / (hi - lo) * rc.Height);
        }

        /// <summary>由鼠标 x 反推数据索引。</summary>
        public static int HitTest(Rectangle rc, int n, int start, int mouseX)
        {
            if (n <= 1 || rc.Width <= 0) return -1;
            double t = (mouseX - rc.X) / (double)rc.Width;
            int idx = start + (int)Math.Round(t * (n - 1));
            if (idx < 0) idx = 0;
            if (idx >= start + n) idx = start + n - 1;
            return idx;
        }

        /// <summary>某根K线的详细数值文本(悬浮提示用)。</summary>
        public static string TooltipText(IndicatorSeries s, int idx)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(s.Title).Append("\r\n").Append(s.Dates[idx]);
            for (int li = 0; li < s.Lines.Count; li++)
            {
                double v = idx < s.Lines[li].Length ? s.Lines[li][idx] : double.NaN;
                sb.Append("\r\n").Append(s.LineNames[li]).Append("  ").Append(double.IsNaN(v) ? "-" : v.ToString("F2"));
            }
            if (s.Hist.Count > 0 && idx < s.Hist.Count)
                sb.Append("\r\n柱  ").Append(s.Hist[idx].ToString("F2"));
            return sb.ToString();
        }

        /// <summary>坐标轴标签缩短:含时间的显示 HH:MM,纯日期原样显示。</summary>
        public static string ShortLabel(string t)
        {
            if (t == null) return "";
            int sp = t.IndexOf(' ');
            if (sp > 0 && t.Length > sp + 5) return t.Substring(sp + 1, 5);
            return t;
        }

        public static void Draw(Graphics g, Rectangle rc, IndicatorSeries s, int showCount, bool withGrid)
        {
            if (rc.Width < 40 || rc.Height < 30) return;
            int n = Math.Min(s.Dates.Count, showCount > 0 ? showCount : s.Dates.Count);
            int start = s.Dates.Count - n;

            // 收集 y 范围
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (double[] line in s.Lines)
                for (int i = start; i < s.Dates.Count; i++)
                    if (!double.IsNaN(line[i])) { if (line[i] < lo) lo = line[i]; if (line[i] > hi) hi = line[i]; }
            if (s.Hist.Count > 0)
                for (int i = start; i < s.Hist.Count; i++)
                { double v = s.Hist[i]; if (v < lo) lo = v; if (v > hi) hi = v; }
            if (s.Bands != null)
                foreach (double b in s.Bands) { if (b < lo) lo = b; if (b > hi) hi = b; }
            if (s.ShowZero && lo > 0) lo = 0;
            if (hi - lo < 1e-9) { lo -= 1; hi += 1; }
            double pad = (hi - lo) * 0.06; lo -= pad; hi += pad;

            int X0 = rc.X, X1 = rc.Right, Y0 = rc.Y, Y1 = rc.Bottom;
            Func<int, float> X = i => X0 + (float)((X1 - X0) * (1.0 * (i - start) / Math.Max(1, n - 1)));
            Func<double, float> Y = v => Y0 + (float)((hi - v) / (hi - lo) * (Y1 - Y0));

            // 网格 + y 轴
            using (Pen grid = new Pen(Color.FromArgb(46, 50, 62), 1f))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(120, 128, 146)))
            using (Font f = new Font("Microsoft YaHei UI", 7f))
            {
                for (int i = 0; i <= 3; i++)
                {
                    double v = hi - (hi - lo) * i / 3.0;
                    float y = Y(v);
                    g.DrawLine(grid, X0, y, X1, y);
                    g.DrawString(v.ToString("F1"), f, tb, X0 + 2, y - 7);
                }
                // x 轴 3 个时间点
                int[] xi = { 0, n / 2, n - 1 };
                foreach (int k in xi)
                {
                    int idx = start + k;
                    if (idx >= 0 && idx < s.Dates.Count)
                    {
                        string t = ShortLabel(s.Dates[idx]);
                        SizeF ts = g.MeasureString(t, f);
                        float x = X(idx) - ts.Width / 2f;
                        if (x < X0) x = X0;
                        if (x + ts.Width > X1) x = X1 - ts.Width;
                        g.DrawString(t, f, tb, x, Y1 + 1);
                    }
                }
            }

            // 参考线
            if (s.Bands != null)
            {
                using (Pen bp = new Pen(Color.FromArgb(120, 120, 140), 1f) { DashStyle = DashStyle.Dash })
                for (int b = 0; b < s.Bands.Length; b++)
                    if (s.Bands[b] >= lo && s.Bands[b] <= hi)
                        g.DrawLine(bp, X0, Y(s.Bands[b]), X1, Y(s.Bands[b]));
            }

            // 零线
            if (s.ShowZero && lo < 0 && hi > 0)
                using (Pen zp = new Pen(Color.FromArgb(90, 100, 120), 1f))
                    g.DrawLine(zp, X0, Y(0), X1, Y(0));

            // 柱状(MACD 柱 / 成交量)
            if (s.Hist.Count > 0)
            {
                using (SolidBrush r = new SolidBrush(Color.FromArgb(232, 84, 84)))
                using (SolidBrush gb = new SolidBrush(Color.FromArgb(72, 180, 120)))
                {
                    float bw = Math.Max(1f, (float)(X1 - X0) / n * 0.6f);
                    for (int i = start; i < s.Dates.Count; i++)
                    {
                        float x = X(i) - bw / 2f;
                        float y0, y1;
                        if (s.ShowZero) { y0 = Y(0); y1 = Y(s.Hist[i]); }
                        else { y0 = Y1; y1 = Y(s.Hist[i]); }
                        if (y0 == y1) y1 = y0 + 0.5f;
                        g.FillRectangle(s.Hist[i] >= 0 ? r : gb, x, Math.Min(y0, y1), bw, Math.Abs(y1 - y0));
                    }
                }
            }

            // 折线
            for (int li = 0; li < s.Lines.Count; li++)
            {
                double[] vals = s.Lines[li];
                using (Pen pen = new Pen(s.LineColors[li], 1.2f))
                {
                    bool started = false;
                    for (int i = start; i < s.Dates.Count; i++)
                    {
                        if (double.IsNaN(vals[i])) { started = false; continue; }
                        if (!started) { started = true; continue; }
                        if (!double.IsNaN(vals[i - 1]))
                            g.DrawLine(pen, X(i - 1), Y(vals[i - 1]), X(i), Y(vals[i]));
                    }
                }
            }

            // 图例(线名 + 最新值)
            using (Font f = new Font("Microsoft YaHei UI", 7f))
            {
                float lx = X0 + 4;
                for (int li = 0; li < s.Lines.Count; li++)
                {
                    double[] vals = s.Lines[li];
                    double last = vals.Length > 0 && !double.IsNaN(vals[vals.Length - 1]) ? vals[vals.Length - 1] : 0;
                    string txt = s.LineNames[li] + " " + last.ToString("F2");
                    SizeF sz = g.MeasureString(txt, f);
                    using (SolidBrush b = new SolidBrush(s.LineColors[li]))
                        g.DrawString(txt, f, b, lx, Y0 + 2);
                    lx += sz.Width + 8;
                }
            }
        }
    }

    /// <summary>小格子单元格:标题 + 迷你图,点击放大,悬浮显示数值。</summary>
    public class IndicatorCell : Control
    {
        public IndicatorSeries Series;
        public int ShowCount;
        public event Action<IndicatorSeries> Enlarge;
        private int hoverIdx = -1;
        private readonly ToolTip tip;

        public IndicatorCell(IndicatorSeries s, int showCount)
        {
            Series = s;
            ShowCount = showCount;
            tip = new ToolTip { ShowAlways = true, AutoPopDelay = 60000, InitialDelay = 0, ReshowDelay = 0 };
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(24, 26, 36);
            Cursor = Cursors.Hand;
        }

        private Rectangle ChartRect { get { return new Rectangle(6, 22, Width - 12, Height - 30); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            using (Pen border = new Pen(Color.FromArgb(56, 62, 80), 1f))
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(200, 208, 222)))
            using (Font tf = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold))
                g.DrawString(Series.Title, tf, tb, 6, 4);
            Rectangle rc = ChartRect;
            if (Series.Dates.Count > 0)
            {
                ChartPainter.Draw(g, rc, Series, ShowCount, false);
                DrawCrosshair(g, rc);
            }
            else
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(120, 128, 146)))
                    g.DrawString("数据不足", new Font("Microsoft YaHei UI", 8f), sb, rc.X, rc.Y);
        }

        private void DrawCrosshair(Graphics g, Rectangle rc)
        {
            if (hoverIdx < 0 || Series.Dates.Count == 0) return;
            int n, start;
            ChartPainter.Slice(Series, ShowCount, out n, out start);
            if (hoverIdx < start || hoverIdx >= start + n) return;
            double lo, hi;
            ChartPainter.GetRange(Series, ShowCount, out lo, out hi);
            float x = ChartPainter.MapX(rc, n, start, hoverIdx);
            using (Pen cp = new Pen(Color.FromArgb(140, 150, 175), 1f) { DashStyle = DashStyle.Dash })
                g.DrawLine(cp, x, rc.Y, x, rc.Bottom);
            using (SolidBrush dot = new SolidBrush(Color.FromArgb(255, 255, 255)))
            {
                for (int li = 0; li < Series.Lines.Count; li++)
                {
                    double v = hoverIdx < Series.Lines[li].Length ? Series.Lines[li][hoverIdx] : double.NaN;
                    if (double.IsNaN(v)) continue;
                    float y = ChartPainter.MapY(rc, lo, hi, v);
                    g.FillEllipse(dot, x - 2.5f, y - 2.5f, 5, 5);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Series.Dates.Count == 0) return;
            int n, start;
            ChartPainter.Slice(Series, ShowCount, out n, out start);
            int idx = ChartPainter.HitTest(ChartRect, n, start, e.X);
            if (idx != hoverIdx) { hoverIdx = idx; Invalidate(); }
            tip.Show(ChartPainter.TooltipText(Series, idx), this, new Point(e.X + 10, e.Y + 10));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverIdx = -1;
            tip.Hide(this);
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (Enlarge != null) Enlarge(Series);
        }
    }

    /// <summary>指标图墙:格子排列,点击放大。</summary>
    public class IndicatorGalleryForm : Form
    {
        private readonly FlowLayoutPanel flow;

        public IndicatorGalleryForm(KlineData kd, List<string> keys)
        {
            Text = "指标图 - " + (kd != null ? kd.Name + " " + kd.Code : "");
            Font = new Font("Microsoft YaHei UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(860, 620);
            MinimumSize = new Size(640, 420);
            BackColor = Color.FromArgb(20, 22, 30);

            flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(20, 22, 30),
                Padding = new Padding(8)
            };
            Controls.Add(flow);

            List<IndicatorSeries> list = IndicatorFactory.ComputeAll(kd, keys);
            foreach (IndicatorSeries s in list)
            {
                IndicatorCell cell = new IndicatorCell(s, 90) { Size = new Size(268, 150), Margin = new Padding(6) };
                cell.Enlarge += delegate(IndicatorSeries series) { OpenDetail(series, kd); };
                flow.Controls.Add(cell);
            }
            if (list.Count == 0)
            {
                Label lb = new Label
                {
                    Text = "暂无指标数据(请先在「配置 → 指标显示」勾选指标,或等待K线刷新)",
                    AutoSize = true, ForeColor = Color.FromArgb(150, 158, 175),
                    Location = new Point(20, 20)
                };
                Controls.Add(lb); lb.BringToFront();
            }
            Label tip = new Label
            {
                Text = "点击任意格子放大查看", AutoSize = true,
                ForeColor = Color.FromArgb(120, 128, 146),
                Location = new Point(12, ClientSize.Height - 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            Controls.Add(tip); tip.BringToFront();
        }

        private void OpenDetail(IndicatorSeries s, KlineData kd)
        {
            using (IndicatorDetailForm f = new IndicatorDetailForm(s, kd))
                f.ShowDialog(this);
        }
    }

    /// <summary>指标大图窗口(悬浮显示数值)。</summary>
    public class IndicatorDetailForm : Form
    {
        private readonly IndicatorSeries series;
        private readonly KlineData kd;
        private int showCount;
        private int hoverIdx = -1;
        private readonly ToolTip tip;
        private Label lblInfo;

        public IndicatorDetailForm(IndicatorSeries s, KlineData kd)
        {
            series = s;
            this.kd = kd;
            showCount = 0;
            tip = new ToolTip { ShowAlways = true, AutoPopDelay = 60000, InitialDelay = 0, ReshowDelay = 0 };
            Text = s.Title + " - " + (kd != null ? kd.Name : "");
            Font = new Font("Microsoft YaHei UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(860, 560);
            MinimumSize = new Size(640, 420);
            BackColor = Color.FromArgb(20, 22, 30);
            DoubleBuffered = true;
            Paint += OnPaintChart;
            MouseMove += delegate(object sender, MouseEventArgs e) { OnHoverMove(e); };
            MouseLeave += delegate { hoverIdx = -1; tip.Hide(this); Invalidate(); };

            ComboBox cmbRange = new ComboBox
            {
                Location = new Point(12, 10), Width = 130, DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbRange.Items.AddRange(new object[] { "最近30日", "最近60日", "最近120日", "全部" });
            cmbRange.SelectedIndex = 3;
            cmbRange.SelectedIndexChanged += delegate
            {
                int[] map = { 30, 60, 120, 0 };
                showCount = map[cmbRange.SelectedIndex];
                hoverIdx = -1;
                Invalidate();
            };
            lblInfo = new Label
            {
                Location = new Point(160, 13), AutoSize = true,
                ForeColor = Color.FromArgb(170, 178, 195)
            };
            Label desc = new Label
            {
                Dock = DockStyle.Bottom, Height = 64, Text = "说明: " + s.Desc,
                ForeColor = Color.FromArgb(140, 148, 168), Padding = new Padding(12, 6, 12, 6)
            };
            Controls.Add(cmbRange); Controls.Add(lblInfo); Controls.Add(desc);
            UpdateInfo();
        }

        private void OnHoverMove(MouseEventArgs e)
        {
            if (series.Dates.Count == 0) return;
            Rectangle rc = new Rectangle(12, 44, ClientSize.Width - 28, ClientSize.Height - 130);
            if (!rc.Contains(e.Location)) { hoverIdx = -1; tip.Hide(this); Invalidate(); return; }
            int n, start;
            ChartPainter.Slice(series, showCount, out n, out start);
            int idx = ChartPainter.HitTest(rc, n, start, e.X);
            if (idx != hoverIdx) { hoverIdx = idx; Invalidate(); }
            tip.Show(ChartPainter.TooltipText(series, idx), this, new Point(e.X + 12, e.Y + 12));
        }

        private void UpdateInfo()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < series.LineNames.Count; i++)
            {
                double v = series.Lines[i].Length > 0 ? series.Lines[i][series.Lines[i].Length - 1] : 0;
                if (i > 0) sb.Append("    ");
                sb.Append(series.LineNames[i]).Append(" = ").Append(v.ToString("F2"));
            }
            if (series.Hist.Count > 0)
                sb.Append("    柱 = ").Append(series.Hist[series.Hist.Count - 1].ToString("F2"));
            lblInfo.Text = sb.ToString();
        }

        private void OnPaintChart(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(20, 22, 30));
            Rectangle rc = new Rectangle(12, 44, ClientSize.Width - 28, ClientSize.Height - 130);
            if (series.Dates.Count > 0)
            {
                ChartPainter.Draw(g, rc, series, showCount, true);
                if (hoverIdx >= 0)
                {
                    int n, start;
                    ChartPainter.Slice(series, showCount, out n, out start);
                    if (hoverIdx >= start && hoverIdx < start + n)
                    {
                        double lo, hi;
                        ChartPainter.GetRange(series, showCount, out lo, out hi);
                        float x = ChartPainter.MapX(rc, n, start, hoverIdx);
                        using (Pen cp = new Pen(Color.FromArgb(150, 160, 185), 1f) { DashStyle = DashStyle.Dash })
                        {
                            g.DrawLine(cp, x, rc.Y, x, rc.Bottom);
                            g.DrawLine(cp, rc.X, rc.Y, rc.Right, rc.Y);
                        }
                        using (SolidBrush dot = new SolidBrush(Color.FromArgb(255, 255, 255)))
                        {
                            for (int li = 0; li < series.Lines.Count; li++)
                            {
                                double v = hoverIdx < series.Lines[li].Length ? series.Lines[li][hoverIdx] : double.NaN;
                                if (double.IsNaN(v)) continue;
                                float y = ChartPainter.MapY(rc, lo, hi, v);
                                g.FillEllipse(dot, x - 3f, y - 3f, 6, 6);
                            }
                        }
                        // 悬浮日期标注
                        using (SolidBrush bg = new SolidBrush(Color.FromArgb(210, 40, 44, 56)))
                        using (SolidBrush tb = new SolidBrush(Color.FromArgb(235, 235, 240)))
                        using (Font f = new Font("Microsoft YaHei UI", 9f))
                        {
                            string d = series.Dates[hoverIdx];
                            SizeF sz = g.MeasureString(d, f);
                            float dx = x - sz.Width / 2f;
                            if (dx < rc.X) dx = rc.X;
                            if (dx + sz.Width > rc.Right) dx = rc.Right - sz.Width;
                            g.FillRectangle(bg, dx, rc.Y + 2, sz.Width + 8, sz.Height + 4);
                            g.DrawString(d, f, tb, dx + 4, rc.Y + 4);
                        }
                    }
                }
            }
        }
    }
}
