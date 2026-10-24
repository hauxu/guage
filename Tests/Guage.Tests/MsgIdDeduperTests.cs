using Core.Librarys;
using System;

namespace Guage.Tests
{
    public static class MsgIdDeduperTests
    {
        public static void RunAll()
        {
            Run("去重-首次登记通过、重复拒绝", TestDedupe);
            Run("去重-空白 MsgId 不参与去重", TestBlank);
        }

        private static void TestDedupe()
        {
            MsgIdDeduper.Reset();
            Assert.IsTrue(MsgIdDeduper.TryRegister("msg-1"), "首次登记应通过");
            Assert.IsFalse(MsgIdDeduper.TryRegister("msg-1"), "重复登记应拒绝");
            Assert.IsTrue(MsgIdDeduper.TryRegister("msg-2"), "新 ID 应通过");
        }

        private static void TestBlank()
        {
            MsgIdDeduper.Reset();
            Assert.IsTrue(MsgIdDeduper.TryRegister(null), "null 不参与去重");
            Assert.IsTrue(MsgIdDeduper.TryRegister(string.Empty), "空串不参与去重");
        }

        private static void Run(string name, Action test) => Program.Run(name, test);
    }
}
