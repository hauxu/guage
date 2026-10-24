using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Models.WebPage
{
    public class NotifyWeb
    {
        public string Url { get; set; }
        public string Title { get; set; }
        public string Icon { get; set; }
        public int ActiveTime { get; set; }
        public int Duration { get; set; }
        /// <summary>
        /// 消息唯一标识（用于断线重发幂等去重）。
        /// </summary>
        public string MsgId { get; set; }
        public string Token { get; set; }
        public DateTime ActiveDateTime
        {
            get
            {
                DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
                return dateTime.AddSeconds(ActiveTime).ToLocalTime();
            }
        }
    }
}
