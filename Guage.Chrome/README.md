# guage Sentry（浏览器扩展）

配合 更矩 客户端统计网页浏览时长的 Chrome / Edge 扩展。只与本机 `127.0.0.1` 上的客户端通信。

## 安装

先启动客户端，在 设置 > 常规 > 功能 里打开「网站浏览统计」，再装扩展：

**扩展商店**：搜 "guage Sentry" 安装。

**本地加载**：打开 `chrome://extensions`，开启右上角「开发者模式」，点「加载已解压的扩展程序」，选择本目录。Edge 用 `edge://extensions`。

装好后在客户端首页右上角看到绿色「已连接」即正常。

从源码克隆仓库时，直接载入 `Guage.Chrome` 目录；客户端 设置 > 关于 > 打开扩展目录 可以定位到它。

`WebExtensions\Chrome` 只是指向 `Guage.Chrome` 的本机符号链接，方便部分工具按固定路径加载。发布包和 zip 里用的是 `Guage.Chrome` 实体目录，压缩时符号链接可能被展开或丢失。

## 做什么

- 统计当前焦点标签页的浏览时长，浏览器失去焦点时结算
- 上报网页标题与 favicon
- 只统计 `http/https` 页面
- 通过 WebSocket 令牌鉴权，只连本机客户端

## 连接状态

扩展不改变浏览器工具栏图标。连接状态在客户端首页看：绿色圆点加「已连接」，装了多个浏览器时显示「已连接 (2)」。

## 端口

| 用途 | 主端口 | 备用 |
|------|--------|------|
| WebSocket | 8908 | 8910 / 8912 |
| 令牌 HTTP | 8909 | 8911 / 8913 |

## 生成图标

```powershell
powershell -ExecutionPolicy Bypass -File scripts/generate-extension-icons.ps1
```
