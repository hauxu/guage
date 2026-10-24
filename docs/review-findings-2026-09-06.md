# 更矩 全面审查问题清单（2026-09-06）

三路并行审查结论（Core 核心逻辑 / UI 层 / 插件与协议），并与上游原版 [Planshit/Tai](https://github.com/Planshit/Tai) 逐项对比。
每条标注：严重度、位置（文件:行号为审查时状态，修复后可能偏移）、来源【继承自 Tai / 更矩新增 / 更矩改进残留】、上游做法、修复方案。

来源统计：继承自 Tai 约 10 条（上游同款缺陷），更矩新增功能引入约 14 条，更矩改进残留约 7 条。
上游关键参考：Tai 的 `Main.UpdateAppDuration()`（休眠路径的会话 flush）可直接复用于退出结算；Tai 上游对退出/BusyTimeout/配置空文件/更新器流/Navigation 泄漏均存在同款问题，修复将超越上游。

---

## P0 严重（4 条）

### 1. 锁定遮罩：密码解锁成功后 1 秒内立即重新锁定 【更矩新增】
- 位置：`Core\Servicers\Instances\TimeLimitServicer.cs:492`（Confirmed 分支 `_lockedRules.Add`）+ `:277-283`（每秒 tick 检查立即重遮罩）+ `:241-245`
- 问题：解锁后 `_activeApp` 仍是受限应用（焦点回受限应用，guage 自身前台事件被忽略不清空），下一 tick 命中 `_lockedRules` 重弹锁；RelockMinutes 宽限失效；叠加密码一次性作废 → 死循环。
- 修复：Confirmed 不加入 `_lockedRules`；解锁记录受限进程+时间戳，仅在「前台仍是受限应用且已过 RelockMinutes」才重锁；RelockMinutes=0 = 不再锁。

### 2. 退出程序不结算当前前台会话 【继承自 Tai】
- 位置：`UI\Servicers\StatusBarIconServicer.cs:233`、`Core\Servicers\Instances\Main.cs:208`、`AppTimerServicer.cs:67`、`UI\App.xaml.cs:48`
- 上游：Tai 的 `ExitApp()` 与 `OnExit` 均不结算（上游代码已核实），flush 只挂在休眠事件（`Main.cs` Sleep 分支调用 `UpdateAppDuration()`）。
- 影响：连续使用数小时后直接退出 → 时长全部丢失。
- 修复：复用上游已有的 `UpdateAppDuration()`；ExitApp/OnExit 先同步 flush；在途写库 Task 可等待。

### 3. SQLite BusyTimeout=60 毫秒过短 【继承自 Tai】
- 位置：`Core\Librarys\SQLite\GuageDbContext.cs:52`
- 上游：`TaiDbContext.cs` 硬编码 `BusyTimeout = 60`（毫秒），完全相同。
- 影响：写事务期间读撞锁抛 "database is locked" → UI 线程异常 → 应用退出。
- 修复：60 → 15000。

### 4. LockOverlayWindow.Result 默认 Confirmed 【更矩新增】
- 位置：`UI\Controls\Window\LockOverlayWindow.xaml.cs:24`
- 影响：应用退出/注销等非正常关闭被当"密码验证成功"，防绕过密码被无辜作废 + 触发重锁逻辑。
- 修复：默认 Cancelled；仅验证成功路径设 Confirmed。

---

## P1 中等（12 条）

### 5. 触发临界区两段锁竞态，当日限制可被无声绕过 【更矩新增】
- 位置：`TimeLimitServicer.cs:294-310`
- 修复：第一段锁内取 rule/_activeApp 快照；`currentRule==null` 时回滚 `_triggeredToday`。

### 6. 通知 TCS 永不完成 → `_isHandlingAction` 卡死，所有限制停摆 【更矩新增】
- 位置：`UI\Servicers\TimeLimitNotifierServicer.cs:77-102, 121-147`
- 修复：Dispatcher 不可达时 `TrySetResult(Cancelled)`；检查 DispatcherOperation Aborted/Failed；窗口创建 try/catch。

### 7. MsgId 幂等未真正落地 【更矩改进残留】（Tai 完全没有幂等——更矩已领先，补缺口）
- 位置：`WebData.cs:62`（先登记后落库）、`MsgIdDeduper.cs`（纯内存）
- 影响：(a) 写库失败该 MsgId 被永久拒收 → 数据丢失；(b) 重启去重表清零 → 重发重复入库。
- 修复：登记移到写库成功后；新增 `MsgIdSeen(msgid TEXT PRIMARY KEY, seenat INTEGER)` 表持久化（同事务写入，启动加载最近 8192 条，滚动清理 7 天前）。

### 8. WebData.Clear 用读上下文执行 DELETE 【疑似继承自 Tai】
- 位置：`WebData.cs:911-918`
- 修复：改 `_database.ExecuteWrite(...)`。

### 9. 配置空文件 → config=null → 启动 NRE 崩溃 【继承自 Tai】
- 位置：`AppConfig.cs:41`、`Main.cs:154/190`
- 上游：同样不判 `DeserializeObject` 返回 null（已核实上游缺陷）。
- 修复：Load 末尾判 null 重建默认配置并保存；`Main.Run` 包 try/catch。

### 10. 数据库损坏无恢复路径；迁移先写版本号失败永不重试 【更矩改进残留/疑似继承】
- 位置：`Main.cs:158-175`、`GuageDbContext.cs:58-62`、`SQLiteBuilder.cs:243-247, 286-298`
- 修复：SelfCheck 损坏 → backup 替换重建；版本号在 HandleTable 成功后写。

### 11. 会话级禁启动按进程名整杀，误杀同名无关进程 【更矩新增】
- 位置：`TimeLimitServicer.cs:332-365, 417, 524`
- 修复：封禁集合与 EnforceBanRestart 记录并比对 ExecutablePath（`TimeLimitLogic.NormalizePath`）。

### 12. BreakReminderPage 返回后 PropertyChanged 不重连 【更矩新增页面】
- 位置：`UI\Views\BreakReminderPage.xaml.cs:72, 129-139`
- 影响：切走再回来后水位冻结、呼吸动画不启动、阶段过渡与数字脉冲全失效。
- 修复：仿 TimeLimitPage 加幂等 AttachVmEvents/DetachVmEvents。

### 13+28. 空闲时 `--:--` 每 0.5 秒闪烁 + 无谓重绘 【更矩新增】
- 位置：`BreakReminderPageVM.cs:285-289, 300-304, 568-571`
- 修复：RemainingText/Progress setter 加等值保护。

### 14/29/30. 分类与详情 VM 后台线程写绑定 + NRE + 并发枚举 【更矩改动残留】
- 位置：`CategoryAppListPageVM.cs:208-227, 231-255`、`CategoryWebSiteListPageVM.cs:81-129`、`DetailPageVM.cs:310-324`
- 正确示范：`CategoryPageVM.cs:574-606`（LoadGate）、`WebSiteDetailPageVM.cs:445-466`（Dispatcher 回填）
- 修复：按示范改写；Category/app 判空；OnDel 与自动刷新串行化。

### 15. 多显示器混合 DPI 下锁定遮罩/倒计时窗错位 【更矩新增】
- 位置：`LockOverlayWindow.xaml.cs:354-380, 385-400`、`CountdownWindow.xaml.cs:70-87`
- 影响：100%+150% 混合缩放时遮罩与目标窗口对不齐，破坏限制效果。
- 修复：每个窗口按自身所在显示器取 DPI（HwndSource.TransformFromDevice / MonitorFromWindow+GetDpiForMonitor）。

### 16. 套圈水机窗口固定尺寸，字体缩放下裁剪 【更矩新增】
- 位置：`RingTossWindow.xaml:6`、`RingTossWindow.xaml.cs:226-236, 525-548`
- 修复：窗口尺寸按 UIFontScale 系数乘算；BuildBoard 移到 ApplyToWindow 前。

---

## P2 轻微（15 条）

| # | 问题 | 位置 | 来源 | 修复 |
|---|------|------|------|------|
| 17 | 下载 zip 流未 using，失败后 update.zip 被锁 | `Updater\MainWindow.xaml.cs:106-136` | 继承自 Tai（上游同样手动 Close） | using 包裹 |
| 18+31 | 哈希解析兜底过宽 + 本地文件名硬编码 | `UpdatePackageVerifier.cs:44-48, 63-68` | 更矩改进残留 | PackageFileName 贯穿；单条哈希才允许省文件名 |
| 19 | Navigation.OnDataChanged 退订错对象（旧集合泄漏控件） | `Navigation.cs:107-123` | 继承自 Tai | 先退订 e.OldValue |
| 20 | RemoveItem 直接索引字典可抛 KeyNotFound | `Navigation.cs:398-403` | 继承自 Tai | TryGetValue |
| 21 | OnApplyTemplate 重复订阅事件（匿名 lambda 无法退订） | `DefaultWindow.cs:113-129, 478-530`、`PageContainer.cs:165` | 继承自 Tai | 委托缓存字段，先 -= 再 += |
| 22 | AppTimerServicer 会话状态无锁，双事件源并发 | `AppTimerServicer.cs:74-118` | 继承自 Tai（上游同样无锁） | lock 保护 |
| 23 | MarqueeTextBlock 暂停即跳回起点 | `MarqueeTextBlock.cs:643-678` | 更矩控件 | 移除动画前把当前动画值写入本地值 |
| 24 | Charts 搜索假异步（Task.Run 内 Dispatcher.Invoke） | `Charts.cs:816-845` | 继承自 Tai | 后台过滤 + UI 线程赋值 |
| 25 | UIFontScale 不覆盖 Popup（A-Z 字母硬编码字号） | `UIFontScale.cs:83-97`、`TimeLimitPage.xaml:55` | 更矩新增 | Popup.Opened 后 ApplyToRoot；字号改 DynamicResource |
| 26 | 删除类操作确认不一致 | CategoryApp/WebSiteListPage、TimeLimitPageVM:406 | 混合 | 统一接入 ShowConfirmDialogAsync |
| 27 | Duration=0 记错误日志（亚秒切页） | `WebData.cs:51`、`tab-tracker.js` | 更矩新增校验副作用 | **已修**：服务端静默忽略 0；插件时长 0 不发送（双拷贝已同步） |
| 28 | 空闲每 0.5s 无谓重绘 | 同 13 | 更矩新增 | 与 13 同根同修 |
| 29 | DetailPageVM.UpdateCategory 后台写绑定 + NRE | `DetailPageVM.cs:310-324` | 更矩改动残留 | 照 WebSiteDetailPageVM.cs:445 写法 |
| 30 | CategoryWebSiteListPageVM 后台读集合与 UI Remove 并发枚举 | `CategoryWebSiteListPageVM.cs:93-129` | 更矩改动残留 | 删除与刷新串行化；结果回 UI 线程 |
| 31 | （并入 18） | — | — | — |

---

## 上游 Tai 对比总结

| 领域 | 上游 Tai 现状 | 更矩现状 | 结论 |
|------|--------------|---------|------|
| 退出结算 | 不结算（flush 只在休眠） | 同样不结算 | 继承缺陷，本轮修（复用上游 UpdateAppDuration） |
| SQLite BusyTimeout | 硬编码 60ms | 同 60ms | 继承缺陷，本轮修 |
| 配置空文件 | null → CheckOption 抛异常被吞 | 同样 null | 继承缺陷，本轮修 |
| 浏览上报幂等 | 无 MsgId 概念，重放即重复计数 | 有内存幂等（缺口：先登记后写库、不持久化） | 更矩领先，本轮补齐 |
| 时长 0 校验 | 无校验（0 也落库，负数污染求和） | 有校验但 0 记错误日志 | 更矩领先，已修（0 静默） |
| 更新器 | 下载流无 using；杀第一个进程+固定延迟 | 有验签闭环（公钥固定+备份回滚），流未 using | 更矩整体领先，补流修复 |
| 异常策略 | Handled=true → TaiBug 上报 → Shutdown | 记日志 → Shutdown | 相同（本轮为关键路径加防护减少异常发生） |

## 修复记录
- 2026-09-06：#27 已修（服务端 WebData.cs 时长 0 静默忽略；插件 Guage.Chrome 与 WebExtensions\Chrome 的 tab-tracker.js 亚秒会话不发送，双拷贝 SHA256 一致）。
- 其余 30 条按 P0 → P1 → P2 批次修复，见 git 式变更说明（本项目非 git 仓库，以本文档 + 构建日志为准）。
