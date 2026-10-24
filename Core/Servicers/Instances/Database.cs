using Core.Librarys.SQLite;
using Core.Servicers.Interfaces;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Servicers.Instances
{
    /// <summary>
    /// SQLite 数据访问入口。
    /// 写操作一律通过 <see cref="ExecuteWrite"/> 串行执行（单写者，杜绝并发写竞态与锁超时放弃）；
    /// 读操作通过 <see cref="GetReaderContext"/> 获取独立上下文（SQLite busy timeout 兜底读写并发）。
    /// </summary>
    public class Database : IDatabase
    {
        private readonly object _writeLocker = new object();

        public GuageDbContext GetReaderContext()
        {
            return new GuageDbContext();
        }

        /// <summary>
        /// 在独占写锁内执行写操作，异常时上下文由 using 自动释放，锁绝不残留。
        /// </summary>
        public void ExecuteWrite(Action<GuageDbContext> action)
        {
            if (action == null)
            {
                return;
            }

            lock (_writeLocker)
            {
                using (var db = new GuageDbContext())
                {
                    action(db);
                }
            }
        }

        /// <summary>
        /// 【兼容旧调用】获取写上下文；调用方必须保证在 finally 中调用 <see cref="CloseWriter"/> 释放写锁。
        /// 新代码请使用 <see cref="ExecuteWrite"/>。
        /// </summary>
        public GuageDbContext GetWriterContext()
        {
            Monitor.Enter(_writeLocker);
            return new GuageDbContext();
        }

        /// <summary>
        /// 【兼容旧调用】释放 <see cref="GetWriterContext"/> 占用的写锁。
        /// </summary>
        public void CloseWriter()
        {
            Monitor.Exit(_writeLocker);
        }
    }
}
