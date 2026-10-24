# guage Sentry 浏览器扩展 v1.0.0

用于 **Google Chrome** 与 **Microsoft Edge** 的 更矩 网页浏览统计插件。仅与本机 `127.0.0.1` 上的 更矩 客户端通信。

## 使用前

1. 启动 更矩 客户端  
2. 在 **设置 > 常规 > 功能** 中开启 **网站浏览统计**  
3. 安装本扩展（见下方两种方式任选其一）  
4. 在 更矩 **首页「概览」右上角** 确认显示绿色 **已连接**

---

## 安装方式一：扩展商店（推荐）

适合大多数用户，安装后由商店自动更新。

### Chrome

1. 打开 [Chrome 网上应用店](https://chromewebstore.google.com/search/guage%20Sentry)  
2. 搜索 **guage Sentry** 并安装  
3. 确认扩展已启用  

### Edge

1. 打开 [Edge 加载项](https://microsoftedge.microsoft.com/addons/search/guage%20Sentry)  
2. 搜索 **guage Sentry** 并获取  
3. 确认扩展已启用  

> 若商店尚未上架，请使用下方的 **方式二（ZIP / 本地载入）**。

---

## 安装方式二：ZIP 或本地文件夹载入

适合开发者、内测用户，或商店暂未提供下载时。

### 从 ZIP 安装

1. 获取 `guage-Sentry.zip`（发布包或 GitHub Releases）  
2. 解压到任意目录（例如 `D:\Apps\guage-Sentry`）  
3. **Chrome**：`chrome://extensions/` → 开启「开发者模式」→ **加载已解压的扩展程序** → 选择解压后的文件夹（内含 `manifest.json`）  
4. **Edge**：`edge://extensions/` → 开启「开发人员模式」→ **加载解压缩的扩展** → 选择同一文件夹  

### 从源码目录安装

若你克隆了 更矩 仓库，可直接载入 **`Guage.Chrome`** 目录：

- 在 更矩 客户端：**设置 > 关于 > 打开扩展目录**，再在浏览器扩展页载入该文件夹  
- 工作区里的 `WebExtensions\Chrome` 只是指向 `Guage.Chrome` 的本机符号链接（方便部分工具按固定路径加载）；**发布包与 zip 请直接使用 `Guage.Chrome` 目录**，符号链接在压缩/CI 流程中可能被展开或丢失  

---

## 功能

- 统计当前焦点标签页的浏览时长  
- 读取并上报网页标题、网页图标（favicon）  
- 浏览器失去焦点时自动结算时长  
- WebSocket 令牌鉴权，仅与本机 更矩 通信  
- 仅统计 `http/https` 页面  

## 连接状态

扩展**不会**改变浏览器工具栏图标颜色。请在 **更矩 客户端首页** 查看连接状态（绿色圆点 +「已连接」；多浏览器同时安装时显示「已连接 (2)」等）。

## 端口

| 用途 | 主端口 | 备用 |
|------|--------|------|
| WebSocket | 8908 | 8910 / 8912 |
| 令牌 HTTP | 8909 | 8911 / 8913 |

## 维护

生成扩展图标：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/generate-extension-icons.ps1
```

## 版本

- **1.0.0** — 正式版；支持扩展商店安装与 ZIP/本地载入两种方式
