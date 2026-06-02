# 工具管理

## 工具目录

工具存放在应用目录下的 `tools/` 文件夹中：

```
ToolboxWinUI/
├── tools/
│   ├── CPU工具/
│   │   ├── CPU-Z.lnk
│   │   └── ...
│   ├── 显卡工具/
│   │   ├── GPU-Z.lnk
│   │   └── ...
│   └── ...
└── ToolboxWinUI.exe
```

## 自动扫描

应用启动时自动扫描 `tools/` 目录，根据子文件夹名称自动归类。新增或删除工具文件后，重新进入对应分类即可看到变化。

## 工具配置

工具配置保存在 `%APPDATA%\ToolboxWinUI\tools.json`，格式如下：

```json
[
  {
    "Icon": "🔧",
    "Name": "工具名称",
    "Description": "工具描述",
    "Action": "C:\\path\\to\\tool.exe",
    "Category": "分类名称"
  }
]
```

### 字段说明

| 字段 | 类型 | 说明 |
|------|------|------|
| Icon | string | 图标：emoji、Segoe Fluent Icons 编码、或图标文件路径 |
| Name | string | 工具名称 |
| Description | string | 工具描述 |
| Action | string | 执行动作（见下表） |
| Category | string | 所属分类（对应导航栏标签） |

### Action 类型

| 前缀 | 说明 | 示例 |
|------|------|------|
| 无前缀 | 直接启动程序 | `C:\Tools\CPU-Z.exe` |
| `cmd:` | 执行命令 | `cmd:msinfo32` |
| `msg:` | 显示消息 | `msg:这是一个提示` |
| `dl:` | 下载插件 | `dl:proxytools` |

## 收藏夹

点击工具卡片右上角的 ⭐ 按钮即可收藏，收藏的工具会在「收藏夹」分类中显示，方便快速访问。

## 导入导出

在 **设置 → 工具管理** 中可以：

- **导出工具配置** — 将当前工具列表导出为 JSON 文件
- **导入工具配置** — 从 JSON 文件导入工具列表
- **恢复默认工具** — 重置为出厂工具列表
