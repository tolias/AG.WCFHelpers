using System;

namespace AG.WCFHelpers
{
    public delegate void CreatingChannelFailedEventHandler(object sender, CreatingChannelFailedEventArgs e);

    public class CreatingChannelFailedEventArgs : EventArgs
    {
        public bool RetryAgain;
        public int WaitBeforeRetryMilliseconds;
    }
}
