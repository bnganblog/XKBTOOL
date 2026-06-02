# 网络工具

## 功能概述

网络工具页面集成三大功能模块：IP 查询、分流测试、网速测试，帮助用户全面了解网络状态。

## IP 查询

支持多个数据源查询当前 IP 地址和地理位置：

| 数据源 | 说明 |
|--------|------|
| ip.sb | 支持 IPv4/IPv6 查询 |
| ip-api.com | 返回详细地理位置和 ISP 信息 |
| ipinfo.io | 返回城市、地区、组织信息 |

支持一键 Ping 测试，检测网络延迟。

## 分流测试

测试不同目标域名的 DNS 解析和 CDN 节点分配：

| 测试项 | 说明 |
|--------|------|
| cf-cdn | Cloudflare CDN 节点检测 |
| cf-cdn-trace | CDN Trace 路径追踪 |

支持多个目标域名同时测试：
- `cloudflare.com`
- `www.visa.com.tw`
- `www.visa.com.hk`
- `jikipedia.com`
- `cn.bing.com`
- `www.bilibili.com`

## 网速测试

基于 Cloudflare 的网速测试服务：

- **下载速度** — 实时 Mbps 显示
- **上传速度** — 实时 Mbps 显示
- **延迟** — Ping 值
- **抖动** — 网络稳定性

支持实时折线图展示速度变化趋势，测试完成后显示详细结果。
