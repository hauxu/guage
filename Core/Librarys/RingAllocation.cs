using System;

namespace Core.Librarys
{
    /// <summary>
    /// 套圈水机「分钟 ↔ 圈」分配纯逻辑（从 RingTossWindow 抽取，便于单元测试）。
    /// </summary>
    public static class RingAllocation
    {
        /// <summary>
        /// 把总分钟数贪心分配为左右两柱的圈数：先尽量填左柱（大单位），余量给右柱。
        /// 旧的"先整除再对左柱单位取模"在满柱时会整段丢弃余量
        /// （工作 420 分钟被缩成 360、休息 210 被缩成 180，重开确认即静默缩减设置）。
        /// 总量超出两柱容量时按容量夹取（水机本身的表达上限）。
        /// </summary>
        /// <param name="totalMinutes">总分钟数</param>
        /// <param name="leftUnitMinutes">左柱单圈分钟数（大单位）</param>
        /// <param name="rightUnitMinutes">右柱单圈分钟数（小单位）</param>
        /// <param name="maxPerColumn">每柱最大圈数</param>
        public static Tuple<int, int> Allocate(int totalMinutes, int leftUnitMinutes, int rightUnitMinutes, int maxPerColumn)
        {
            if (totalMinutes < 0)
            {
                totalMinutes = 0;
            }
            if (leftUnitMinutes < 1)
            {
                leftUnitMinutes = 1;
            }
            if (rightUnitMinutes < 1)
            {
                rightUnitMinutes = 1;
            }
            if (maxPerColumn < 0)
            {
                maxPerColumn = 0;
            }

            int left = Math.Min(totalMinutes / leftUnitMinutes, maxPerColumn);
            int right = Math.Min((totalMinutes - left * leftUnitMinutes) / rightUnitMinutes, maxPerColumn);
            return Tuple.Create(left, right);
        }
    }
}
