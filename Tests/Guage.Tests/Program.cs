using System;

namespace Guage.Tests
{
    /// <summary>
    /// 零依赖测试运行器：运行全部套件，打印 PASS/FAIL 汇总，失败时退出码非 0。
    /// </summary>
    public static class Program
    {
        private static int _passed;
        private static int _failed;

        public static int Main()
        {
            Console.WriteLine("===== Guage.Tests 开始 =====");

            DurationBucketingTests.RunAll();
            MsgIdDeduperTests.RunAll();
            TimeLimitLogicTests.RunAll();
            RingAllocationTests.RunAll();
            NetworkSecurityTests.RunAll();
            UrlSecurityTests.RunAll();
            UnzipTests.RunAll();
            UpdatePackageVerifierTests.RunAll();
            UpdateSignatureVerifierTests.RunAll();
            BreakReminderTests.RunAll();
            AppConfigTests.RunAll();

            Console.WriteLine();
            Console.WriteLine($"===== 汇总：通过 {_passed}，失败 {_failed} =====");
            return _failed == 0 ? 0 : 1;
        }

        public static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("  PASS  " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("  FAIL  " + name);
                Console.WriteLine("        " + ex.Message.Replace("\r", " ").Replace("\n", " "));
            }
        }
    }
}
