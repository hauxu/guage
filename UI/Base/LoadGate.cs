using Core.Librarys;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace UI.Base
{
    /// <summary>
    /// 异步加载版本闸门：以“刷新批次”为单位推进版本——
    /// 每个用户触发的刷新调用一次 <see cref="BeginRefresh"/> 获取版本号，
    /// 该批次内的所有加载共享同一版本；完成时版本不匹配（期间又发生了新刷新）即丢弃结果，
    /// 防止快速切换日期/标签时旧任务覆盖新数据。
    /// 注意：同一次刷新中的多个加载必须传入同一个版本号，否则会互相判定过期。
    /// 同时统一“后台查询 + UI 线程回填”模式，消除后台线程直接写绑定属性的写法。
    /// </summary>
    public class LoadGate
    {
        private int _version;

        /// <summary>
        /// 开始新一轮刷新，返回本次刷新的版本号（批次内所有加载共用）。
        /// </summary>
        public int BeginRefresh()
        {
            return Interlocked.Increment(ref _version);
        }

        public bool IsCurrent(int version)
        {
            return Volatile.Read(ref _version) == version;
        }

        /// <summary>
        /// 后台执行 loader，UI 线程校验版本后回填；loader 异常时在 UI 线程执行 onError（可选，用于复位加载状态）。
        /// </summary>
        public void Run<T>(int version, Func<T> loader, Action<T> apply, Action onError = null)
        {
            Task.Run(() =>
            {
                try
                {
                    T result = loader();
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (IsCurrent(version))
                        {
                            try
                            {
                                apply(result);
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex.ToString());
                            }
                        }
                    }));
                }
                catch (Exception ex)
                {
                    Logger.Error(ex.ToString());
                    if (onError != null)
                    {
                        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (IsCurrent(version))
                            {
                                try
                                {
                                    onError();
                                }
                                catch (Exception onErrorEx)
                                {
                                    Logger.Error(onErrorEx.ToString());
                                }
                            }
                        }));
                    }
                }
            });
        }

        /// <summary>
        /// 无返回值版本：后台执行 loader，UI 线程执行 apply。
        /// </summary>
        public void Run(int version, Action loader, Action apply, Action onError = null)
        {
            Run<object>(version, () =>
            {
                loader();
                return null;
            }, _ => apply(), onError);
        }
    }
}
