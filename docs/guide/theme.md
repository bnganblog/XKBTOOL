# 主题设置

## 主题选项

在 **设置 → 外观 → 主题** 中可以切换以下主题：

| 主题 | 说明 |
|------|------|
| 浅色模式 | 标准浅色主题，纯白背景 |
| 深色毛玻璃 | 深色背景 + 亚克力毛玻璃效果 |
| 浅色毛玻璃 | 浅色背景 + 亚克力毛玻璃效果（默认） |

## 毛玻璃效果

毛玻璃主题使用 Windows 11 原生亚克力材质（Acrylic），在窗口背景上呈现半透明模糊效果，与系统风格统一。

::: tip 系统要求
毛玻璃效果需要 Windows 11 或 Windows 10 1809 以上版本。
:::

## 图表样式

在 **设置 → 外观 → 占用图表样式** 中可以切换系统信息图表的展示方式：

| 样式 | 说明 |
|------|------|
| 圆形图 | 环形进度图，类似 Windows 任务管理器 |
| 横向进度条 | 水平条形图，紧凑直观 |

## 下载代理

在 **设置 → 更新 → 下载代理** 中可以配置 GitHub 资源加速下载代理：

- **默认**: `https://ghfast.top/`
- 支持自定义代理地址
- 留空则直连 GitHub

::: tip 加速代理
如果 GitHub 下载速度较慢，建议使用加速代理。常见的代理服务：
- `https://ghfast.top/`
- `https://ghproxy.com/`
- `https://mirror.ghproxy.com/`
:::

## 配置文件

设置保存在 `%APPDATA%\ToolboxWinUI\settings.json`：

```json
{
  "theme": "LightGlass",
  "sysInfoStyle": "Circular",
  "downloadProxy": "https://ghfast.top/"
}
```
