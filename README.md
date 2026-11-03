# 更矩

Windows 上的应用与网页用时统计。统筹规划，定时定量。

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Release](https://img.shields.io/github/v/release/hauxu/guage)](https://github.com/hauxu/guage/releases)

> 数据全部留在本机：没有账号，没有云端，没有遥测。

## 功能

| 功能 | 说明 |
| --- | --- |
| 应用用时 | 按前台窗口统计，今日 / 本周 / 本月 / 自定义区间，可分类、可设别名 |
| 网页用时 | 配合浏览器扩展统计 http/https 页面时长（Chrome / Edge 通用） |
| 时长限制 | 给程序设每日额度：仅提醒 / 倒计时后结束进程 / 锁定遮罩；支持密码解锁、禁止重启、跨天重置 |
| 休息提醒 | 工作与休息循环计时，到点弹窗加托盘通知 |
| 过滤与白名单 | 按进程名、URL 或正则排除，也可只统计白名单里的程序 |
| 数据导出 | 导出 xlsx 或 csv |

## 下载与安装

到 [Releases](https://github.com/hauxu/guage/releases) 下载最新压缩包，解压后运行 `guage.exe`。程序常驻托盘：双击图标打开主界面，右键出菜单。

需要 Windows 10（1803 及以上）与 .NET Framework 4.8（Win10 一般自带）。

建议以管理员身份运行，否则读不到部分提权进程，那部分软件不会被统计。

卸载：在设置里关掉开机自启，再删除程序目录。

## 浏览器扩展

网页用时需要安装 `Guage.Chrome` 目录里的扩展（Chrome / Edge 通用）：

1. 打开 `chrome://extensions`（Edge 用 `edge://extensions`），开启开发者模式
2. 「加载已解压的扩展程序」，选择 `Guage.Chrome` 目录
3. 在客户端「设置 > 常规 > 功能」里打开网页统计；首页右上角显示「已连接」即正常

扩展只与客户端在本机通信，不上传任何数据。

## 数据与隐私

- 数据只存在本机，存放位置可在设置里修改
- 没有账号，没有云端，没有遥测
- 只有两种情况会出网：检查更新时拉取发布信息；开启网页统计时抓取站点图标（只连该页面的域名，不跟随指向内网与回环地址的重定向）
- 隐私政策见 [privacy.txt](privacy.txt)

## 常见问题

Q：某些程序统计不到？

用管理员身份运行客户端；普通权限读不到提权进程。

Q：扩展装好了，首页却没显示「已连接」？

先确认客户端在运行，且「设置 > 常规 > 功能」里的网页统计已打开；再确认加载扩展时选的是 `Guage.Chrome` 目录本身。

Q：发布的压缩包怎么校验？

每个发布包都附带 SHA-256 与签名，客户端安装更新时会自动校验。

## 反馈

- Bug 与建议：[Issues](https://github.com/hauxu/guage/issues)
- 讨论：[Discussions](https://github.com/hauxu/guage/discussions)

## 致谢

界面与功能基于 [Tai](https://github.com/planshit/Tai)（MIT）修改，本项目在其基础上重构并继续维护。

## 许可

[MIT](LICENSE)

> guage 是 gauge 的拼写变体，中文名「更矩」。
