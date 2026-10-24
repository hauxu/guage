using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Guage.Tests
{
    public static class UnzipTests
    {
        public static void RunAll()
        {
            Run("解压-正常文件与目录条目", TestNormalExtract);
            Run("解压-路径穿越条目被拒绝", TestZipSlipRejected);
        }

        private static string CreateZip(Action<ZipArchive> fill)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "guage_test_" + Guid.NewGuid().ToString("N") + ".zip");
            using (var fs = new FileStream(tmp, FileMode.Create))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                fill(zip);
            }
            return tmp;
        }

        private static void TestNormalExtract()
        {
            string zipPath = CreateZip(zip =>
            {
                var entry = zip.CreateEntry("app/guage.exe");
                using (var s = entry.Open())
                {
                    byte[] data = Encoding.UTF8.GetBytes("MZ-fake");
                    s.Write(data, 0, data.Length);
                }

                zip.CreateEntry("subdir/");
            });

            string outDir = Path.Combine(Path.GetTempPath(), "guage_out_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(outDir);
                Assert.IsTrue(Updater.Unzip.ExtractZipFile(zipPath, outDir), "正常包应解压成功");
                Assert.IsTrue(File.Exists(Path.Combine(outDir, "app", "guage.exe")), "文件应解压到目标目录");
                Assert.IsTrue(Directory.Exists(Path.Combine(outDir, "subdir")), "目录条目应创建目录");
            }
            finally
            {
                try { Directory.Delete(outDir, true); } catch { }
                File.Delete(zipPath);
            }
        }

        private static void TestZipSlipRejected()
        {
            string zipPath = CreateZip(zip =>
            {
                var evil = zip.CreateEntry("../../evil.txt");
                using (var s = evil.Open())
                {
                    byte[] data = Encoding.UTF8.GetBytes("evil");
                    s.Write(data, 0, data.Length);
                }

                var good = zip.CreateEntry("good.txt");
                using (var s = good.Open())
                {
                    byte[] data = Encoding.UTF8.GetBytes("good");
                    s.Write(data, 0, data.Length);
                }
            });

            string parentDir = Path.Combine(Path.GetTempPath(), "guage_slip_parent_" + Guid.NewGuid().ToString("N"));
            string outDir = Path.Combine(parentDir, "target");
            string expectedEvilPath = Path.Combine(parentDir, "evil.txt");
            try
            {
                Directory.CreateDirectory(outDir);
                Assert.IsTrue(Updater.Unzip.ExtractZipFile(zipPath, outDir), "含穿越条目的包其余内容仍应解压");
                Assert.IsFalse(File.Exists(expectedEvilPath), "穿越条目不得写出目标目录");
                Assert.IsFalse(File.Exists(Path.Combine(parentDir, "..", "evil.txt")), "上级目录不得出现穿越文件");
                Assert.IsTrue(File.Exists(Path.Combine(outDir, "good.txt")), "正常条目应解压");
            }
            finally
            {
                try { Directory.Delete(parentDir, true); } catch { }
                File.Delete(zipPath);
            }
        }

        private static void Run(string name, Action test) => Program.Run(name, test);
    }
}
