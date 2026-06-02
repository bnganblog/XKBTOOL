# 代理工具

## 简介

代理工具基于 [Mihomo](https://github.com/MetaCubeX/mihomo) 内核，提供一键启停、配置管理、系统代理设置等功能。

::: tip 安装代理工具
首次使用需要在 **工具商店** 中安装 ProxyTools 插件。
:::

## 功能特性

- **一键启停** — 点击按钮即可启动/停止代理
- **状态监控** — 实时显示代理运行状态（绿灯/红灯）
- **配置管理** — 支持上传配置文件（YAML 格式）和在线编辑
- **系统代理** — 自动设置/清除 Windows 系统代理
- **Dashboard** — 内置 MetaCubeXD 面板，端口 19090

## 使用方法

### 1. 安装插件

1. 点击左侧导航栏「工具商店」
2. 找到 ProxyTools 卡片，点击「安装」
3. 等待下载完成，左侧导航栏将出现「代理工具」

### 2. 配置代理

1. 进入「代理工具」页面
2. 点击「上传配置」选择 `.yaml` 配置文件
3. 或点击「编辑配置」在线修改

### 3. 启动代理

1. 点击「启动」按钮
2. 状态指示灯变为绿色表示运行中
3. 系统代理自动设置

### 4. Dashboard

代理运行后，可通过以下地址访问面板：

```
http://127.0.0.1:19090/ui
```

## 配置文件

配置文件保存在 `%APPDATA%\ToolboxWinUI\proxy\config.yaml`。

::: warning 注意
配置文件会自动追加以下必要字段，请勿手动删除：
- `mixed-port: 7890`（混合端口）
- `allow-lan: false`（不允许局域网连接）
- `mode: rule`（规则模式）
- `external-controller: 0.0.0.0:9097`（REST API 端口）
- `log-file: logs/run.log`（日志文件）
:::

## Mihomo 内核

- **版本**: v1.19.11
- **内核文件**: `ProxyTools/mihomo/mihomo.exe`
- **地理数据**: `ProxyTools/mihomo/country.mmdb`、`geo.mmdb`、`geoSite.dat`

## 故障排除

### 代理无法启动

1. 检查端口 7890 是否被占用
2. 检查配置文件格式是否正确
3. 查看日志文件 `logs/run.log`

### 无法访问 Dashboard

1. 确认代理已启动
2. 检查端口 19090 是否被占用
3. 尝试访问 `http://127.0.0.1:9097` 检查 REST API
