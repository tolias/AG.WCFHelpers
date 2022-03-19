using AG.Loggers;
using AG.Loggers.Helpers;
using System;
using System.ComponentModel;
using System.ServiceModel;
using System.Threading;

namespace AG.WCFHelpers
{
    public class WcfClientProcessor<TService>
        where TService : class
    {
        private const string CLASS_NAME = "WcfClientProcessor.";

        public TService Service;
        public Func<TService> CreateChannelMethod;
        public LoggerBase Logger;
        public event CreationDisposingEventHandler CreationIsRequestedDuringChannelDisposing;
        public event CancelEventHandler CreatingChannel;
        //public event EventHandler RecreatingChannel;
        public event CreatingChannelFailedEventHandler CreatingChannelFailed;

        //private object _createChannelSyncObj = new object();
        private object _createChannelSyncObg = new object();
        private object _disposeChannelSyncObg = new object();
        private ManualResetEvent _disposeChannelStarted = new ManualResetEvent(false);

        public WcfClientProcessor(TService service = null)
        {
            Service = service;
        }

        public void DoOperationAsync(Action<TService> action)
        {
            ThreadPool.QueueUserWorkItem(objState =>
            {
                DoOperation(action);
            });
        }

        private bool TryToCreateChannel(Delegate action, Exception innerException = null)
        {
            const string METH_NAME = "TryToCreateChannel() ";
            if (CreatingChannel != null)
            {
                CancelEventArgs e = new CancelEventArgs();
                CreatingChannel(this, e);
                if (e.Cancel)
                    return false;
            }
            if (CreateChannelMethod != null)
            {
                Service = CreateChannel();
                if (Service == null)
                {
                    ConditionalLogging.ExecuteIfParamIsNotNull(Logger, () => Logger.Log(LogLevel.Error, CLASS_NAME + METH_NAME + "Action \"{0}\" was aborted because channel creation was cancelled", action));
                    return false;
                }
            }
            else
            {
                throw new InvalidOperationException("Cannot recreate the channel because WcfProcessor<TService>.CreateChannelMethod is not set", innerException);
            }
            return true;
        }

        public void DoOperation(Action<TService> action)
        {
            //const string METH_NAME = "DoOperation: ";
            if (Service == null)
            {
                if (!TryToCreateChannel(action))
                    return;
            }
            Exception previousException = null;
            int attempts = 0;
            tryAgain:
            try
            {
                attempts++;
                action(Service);
                if (attempts > 1)
                {
                    Logger?.Log(LogLevel.Debug, $"The action was executed after {attempts} attempts: \"{action}\"");
                }
            }
            catch (Exception ex)
            {
                //if after channel recreation we are receiving the same exception then break this cycle by rethrowing this error...
                if (previousException != null && previousException.GetType() == ex.GetType())
                    throw new CommunicationException("Channel creation didn't solve the exception (see inner exception). Action: " + action, ex);
                previousException = ex;
                Exception innerException = ex;
                do
                {
                    CommunicationException wcfException = innerException as CommunicationException;
                    //Exception wcfException = innerException as CommunicationObjectFaultedException;
                    //if (wcfException == null)
                    //    wcfException = innerException as EndpointNotFoundException;

                    if (wcfException == null)
                    {
                        innerException = innerException.InnerException;
                        if (innerException == null)
                            throw new CommunicationException($"An exception has occurred while executing action \"{action}\". See inner exception", ex);
                    }
                    else
                    {
                        Logger?.Log(LogLevel.Debug, $"CommunicationException has occurred while executing action \"{action}\". Trying to recreate the channel", ex);
                        if (RecreateChannel(wcfException, action))
                            goto tryAgain;
                        return;
                    }
                } while (true);
            }
        }

        public TResult DoOperation<TResult>(Func<TService, TResult> func)
        {
            TResult result = default(TResult);
            Action<TService> action = (service) => result = func(service);
            DoOperation(action);
            return result;
        }

        private bool RecreateChannel(Exception arisedException, Delegate action)
        {
            const string METH_NAME = "RecreateChannel: ";
            ConditionalLogging.ExecuteIfParamIsNotNull(Logger, () => Logger.Log(LogLevel.Error, arisedException, CLASS_NAME + METH_NAME + "Exception has been thrown. Disposing and recreating channel..."));
            DisposeClientChannel();
            //if (RecreatingChannel != null)
            //{
            //    RecreatingChannel(this, EventArgs.Empty);
            //}
            return TryToCreateChannel(action, arisedException);
        }

        private CreationDisposingAction CheckIfChannelIsDisposing(Exception innerException = null)
        {
            if (!Monitor.TryEnter(_disposeChannelSyncObg))
            {
                if (CreationIsRequestedDuringChannelDisposing == null)
                {
                    throw new InvalidOperationException("Channel creation has aborted because it's currently disposing. You can handle this error subscribing to the event 'CreationIsRequestedDuringChannelDisposing'",
                        innerException);
                }
                CreationDisposingEventArgs e = new CreationDisposingEventArgs();
                CreationIsRequestedDuringChannelDisposing(this, e);
                return e.Action;
            }
            else
            {
                Monitor.Exit(_disposeChannelSyncObg);
            }
            return CreationDisposingAction.CreateChannelAndExecuteOperation;
        }

        public TService CreateChannel(int retryStartMilliSeconds = 0, int retryMaxMilliSeconds = 10000)
        {
            const string METH_NAME = "CreateChannel: ";

            if (CheckIfChannelIsDisposing() == CreationDisposingAction.DontExecuteOperation)
            {
                Service = null;
                return null;
            }

            if (Monitor.TryEnter(_createChannelSyncObg, 0))
            {
                try
                {
                    tryAgain:
                    if (CheckIfChannelIsDisposing() == CreationDisposingAction.DontExecuteOperation)
                    {
                        Service = null;
                        return null;
                    }
                    try
                    {
                        ConditionalLogging.ExecuteIfParamIsNotNull(Logger, () => Logger.Log(LogLevel.Debug, CLASS_NAME + METH_NAME + "Creating channel..."));
                        Service = CreateChannelMethod();
                        return Service;
                    }
                    catch (EndpointNotFoundException ex)
                    {
                        if (CheckIfChannelIsDisposing(ex) == CreationDisposingAction.DontExecuteOperation)
                        {
                            Service = null;
                            return null;
                        }
                        if (CreatingChannelFailed != null)
                        {
                            CreatingChannelFailedEventArgs e = new CreatingChannelFailedEventArgs { RetryAgain = true, WaitBeforeRetryMilliseconds = retryStartMilliSeconds };
                            CreatingChannelFailed(this, e);
                            if (!e.RetryAgain)
                            {
                                ConditionalLogging.ExecuteIfParamIsNotNull(Logger, () => Logger.Log(LogLevel.Error, ex, CLASS_NAME + METH_NAME + "Couldn't create channel. Returning without creation because RetryAgain=false"));
                                Service = null;
                                return null;
                            }
                            retryStartMilliSeconds = e.WaitBeforeRetryMilliseconds;
                        }
                        ConditionalLogging.ExecuteIfParamIsNotNull(Logger, () => Logger.Log(LogLevel.Error, ex, CLASS_NAME + METH_NAME + "Couldn't create channel. Will retry in {0} msecs...", retryStartMilliSeconds));
                        _disposeChannelStarted.WaitOne(retryStartMilliSeconds);
                        retryStartMilliSeconds += 5000;
                        if (retryStartMilliSeconds > retryMaxMilliSeconds)
                            retryStartMilliSeconds = retryMaxMilliSeconds;
                        goto tryAgain;
                    }
                }
                finally
                {
                    Monitor.Exit(_createChannelSyncObg);
                }
            }
            else
            {
                ConditionalLogging.ExecuteIfParamIsNotNull(Logger, () => Logger.Log(LogLevel.Debug, CLASS_NAME + METH_NAME + "Channel is already creating. Waiting when it will be disposed..."));
                lock (_createChannelSyncObg) { }
                ConditionalLogging.ExecuteIfParamIsNotNull(Logger, () => Logger.Log(LogLevel.Debug, CLASS_NAME + METH_NAME + "Channel creation waiting finished."));
                return Service;
            }
        }

        public void DisposeClientChannel()
        {
            const string METH_NAME = "DisposeClientChannel: ";
            lock (_disposeChannelSyncObg)
            {
                _disposeChannelStarted.Set();
                ConditionalLogging.ExecuteIfParamIsNotNull(Logger, () => Logger.Log(LogLevel.Debug, CLASS_NAME + METH_NAME + "Waiting if channel is creating..."));
                lock (_createChannelSyncObg) { }
                ConditionalLogging.ExecuteIfParamIsNotNull(Logger, () => Logger.Log(LogLevel.Debug, CLASS_NAME + METH_NAME + "Waiting if channel is creating finished."));
                if (Service == null)
                {
                    Logger?.Log(LogLevel.Error, $"Can't dispose \"{typeof(WcfClientProcessor<TService>).FullName}.Service\" as it's null. Possibly it was already disposed. Don't dispose anything...");
                }
                else if (!(Service is IClientChannel))
                {
                    throw new InvalidOperationException($"Service \"{Service}\" doesn't implement IClientChannel interface");
                }
                else
                {
                    IClientChannel clientChannel = Service as IClientChannel;
                    Service = null;
                    clientChannel.DisposeClientChannel(_disposeChannelSyncObg, Logger);
                }
            }
            _disposeChannelStarted.Reset();
        }
    }
}
