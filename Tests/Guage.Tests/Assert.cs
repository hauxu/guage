using System;
using System.Collections.Generic;
using System.Linq;

namespace Guage.Tests
{
    /// <summary>
    /// 微型断言器（零依赖测试台专用）。
    /// </summary>
    public static class Assert
    {
        public static void AreEqual<T>(T expected, T actual, string message = null)
        {
            if (!Equals(expected, actual))
            {
                throw new Exception((message ?? "AreEqual 失败") + $"（期望：{expected}，实际：{actual}）");
            }
        }

        public static void IsTrue(bool condition, string message = null)
        {
            if (!condition)
            {
                throw new Exception(message ?? "IsTrue 失败");
            }
        }

        public static void IsFalse(bool condition, string message = null)
        {
            if (condition)
            {
                throw new Exception(message ?? "IsFalse 失败");
            }
        }

        public static void IsNull(object value, string message = null)
        {
            if (value != null)
            {
                throw new Exception(message ?? "IsNull 失败");
            }
        }

        public static void IsNotNull(object value, string message = null)
        {
            if (value == null)
            {
                throw new Exception(message ?? "IsNotNull 失败");
            }
        }

        public static void Throws<TException>(Action action, string message = null) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new Exception((message ?? "Throws 失败") + $"（期望 {typeof(TException).Name}，实际 {ex.GetType().Name}）", ex);
            }

            throw new Exception((message ?? "Throws 失败") + $"（期望 {typeof(TException).Name}，但未抛出异常）");
        }

        public static void AreSequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message = null)
        {
            var e = expected == null ? new List<T>() : expected.ToList();
            var a = actual == null ? new List<T>() : actual.ToList();
            if (!e.SequenceEqual(a))
            {
                throw new Exception((message ?? "AreSequenceEqual 失败")
                    + $"（期望：[{string.Join(",", e)}]，实际：[{string.Join(",", a)}]）");
            }
        }
    }
}
