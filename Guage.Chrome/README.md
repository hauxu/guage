# 更矩 网页统计（浏览器扩展）

配合 更矩 客户端记录网页浏览时长。只连本机 `127.0.0.1`。

## 安装

先启动客户端，在 设置 > 常规 > 功能 打开网页统计，然后：

1. 打开 `chrome://extensions`（Edge 用 `edge://extensions`）
2. 开启右上角开发者模式
3. 「加载已解压的扩展程序」，选本目录

装好后客户端首页右上角显示绿色「已连接」。多个浏览器同时装会显示「已连接 (2)」。

## 做什么

- 记录当前标签页的浏览时长，浏览器失焦时结算
- 上报标题和 favicon
- 只统计 `http/https`
- 令牌鉴权，只连本机

## 端口

| 用途 | 主 | 备用 |
|---|---|---|
| WebSocket | 8908 | 8910 / 8912 |
| 令牌 | 8909 | 8911 / 8913 |

## 图标

```powershell
powershell -ExecutionPolicy Bypass -File scripts/generate-extension-icons.ps1
```
