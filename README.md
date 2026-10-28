# 更矩

Windows 上的应用与网页用时统计。看时间花在哪，并可以给程序和网站设每日上限、定时提醒休息。

## 运行

需要 Windows 10 以上、.NET Framework 4.8（Win10 一般都自带）。

从 Releases 下载压缩包，解压后运行 `guage.exe`。程序常驻托盘：双击打开主界面，右键出菜单。

建议以管理员身份运行，否则读不到部分提权进程，那部分软件不会被统计。

卸载：设置里关掉开机自启，再删程序目录。

## 功能

- 应用用时：按前台窗口统计，今日 / 本周 / 本月 / 自定义区间，可分类、可设别名
- 网页用时：需装浏览器扩展，只统计 http/https 页面
- 时长限制：给程序设每日额度，到点可选仅提醒、倒计时后结束进程、全屏锁定
- 休息提醒：工作与休息循环计时，到点弹窗加托盘通知
- 过滤与白名单：按进程名、URL 或正则排除，也可只统计白名单里的程序
- 数据导出：xlsx 或 csv

## 浏览器扩展

网页统计要装 `Guage.Chrome` 目录里的扩展（Chrome / Edge 通用）：

1. 打开 `chrome://extensions`（Edge 用 `edge://extensions`），开启开发者模式
2. 「加载已解压的扩展程序」，选 `Guage.Chrome` 目录
3. 客户端 设置 > 常规 > 功能 里打开网页统计，首页右上角显示「已连接」即正常

## 数据

全部在本机，默认在程序目录下的 `Data`，可在设置里改位置，核心是 `Data\\data.db`。没有账号，没有云端，没有遥测。

出网只有两件事：点检查更新时拉 release；开了网页统计时抓站点图标，只连该页面的域名，不跟内网和回环地址的重定向。

## 构建

用 Visual Studio 打开 `guage.sln`，或：

```bash
MSBuild.exe guage.sln -t:Build -p:Configuration=Release -p:Platform="Any CPU"
```

产物在 `UI\\bin\\Release\\guage.exe`。测试是零依赖控制台工程：

```bash
MSBuild.exe guage.sln -t:Build -p:Configuration=Debug -p:Platform="Any CPU"
Tests\\Guage.Tests\\bin\\Debug\\Guage.Tests.exe
```

发布包要一起上传 `.sha256` 与 `.sha256.sig`，用 `python scripts/sign-release.py <zip>` 生成，否则客户端不会安装更新。

## 许可

MIT，见 LICENSE。
