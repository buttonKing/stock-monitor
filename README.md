# 实时股价监控 (StockMonitor)

一个 **Windows 桌面实时股价监控悬浮窗**:输入股票代码或名称,实时显示股价与分时走势,内置 KDJ / MACD / RSI / BOLL / WR / BIAS 等专业指标,支持条件提醒(全屏红光闪烁)。纯绿色单文件,双击即用,无需安装,支持在线更新。

![platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![framework](https://img.shields.io/badge/.NET-Framework%204.x-green)
![license](https://img.shields.io/badge/license-MIT-yellow)

---

## ✨ 功能特性

- 🪟 **透明悬浮窗**:无边框、置顶、可拖动、可缩放,位置大小自动记忆
- 🔍 **代码 + 名称搜索**:输 `600519` 或直接输 `茅台`,自动弹出搜索下拉
- 📊 **分时 / 日K 一键切换**:分时主图(价格+均价)或日K蜡烛图(近120天)
- 📈 **指标格子墙**:MA / MACD / KDJ / RSI / BOLL / WR / BIAS / 量能,格子排列或行排列(可配置),悬浮看数值、点击放大
- 🚨 **条件提醒**:如 `KDJ_J ≥ 100`、`RSI6 ≤ 20`、`价格 ≥ 1500`、`MACD_DIF 上穿`,触发后**全屏红色闪烁 + 提示音**
- 🪙 **一键隐藏**:`Ctrl+Shift+H` 全局热键缩成小条(名称+股价),双击小条切换自选股,拖到屏幕边缘自动吸附
- 🗂️ **自选股管理**:自动刷新所有自选,搜索添加,配置自动保存
- 💝 **捐赠入口**:内置收款二维码,支持开发者
- 🔄 **在线更新**:启动自动检查新版本,一键升级

## 🚀 快速使用

直接下载 [StockMonitor.exe](StockMonitor.exe),双击运行即可(Windows 10/11 自带运行环境,无需安装)。

```
输入框: 600519 / hk00700 / usAAPL   (代码)
        茅台 / 平安                  (名称,自动搜索)
```

## 🔄 在线更新

程序内置自动更新(经 `api.github.com`,国内可直连):

- 启动后自动检查,发现新版弹窗提示,一键更新并自动重启
- **支持任意安装目录**:exe 所在目录不可写时(如 Program Files),自动重定向到用户目录 `%LOCALAPPDATA%\StockMonitor\` 运行
- 更新文件经 **sha256 校验**,防止下载损坏或篡改
- 也可在「配置 → 提醒方式 → 检查更新」手动检查

## 🔨 从源码编译

环境:Windows + .NET Framework 4.x(系统自带编译器 csc)。

```
build.bat
```

编译产物为 `StockMonitor.exe`(单文件,含捐赠二维码资源)。

## 📁 项目结构

```
StockMonitor.Core.cs    # 数据源 + 指标计算 + 配置 + 提醒引擎 + 在线更新
StockMonitor.UI.cs      # 悬浮窗 / 配置窗口 / 捐赠窗口 / 主程序 / 更新流程
StockMonitor.Chart.cs   # 指标图渲染 / 图墙 / 大图窗口
build.bat               # 一键编译(系统自带 csc)
version.json            # 在线更新版本清单(version / notes / sha256)
捐赠.png                # 捐赠收款二维码(内嵌进 exe)
```

## 🛠️ 维护者:如何发布新版本

更新机制:程序启动时读取仓库 `version.json`,比对版本号,发现新版则下载 exe 并自动替换重启。

**发布步骤:**

1. **改版本号**:编辑 `StockMonitor.Core.cs`,修改 `Updater.APP_VERSION`(如 `"14.1.0"`)
2. **编译**:运行 `build.bat`,生成新的 `StockMonitor.exe`
3. **计算哈希**:

   ```powershell
   Get-FileHash StockMonitor.exe -Algorithm SHA256
   ```

4. **更新 `version.json`**:修改 `version`(与第 1 步一致)、`notes`(本次更新说明,用户更新弹窗会显示)、`sha256`(第 3 步的哈希)
5. **上传两个文件到仓库**:
   - `StockMonitor.exe`
   - `version.json`

   (建议同时创建 GitHub Release 并附加 exe;网络受限时可调用 `api.github.com` 的 contents API 上传)
6. **完成**:用户下次启动(或点「配置 → 检查更新」)即收到更新提示,一键升级。

> ⚠️ 注意:`sha256` 必须与上传的 exe 完全一致,否则客户端会拒绝更新(安全校验)。
> 若同时修改了其他行为,记得同步更新 `README.md` 与「使用说明.txt」。

## 🔌 数据来源

- 实时行情:腾讯财经公开接口
- 分时/日K:腾讯财经、东方财富公开接口(免费,无需 Key)

## ⚠️ 免责声明

本软件仅为**行情展示与提醒工具**,不构成任何投资建议。股市有风险,入市需谨慎。

## 📄 License

[MIT](LICENSE)

## ## 👊捐赠者

* Don Quijote
*  页
