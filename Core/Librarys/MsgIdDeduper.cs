using System;
using System.Collections.Generic;

namespace Core.Librarys
{
    /// <summary>
    /// 浏览器上报消息幂等去重（从 WebData 抽取，便于单元测试）。
    /// 同一 MsgId 只允许处理一次；无 MsgId 的消息不参与去重；容量有上限（最旧条目被淘汰）。
    /// </summary>
    public static class MsgIdDeduper
    {
        private const int MaxProcessedMsgIds = 8192;

        private static readonly object Locker = new object();
        private static readonly HashSet<string> Processed = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Queue<string> Order = new Queue<string>();

        /// <summary>
        /// 登记 MsgId：首次出现返回 true；重复或为空白时返回 false/true（空白不参与去重）。
        /// 注意：应在业务数据写库成功后再调用，避免写库失败的消息被永久拒收。
        /// </summary>
        public static bool TryRegister(string msgId)
        {
            if (string.IsNullOrEmpty(msgId))
            {
                return true;
            }

            lock (Locker)
            {
                if (Processed.Contains(msgId))
                {
                    return false;
                }

                Processed.Add(msgId);
                Order.Enqueue(msgId);
                Trim();

                return true;
            }
        }

        /// <summary>
        /// 快速判重（不登记）：已处理过的 MsgId 返回 true。空白返回 false。
        /// </summary>
        public static bool IsKnown(string msgId)
        {
            if (string.IsNullOrEmpty(msgId))
            {
                return false;
            }

            lock (Locker)
            {
                return Processed.Contains(msgId);
            }
        }

        /// <summary>
        /// 登记 MsgId（仅登记不判重）：在业务数据写库成功后调用。
        /// </summary>
        public static void Register(string msgId)
        {
            if (string.IsNullOrEmpty(msgId))
            {
                return;
            }

            lock (Locker)
            {
                if (Processed.Add(msgId))
                {
                    Order.Enqueue(msgId);
                    Trim();
                }
            }
        }

        /// <summary>
        /// 启动时从持久化存储加载最近处理过的 MsgId（跨重启防重放）。
        /// </summary>
        public static void Load(IEnumerable<string> msgIds)
        {
            if (msgIds == null)
            {
                return;
            }

            lock (Locker)
            {
                Processed.Clear();
                Order.Clear();
                foreach (string id in msgIds)
                {
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }
                    if (Processed.Add(id))
                    {
                        Order.Enqueue(id);
                    }
                }
                Trim();
            }
        }

        private static void Trim()
        {
            while (Order.Count > MaxProcessedMsgIds)
            {
                Processed.Remove(Order.Dequeue());
            }
        }

        /// <summary>
        /// 清空去重表（测试隔离用）。
        /// </summary>
        public static void Reset()
        {
            lock (Locker)
            {
                Processed.Clear();
                Order.Clear();
            }
        }
    }
}
