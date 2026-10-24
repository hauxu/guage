# 更矩全量审查修复（3 代理审查 → 全部执行）

审查结论：P0×3、P1×14、P2×15+，全部有代码证据（文件:行号）。你点名的方向：**圆角**问题实为玻璃 accent 提交竞态+透明度100仅6%着色的可见症状；**套圈水机**有确认丢圈/重开缩水/缩放死区；**锁定应用**有重启绕过限制/全局闸门冻结/遮罩永久挂起/取消绕过。另查明"设置偶尔全部重置"根因：第二实例退出时 OnExit 用未加载的 null 配置覆写 AppConfig.json（写进字面量 "null"）→ 下次启动触发全量重置；DEBUG 构建多开放行使旧实例退出时用陈旧快照覆盖新设置（GlassTransparency=100 的最可能成因）。

## A. P0/P1 核心修复
1. **UI\Base\WindowBackdrop.cs**：①pending 增加 `_pendingEnabled`，Tick 用它提交并回写；②enabled 翻转时立即同步提交（不进 66ms 限流，只有 tint/alpha 变化才挂起）——修"启动开玻璃被吞/关玻璃被吞"；③SubmitAccent 返回 false 时跳过 `_last*/_hasSubmitted` 回写（允许重试）并记日志。
2. **App.xaml.cs + AppConfig.cs**：①`Save()` 开头加 `config==null` 守卫；②OnExit 兜底 Save 仅在配置已加载时执行；③`IsRuned()` 去掉 `#if !DEBUG`——DEBUG 也启用单实例互斥，堵死多开覆盖。
3. **BreakReminderPageVM.cs + IncenseBurnControl.cs**：阶段完成"庆祝"事件目前在线程池线程直通 UI（PlayBurnout 必抛异常且斩断事件链、台账缺记录）——VM 侧改 Dispatcher 调度；控件的 PlayIgnition/PlayBurnout/ApplyPhaseColor 内部加 `Dispatcher.CheckAccess→BeginInvoke` 自愈（根治，不依赖宿主线程纪律）。
4. **TimeLimitServicer.cs + LockOverlayWindow.xaml.cs + CountdownWindow**：①`_isHandlingAction` 全局布尔 → 按 ruleKey 的 HashSet（一个遮罩不再冻结所有规则计数）；②重启后已超限规则在目标应用首次再次前台时重新执行动作（堵"重启绕过限制"）；③遮罩遇"进程存活但无可见窗口"时 60s 超时退出并保持规则锁定；④接线 `RelockAfterExit`（模型/VM 有、Servicer 从不读取）；⑤倒计时"取消"不再彻底绕过（规则保持锁定，下次前台重触发）；"延长5分钟"在密码模式下要求先验证密码。
5. **MainWindow.OnClosed → PageContainer.Dispose()**（实现已有、无任何调用点——修每关一次窗泄漏一套页面 VM+每秒计时器+键盘钩子处理器累积）；IndexPageVM 每秒 tick 加窗口可见性门控。
6. **RingTossWindow.xaml.cs**：①Confirm 改用 PendingCount（飞行/悬挂中的圈计入，不再丢时长）；②SeedInitialRings 改贪心分配保证 round-trip 恒等（420 不再缩成 360）；③字体缩放改 `ShellRoot.LayoutTransform`（机身跟随缩放，消除 105%/150% 的死区），删手改 Width/Height。
7. **崩溃面收敛**：MainViewModel `Items[startPageIndex]` 钳制（旧配置大 StartPage 直接崩）；详情页右键菜单/白名单/屏蔽对"未知应用"（AppModel==null）判空——当前右键即可复现全应用退出。

## B. 观感/功能修复
8. **暗色主题弹层白底白字**：DefaultWindow.xaml 的 Toast/ConfirmDialog/InputModal 背景改 `DynamicResource ToastBackground`，Light/Dark 主题字典各补该资源。
9. **ThemeServicer.UpdateBackdrop**：glass-off 的笔刷清理移出 `themeDict!=null` 作用域；themeDict==null 时记日志并跳过 accent 提交（防"玻璃关了底色仍透明"）。
10. **玻璃着色保底（你截图的"无着色纯模糊+红黑残影"）**：`MinimumTintAlpha` 16→48，透明度映射 floor 同步——100 档仍有约 19% 底色，滑块仍 0-100 可调。
11. **休息提醒**：台账写盘改临时文件+File.Replace（防崩溃损坏全丢）；开始/暂停/重置即时刷新记录列表；提醒弹窗 60s 自动关闭+30s 降 Topmost（不再永久置顶）；Notifier 选 Owner 加 IsVisible 判据。

## C. 健壮性/性能（P2 全做）
12. 开机自启：仅 IsStartatboot 变化才重建计划任务（现在每次保存都重删重建）。
13. ConfigChanged 订阅者（ThemeServicer/SettingPageVM）加 Dispatcher 分流（后台保存改 General 字段时不再有跨线程 UI 隐患）。
14. Data.cs/WebData.cs 全部日期格式化加 InvariantCulture（非格里历区域统计全空的隐患）。
15. 目录分类前缀加 `\` 边界（D:\Apps 不再误吞 D:\Apps2）。
16. ClearRange/按段删除同步重置 TotalTime 与网站总时长。
17. Categorys.GetCategories 返回副本（防并发修改异常）。
18. Imager 缺失文件进负缓存（TTL）+降级 Info（不再每 5 秒刷 Error）。
19. Logger：写失败丢弃+计数（防队列无界增长）；flush 定时器脱离 UI Dispatcher 依赖。
20. LockOverlay：锁定期间周期比对 PID 增量补遮罩；"三击隐藏"计数修正（当前实际需 5 击）；删 _failedCount 死字段。
21. RingToss：环境气泡加 12-20s 寿命（防满柱后水面死气）；引导圈满柱兜底不再无限 Carry 活锁；`_frameHooked` 幂等守卫；静态 BitmapCache 按 DPI 设 RenderAtScale（高 DPI 不发虚）。
22. BreakReminderPageVM.IsTiming 加同值守卫；HandleLinks 的 Process 对象 Dispose；AppConfig.Load 清洗规则路径双反斜杠脏数据（含当前配置里的 QQMusic 路径）。

## D. 死代码清理
23. 删 BreakReminderDial.cs（含 csproj 条目）、DateTimeObserver.cs（含服务注册）、ThemeServicer.UpdateWindowStyle（资源键不存在、纯死代码）、RingToss 俄语注释。

## E. 测试与验证
24. Guage.Tests 补：AppConfig（null Save 不落盘/损坏重建）、水机分钟↔圈贪心映射 round-trip、TimeLimit 触发恢复纯逻辑。
25. 构建 Debug+Release → 重启 → 回归走查：玻璃观感（100 档不再出现无底色残影）、暗色主题弹层可读、圆角最大化/还原/拖动、休息提醒跑 1 分钟短规则验证燃尽动画+记录即时刷新、水机 420 round-trip 与飞行中确认、临时规则（记事本）验证锁定/解锁/重锁/延长需密码/取消仍受限、开关窗口 5 次验证无泄漏增长、日志零错误。
26. 更新记忆文件。

## 有意不改（备案）
Snap Layout 悬停浮层（平台限制）；异常即退出+GuageBug 的全局策略（既有设计）；KillProcessTree 同名语义与会话归因 0-1s 取舍（既有取舍）；RootBorder Clip/描边细节（09-12/13 已验证的刻意修复）。