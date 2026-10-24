using Core.Librarys.SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Servicers.Interfaces
{
    public interface IDatabase
    {
        GuageDbContext GetReaderContext();

        /// <summary>
        /// 在独占写锁内执行写操作（串行单写者）。
        /// </summary>
        void ExecuteWrite(Action<GuageDbContext> action);

        //  兼容旧调用，全部改造完成后移除
        GuageDbContext GetWriterContext();
        void CloseWriter();
    }
}
