using System;

namespace AG.WCFHelpers
{
    public delegate void CreationDisposingEventHandler(object sender, CreationDisposingEventArgs e);

    public class CreationDisposingEventArgs : EventArgs
    {
        public CreationDisposingAction Action;
    }
}
