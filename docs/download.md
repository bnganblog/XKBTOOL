# 下载

## 系统要求

- **操作系统**: Windows 10 1809 及以上版本（推荐 Windows 11）
- **运行环境**: .NET 10 Desktop Runtime（首次运行自动提示安装）
- **磁盘空间**: 约 300 MB

## 下载方式

### GitHub Release（推荐）

从 GitHub Release 页面下载最新版本：

<div style="margin: 24px 0">
<a href="https://github.com/bnganblog/xkbtool/releases/latest" target="_blank" style="display: inline-block; padding: 12px 32px; background: #0078d4; color: #fff; border-radius: 8px; text-decoration: none; font-weight: 600; font-size: 16px">📦 前往下载</a>
</div>

### 文件说明

| 文件 | 说明 |
|------|------|
| `XKBToolbox.zip` | 完整包，解压即用 |
| `ProxyTools.zip` | 代理工具插件（Mihomo 内核），可选安装 |

## 安装步骤

1. 下载 `XKBToolbox.zip` 并解压到任意目录
2. 运行 `ToolboxWinUI.exe`
3. 如需代理功能，点击左侧「工具商店」→「安装」ProxyTools

## 便携版

本软件为绿色便携版，无需安装，解压即可使用。所有配置文件保存在：

```
%APPDATA%\ToolboxWinUI\
├── settings.json    # 应用设置
├── tools.json       # 工具配置
└── proxy/           # 代理配置
```

## 更新

软件内置自动检查更新功能，也可在 设置 → 更新 中手动检查。新版本下载后解压覆盖即可，配置文件不会丢失。
