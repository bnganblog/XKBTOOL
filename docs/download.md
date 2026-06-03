# 下载

## 系统要求

- **操作系统**: Windows 10 1809 及以上版本（推荐 Windows 11）
- **运行环境**: .NET 10 Desktop Runtime（首次运行自动提示安装）
- **磁盘空间**: 约 300 MB

---

<div class="dl-card">
  <div class="dl-card-bg">
    <div class="dl-orb"></div>
    <div class="dl-grid"></div>
  </div>
  <div class="dl-card-content">
    <div class="dl-card-icon">
      <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4M7 10l5 5 5-5M12 15V3"/></svg>
    </div>
    <div class="dl-card-title">显卡吧工具箱 WinUI3</div>
    <div class="dl-card-version">v1.0.0 · Windows 10/11 · 约 300 MB</div>
    <div class="dl-card-desc">面向 PC 硬件爱好者与 DIY 玩家的 Windows 桌面工具集，将各类硬件检测、性能测试、系统优化工具整合到统一的现代化界面中，开箱即用。</div>
    <div class="dl-card-buttons">
      <a href="https://pan.quark.cn/s/002075914b65" target="_blank" class="dl-btn primary">
        夸克网盘下载
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4M7 10l5 5 5-5M12 15V3"/></svg>
      </a>
      <a href="https://github.com/bnganblog/xkbtool/releases/latest" target="_blank" class="dl-btn secondary">
        <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor"><path d="M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z"/></svg>
        GitHub Release
      </a>
    </div>
  </div>
</div>

---

### 文件说明

| 文件 | 说明 |
|------|------|
| `XKBToolbox.zip` | 完整包，解压即用 |
| `ProxyTools.zip` | 代理工具插件（Mihomo 内核），可选安装 |

## 安装步骤

<div class="steps-grid">
  <div class="step-card">
    <div class="step-num">1</div>
    <div class="step-title">下载解压</div>
    <div class="step-desc">下载 XKBToolbox.zip 并解压到任意目录</div>
  </div>
  <div class="step-card">
    <div class="step-num">2</div>
    <div class="step-title">运行程序</div>
    <div class="step-desc">双击运行 ToolboxWinUI.exe</div>
  </div>
  <div class="step-card">
    <div class="step-num">3</div>
    <div class="step-title">安装插件</div>
    <div class="step-desc">点击左侧「工具商店」→「安装」所需插件</div>
  </div>
</div>

## 便携版

本软件为绿色便携版，无需安装，解压即可使用。所有配置文件保存在：

```
%APPDATA%\ToolboxWinUI\
├── settings.json    # 应用设置
├── tools.json       # 工具配置
└── proxy/           # 代理配置
```

## 更新

软件内置自动检查更新功能，也可在 **设置 → 更新** 中手动检查。新版本下载后解压覆盖即可，配置文件不会丢失。

<style>
.dl-card {
  position: relative;
  border-radius: 20px;
  overflow: hidden;
  margin: 32px 0;
  background: linear-gradient(145deg, rgba(124,58,237,0.08), rgba(34,211,238,0.04));
  border: 1px solid rgba(255,255,255,0.08);
}
.dl-card-bg { position: absolute; inset: 0; pointer-events: none; }
.dl-orb {
  position: absolute; top: -50%; right: -20%;
  width: 400px; height: 400px; border-radius: 50%;
  background: radial-gradient(circle, rgba(124,58,237,0.12), transparent 60%);
}
.dl-grid {
  position: absolute; inset: 0;
  background-image: linear-gradient(rgba(255,255,255,0.03) 1px, transparent 1px),
    linear-gradient(90deg, rgba(255,255,255,0.03) 1px, transparent 1px);
  background-size: 40px 40px;
}
.dl-card-content { position: relative; padding: 40px; text-align: center; }
.dl-card-icon {
  width: 64px; height: 64px; border-radius: 16px;
  background: linear-gradient(135deg, var(--accent), var(--accent-cyan));
  display: flex; align-items: center; justify-content: center;
  margin: 0 auto 20px; color: #fff;
  box-shadow: 0 0 24px -4px rgba(124,58,237,0.4);
}
.dl-card-title { font-size: 28px; font-weight: 700; color: var(--vp-c-text-1); margin-bottom: 6px; }
.dl-card-version { font-size: 14px; color: var(--vp-c-text-2); margin-bottom: 16px; }
.dl-card-desc {
  font-size: 15px; color: var(--vp-c-text-2); line-height: 1.7;
  max-width: 500px; margin: 0 auto 28px;
}
.dl-card-buttons { display: flex; gap: 12px; justify-content: center; flex-wrap: wrap; }

.dl-btn {
  display: inline-flex; align-items: center; gap: 8px;
  padding: 14px 32px; border-radius: 10px; font-size: 16px; font-weight: 600;
  text-decoration: none; transition: all 0.25s; border: none; cursor: pointer;
}
.dl-btn.primary {
  background: linear-gradient(135deg, var(--accent), var(--accent-cyan));
  color: #fff;
  box-shadow: 0 0 32px -6px rgba(124,58,237,0.4);
}
.dl-btn.primary:hover {
  transform: translateY(-2px);
  box-shadow: 0 8px 36px -8px rgba(124,58,237,0.6);
}
.dl-btn.secondary {
  background: rgba(255,255,255,0.06); color: var(--vp-c-text-1);
  border: 1px solid rgba(255,255,255,0.1); backdrop-filter: blur(8px);
}
.dl-btn.secondary:hover {
  background: rgba(255,255,255,0.12); border-color: rgba(255,255,255,0.2);
}

/* Steps */
.steps-grid {
  display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px;
}
.step-card {
  background: rgba(255,255,255,0.03); border: 1px solid rgba(255,255,255,0.05);
  border-radius: 14px; padding: 32px 24px; text-align: center;
  backdrop-filter: blur(8px); transition: all 0.3s;
}
.step-card:hover {
  background: rgba(255,255,255,0.06);
  border-color: rgba(124,58,237,0.3);
  transform: translateY(-2px);
}
.step-num {
  width: 48px; height: 48px; border-radius: 50%;
  background: linear-gradient(135deg, var(--accent), var(--accent-cyan));
  color: #fff; font-size: 22px; font-weight: 700;
  display: flex; align-items: center; justify-content: center;
  margin: 0 auto 16px;
}
.step-title { font-size: 18px; font-weight: 600; color: var(--vp-c-text-1); margin-bottom: 8px; }
.step-desc { font-size: 14px; color: var(--vp-c-text-2); line-height: 1.5; }

@media (max-width: 600px) {
  .steps-grid { grid-template-columns: 1fr; }
  .dl-card-content { padding: 28px 20px; }
}
</style>
