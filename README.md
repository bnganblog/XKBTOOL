# 显卡吧工具箱 WinUI3

## 概述

**显卡吧工具箱 WinUI3** 是一款面向 PC 硬件爱好者、DIY 玩家和维修人员的 Windows 桌面工具集。它将各类硬件检测、系统优化、性能测试工具整合到统一的现代化界面中，让你告别杂乱的桌面和繁琐的文件夹翻找。

![img](img\main.jpg)

一个**开箱即用、美观高效**的工具管理平台。你只需将常用硬件工具放入 `tools/` 目录，应用便会自动识别并以卡片形式呈现，配合 WinUI 3 的原生流畅设计语言和毛玻璃特效，带来媲美系统级应用的体验。

> **用 WinUI 3 重写的图吧工具箱精神继承者 —— 更现代、更轻量、更易扩展。**

## 特性

### 🧰 智能工具管理
将 exe/bat 放入 `tools/` 目录，启动即自动识别。支持收藏夹、快捷打开、自定义设置入口，告别散落桌面的绿色软件。

### 📊 实时硬件监控
一键查看 CPU 型号与频率（精确到 2 位小数）、内存规格与频率、GPU 信息、硬盘型号，配合**纯自绘环形用量图表**实时监控资源占用，无需额外安装 AIDA64 或 HWiNFO。

### 🌐 网络诊断工具
- **IP 查询** — 聚合多个来源的公共 IP 地址，准确识别运营商与地理位置
- **分流测试** — DNS 解析地理定位 + Ping 延迟测试，快速判断网络分流是否正常

### 🎨 WinUI 3 原生质感
基于 Windows App SDK 构建，支持三种主题自由切换：

| 主题 | 视觉效果 | 默认 |
|------|----------|------|
| 浅色毛玻璃 | DesktopAcrylicBackdrop 亚克力模糊 + 浅色元素 | ✅ |
| 深色毛玻璃 | DesktopAcrylicBackdrop 亚克力模糊 + 深色元素 | |
| 纯浅色模式 | 经典纯白实心背景，低功耗 | |

### 🔄 动态目录同步
每次启动自动扫描 `tools/` 目录，新增工具即刻出现、删除工具自动移除，无需手动编辑配置文件。

### 💾 全自动数据持久化
收藏夹、工具列表、主题偏好自动保存至 `%APPDATA%\ToolboxWinUI\`，重装不丢失。

## 截图

> *(添加截图)*

## 系统要求

- Windows 11 
- [.NET 10.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)（仅开发时需要）
- [Windows App SDK 1.8+](https://github.com/microsoft/windowsappsdk)

## 构建与运行

```bash
git clone https://github.com/bnganblog/XKBTOOL.git
cd XKBTOOL
dotnet build ToolboxWinUI --configuration Debug
.\ToolboxWinUI\bin\Debug\net10.0-windows10.0.19041.0\ToolboxWinUI.exe
```

发布：

```bash
dotnet publish ToolboxWinUI --configuration Release -p:Platform=x64
```

## 项目结构

```
XKBTOOL/
├── ToolboxWinUI/              # 主项目
│   ├── MainWindow.xaml(.cs)   # 主窗口（导航、工具卡片、系统信息、网络工具）
│   ├── App.xaml(.cs)          # 应用入口（主题初始化）
│   ├── Pages/
│   │   └── SettingsPage.xaml(.cs)  # 设置页面
│   ├── Controls/
│   │   └── UsageChart.cs      # 环状用量图表控件
│   ├── Models/
│   │   └── ToolInfo.cs        # 工具数据模型
│   └── tools/                 # 硬件工具目录（自动扫描）
├── Assets/                    # 应用资源
├── Package.appxmanifest       # 包清单
└── tools.json                 # 默认工具定义
```

## 技术栈

- **框架**: WinUI 3 / Windows App SDK 1.x
- **语言**: C# 14 (.NET 10)
- **图表**: 自定义 `PathGeometry` 环形图表（无第三方依赖）
- **系统信息**: WMI (`System.Management`) + `PerformanceCounter`
- **网络**: `HttpClient` + `System.Net.NetworkInformation`

## 主题系统

| 主题 | 背景 | 标题栏 |
|------|------|--------|
| 浅色模式 | 纯白实心 | 浅色 |
| 浅色毛玻璃 (默认) | DesktopAcrylicBackdrop + 浅色元素 | 浅色 |
| 深色毛玻璃 | DesktopAcrylicBackdrop + 深色元素 | 深色 |

## 数据存储

所有用户数据保存在 `%APPDATA%\ToolboxWinUI\`：

- `tools.json` — 工具列表（合并默认 + 扫描结果）
- `favorites.json` — 用户收藏的工具
- `settings.json` — 主题等设置

## 许可

MIT
