using System;

namespace Core.Event
{
    public class WebBrowseUpdatedEventArgs : EventArgs
    {
        public string Domain { get; }
        public int Duration { get; }

        public WebBrowseUpdatedEventArgs(string domain, int duration)
        {
            Domain = domain;
            Duration = duration;
        }
    }
}
