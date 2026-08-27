using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace StockMonitor
{
    /// <summary>透明悬浮主窗口。</summary>
    public class FloatingWidget : Form
    {
        private TextBox txtCode;
        private Button btnQuery, btnConfig, btnChart, btnDonate, btnMin, btnClose, btnMode;
        private ComboBox cmbWatch;

        private AppConfig cfg;
        private readonly string cfgPath = "config.json";
        private readonly List<string> watch = new List<string>();
        private readonly Dictionary<string, Quote> quotes = new Dictionary<string, Quote>();
        private readonly Dictionary<string, KlineData> klines = new Dictionary<string, KlineData>();
        private string selected = "";
        private List<TrendPoint> curTrend;
        private StockIndicators curInd;
        private List<IndicatorSeries> minuteSeries = new List<IndicatorSeries>();
        private List<IndicatorSeries> dailySeries = new List<IndicatorSeries>();
        private bool dailyMode;
        private List<KeyValuePair<string, double[]>> maOverlays = new List<KeyValuePair<string, double[]>>();
        private string status = "就绪";
        private readonly AlertEngine engine = new AlertEngine();
        private bool busy;
        private int tick;
        private int alpha = 235;
        private bool minimized;
        private Size normalSize;
        private bool dragging;
        private Point dragOffset;

        // 全局热键
        private const int HOTKEY_ID = 0x5A11;
        private bool hotkeyRegistered;
        private int hotMods, hotVk;
        private System.Windows.Forms.Timer restoreTimer;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        private int hoverTrend = -1;
        private int hoverCell = -1;      // 当前悬浮的指标格子索引
        private int hoverCellIdx = -1;   // 格内悬浮的数据索引
        private readonly List<Rectangle> cellRects = new List<Rectangle>();
        private int scrollOffset;
        private int maxScroll;
        private int contentH;
        private bool scrollDrag;
        private int scrollDragStartY;
        private int scrollDragStartOffset;
        private ListBox lstSearch;
        private readonly List<string> searchCodes = new List<string>();
        private System.Windows.Forms.Timer searchTimer;
        private int searchSeq;
        private readonly ToolTip tip = new ToolTip { ShowAlways = true, AutoPopDelay = 60000, InitialDelay = 0, ReshowDelay = 0 };

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
        private const uint LWA_ALPHA = 0x2;
        private const int WS_EX_LAYERED = 0x80000;

        private System.Windows.Forms.Timer timer;

        public FloatingWidget()
        {
            cfg = ConfigIO.Load();
            Text = "实时股价监控";
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(60, 40);
            if (cfg.WinW >= 320 && cfg.WinH >= 260)
                ClientSize = new Size(cfg.WinW, cfg.WinH);
            else
                ClientSize = new Size(760, 560);
            if (cfg.WinX >= 0 && cfg.WinY >= 0) Location = new Point(cfg.WinX, cfg.WinY);
            MinimumSize = new Size(420, 320);
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
            Font = new Font("Microsoft YaHei UI", 9f);

            watch.Clear();
            foreach (string c in cfg.Watch) if (!watch.Contains(c)) watch.Add(c);

            BuildUi();
            selected = watch.Count > 0 ? watch[0] : "";
            timer = new System.Windows.Forms.Timer { Interval = Math.Max(2, cfg.RefreshSeconds) * 1000 };
            timer.Tick += delegate { KickRefresh(); };
            timer.Start();
            restoreTimer = new System.Windows.Forms.Timer { Interval = 240 };
            restoreTimer.Tick += delegate { restoreTimer.Stop(); Restore(); };
            KickRefresh();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { SetLayeredWindowAttributes(Handle, 0, (byte)alpha, LWA_ALPHA); } catch { }
            RegisterHotkey();
        }

        private void BuildUi()
        {
            txtCode = new TextBox
            {
                Location = new Point(8, 10), Width = 68,
                BackColor = Color.FromArgb(36, 38, 48), ForeColor = Color.FromArgb(235, 235, 240),
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9.5f)
            };
            txtCode.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (lstSearch.Visible)
                {
                    if (e.KeyCode == Keys.Down) { if (lstSearch.SelectedIndex < lstSearch.Items.Count - 1) { lstSearch.SelectedIndex++; e.Handled = true; } }
                    else if (e.KeyCode == Keys.Up) { if (lstSearch.SelectedIndex > 0) { lstSearch.SelectedIndex--; e.Handled = true; } }
                    else if (e.KeyCode == Keys.Enter) { PickSearchSelection(); e.Handled = true; }
                    else if (e.KeyCode == Keys.Escape) { HideSearch(); e.Handled = true; }
                }
                else if (e.KeyCode == Keys.Enter) DoAdd();
            };
            txtCode.TextChanged += delegate
            {
                if (searchTimer != null) { searchTimer.Stop(); searchTimer.Start(); }
            };

            // 名称搜索下拉
            lstSearch = new ListBox
            {
                Location = new Point(12, 40), Width = 250, Height = 150, Visible = false,
                BackColor = Color.FromArgb(30, 32, 44), ForeColor = Color.FromArgb(235, 235, 240),
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Microsoft YaHei UI", 9f)
            };
            lstSearch.MouseClick += delegate { PickSearchSelection(); };
            lstSearch.DoubleClick += delegate { PickSearchSelection(); };

            searchTimer = new System.Windows.Forms.Timer { Interval = 380 };
            searchTimer.Tick += delegate { searchTimer.Stop(); DoNameSearch(txtCode.Text, false); };

            btnQuery = new Button { Text = "查询", Location = new Point(80, 9), Width = 46, Height = 26,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(46, 52, 72), ForeColor = Color.FromArgb(235, 235, 240),
                FlatAppearance = { BorderColor = Color.FromArgb(80, 90, 120) } };
            btnQuery.Click += delegate { DoAdd(); };

            btnConfig = new Button { Text = "配置", Location = new Point(130, 9), Width = 46, Height = 26,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(46, 52, 72), ForeColor = Color.FromArgb(235, 235, 240),
                FlatAppearance = { BorderColor = Color.FromArgb(80, 90, 120) } };
            btnConfig.Click += delegate { OpenConfig(); };

            btnChart = new Button { Text = "指标图", Location = new Point(180, 9), Width = 56, Height = 26,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(46, 52, 72), ForeColor = Color.FromArgb(235, 235, 240),
                FlatAppearance = { BorderColor = Color.FromArgb(80, 90, 120) } };
            btnChart.Click += delegate { OpenGallery(); };

            btnDonate = new Button { Text = "捐赠", Location = new Point(ClientSize.Width - 96, 9), Width = 46, Height = 26, Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(96, 60, 44), ForeColor = Color.FromArgb(255, 230, 200),
                FlatAppearance = { BorderColor = Color.FromArgb(150, 100, 70) } };
            btnDonate.Click += delegate { OpenDonate(); };

            btnMin = new Button { Text = "—", Location = new Point(ClientSize.Width - 48, 9), Width = 20, Height = 26, Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(56, 58, 70), ForeColor = Color.FromArgb(200, 205, 215),
                FlatAppearance = { BorderColor = Color.FromArgb(80, 90, 120) } };
            btnMin.Click += delegate { Minimize(); };

            btnMode = new Button { Text = "切到日K", Location = new Point(ClientSize.Width - 92, 116), Width = 78, Height = 22, Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 46, 62), ForeColor = Color.FromArgb(220, 228, 240),
                FlatAppearance = { BorderColor = Color.FromArgb(80, 90, 120) } };
            btnMode.Click += delegate { ToggleMode(); };

            btnClose = new Button { Text = "×", Location = new Point(ClientSize.Width - 26, 9), Width = 22, Height = 26, Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(56, 58, 70), ForeColor = Color.FromArgb(200, 205, 215),
                FlatAppearance = { BorderSize = 0 } };
            btnClose.Click += delegate { Close(); };

            cmbWatch = new ComboBox
            {
                Location = new Point(240, 10), Width = Math.Max(60, ClientSize.Width - 338), DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(30, 32, 42), ForeColor = Color.FromArgb(235, 235, 240),
                FlatStyle = FlatStyle.Flat
            };
            cmbWatch.SelectedIndexChanged += delegate
            {
                if (cmbWatch.SelectedIndex >= 0 && cmbWatch.SelectedIndex < watch.Count)
                {
                    selected = watch[cmbWatch.SelectedIndex];
                    curTrend = null;
                    Invalidate();
                }
            };

            Controls.Add(txtCode); Controls.Add(btnQuery); Controls.Add(btnConfig); Controls.Add(btnChart); Controls.Add(btnDonate); Controls.Add(btnMin); Controls.Add(btnClose); Controls.Add(cmbWatch); Controls.Add(btnMode); Controls.Add(lstSearch);
        }

        // ---------------- 最小化 / 还原 ----------------
        private void Minimize()
        {
            if (minimized) return;
            minimized = true;
            normalSize = ClientSize;
            HideSearch();
            foreach (Control c in Controls) c.Visible = false;
            MinimumSize = new Size(0, 0);           // 允许缩小(否则被 MinimumSize 钳制)
            ClientSize = new Size(250, 56);
            Invalidate();
        }

        private void Restore()
        {
            if (!minimized) return;
            minimized = false;
            ClientSize = normalSize;
            MinimumSize = new Size(420, 320);       // 恢复最小尺寸限制
            foreach (Control c in Controls) c.Visible = true;
            Invalidate();
            KickRefresh();
        }

        private void DrawMinimized(Graphics g)
        {
            Rectangle card = new Rectangle(2, 2, ClientSize.Width - 4, ClientSize.Height - 4);
            using (GraphicsPath path = Rounded(card, 12))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(22, 24, 32)))
            using (Pen border = new Pen(Color.FromArgb(70, 82, 108), 1f))
            { g.FillPath(bg, path); g.DrawPath(border, path); }

            Quote q;
            quotes.TryGetValue(selected, out q);
            if (q != null && q.Ok)
            {
                bool up = q.Change >= 0;
                Color pc = up ? Color.FromArgb(232, 84, 84) : Color.FromArgb(72, 180, 120);
                using (SolidBrush nb = new SolidBrush(Color.FromArgb(235, 235, 240)))
                using (Font nf = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold))
                    g.DrawString(q.Name, nf, nb, 12, 16);
                using (SolidBrush pb = new SolidBrush(pc))
                using (Font pf = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold))
                {
                    string price = q.Price.ToString("F2");
                    SizeF sz = g.MeasureString(price, pf);
                    g.DrawString(price, pf, pb, ClientSize.Width - 12 - sz.Width, 14);
                }
                using (SolidBrush cb = new SolidBrush(pc))
                using (Font cf = new Font("Microsoft YaHei UI", 9f))
                {
                    string ch = (up ? "+" : "") + q.ChangePct.ToString("F2") + "%";
                    g.DrawString(ch, cf, cb, 12, 36);
                }
            }
            else
            {
                using (SolidBrush gb = new SolidBrush(Color.FromArgb(160, 165, 180)))
                using (Font gf = new Font("Microsoft YaHei UI", 11f))
                    g.DrawString(selected.Length > 0 ? selected : "暂无数据", gf, gb, 12, 16);
            }
        }

        // ---------------- 一键隐藏热键 ----------------
        private void RegisterHotkey()
        {
            UnregisterHotkey();
            string spec = cfg != null ? cfg.HideHotkey : "";
            int mods, vk;
            if (ParseHotkey(spec, out mods, out vk))
            {
                try
                {
                    hotkeyRegistered = RegisterHotKey(Handle, HOTKEY_ID, (uint)mods, (uint)vk);
                    if (hotkeyRegistered) { hotMods = mods; hotVk = vk; }
                }
                catch { hotkeyRegistered = false; }
            }
        }

        private void UnregisterHotkey()
        {
            if (hotkeyRegistered)
            {
                try { UnregisterHotKey(Handle, HOTKEY_ID); } catch { }
                hotkeyRegistered = false;
            }
        }

        /// <summary>解析 "Ctrl+Shift+H" 之类的组合键。返回 false 表示禁用/无效。</summary>
        public static bool ParseHotkey(string spec, out int mods, out int vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(spec)) return false;
            string[] parts = spec.Trim().Split('+');
            if (parts.Length == 0) return false;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string m = parts[i].Trim().ToLowerInvariant();
                if (m == "ctrl") mods |= 0x2;
                else if (m == "alt") mods |= 0x1;
                else if (m == "shift") mods |= 0x4;
                else if (m == "win") mods |= 0x8;
            }
            string key = parts[parts.Length - 1].Trim();
            if (key.Length == 0) return false;
            if (key[0] == 'F' || key[0] == 'f')
            {
                int fn;
                if (int.TryParse(key.Substring(1), out fn) && fn >= 1 && fn <= 12) { vk = 0x70 + fn - 1; return true; }
                return false;
            }
            if (key.Length == 1)
            {
                char c = char.ToUpperInvariant(key[0]);
                if (c >= '0' && c <= '9') vk = c;
                else if (c >= 'A' && c <= 'Z') vk = c;
                else return false;
                return mods != 0 || vk != 0;
            }
            return false;
        }

        private void ToggleMinimize()
        {
            if (minimized) Restore();
            else Minimize();
        }

        /// <summary>最小化时双击切换到下一只自选股。</summary>
        private void NextStock()
        {
            if (watch.Count < 2) return;
            int i = watch.IndexOf(selected);
            if (i < 0) i = 0;
            int ni = (i + 1) % watch.Count;
            selected = watch[ni];
            if (cmbWatch.IsHandleCreated)
            {
                int idx = watch.IndexOf(selected);
                if (idx >= 0) cmbWatch.SelectedIndex = idx;
            }
            Invalidate();
        }

        private void ToggleMode()
        {
            dailyMode = !dailyMode;
            btnMode.Text = dailyMode ? "切到分时" : "切到日K";
            hoverTrend = -1; hoverCell = -1; hoverCellIdx = -1;
            if (dailyMode) EnsureKlineForSelected();
            Invalidate();
        }

        private void EnsureKlineForSelected()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (selected.Length == 0) return;
                    string err;
                    KlineData kd = StockApi.GetKline(selected, out err);
                    if (kd != null) { lock (klines) { klines[selected] = kd; } }
                    BeginInvoke(new Action(delegate { Invalidate(); }));
                }
                catch (Exception ex) { Log.Error("EnsureKlineForSelected", ex); }
            });
        }

        private static List<string> DailyKeys(List<string> shown)
        {
            List<string> r = new List<string>();
            if (shown == null) return r;
            string[] supported = { "MACD", "KDJ", "RSI6", "BOLL", "WR14", "BIAS6", "VOLMA5" };
            foreach (string s in supported) if (shown.Contains(s)) r.Add(s);
            foreach (string s in shown) if (s == "MA5" || s == "MA10" || s == "MA20") r.Add(s);
            while (r.Count > 8) r.RemoveAt(r.Count - 1);
            return r;
        }

        private void OpenDonate()
        {
            using (DonateForm f = new DonateForm())
                f.ShowDialog(this);
        }

        private void OpenGallery()
        {
            KlineData kd = null;
            lock (klines) { if (klines.ContainsKey(selected)) kd = klines[selected]; }
            List<string> keys = cfg.ShowIndicators != null ? new List<string>(cfg.ShowIndicators) : new List<string>();
            if (keys.Count == 0) keys.Add("KDJ");
            // 确保K线可用:没有则先取一次
            if (kd == null && selected.Length > 0)
            {
                string err;
                kd = StockApi.GetKline(selected, out err);
                if (kd != null) { lock (klines) { klines[selected] = kd; } }
            }
            if (kd == null)
            {
                MessageBox.Show(this, "暂无K线数据(网络或代码问题),无法绘制指标图。", "实时股价", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            using (IndicatorGalleryForm f = new IndicatorGalleryForm(kd, keys))
                f.ShowDialog(this);
        }

        private void DoAdd()
        {
            string raw = txtCode.Text.Trim();
            if (raw.Length == 0) return;
            if (!IsCodeLike(raw))
            {
                // 名称搜索:搜到后自动选中第一条
                DoNameSearch(raw, true);
                return;
            }
            string code = StockApi.NormalizeCode(raw);
            if (code.Length == 0) return;
            if (!watch.Contains(code)) { watch.Add(code); cfg.Watch.Add(code); ConfigIO.Save(cfg); }
            selected = code;
            RebuildCombo();
            int idx = watch.IndexOf(code);
            if (idx >= 0) cmbWatch.SelectedIndex = idx;
            HideSearch();
            KickRefresh();
        }

        private static bool IsCodeLike(string s)
        {
            s = s.Trim().ToLowerInvariant();
            if (s.Length == 0) return false;
            if (s.Length >= 2 && (s.StartsWith("sh") || s.StartsWith("sz") || s.StartsWith("bj") ||
                                  s.StartsWith("hk") || s.StartsWith("us"))) return true;
            bool allDigits = true;
            foreach (char c in s) if (c < '0' || c > '9') { allDigits = false; break; }
            return allDigits && s.Length >= 4;
        }

        /// <summary>按名称搜索股票并显示下拉;autoPick=true 时自动选中第一条。</summary>
        private void DoNameSearch(string kw, bool autoPick)
        {
            kw = (kw ?? "").Trim();
            if (kw.Length < 2 || IsCodeLike(kw)) { HideSearch(); return; }
            searchSeq++;
            int mySeq = searchSeq;
            string k = kw;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string err;
                    List<Quote> list = StockApi.Search(k, out err);
                    BeginInvoke(new Action(delegate
                    {
                        if (mySeq != searchSeq) return; // 过期结果丢弃
                        searchCodes.Clear();
                        lstSearch.Items.Clear();
                        foreach (Quote q in list)
                        {
                            searchCodes.Add(q.Code);
                            lstSearch.Items.Add(q.Name + "  " + q.Code);
                        }
                        if (lstSearch.Items.Count > 0)
                        {
                            lstSearch.SelectedIndex = 0;
                            lstSearch.Visible = true;
                            lstSearch.BringToFront();
                        }
                        else HideSearch();
                        if (autoPick && lstSearch.Items.Count > 0) PickSearchSelection();
                    }));
                }
                catch (Exception ex) { Log.Error("DoNameSearch " + k, ex); }
            });
        }

        private void PickSearchSelection()
        {
            if (!lstSearch.Visible || lstSearch.SelectedIndex < 0 || lstSearch.SelectedIndex >= searchCodes.Count) return;
            string code = searchCodes[lstSearch.SelectedIndex];
            string name = lstSearch.SelectedItem.ToString();
            txtCode.Text = code;
            if (!watch.Contains(code)) { watch.Add(code); cfg.Watch.Add(code); ConfigIO.Save(cfg); }
            selected = code;
            RebuildCombo();
            int idx = watch.IndexOf(code);
            if (idx >= 0) cmbWatch.SelectedIndex = idx;
            HideSearch();
            status = "已添加 " + name;
            KickRefresh();
            Invalidate();
        }

        private void HideSearch()
        {
            if (searchTimer != null) searchTimer.Stop();
            lstSearch.Visible = false;
        }

        private void OpenConfig()
        {
            using (ConfigForm f = new ConfigForm(cfg, watch, quotes, klines))
            {
                f.ShowDialog(this);
            }
            cfg = ConfigIO.Load();
            watch.Clear();
            foreach (string c in cfg.Watch) if (!watch.Contains(c)) watch.Add(c);
            if (!watch.Contains(selected)) selected = watch.Count > 0 ? watch[0] : "";
            timer.Interval = Math.Max(2, cfg.RefreshSeconds) * 1000;
            RegisterHotkey();
            RebuildCombo();
            Invalidate();
            KickRefresh();
        }

        private void RebuildCombo()
        {
            int keep = cmbWatch.SelectedIndex;
            cmbWatch.Items.Clear();
            foreach (string c in watch)
            {
                Quote q;
                quotes.TryGetValue(c, out q);
                if (q != null && q.Ok)
                {
                    string up = q.Change >= 0 ? "+" : "";
                    cmbWatch.Items.Add(q.Name + " " + q.Code + "  " + q.Price.ToString("F2") + "  " + up + q.ChangePct.ToString("F2") + "%");
                }
                else cmbWatch.Items.Add(c);
            }
            if (watch.Count > 0)
            {
                int idx = watch.IndexOf(selected);
                cmbWatch.SelectedIndex = idx >= 0 ? idx : 0;
                if (idx < 0) selected = watch[0];
            }
        }

        // ---------------- 刷新引擎 ----------------
        private void KickRefresh()
        {
            if (busy) return;
            busy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    tick++;
                    bool klineDue = (tick % 15 == 0) || klines.Count == 0;
                    string sel = selected;
                    List<string> alerts = new List<string>();

                    // 1) 全部自选股行情
                    foreach (string code in watch)
                    {
                        string err;
                        Quote q = StockApi.GetQuote(code, out err);
                        lock (quotes) { if (q != null && q.Ok) quotes[code] = q; }
                    }
                    // 2) 选中股票分时;日K模式下同时刷新选中股票的日K
                    List<TrendPoint> trend = null;
                    if (sel.Length > 0)
                    {
                        string err;
                        trend = StockApi.GetTrend(sel, out err);
                        if (dailyMode)
                        {
                            KlineData kd = StockApi.GetKline(sel, out err);
                            if (kd != null && kd.Bars.Count > 0) { lock (klines) { klines[sel] = kd; } }
                        }
                    }
                    // 3) 周期刷新K线 + 指标 + 提醒
                    StockIndicators ind = null;
                    if (klineDue)
                    {
                        foreach (string code in watch)
                        {
                            string err;
                            KlineData kd = StockApi.GetKline(code, out err);
                            if (kd != null && kd.Bars.Count > 0) { lock (klines) { klines[code] = kd; } }
                        }
                        lock (quotes)
                        {
                            foreach (string code in watch)
                            {
                                Quote q;
                                quotes.TryGetValue(code, out q);
                                KlineData kd;
                                klines.TryGetValue(code, out kd);
                                if (q == null || kd == null) continue;
                                alerts.AddRange(engine.Check(cfg, code, q.Name, q, kd));
                            }
                        }
                    }
                    if (sel.Length > 0)
                    {
                        KlineData kd;
                        lock (klines) { klines.TryGetValue(sel, out kd); }
                        if (kd != null) ind = StockIndicators.Compute(kd);
                    }

                    // UI 更新
                    BeginInvoke(new Action(delegate
                    {
                        RebuildCombo();
                        curTrend = trend;
                        curInd = ind;
                        minuteSeries = MinuteSeries.Build(trend, SubKeys(cfg.ShowIndicators));
                        maOverlays = MinuteSeries.MaOverlays(trend, cfg.ShowIndicators != null ? cfg.ShowIndicators : new List<string>());
                        KlineData selKd = null;
                        lock (klines) { if (klines.ContainsKey(selected)) selKd = klines[selected]; }
                        dailySeries = selKd != null ? IndicatorFactory.ComputeAll(selKd, DailyKeys(cfg.ShowIndicators)) : new List<IndicatorSeries>();
                        status = "更新 " + DateTime.Now.ToString("HH:mm:ss");
                        if (alerts.Count > 0) FireAlerts(alerts);
                        Invalidate();
                    }));
                }
                catch (Exception ex) { Log.Error("refresh", ex); }
                finally { busy = false; }
            });
        }

        /// <summary>配置的显示指标 → 支持的分钟副图指标,最多取 3 个。</summary>
        private static List<string> SubKeys(List<string> shown)
        {
            List<string> r = new List<string>();
            if (shown == null) return r;
            string[] supported = { "MACD", "KDJ", "RSI6", "BOLL", "WR14", "BIAS6", "VOL" };
            foreach (string s in supported)
                if (shown.Contains(s)) r.Add(s);
            while (r.Count > 3) r.RemoveAt(r.Count - 1);
            return r;
        }

        private void FireAlerts(List<string> alerts)
        {
            status = "🔔 " + string.Join(" ; ", alerts.ToArray());
            if (cfg.FlashAlert)
            {
                AlertForm f = new AlertForm(cfg.FlashSeconds, cfg.SoundAlert);
                f.Show();
            }
        }

        // ---------------- 拖动 + 滚动 + 悬浮 ----------------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            HideSearch();
            if (e.Button != MouseButtons.Left) return;
            if (maxScroll > 0 && e.X >= ClientSize.Width - 18)
            {
                scrollDrag = true;
                scrollDragStartY = e.Y;
                scrollDragStartOffset = scrollOffset;
                return;
            }
            dragging = true; dragOffset = e.Location;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (scrollDrag)
            {
                int top = 254, bottom = ClientSize.Height - 30;
                int availH = bottom - top;
                int thumbH = Math.Max(24, availH * availH / Math.Max(1, contentH));
                int trackH = availH - thumbH;
                int off = trackH > 0 ? (int)((double)(e.Y - top - thumbH / 2) * maxScroll / trackH) : 0;
                if (off < 0) off = 0;
                if (off > maxScroll) off = maxScroll;
                scrollOffset = off;
                Invalidate();
                return;
            }
            if (dragging)
            {
                Point screen = PointToScreen(e.Location);
                Location = new Point(screen.X - dragOffset.X, screen.Y - dragOffset.Y);
                if (minimized) SnapToEdge();
            }
            // 悬浮:主图(分时/日K)或指标格子
            List<TrendPoint> pts = curTrend;
            KlineData kdHover = null;
            lock (klines) { if (klines.ContainsKey(selected)) kdHover = klines[selected]; }
            bool handled = false;
            Rectangle mainArea = new Rectangle(14, 114, ClientSize.Width - 28, 130);
            if (mainArea.Contains(e.Location))
            {
                if (dailyMode && kdHover != null && kdHover.Bars.Count > 1)
                {
                    int total = kdHover.Bars.Count;
                    int nn = Math.Min(120, total), st = total - nn;
                    double t = (e.X - mainArea.X) / (double)mainArea.Width;
                    int idx = st + (int)Math.Round(t * (nn - 1));
                    if (idx < st) idx = st;
                    if (idx >= total) idx = total - 1;
                    if (hoverTrend != idx || hoverCell >= 0) { hoverTrend = idx; hoverCell = -1; hoverCellIdx = -1; Invalidate(); }
                    Bar b = kdHover.Bars[idx];
                    StringBuilder tb = new StringBuilder();
                    tb.Append("日期 ").Append(b.Date);
                    tb.Append("\r\n开盘 ").Append(b.Open.ToString("F2"));
                    tb.Append("\r\n最高 ").Append(b.High.ToString("F2"));
                    tb.Append("\r\n最低 ").Append(b.Low.ToString("F2"));
                    tb.Append("\r\n收盘 ").Append(b.Close.ToString("F2"));
                    tb.Append("\r\n成交量 ").Append((b.Volume / 10000).ToString("F1")).Append("万手");
                    tip.Show(tb.ToString(), this, new Point(e.X + 12, e.Y + 12));
                    handled = true;
                }
                else if (pts != null && pts.Count > 1)
                {
                    double t = (e.X - mainArea.X) / (double)mainArea.Width;
                    int idx = (int)Math.Round(t * (pts.Count - 1));
                    if (idx < 0) idx = 0;
                    if (idx >= pts.Count) idx = pts.Count - 1;
                    if (hoverTrend != idx || hoverCell >= 0) { hoverTrend = idx; hoverCell = -1; hoverCellIdx = -1; Invalidate(); }
                    TrendPoint p = pts[idx];
                    StringBuilder tb = new StringBuilder();
                    tb.Append("时间 ").Append(p.Time);
                    tb.Append("\r\n价格 ").Append(p.Price.ToString("F2"));
                    if (p.Avg > 0) tb.Append("\r\n均价 ").Append(p.Avg.ToString("F2"));
                    tip.Show(tb.ToString(), this, new Point(e.X + 12, e.Y + 12));
                    handled = true;
                }
            }
            if (!handled)
            {
                List<IndicatorSeries> shown = dailyMode ? dailySeries : minuteSeries;
                int cellShowCount = dailyMode ? 120 : 0;
                Point pt = new Point(e.X, e.Y + scrollOffset); // 内容坐标
                int cell = -1, cidx = -1;
                for (int i = 0; i < cellRects.Count; i++)
                {
                    if (cellRects[i].Contains(pt))
                    {
                        cell = i;
                        IndicatorSeries s = shown[i];
                        Rectangle cr = new Rectangle(cellRects[i].X + 6, cellRects[i].Y + 22, cellRects[i].Width - 12, cellRects[i].Height - 30);
                        int nn, start;
                        ChartPainter.Slice(s, cellShowCount, out nn, out start);
                        cidx = ChartPainter.HitTest(cr, nn, start, e.X);
                        tip.Show(ChartPainter.TooltipText(s, cidx), this, new Point(e.X + 12, e.Y + 12));
                        break;
                    }
                }
                if (cell != hoverCell || cidx != hoverCellIdx || hoverTrend >= 0)
                {
                    hoverCell = cell;
                    hoverCellIdx = cell >= 0 ? cidx : -1;
                    hoverTrend = -1;
                    Invalidate();
                }
                if (cell < 0) tip.Hide(this);
            }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
            scrollDrag = false;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            HideSearch();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (minimized)
            {
                if (e.Button == MouseButtons.Left) restoreTimer.Start(); // 单击延迟还原,双击则切换自选
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            List<IndicatorSeries> shown = dailyMode ? dailySeries : minuteSeries;
            Point pt = new Point(e.X, e.Y + scrollOffset);
            for (int i = 0; i < cellRects.Count; i++)
                if (cellRects[i].Contains(pt))
                {
                    KlineData kd = null;
                    if (dailyMode) lock (klines) { if (klines.ContainsKey(selected)) kd = klines[selected]; }
                    OpenCellDetail(shown[i], kd);
                    return;
                }
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            if (minimized)
            {
                restoreTimer.Stop();
                NextStock();
                Invalidate();
            }
        }

        private void SnapToEdge()
        {
            Rectangle vs = SystemInformation.VirtualScreen;
            const int snap = 10;
            int x = Location.X, y = Location.Y;
            if (x <= snap) x = 0;
            else if (x + Width >= vs.Right - snap) x = vs.Right - Width;
            if (y <= snap) y = 0;
            else if (y + Height >= vs.Bottom - snap) y = vs.Bottom - Height;
            if (x != Location.X || y != Location.Y) Location = new Point(x, y);
        }

        private void OpenCellDetail(IndicatorSeries s, KlineData kd)
        {
            using (IndicatorDetailForm f = new IndicatorDetailForm(s, kd))
                f.ShowDialog(this);
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverTrend >= 0 || hoverCell >= 0) { hoverTrend = -1; hoverCell = -1; hoverCellIdx = -1; tip.Hide(this); Invalidate(); }
        }

        // ---------------- 无边框窗口缩放(边缘拖拽) ----------------
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84) // WM_NCHITTEST
            {
                if (minimized) { m.Result = (IntPtr)1; return; } // 最小化状态不缩放
                Point pt = PointToClient(Cursor.Position);
                int x = pt.X, y = pt.Y, w = ClientSize.Width, h = ClientSize.Height;
                const int edge = 7;
                bool l = x <= edge, r = x >= w - edge, t = y <= edge, b = y >= h - edge;
                int ht = 0;
                if (l && t) ht = 13; else if (r && t) ht = 14; else if (l && b) ht = 16; else if (r && b) ht = 17;
                else if (l) ht = 10; else if (r) ht = 11; else if (t) ht = 12; else if (b) ht = 15;
                if (ht != 0) { m.Result = (IntPtr)ht; return; }
            }
            else if (m.Msg == 0x0232) // WM_EXITSIZEMOVE
            {
                base.WndProc(ref m);
                SaveWindowState();
                return;
            }
            else if (m.Msg == 0x020A) // WM_MOUSEWHEEL
            {
                int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                if (maxScroll > 0)
                {
                    scrollOffset -= delta / 120 * 60;
                    if (scrollOffset < 0) scrollOffset = 0;
                    if (scrollOffset > maxScroll) scrollOffset = maxScroll;
                    Invalidate();
                    m.Result = IntPtr.Zero;
                    return;
                }
            }
            else if (m.Msg == 0x0312) // WM_HOTKEY:一键隐藏/还原
            {
                if ((int)m.WParam == HOTKEY_ID) { ToggleMinimize(); m.Result = IntPtr.Zero; return; }
            }
            base.WndProc(ref m);
        }

        private void SaveWindowState()
        {
            try
            {
                cfg.WinX = Location.X;
                cfg.WinY = Location.Y;
                cfg.WinW = minimized ? normalSize.Width : ClientSize.Width;
                cfg.WinH = minimized ? normalSize.Height : ClientSize.Height;
                ConfigIO.Save(cfg);
            }
            catch (Exception ex) { Log.Error("SaveWindowState", ex); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            UnregisterHotkey();
            SaveWindowState();
            base.OnFormClosing(e);
        }

        // ---------------- 绘制 ----------------
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Magenta);

            if (minimized) { DrawMinimized(g); return; }

            Rectangle card = new Rectangle(2, 2, ClientSize.Width - 4, ClientSize.Height - 4);
            using (GraphicsPath path = Rounded(card, 14))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(22, 24, 32)))
            using (Pen border = new Pen(Color.FromArgb(70, 82, 108), 1f))
            { g.FillPath(bg, path); g.DrawPath(border, path); }

            int left = 14, right = ClientSize.Width - 14;
            Quote q;
            quotes.TryGetValue(selected, out q);

            // 价格区
            int y = 44;
            if (q != null && q.Ok)
            {
                bool up = q.Change >= 0;
                Color pc = up ? Color.FromArgb(232, 84, 84) : Color.FromArgb(72, 180, 120);
                using (SolidBrush pb = new SolidBrush(pc))
                {
                    g.DrawString(q.Name + "  " + q.Code, new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold), pb, left, y);
                    g.DrawString(q.Price.ToString("F2"), new Font("Microsoft YaHei UI", 25f, FontStyle.Bold), pb, left, y + 22);
                    string ch = (up ? "+" : "") + q.Change.ToString("F2") + "  " + (up ? "+" : "") + q.ChangePct.ToString("F2") + "%";
                    g.DrawString(ch, new Font("Microsoft YaHei UI", 11.5f), pb, left + 128, y + 34);
                }
                using (SolidBrush mb = new SolidBrush(Color.FromArgb(150, 155, 170)))
                    g.DrawString("开 " + q.Open.ToString("F2") + "  高 " + q.High.ToString("F2") + "  低 " + q.Low.ToString("F2") +
                                 "  量 " + (q.Volume / 10000).ToString("F1") + "万手  时间 " + (q.Time.Length > 16 ? q.Time.Substring(11) : q.Time),
                                 new Font("Microsoft YaHei UI", 8.5f), mb, left + 128, y + 6);
            }
            else
            {
                using (SolidBrush gb = new SolidBrush(Color.FromArgb(160, 165, 180)))
                    g.DrawString("暂无行情", new Font("Microsoft YaHei UI", 15f), gb, left, y + 12);
            }

            // 主图:分时 或 日K
            Rectangle mainRect = new Rectangle(left, 114, right - left, 130);
            KlineData kdMain = null;
            lock (klines) { if (klines.ContainsKey(selected)) kdMain = klines[selected]; }
            if (dailyMode && kdMain != null && kdMain.Bars.Count > 0)
                DrawKline(g, mainRect, kdMain, cfg.ShowIndicators != null ? cfg.ShowIndicators : new List<string>());
            else
                DrawTrend(g, mainRect, q, maOverlays);

            // 指标图区域(格子/行,可滚动);日K模式显示近120天日线指标
            int gridTop = 254;
            int gridBottom = ClientSize.Height - 30;
            int availH = gridBottom - gridTop;
            int availW = right - left;
            List<IndicatorSeries> shown = dailyMode ? dailySeries : minuteSeries;
            int cellShowCount = dailyMode ? 120 : 0;
            cellRects.Clear();
            int n = shown.Count;
            contentH = 0;
            if (n > 0)
            {
                if (cfg.ChartLayout == "rows")
                {
                    int stripGap = 6;
                    int stripH = Math.Max(64, (availH - (n - 1) * stripGap) / n);
                    contentH = n * stripH + (n - 1) * stripGap;
                    for (int i = 0; i < n; i++)
                    {
                        Rectangle rc = new Rectangle(left, gridTop + i * (stripH + stripGap), availW, stripH);
                        cellRects.Add(rc);
                    }
                }
                else
                {
                    int cols = Math.Max(1, Math.Min(4, availW / 230));
                    int rows = (n + cols - 1) / cols;
                    int cellW = (availW - (cols - 1) * 6) / cols;
                    int cellH = Math.Max(108, (availH - (rows - 1) * 6) / Math.Max(1, rows));
                    contentH = rows * cellH + (rows - 1) * 6;
                    for (int i = 0; i < n; i++)
                    {
                        int cx = left + (i % cols) * (cellW + 6);
                        int cy = gridTop + (i / cols) * (cellH + 6);
                        cellRects.Add(new Rectangle(cx, cy, cellW, cellH));
                    }
                }
            }
            else if (dailyMode && kdMain == null)
            {
                using (SolidBrush hb = new SolidBrush(Color.FromArgb(150, 158, 175)))
                using (Font hf = new Font("Microsoft YaHei UI", 9f))
                    g.DrawString("日K数据加载中...", hf, hb, left, gridTop + 12);
            }

            // 滚动范围 + 绘制(内容坐标平移,并裁剪在指标区域内,避免遮挡上方主图)
            maxScroll = Math.Max(0, contentH - availH);
            if (scrollOffset > maxScroll) scrollOffset = maxScroll;
            if (scrollOffset < 0) scrollOffset = 0;
            g.SetClip(new Rectangle(left, gridTop, availW, availH));
            g.TranslateTransform(0, -scrollOffset);
            for (int i = 0; i < cellRects.Count; i++)
                DrawCell(g, cellRects[i], shown[i], hoverCell == i ? hoverCellIdx : -1, cellShowCount);
            g.ResetTransform();
            g.ResetClip();

            // 滚动条
            if (maxScroll > 0)
                DrawScrollbar(g, gridTop, gridBottom, availH, contentH);

            // 状态
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(120, 128, 146)))
                g.DrawString(status, new Font("Microsoft YaHei UI", 8f), sb, left, ClientSize.Height - 24);
        }

        /// <summary>绘制竖直滚动条(内容超高时)。</summary>
        private void DrawScrollbar(Graphics g, int top, int bottom, int availH, int contentHeight)
        {
            int sbX = ClientSize.Width - 16;
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(42, 46, 58)))
                g.FillRectangle(tb, sbX, top, 8, bottom - top);
            int thumbH = Math.Max(24, availH * availH / Math.Max(1, contentHeight));
            int thumbY = top + (int)((long)scrollOffset * (availH - thumbH) / Math.Max(1, maxScroll));
            using (SolidBrush tbb = new SolidBrush(Color.FromArgb(110, 120, 145)))
                g.FillRectangle(tbb, sbX, thumbY, 8, thumbH);
        }

        /// <summary>绘制一个指标格子(边框+标题+迷你图+悬浮十字线)。</summary>
        private void DrawCell(Graphics g, Rectangle rc, IndicatorSeries s, int hoverIdx, int showCount)
        {
            using (Pen border = new Pen(Color.FromArgb(56, 62, 80), 1f))
                g.DrawRectangle(border, rc.X, rc.Y, rc.Width - 1, rc.Height - 1);
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(200, 208, 222)))
            using (Font tf = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold))
                g.DrawString(s.Title, tf, tb, rc.X + 6, rc.Y + 4);
            Rectangle cr = new Rectangle(rc.X + 6, rc.Y + 22, rc.Width - 12, rc.Height - 30);
            if (cr.Width > 30 && cr.Height > 20)
            {
                ChartPainter.Draw(g, cr, s, showCount, false);
                if (hoverIdx >= 0)
                {
                    int nn, start;
                    ChartPainter.Slice(s, showCount, out nn, out start);
                    if (hoverIdx >= start && hoverIdx < start + nn)
                    {
                        double lo, hi;
                        ChartPainter.GetRange(s, showCount, out lo, out hi);
                        float hx = ChartPainter.MapX(cr, nn, start, hoverIdx);
                        using (Pen hp = new Pen(Color.FromArgb(140, 150, 175), 1f) { DashStyle = DashStyle.Dash })
                            g.DrawLine(hp, hx, cr.Y, hx, cr.Bottom);
                        using (SolidBrush hd = new SolidBrush(Color.FromArgb(255, 255, 255)))
                        {
                            for (int li = 0; li < s.Lines.Count; li++)
                            {
                                double v = hoverIdx < s.Lines[li].Length ? s.Lines[li][hoverIdx] : double.NaN;
                                if (double.IsNaN(v)) continue;
                                float hy = ChartPainter.MapY(cr, lo, hi, v);
                                g.FillEllipse(hd, hx - 2f, hy - 2f, 4, 4);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>日K蜡烛图 + 均线叠加(近120天)。</summary>
        private void DrawKline(Graphics g, Rectangle rc, KlineData kd, List<string> shownKeys)
        {
            int total = kd.Bars.Count;
            int n = Math.Min(120, total);
            int start = total - n;
            if (n < 1 || rc.Width < 40) return;

            // 价格范围(高低点 + 均线)
            double lo = double.MaxValue, hi = double.MinValue;
            for (int i = start; i < total; i++)
            {
                if (kd.Bars[i].Low < lo) lo = kd.Bars[i].Low;
                if (kd.Bars[i].High > hi) hi = kd.Bars[i].High;
            }
            List<double> closes = new List<double>();
            for (int i = start; i < total; i++) closes.Add(kd.Bars[i].Close);
            List<KeyValuePair<string, double[]>> mas = new List<KeyValuePair<string, double[]>>();
            foreach (string key in shownKeys)
                if (key == "MA5" || key == "MA10" || key == "MA20")
                {
                    int p = int.Parse(key.Substring(2));
                    if (p < closes.Count)
                    {
                        List<double> ma = Indicators.MA(closes, p);
                        double[] arr = ma.ToArray();
                        for (int i = 0; i < arr.Length; i++)
                            if (!double.IsNaN(arr[i])) { if (arr[i] < lo) lo = arr[i]; if (arr[i] > hi) hi = arr[i]; }
                        mas.Add(new KeyValuePair<string, double[]>(key, arr));
                    }
                }
            if (hi - lo < 0.0001) { lo -= 1; hi += 1; }
            double pad = (hi - lo) * 0.05; lo -= pad; hi += pad;

            int X0 = rc.X, X1 = rc.Right, Y0 = rc.Y, Y1 = rc.Bottom;
            Func<int, float> X = i => X0 + (float)((X1 - X0) * (1.0 * (i - start) / Math.Max(1, n - 1)));
            Func<double, float> Y = v => Y0 + (float)((hi - v) / (hi - lo) * (Y1 - Y0));

            // 网格 + 价格标签
            using (Pen grid = new Pen(Color.FromArgb(46, 50, 62), 1f))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(120, 128, 146)))
            using (Font f = new Font("Microsoft YaHei UI", 7.5f))
            {
                for (int i = 0; i <= 3; i++)
                {
                    double v = hi - (hi - lo) * i / 3.0;
                    float y = Y(v);
                    g.DrawLine(grid, X0, y, X1, y);
                    g.DrawString(v.ToString("F1"), f, tb, X0 + 2, y - 7);
                }
                int[] xi = { 0, n / 4, n / 2, (3 * n) / 4, n - 1 };
                foreach (int k in xi)
                {
                    string t = ChartPainter.ShortLabel(kd.Bars[start + k].Date);
                    SizeF ts = g.MeasureString(t, f);
                    float x = X(start + k) - ts.Width / 2f;
                    if (x < X0) x = X0;
                    if (x + ts.Width > X1) x = X1 - ts.Width;
                    g.DrawString(t, f, tb, x, Y1 + 1);
                }
            }

            // 蜡烛
            float bw = Math.Max(1.2f, (float)(X1 - X0) / n * 0.62f);
            using (Pen redW = new Pen(Color.FromArgb(232, 84, 84), 1f))
            using (Pen greenW = new Pen(Color.FromArgb(72, 180, 120), 1f))
            {
                for (int i = start; i < total; i++)
                {
                    Bar b = kd.Bars[i];
                    bool up = b.Close >= b.Open;
                    Color c = up ? Color.FromArgb(232, 84, 84) : Color.FromArgb(72, 180, 120);
                    float cx = X(i);
                    // 影线(注意:不能 using 释放共享画笔,redW/greenW 由外层 using 统一释放)
                    Pen wick = up ? redW : greenW;
                    g.DrawLine(wick, cx, Y(b.High), cx, Y(b.Low));
                    // 实体
                    float yO = Y(b.Open), yC = Y(b.Close);
                    float top = Math.Min(yO, yC), hgt = Math.Max(1.2f, Math.Abs(yO - yC));
                    using (SolidBrush body = new SolidBrush(c))
                        g.FillRectangle(body, cx - bw / 2f, top, bw, hgt);
                }
            }

            // 均线
            foreach (KeyValuePair<string, double[]> ov in mas)
            {
                Color c = ov.Key == "MA5" ? Color.FromArgb(255, 220, 90) :
                          ov.Key == "MA10" ? Color.FromArgb(255, 150, 60) : Color.FromArgb(200, 120, 240);
                using (Pen op = new Pen(c, 1f))
                {
                    for (int i = 1; i < closes.Count; i++)
                    {
                        if (double.IsNaN(ov.Value[i]) || double.IsNaN(ov.Value[i - 1])) continue;
                        g.DrawLine(op, X(start + i - 1), Y(ov.Value[i - 1]), X(start + i), Y(ov.Value[i]));
                    }
                }
            }

            // 悬浮十字线 + 高亮蜡烛
            if (hoverTrend >= start && hoverTrend < total)
            {
                float hx = X(hoverTrend);
                using (Pen hp = new Pen(Color.FromArgb(150, 160, 185), 1f) { DashStyle = DashStyle.Dash })
                    g.DrawLine(hp, hx, Y0, hx, Y1);
                Bar hb = kd.Bars[hoverTrend];
                using (Pen hl = new Pen(Color.FromArgb(255, 255, 255), 1.2f))
                    g.DrawRectangle(hl, hx - bw / 2f - 1, Y(hb.High) - 1, bw + 2, Y(hb.Low) - Y(hb.High) + 2);
            }
        }

        private void DrawTrend(Graphics g, Rectangle rc, Quote q, List<KeyValuePair<string, double[]>> overlays)
        {
            List<TrendPoint> pts = curTrend;
            if (pts == null || pts.Count == 0)
            {
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(120, 128, 146)))
                    g.DrawString("暂无分时数据", new Font("Microsoft YaHei UI", 9f), sb, rc.X + 8, rc.Y + rc.Height / 2 - 8);
                return;
            }
            int n = pts.Count;
            double prevClose = q != null ? q.PrevClose : 0;
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (TrendPoint p in pts)
            {
                if (p.Price < lo) lo = p.Price; if (p.Price > hi) hi = p.Price;
                if (p.Avg > 0 && p.Avg < lo) lo = p.Avg; if (p.Avg > 0 && p.Avg > hi) hi = p.Avg;
            }
            if (prevClose > 0) { if (prevClose < lo) lo = prevClose; if (prevClose > hi) hi = prevClose; }
            if (hi - lo < 0.0001) { lo -= 0.5; hi += 0.5; }
            double pad = (hi - lo) * 0.08; lo -= pad; hi += pad;

            int X0 = rc.X, X1 = rc.Right, Y0 = rc.Y, Y1 = rc.Bottom;
            Func<int, float> X = i => X0 + (float)((X1 - X0) * (1.0 * i / Math.Max(1, n - 1)));
            Func<double, float> Y = v => Y0 + (float)((hi - v) / (hi - lo) * (Y1 - Y0));

            using (Pen grid = new Pen(Color.FromArgb(46, 50, 62), 1f))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(120, 128, 146)))
            using (Font f = new Font("Microsoft YaHei UI", 7.5f))
            {
                for (int i = 0; i <= 2; i++)
                {
                    double v = hi - (hi - lo) * i / 2.0;
                    float y = Y(v);
                    g.DrawLine(grid, X0, y, X1, y);
                    g.DrawString(v.ToString("F2"), f, tb, X0 + 2, y - 8);
                }
                if (prevClose > 0)
                    using (Pen dash = new Pen(Color.FromArgb(110, 150, 255), 1f) { DashStyle = DashStyle.Dash })
                        g.DrawLine(dash, X0, Y(prevClose), X1, Y(prevClose));
            }
            using (Pen avgPen = new Pen(Color.FromArgb(240, 200, 60), 1.1f))
            {
                bool started = false;
                for (int i = 0; i < n; i++)
                {
                    if (pts[i].Avg <= 0) continue;
                    if (!started) { started = true; continue; }
                    if (pts[i - 1].Avg > 0)
                        g.DrawLine(avgPen, X(i - 1), Y(pts[i - 1].Avg), X(i), Y(pts[i].Avg));
                }
            }
            using (Pen pricePen = new Pen(Color.FromArgb(90, 170, 255), 1.5f))
                for (int i = 1; i < n; i++)
                    g.DrawLine(pricePen, X(i - 1), Y(pts[i - 1].Price), X(i), Y(pts[i].Price));

            // MA 叠加线(分时均线)
            foreach (KeyValuePair<string, double[]> ov in overlays)
            {
                Color c = ov.Key == "MA5" ? Color.FromArgb(255, 220, 90) :
                          ov.Key == "MA10" ? Color.FromArgb(255, 150, 60) : Color.FromArgb(200, 120, 240);
                using (Pen op = new Pen(c, 1f))
                {
                    for (int i = 1; i < n; i++)
                    {
                        if (i >= ov.Value.Length || double.IsNaN(ov.Value[i]) || double.IsNaN(ov.Value[i - 1])) continue;
                        g.DrawLine(op, X(i - 1), Y(ov.Value[i - 1]), X(i), Y(ov.Value[i]));
                    }
                }
            }

            TrendPoint last = pts[n - 1];
            float lx = X(n - 1), ly = Y(last.Price);
            using (SolidBrush dot = new SolidBrush(Color.FromArgb(90, 170, 255)))
                g.FillEllipse(dot, lx - 3f, ly - 3f, 6, 6);

            // 悬浮价格点(十字线由主窗口统一绘制)
            if (hoverTrend >= 0 && hoverTrend < n)
            {
                using (SolidBrush hd = new SolidBrush(Color.FromArgb(255, 255, 255)))
                    g.FillEllipse(hd, X(hoverTrend) - 2.5f, Y(pts[hoverTrend].Price) - 2.5f, 5, 5);
            }

            using (SolidBrush tb = new SolidBrush(Color.FromArgb(110, 118, 136)))
            using (Font f = new Font("Microsoft YaHei UI", 7.5f))
            {
                int[] idx = { 0, n / 4, n / 2, (3 * n) / 4, n - 1 };
                foreach (int i in idx)
                {
                    string t = TimeLabel(pts[i].Time);
                    SizeF ts = g.MeasureString(t, f);
                    float x = X(i) - ts.Width / 2f;
                    if (x < X0) x = X0;
                    if (x + ts.Width > X1) x = X1 - ts.Width;
                    g.DrawString(t, f, tb, x, Y1 + 2);
                }
            }
        }

        private static string F(double v) { return v.ToString("F2"); }
        private static string F1(double v) { return v.ToString("F1"); }
        private static string F2(double v) { return v.ToString("F1"); }

        private static string TimeLabel(string t)
        {
            if (t == null) return "";
            int sp = t.IndexOf(' ');
            return sp >= 0 && t.Length > sp + 5 ? t.Substring(sp + 1, 5) : t;
        }

        internal static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ================================================================ 配置窗口

    public class ConfigForm : Form
    {
        private readonly AppConfig cfg;
        private readonly List<string> watch;
        private readonly Dictionary<string, Quote> quotes;
        private readonly Dictionary<string, KlineData> klines;

        private TextBox txtSearch;
        private ListBox lstResult;
        private ListBox lstWatch;
        private Button btnAddResult, btnDelWatch;
        private CheckedListBox clbInd;
        private ComboBox cmbLayout;
        private ComboBox cmbHotkey;
        private DataGridView grid;
        private CheckBox chkFlash, chkSound;
        private NumericUpDown numSec, numRefresh;
        private Label lblExplain;

        private static readonly string[] IndItems = {
            "MA5","MA10","MA20","MACD","KDJ","RSI6","RSI12","RSI24","BOLL","WR14","BIAS6","BIAS12","VOLMA5"
        };
        private static readonly string[] IndChoices = {
            "PRICE","MA5","MA10","MA20","MACD_DIF","MACD_DEA","MACD_HIST","KDJ_K","KDJ_D","KDJ_J",
            "RSI6","RSI12","RSI24","BOLL_UP","BOLL_MID","BOLL_LOW","WR14","BIAS6","BIAS12"
        };
        private static readonly string[] CondChoices = { ">=", "<=", "CROSS_UP(上穿)", "CROSS_DOWN(下穿)" };

        public ConfigForm(AppConfig cfg, List<string> watch, Dictionary<string, Quote> quotes, Dictionary<string, KlineData> klines)
        {
            this.cfg = cfg;
            this.watch = watch;
            this.quotes = quotes;
            this.klines = klines;
            Text = "配置 - 实时股价监控";
            Font = new Font("Microsoft YaHei UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(860, 520);
            MinimumSize = new Size(760, 460);
            FormClosing += delegate { Save(); };
            BuildUi();
        }

        private void BuildUi()
        {
            // ---- 左侧:自选股管理 ----
            Panel left = new Panel { Location = new Point(8, 8), Size = new Size(250, ClientSize.Height - 70), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left };
            Label lbSearch = new Label { Text = "搜索股票(名称/代码):", Location = new Point(2, 2), AutoSize = true };
            txtSearch = new TextBox { Location = new Point(2, 24), Width = 246 };
            txtSearch.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) DoSearch(); };
            btnAddResult = new Button { Text = "添加选中到自选", Location = new Point(2, 50), Width = 120 };
            btnAddResult.Click += delegate { DoAddResult(); };
            lstResult = new ListBox { Location = new Point(2, 78), Width = 246, Height = 130 };
            Label lbWatch = new Label { Text = "自选股(双击切换):", Location = new Point(2, 212), AutoSize = true };
            lstWatch = new ListBox { Location = new Point(2, 234), Width = 246, Height = 130 };
            btnDelWatch = new Button { Text = "删除选中自选", Location = new Point(2, 370), Width = 120 };
            btnDelWatch.Click += delegate { DoDelWatch(); };
            left.Controls.Add(lbSearch); left.Controls.Add(txtSearch); left.Controls.Add(btnAddResult);
            left.Controls.Add(lstResult); left.Controls.Add(lbWatch); left.Controls.Add(lstWatch); left.Controls.Add(btnDelWatch);
            foreach (string c in watch)
            {
                Quote q;
                quotes.TryGetValue(c, out q);
                lstWatch.Items.Add(q != null && q.Ok ? q.Name + " " + q.Code : c);
            }

            // ---- 右侧:Tabs ----
            TabControl tabs = new TabControl { Location = new Point(266, 8), Size = new Size(ClientSize.Width - 280, ClientSize.Height - 70), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };

            // Tab 指标
            TabPage tpInd = new TabPage("指标显示");
            Panel pnLayout = new Panel { Dock = DockStyle.Top, Height = 42 };
            Label lbLayout = new Label { Text = "指标图显示方式:", Location = new Point(8, 12), AutoSize = true };
            cmbLayout = new ComboBox
            {
                Location = new Point(130, 8), Width = 140, DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbLayout.Items.AddRange(new object[] { "格子排列", "行排列(叠放)" });
            cmbLayout.SelectedIndex = cfg.ChartLayout == "rows" ? 1 : 0;
            pnLayout.Controls.Add(lbLayout); pnLayout.Controls.Add(cmbLayout);
            clbInd = new CheckedListBox { Dock = DockStyle.Top, Height = 190, CheckOnClick = true };
            foreach (string s in IndItems) clbInd.Items.Add(s);
            for (int i = 0; i < IndItems.Length; i++)
                clbInd.SetItemChecked(i, cfg.ShowIndicators != null && cfg.ShowIndicators.Contains(IndItems[i]));
            lblExplain = new Label
            {
                Dock = DockStyle.Fill, AutoSize = false,
                ForeColor = Color.FromArgb(70, 74, 88),
                Text = "指标说明:\r\n" +
                       "· MA(5/10/20) 均线:收盘价N日均值,多头排列(5>10>20)趋势看涨\r\n" +
                       "· MACD(12,26,9):DIF=快线(EMA12-EMA26),DEA=慢线(DIF的9日EMA),柱=2×(DIF-DEA)。DIF上穿DEA=金叉看涨,下穿=死叉看跌\r\n" +
                       "· KDJ(9,3,3) 随机指标:K/D/J三线。J>100超买、J<0超卖;K上穿D=金叉。80/20为超买超卖参考\r\n" +
                       "· RSI(6/12/24) 相对强弱:>70超买、<30超卖;50为强弱分界\r\n" +
                       "· BOLL(20,2) 布林带:中轨=MA20,上下轨=±2倍标准差。触上轨偏超买、触下轨偏超卖;开口放大=波动加剧\r\n" +
                       "· WR14 威廉指标:>80超卖(反弹信号)、<20超买(回调信号)\r\n" +
                       "· BIAS(6/12) 乖离率:价格偏离均线幅度(%),过大有回归需求\r\n" +
                       "· VOLMA5 5日均量:量能趋势"
            };
            tpInd.Controls.Add(lblExplain); tpInd.Controls.Add(clbInd); tpInd.Controls.Add(pnLayout);

            // Tab 提醒
            TabPage tpAlert = new TabPage("提醒规则");
            grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = true, RowHeadersVisible = false, BackgroundColor = Color.White };
            DataGridViewComboBoxColumn colInd = new DataGridViewComboBoxColumn { HeaderText = "指标", DataSource = IndChoices };
            DataGridViewComboBoxColumn colCond = new DataGridViewComboBoxColumn { HeaderText = "条件", DataSource = CondChoices };
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "股票(留空=所有)", Width = 150 });
            grid.Columns.Add(colInd);
            grid.Columns.Add(colCond);
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "触发值", Width = 80 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "启用", Width = 50 });
            foreach (AlertRule r in cfg.Alerts)
            {
                int idx = grid.Rows.Add(r.Stock, r.Indicator, r.Condition, r.Value.ToString("F2"), r.Enabled);
            }
            Button btnAddRule = new Button { Text = "添加规则", Location = new Point(8, 8), AutoSize = false, Size = new Size(90, 26) };
            btnAddRule.Click += delegate { grid.Rows.Add("", "KDJ_J", ">=", "80", true); };
            Button btnTest = new Button { Text = "测试红光提醒", Location = new Point(104, 8), Size = new Size(110, 26) };
            btnTest.Click += delegate { new AlertForm(Math.Max(1, (int)numSec.Value), false).Show(); };
            Panel top = new Panel { Dock = DockStyle.Top, Height = 42 };
            top.Controls.Add(btnAddRule); top.Controls.Add(btnTest);
            tpAlert.Controls.Add(grid); tpAlert.Controls.Add(top);
            grid.BringToFront();

            // Tab 提醒方式
            TabPage tpWay = new TabPage("提醒方式");
            chkFlash = new CheckBox { Text = "全屏红色闪烁提醒", Location = new Point(20, 24), AutoSize = true, Checked = cfg.FlashAlert };
            numSec = new NumericUpDown { Location = new Point(220, 22), Width = 60, Minimum = 1, Maximum = 20, Value = cfg.FlashSeconds };
            Label lbSec = new Label { Text = "闪烁秒数", Location = new Point(288, 24), AutoSize = true };
            chkSound = new CheckBox { Text = "同时播放提示音", Location = new Point(20, 56), AutoSize = true, Checked = cfg.SoundAlert };
            Label lbRefresh = new Label { Text = "刷新间隔(秒):", Location = new Point(20, 90), AutoSize = true };
            numRefresh = new NumericUpDown { Location = new Point(130, 88), Width = 60, Minimum = 2, Maximum = 60, Value = cfg.RefreshSeconds };
            Label lbHotkey = new Label { Text = "一键隐藏快捷键:", Location = new Point(20, 124), AutoSize = true };
            cmbHotkey = new ComboBox
            {
                Location = new Point(130, 120), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbHotkey.Items.AddRange(new object[] { "无(禁用)", "Ctrl+Shift+H", "Ctrl+Alt+H", "Ctrl+H", "Alt+H", "F8", "F9", "Ctrl+F8" });
            string cur = string.IsNullOrEmpty(cfg.HideHotkey) ? "无(禁用)" : cfg.HideHotkey;
            int hi = cmbHotkey.Items.IndexOf(cur);
            cmbHotkey.SelectedIndex = hi >= 0 ? hi : 1;
            Label lbTip = new Label { Text = "提醒规则示例:KDJ_J ≥ 100 超买提醒;RSI6 ≤ 20 超卖提醒;价格 ≥ 1500 提醒;\r\nBOLL_UP 上穿 提醒。触发后全屏闪烁红光,点击任意处关闭。\r\n\r\n一键隐藏:最小化状态下按快捷键可还原/再隐藏;最小化时双击切换自选股,拖到屏幕边缘自动吸附。", Location = new Point(20, 170), AutoSize = true, ForeColor = Color.FromArgb(90, 95, 110) };
            tpWay.Controls.Add(chkFlash); tpWay.Controls.Add(numSec); tpWay.Controls.Add(lbSec);
            tpWay.Controls.Add(chkSound); tpWay.Controls.Add(lbRefresh); tpWay.Controls.Add(numRefresh);
            tpWay.Controls.Add(lbHotkey); tpWay.Controls.Add(cmbHotkey); tpWay.Controls.Add(lbTip);

            tabs.TabPages.Add(tpInd); tabs.TabPages.Add(tpAlert); tabs.TabPages.Add(tpWay);

            Button btnOk = new Button { Text = "保存并关闭", Location = new Point(ClientSize.Width - 120, ClientSize.Height - 48), Size = new Size(110, 32), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            btnOk.Click += delegate { Save(); Close(); };
            Button btnCancel = new Button { Text = "取消", Location = new Point(ClientSize.Width - 220, ClientSize.Height - 48), Size = new Size(90, 32), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            btnCancel.Click += delegate { Close(); };

            Controls.Add(left); Controls.Add(tabs); Controls.Add(btnOk); Controls.Add(btnCancel);
        }

        private void DoSearch()
        {
            string kw = txtSearch.Text.Trim();
            if (kw.Length == 0) return;
            lstResult.Items.Clear();
            string err;
            List<Quote> list = StockApi.Search(kw, out err);
            foreach (Quote q in list) lstResult.Items.Add(q.Name + "  " + q.Code);
            if (list.Count == 0) lstResult.Items.Add("(无结果 " + err + ")");
        }

        private void DoAddResult()
        {
            if (lstResult.SelectedIndex < 0) return;
            // 从搜索缓存里取(重查一次更稳)
            string err;
            List<Quote> list = StockApi.Search(txtSearch.Text.Trim(), out err);
            if (lstResult.SelectedIndex < list.Count)
            {
                string code = list[lstResult.SelectedIndex].Code;
                if (!watch.Contains(code)) { watch.Add(code); cfg.Watch.Add(code); }
                lstWatch.Items.Add(list[lstResult.SelectedIndex].Name + "  " + code);
            }
        }

        private void DoDelWatch()
        {
            if (lstWatch.SelectedIndex < 0 || lstWatch.SelectedIndex >= watch.Count) return;
            string code = watch[lstWatch.SelectedIndex];
            watch.Remove(code);
            cfg.Watch.Remove(code);
            lstWatch.Items.RemoveAt(lstWatch.SelectedIndex);
        }

        private void Save()
        {
            try
            {
                cfg.ShowIndicators.Clear();
                for (int i = 0; i < clbInd.Items.Count; i++)
                    if (clbInd.GetItemChecked(i)) cfg.ShowIndicators.Add(clbInd.Items[i].ToString());
                cfg.Alerts.Clear();
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.IsNewRow) continue;
                    string stock = Cell(row, 0);
                    string ind = Cell(row, 1);
                    string cond = Cell(row, 2);
                    string valS = Cell(row, 3);
                    if (ind.Length == 0 || cond.Length == 0) continue;
                    double val;
                    double.TryParse(valS, NumberStyles.Float, CultureInfo.InvariantCulture, out val);
                    bool en = row.Cells[4].Value != null && (bool)row.Cells[4].Value;
                    AlertRule r = new AlertRule { Stock = stock.Trim(), Indicator = ind, Condition = CondToKey(cond), Value = val, Enabled = en };
                    cfg.Alerts.Add(r);
                }
                cfg.FlashAlert = chkFlash.Checked;
                cfg.SoundAlert = chkSound.Checked;
                cfg.FlashSeconds = (int)numSec.Value;
                cfg.RefreshSeconds = (int)numRefresh.Value;
                cfg.ChartLayout = cmbLayout.SelectedIndex == 1 ? "rows" : "grid";
                cfg.HideHotkey = cmbHotkey.SelectedIndex <= 0 ? "" : cmbHotkey.SelectedItem.ToString();
                ConfigIO.Save(cfg);
            }
            catch (Exception ex) { Log.Error("ConfigForm.Save", ex); }
        }

        private static string Cell(DataGridViewRow row, int i)
        {
            return row.Cells[i].Value == null ? "" : row.Cells[i].Value.ToString();
        }

        private static string CondToKey(string cond)
        {
            if (cond.StartsWith("CROSS_UP")) return "CROSS_UP";
            if (cond.StartsWith("CROSS_DOWN")) return "CROSS_DOWN";
            return cond.Trim();
        }
    }

    // ================================================================ 捐赠界面

    public class DonateForm : Form
    {
        private Image qr;

        public DonateForm()
        {
            Text = "支持开发者";
            Font = new Font("Microsoft YaHei UI", 9f);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(400, 590);
            MinimumSize = new Size(400, 590);
            MaximumSize = new Size(400, 590);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(24, 26, 34);
            ForeColor = Color.FromArgb(235, 235, 240);
            DoubleBuffered = true;
            Paint += OnPaintForm;

            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("StockMonitor.Donate.png"))
                    if (s != null) qr = Image.FromStream(s);
            }
            catch (Exception ex) { Log.Error("DonateForm load qr", ex); }

            Button btnClose = new Button
            {
                Text = "关 闭", Location = new Point((ClientSize.Width - 120) / 2, ClientSize.Height - 52),
                Size = new Size(120, 34), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(56, 62, 80), ForeColor = Color.FromArgb(235, 235, 240),
                FlatAppearance = { BorderColor = Color.FromArgb(90, 100, 130) }
            };
            btnClose.Click += delegate { Close(); };
            Controls.Add(btnClose);
        }

        private void OnPaintForm(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            int w = ClientSize.Width;
            using (SolidBrush title = new SolidBrush(Color.FromArgb(255, 215, 160)))
            using (Font tf = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold))
            {
                string t = "☕ 支持开发者";
                SizeF sz = g.MeasureString(t, tf);
                g.DrawString(t, tf, title, (w - sz.Width) / 2, 22);
            }
            using (SolidBrush sub = new SolidBrush(Color.FromArgb(170, 178, 195)))
            using (Font sf = new Font("Microsoft YaHei UI", 9.5f))
            {
                string t1 = "如果这个工具帮到了你,欢迎扫码支持一下,";
                string t2 = "你的支持是持续更新的最大动力 ^_^";
                SizeF s1 = g.MeasureString(t1, sf), s2 = g.MeasureString(t2, sf);
                g.DrawString(t1, sf, sub, (w - s1.Width) / 2, 58);
                g.DrawString(t2, sf, sub, (w - s2.Width) / 2, 82);
            }

            // 二维码(白底)
            int areaTop = 118;
            int areaH = 400;
            Rectangle qrBox = new Rectangle((w - 340) / 2, areaTop, 340, areaH);
            using (SolidBrush white = new SolidBrush(Color.White))
                g.FillRectangle(white, qrBox);
            using (Pen border = new Pen(Color.FromArgb(70, 82, 108), 1f))
                g.DrawRectangle(border, qrBox);

            if (qr != null)
            {
                int pad = 10;
                Rectangle fit = Fit(qrBox, new Size(qrBox.Width - pad * 2, qrBox.Height - pad * 2), qr.Width, qr.Height);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(qr, fit);
            }
            else
            {
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(120, 128, 146)))
                    g.DrawString("二维码加载失败", new Font("Microsoft YaHei UI", 10f), sb, qrBox.X + 10, qrBox.Y + qrBox.Height / 2 - 8);
            }

            using (SolidBrush hint = new SolidBrush(Color.FromArgb(150, 158, 175)))
            using (Font hf = new Font("Microsoft YaHei UI", 9f))
            {
                string h = "微信 / 支付宝 扫码即可";
                SizeF hs = g.MeasureString(h, hf);
                g.DrawString(h, hf, hint, (w - hs.Width) / 2, ClientSize.Height - 84);
            }
        }

        private static Rectangle Fit(Rectangle box, Size max, int imgW, int imgH)
        {
            double scale = Math.Min((double)max.Width / imgW, (double)max.Height / imgH);
            int dw = (int)(imgW * scale), dh = (int)(imgH * scale);
            int x = box.X + (box.Width - dw) / 2;
            int y = box.Y + (box.Height - dh) / 2;
            return new Rectangle(x, y, dw, dh);
        }
    }

    // ================================================================ 程序入口

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--selftest") return SelfTest();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                Log.Error("UnhandledException", ex);
                MessageBox.Show("发生未处理异常,详情见 error.log:\r\n" + (ex != null ? ex.Message : "未知"), "实时股价", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                Log.Error("ThreadException", e.Exception);
                MessageBox.Show("发生异常,详情见 error.log:\r\n" + e.Exception.Message, "实时股价", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.Run(new FloatingWidget());
            return 0;
        }

        private static int SelfTest()
        {
            StringBuilder sb = new StringBuilder();
            string err;
            Quote q = StockApi.GetQuote("sh600519", out err);
            if (q != null && q.Ok)
                sb.AppendLine("QUOTE OK " + q.Name + " price=" + q.Price.ToString("F2") + " pct=" + q.ChangePct.ToString("F2"));
            else sb.AppendLine("QUOTE FAIL " + err);
            List<TrendPoint> t = StockApi.GetTrend("sh600519", out err);
            sb.AppendLine(t != null && t.Count > 0 ? "TREND OK count=" + t.Count : "TREND FAIL " + err);
            KlineData kd = StockApi.GetKline("sh600519", out err);
            if (kd != null && kd.Bars.Count > 0)
            {
                sb.AppendLine("KLINE OK bars=" + kd.Bars.Count + " last=" + kd.Bars[kd.Bars.Count - 1].Date + " close=" + kd.Bars[kd.Bars.Count - 1].Close.ToString("F2"));
                StockIndicators si = StockIndicators.Compute(kd);
                sb.AppendLine("IND  MA5=" + si.MA5.ToString("F2") + " MACD DIF=" + si.Dif.ToString("F2") + " DEA=" + si.Dea.ToString("F2") +
                              " KDJ K=" + si.K.ToString("F1") + " D=" + si.D.ToString("F1") + " J=" + si.J.ToString("F1") +
                              " RSI6=" + si.Rsi6.ToString("F1") + " BOLL UP=" + si.BollUp.ToString("F2") + " LOW=" + si.BollLow.ToString("F2"));
            }
            else sb.AppendLine("KLINE FAIL " + err);
            List<Quote> search = StockApi.Search("茅台", out err);
            sb.AppendLine(search != null && search.Count > 0 ? "SEARCH OK " + search[0].Name + " " + search[0].Code : "SEARCH FAIL " + err);
            // 图表渲染自测
            try
            {
                if (kd != null && kd.Bars.Count > 0)
                {
                    List<string> keys = new List<string> { "MA5", "MACD", "KDJ", "RSI6", "BOLL", "WR14", "BIAS6", "VOLMA5" };
                    List<IndicatorSeries> series = IndicatorFactory.ComputeAll(kd, keys);
                    using (Bitmap bmp = new Bitmap(900, 640))
                    {
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.FromArgb(20, 22, 30));
                            int cols = 2, cw = 440, ch = 210;
                            for (int i = 0; i < series.Count && i < 8; i++)
                            {
                                Rectangle rc = new Rectangle(10 + (i % cols) * cw, 10 + (i / cols) * ch, cw - 20, ch - 20);
                                ChartPainter.Draw(g, rc, series[i], 90, false);
                            }
                        }
                        bmp.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chart-test.png"));
                    }
                    sb.AppendLine("CHART OK series=" + series.Count);
                }
                else sb.AppendLine("CHART SKIP no kline");
            }
            catch (Exception ex) { sb.AppendLine("CHART FAIL " + ex.Message); }
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("StockMonitor.Donate.png"))
                    sb.AppendLine(s != null ? "DONATE RESOURCE OK len=" + s.Length : "DONATE RESOURCE MISSING");
            }
            catch (Exception ex) { sb.AppendLine("DONATE RESOURCE FAIL " + ex.Message); }
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.txt"), sb.ToString(), Encoding.UTF8);
            return 0;
        }
    }
}
