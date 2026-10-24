using Core.Librarys;
using System;
using System.IO;

namespace Guage.Tests
{
    /// <summary>
    /// 把 FileHelper 的存储根目录临时改道到独立临时目录，作用域结束时恢复并清理。
    /// Core 没有注入存储路径的接缝（FileHelper 是静态类，路径在静态构造里按入口程序集定死），
    /// 所以只能用它的公开入口 ApplyStoragePath 改道；迁移参数固定 false，
    /// 避免把真实 Data/Log 拷进临时目录，更不会碰到 UI\bin\Debug\Data 下的真实配置。
    /// </summary>
    internal sealed class TempStorageScope : IDisposable
    {
        private readonly string _root;

        private TempStorageScope(string root)
        {
            _root = root;
        }

        /// <summary>
        /// 本次测试独占的临时存储根目录。
        /// </summary>
        public string Root
        {
            get { return _root; }
        }

        /// <summary>
        /// 改道后 AppConfig 真正读写的配置文件路径（位于临时目录内）。
        /// </summary>
        public string AppConfigPath
        {
            get { return Path.Combine(_root, "Data", "AppConfig.json"); }
        }

        public static TempStorageScope Begin()
        {
            string root = Path.Combine(Path.GetTempPath(), "Guage.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            FileHelper.ApplyStoragePath(root, false);
            return new TempStorageScope(root);
        }

        public void Dispose()
        {
            try
            {
                //  空路径 = 恢复为默认安装目录，同时删除安装目录下的 storage.path 引导文件
                FileHelper.ApplyStoragePath(string.Empty, false);
            }
            catch
            {
                //  恢复失败不影响测试结论
            }

            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, true);
                }
            }
            catch
            {
                //  临时目录清理失败不影响测试结论
            }
        }
    }
}
