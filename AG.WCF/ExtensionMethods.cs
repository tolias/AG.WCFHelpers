using AG.Loggers;
using AG.Loggers.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading;

namespace AG.WCFHelpers
{
    public static class ExtensionMethods
    {
        private const string CLASS_NAME = "ExtensionMethods.";

        public static void DisposeClientChannel(this IClientChannel clientChannel, object syncObj, LoggerBase logger = null)
        {
            const string METH_NAME = "DisposeClientChannel: ";

            if (Monitor.TryEnter(syncObj, 0))
            {
                try
                {
                    ConditionalLogging.ExecuteIfParamIsNotNull(logger, () => logger.Log(LogLevel.Debug, CLASS_NAME + METH_NAME + "Disposing channel \"{0}\"...", clientChannel));
                    if (clientChannel != null)
                    {
                        if (clientChannel.State != CommunicationState.Faulted)
                        {
                            clientChannel.Close();
                        }
                        else
                        {
                            clientChannel.Abort();
                        }
                    }
                }
                catch (CommunicationException ex)
                {
                    ConditionalLogging.ExecuteIfParamIsNotNull(logger, () => logger.Log(LogLevel.Warning, ex, CLASS_NAME + METH_NAME + "Aborting channel..."));
                    // Communication exceptions are normal when
                    // closing the connection.
                    clientChannel.Abort();
                }
                catch (TimeoutException ex)
                {
                    ConditionalLogging.ExecuteIfParamIsNotNull(logger, () => logger.Log(LogLevel.Warning, ex, CLASS_NAME + METH_NAME + "Aborting channel..."));
                    // Timeout exceptions are normal when closing
                    // the connection.
                    clientChannel.Abort();
                }
                catch (Exception ex)
                {
                    ConditionalLogging.ExecuteIfParamIsNotNull(logger, () => logger.Log(LogLevel.Error, ex, CLASS_NAME + METH_NAME + "Aborting channel and rethrowing an unknown exception..."));
                    // Any other exception and you should 
                    // abort the connection and rethrow to 
                    // allow the exception to bubble upwards.
                    clientChannel.Abort();
                    throw;
                }
                finally
                {
                    Monitor.Exit(syncObj);
                }
            }
            else
            {
                ConditionalLogging.ExecuteIfParamIsNotNull(logger, () => logger.Log(LogLevel.Debug, CLASS_NAME + METH_NAME + "Channel \"{0}\" is already disposing. Waiting when it will be disposed...", clientChannel));
                lock (syncObj) { }
                ConditionalLogging.ExecuteIfParamIsNotNull(logger, () => logger.Log(LogLevel.Debug, CLASS_NAME + METH_NAME + "Channel \"{0}\". Dispose waiting finished.", clientChannel));
            }
        }
    }
}
