using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using static Updater.GithubRelease;

namespace Updater
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        GithubRelease githubRelease;

        /// <summary>
        /// 新版本保存目录路径
        /// </summary>
        private string SaveDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
        /// <summary>
        /// 新版本保存名字
        /// </summary>
        private string SaveName = "update.zip";
        /// <summary>
        /// 新版本下载路径
        /// </summary>
        private string NewVersionZipURL;
        private string NewVersionURL;
        private string ExpectedSha256;
        private bool HasChecksum;
        private string NewVersionChecksumURL;
        private string NewVersionSignatureURL;
        private bool HasSignature;
        private MainModel mainModel;
        public MainWindow()
        {
            InitializeComponent();

            mainModel = new MainModel();

            DataContext = mainModel;

            mainModel.PropertyChanged += MainModel_PropertyChanged;
        }

        private void MainModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(mainModel.Version))
            {
                githubRelease = new GithubRelease("https://api.github.com/repos/planshit/guage/releases/latest", mainModel.Version);

                Check();
            }
        }

        private async void Download()
        {
            //  安全优先：校验和文件与 Ed25519 签名缺一不可（SHA256 同源校验只能防损坏，签名才能防篡改）
            if (!HasChecksum || !HasSignature)
            {
                SetStatus("该版本缺少 SHA256 校验文件或签名文件，已中止更新。", false);
                UpdateBtn.Visibility = Visibility.Visible;
                ReCheckBtn.Visibility = Visibility.Visible;
                return;
            }

            SetStatus("正在下载新版本文件...", false);

            UpdateBtn.Visibility = Visibility.Collapsed;
            ReCheckBtn.Visibility = Visibility.Collapsed;
            ProgressBar.Visibility = Visibility.Visible;
            mainModel.ProcessValue = 0;

            var res = await Task.Run(() =>
            {
                try
                {
                    //  确认保存目录
                    if (!Directory.Exists(SaveDir))
                    {
                        Directory.CreateDirectory(SaveDir);
                    }

                    Uri uri = new Uri(NewVersionZipURL);
                    HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(uri);
                    httpWebRequest.Timeout = 120 * 1000;
                    HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();

                    long totalBytes = httpWebResponse.ContentLength;

                    //  using 包裹：异常路径也要释放响应流与文件流，否则 update.zip 被锁定
                    using (Stream st = httpWebResponse.GetResponseStream())
                    using (Stream so = new FileStream(System.IO.Path.Combine(SaveDir, SaveName), FileMode.Create))
                    {
                        long totalDownloadedByte = 0;
                        byte[] by = new byte[1024];
                        int osize = st.Read(by, 0, (int)by.Length);
                        while (osize > 0)
                        {
                            totalDownloadedByte = osize + totalDownloadedByte;
                            so.Write(by, 0, osize);

                            osize = st.Read(by, 0, (int)by.Length);

                            //进度计算
                            if (totalBytes > 0)
                            {
                                double process = double.Parse(String.Format("{0:F}",
                                       ((double)totalDownloadedByte / (double)totalBytes * 100)));
                                mainModel.ProcessValue = process;
                            }
                        }
                    }

                    httpWebResponse.Close();
                    return true;
                }
                catch (Exception ec)
                {
                    //  失败时删除半截文件，避免残包参与后续校验
                    try
                    {
                        string partial = System.IO.Path.Combine(SaveDir, SaveName);
                        if (System.IO.File.Exists(partial))
                        {
                            System.IO.File.Delete(partial);
                        }
                    }
                    catch
                    {
                        //  忽略
                    }
                    return false;
                }
            });

            if (res)
            {
                string zipPath = System.IO.Path.Combine(SaveDir, SaveName);

                //  下载校验和文件与签名文件，并先验签（签名的校验和才是权威哈希来源）
                string checksumPath = zipPath + ".sha256";
                string signaturePath = checksumPath + ".sig";
                bool signatureOk = DownloadFileTo(NewVersionChecksumURL, checksumPath)
                    && DownloadFileTo(NewVersionSignatureURL, signaturePath)
                    && UpdateSignatureVerifier.VerifySha256File(checksumPath, signaturePath);

                if (!signatureOk)
                {
                    TryDeleteLocal(zipPath);
                    TryDeleteLocal(checksumPath);
                    TryDeleteLocal(signaturePath);
                    SetStatus("更新包签名校验失败，文件可能已被篡改，已中止安装。", false);
                    UpdateBtn.Visibility = Visibility.Visible;
                    ReCheckBtn.Visibility = Visibility.Visible;
                    return;
                }

                //  用签名过的校验和文件校验更新包本体
                string checksumText = System.IO.File.ReadAllText(checksumPath);
                if (!UpdatePackageVerifier.TryParseExpectedHash(checksumText, SaveName, out string fileHash)
                    || !UpdatePackageVerifier.VerifyFile(zipPath, fileHash))
                {
                    TryDeleteLocal(zipPath);
                    TryDeleteLocal(checksumPath);
                    TryDeleteLocal(signaturePath);
                    SetStatus("更新包校验失败，文件可能已被篡改，已中止安装。", false);
                    UpdateBtn.Visibility = Visibility.Visible;
                    ReCheckBtn.Visibility = Visibility.Visible;
                    return;
                }

                //  定位主程序安装根目录（guage.exe 所在目录）
                string unpath = ResolveTargetRoot();
                if (string.IsNullOrEmpty(unpath))
                {
                    SetStatus("未找到主程序 guage.exe，无法完成更新。", false);
                    UpdateBtn.Visibility = Visibility.Visible;
                    ReCheckBtn.Visibility = Visibility.Visible;
                    return;
                }

                //  准备更新：结束全部主程序实例并等待退出（多开场景全部结束）
                var processes = Process.GetProcessesByName("guage");
                if (processes != null)
                {
                    foreach (var proc in processes)
                    {
                        try
                        {
                            proc.Kill();
                            proc.WaitForExit(5000);
                        }
                        catch
                        {
                            //  进程可能已退出
                        }
                        finally
                        {
                            proc.Dispose();
                        }
                    }
                }

                SetStatus("下载完成，正在解压请勿关闭此窗口...");

                string tempDir = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "guage_update_" + Guid.NewGuid().ToString("N"));

                var unresult = await Task.Run(() =>
                {
                    try
                    {
                        Directory.CreateDirectory(tempDir);
                        return Unzip.ExtractZipFile(System.IO.Path.Combine(SaveDir, SaveName), tempDir);
                    }
                    catch
                    {
                        return false;
                    }
                });

                if (!unresult)
                {
                    TryDeleteDirectory(tempDir);
                    SetStatus("解压文件时发生异常，请重试！", false);
                    UpdateBtn.Visibility = Visibility.Visible;
                    ReCheckBtn.Visibility = Visibility.Visible;
                    return;
                }

                //  备份旧文件后原子覆盖安装，失败自动回滚
                SetStatus("正在安装更新，请勿关闭此窗口...");
                bool applied = await Task.Run(() => ApplyUpdate(tempDir, unpath));
                TryDeleteDirectory(tempDir);

                if (applied)
                {
                    SetStatus("更新完成！", false);
                    try
                    {
                        Process guageProcess = new Process();
                        ProcessStartInfo startInfo = new ProcessStartInfo(System.IO.Path.Combine(unpath, "guage.exe"));
                        guageProcess.StartInfo = startInfo;
                        guageProcess.Start();
                        guageProcess.Dispose();
                    }
                    catch
                    {
                        SetStatus("更新完成，但启动更矩失败，请手动启动。", false);
                    }
                }
                else
                {
                    SetStatus("安装更新失败，已回滚，请重试！", false);
                    UpdateBtn.Visibility = Visibility.Visible;
                    ReCheckBtn.Visibility = Visibility.Visible;
                }
            }
            else
            {
                //下载发生异常
                SetStatus("下载时发生异常，请重试。", false);
                UpdateBtn.Visibility = Visibility.Visible;
            }
        }


        /// <summary>
        /// 定位主程序安装根目录（guage.exe 所在目录）：
        /// 优先更新器自身目录，其次父目录、祖父目录。
        /// </summary>
        private static string ResolveTargetRoot()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (File.Exists(System.IO.Path.Combine(baseDir, "guage.exe")))
            {
                return baseDir;
            }

            var parent = Directory.GetParent(baseDir);
            if (parent != null && File.Exists(System.IO.Path.Combine(parent.FullName, "guage.exe")))
            {
                return parent.FullName;
            }

            if (parent != null)
            {
                var grand = Directory.GetParent(parent.FullName);
                if (grand != null && File.Exists(System.IO.Path.Combine(grand.FullName, "guage.exe")))
                {
                    return grand.FullName;
                }
            }

            return null;
        }

        /// <summary>
        /// 将临时目录中的新版本文件安装到目标根目录：
        /// 每个被覆盖的旧文件先备份，任何一步失败即回滚，避免“半更新”损坏态。
        /// </summary>
        private static bool ApplyUpdate(string tempDir, string targetRoot)
        {
            string backupDir = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "guage_backup_" + Guid.NewGuid().ToString("N"));

            try
            {
                var files = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
                foreach (string file in files)
                {
                    string rel = file.Substring(tempDir.Length)
                        .TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                    string target = System.IO.Path.Combine(targetRoot, rel);
                    string targetDirPath = System.IO.Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(targetDirPath))
                    {
                        Directory.CreateDirectory(targetDirPath);
                    }

                    if (File.Exists(target))
                    {
                        string backupFile = System.IO.Path.Combine(backupDir, rel);
                        string backupFileDir = System.IO.Path.GetDirectoryName(backupFile);
                        if (!string.IsNullOrEmpty(backupFileDir))
                        {
                            Directory.CreateDirectory(backupFileDir);
                        }

                        File.Copy(target, backupFile, true);
                    }

                    File.Copy(file, target, true);
                }

                return true;
            }
            catch
            {
                //  回滚：把已备份的旧文件复制回目标
                try
                {
                    RollbackUpdate(backupDir, targetRoot);
                }
                catch
                {
                    //  回滚失败也只能提示重试
                }

                return false;
            }
            finally
            {
                TryDeleteDirectory(backupDir);
            }
        }

        /// <summary>
        /// 将备份目录中的旧文件复制回目标根目录。
        /// </summary>
        private static void RollbackUpdate(string backupDir, string targetRoot)
        {
            if (!Directory.Exists(backupDir))
            {
                return;
            }

            var files = Directory.GetFiles(backupDir, "*", SearchOption.AllDirectories);
            foreach (string file in files)
            {
                string rel = file.Substring(backupDir.Length)
                    .TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                string target = System.IO.Path.Combine(targetRoot, rel);
                string targetDirPath = System.IO.Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(targetDirPath))
                {
                    Directory.CreateDirectory(targetDirPath);
                }

                File.Copy(file, target, true);
            }
        }

        /// <summary>
        /// 下载文件到本地（校验和/签名等小文件）。
        /// </summary>
        private static bool DownloadFileTo(string url, string destPath)
        {
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(destPath))
            {
                return false;
            }

            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = 120 * 1000;
                request.ReadWriteTimeout = 120 * 1000;
                request.UserAgent = "Guage-Updater/1.0 (Windows NT 10.0; .NET Framework 4.8)";
                request.AllowAutoRedirect = true;

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fs);
                }

                return File.Exists(destPath) && new FileInfo(destPath).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 删除单个本地文件（失败忽略）。
        /// </summary>
        private static void TryDeleteLocal(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                //  忽略
            }
        }

        private static void TryDeleteDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                return;
            }

            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                //  清理失败不影响主流程
            }
        }

        private async void Check()
        {
            NewVersionSP.Visibility = Visibility.Collapsed;
            PreTag.Visibility = Visibility.Collapsed;

            SetStatus("正在检查更新");
            UpdateBtn.IsEnabled = false;
            ReCheckBtn.IsEnabled = false;

            var info = await githubRelease.GetRequest();

            if (info != null)
            {
                if (githubRelease.IsCanUpdate())
                {
                    NewVersionZipURL = info.DownloadUrl;
                    NewVersionURL = info.HtmlUrl;
                    ExpectedSha256 = info.ExpectedSha256;
                    HasChecksum = info.HasChecksum;
                    NewVersionChecksumURL = info.ChecksumDownloadUrl;
                    NewVersionSignatureURL = info.SignatureDownloadUrl;
                    HasSignature = info.HasSignature;

                    UpdateBtn.Visibility = Visibility.Visible;
                    UpdateBtn.IsEnabled = HasChecksum && HasSignature;

                    NewVersionSP.Visibility = Visibility.Visible;
                    Version.Text = info.Version;
                    VersionTitle.Text = info.Title;
                    if (info.IsPre)
                    {
                        PreTag.Visibility = Visibility.Visible;
                    }
                    SetStatus(HasChecksum && HasSignature
                        ? "检测到新的版本！"
                        : "检测到新版本，但发布包缺少 SHA256 校验文件或签名文件，无法安全更新。", false);
                }
                else
                {
                    UpdateBtn.Visibility = Visibility.Visible;
                    UpdateBtn.IsEnabled = false;
                    SetStatus("目前没有可用的更新。", false);
                }
            }
            else
            {
                UpdateBtn.Visibility = Visibility.Visible;
                UpdateBtn.IsEnabled = false;
                SetStatus("无法获取版本信息，请检查代理或网络。", false);
            }
            ReCheckBtn.Visibility = Visibility.Visible;
            ReCheckBtn.IsEnabled = true;

        }

        private void SetStatus(string statusText, bool isLoading = true)
        {
            StatusLabel.Text = statusText;
            ProgressBar.IsIndeterminate = isLoading;
            if (isLoading)
            {
                ProgressBar.Visibility = Visibility.Visible;
            }
            else
            {
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void ReCheckBtn_Click(object sender, RoutedEventArgs e)
        {
            Check();
        }

        private void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            Download();
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo(NewVersionURL));
        }
    }
}
