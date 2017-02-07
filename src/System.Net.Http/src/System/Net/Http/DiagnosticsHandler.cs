using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Http
{
    /// <summary>
    /// DiagnosticHandler notifies DiagnosticSource subscribers about outgoing Http requests
    /// </summary>
    internal sealed class DiagnosticsHandler : DelegatingHandler
    {
        /// <summary>
        /// DiagnosticHandler constructor
        /// </summary>
        /// <param name="innerHandler">Inner handler: Windows or Unix implementation of HttpMessageHandler. 
        /// Note that DiagnosticHandler is the latest in the pipeline </param>
        public DiagnosticsHandler(HttpMessageHandler innerHandler) : base(innerHandler) {}

        protected internal override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            //do not write to diagnostic source if request is invalid or cancelled,
            //let inner handler decide what to do with the it
            if (request == null || cancellationToken.IsCancellationRequested)
            {
                return await base.SendAsync(request, cancellationToken);
            }

            Activity requestActivity = null;

            //Check if DiagnosticSource is enabled for this RequestUri: 
            //Client code may subscribe to Http Diagnostic Listener with
            // - no predicate: in this case it will receive ALL events for all outgoing requests which are
            //      System.Net.Http.Request
            //      System.Net.Http.Response
            //      System.Net.Http.Activity.Start
            //      System.Net.Http.Activity.Stop
            // - Predicate: Func<string, bool> to filter event names and URIs:
            //      If user does not want to receive any System.Net.Http.Activity.* events, 
            //        he may subscribe to Request, Response events only,
            //      If user wants to filter particular URIs, he may write predicate to achieve it
            // Also check for Activity.Current: if it's null (subject to change to Activity.IsEnabled) 
            // this request is not sampled and we don't inject headers
            if (Activity.Current != null &&
                s_diagnosticListener.IsEnabled(request.RequestUri.ToString()))
            {
                // create a new activity for the outgoing Http request
                requestActivity = new Activity(HttpHandlerLoggingStrings.HttpActivityWriteName)
                    .SetStartTime(DateTimeStopwatch.GetTime());
                
                //Start it and notify subscribers
                s_diagnosticListener.StartActivity(requestActivity, new {Request = request});

                //Inject correlation headers
                request.Headers.Add(RequestIdHeaderName, requestActivity.Id);
                List<string> baggage = FormatBaggageHeader(requestActivity.Baggage);
                if (baggage.Count != 0)
                {
                    request.Headers.Add(CorrelationContextHeaderName, baggage);
                }
            }

            //notify subscribers listening to Request, Response events
            //System.Net.Http.Request and Response events may be eventually deprecated 
            //for now let's keep those events, so existing users will not be affected
            Guid loggingRequestId = LogHttpRequestCore(request);

            HttpResponseMessage response = null;
            Exception exception = null;
            try
            {
                response = await base.SendAsync(request, cancellationToken);
                //previously Response event was only fired if request was sucessful, 
                //this behavior does not change
                LogHttpResponseCore(response, loggingRequestId);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw;
            }
            finally
            {
                //Stop activity if it was started and fire event to DiagnosticSource
                if (requestActivity != null)
                {
                    requestActivity.SetEndTime(DateTimeStopwatch.GetTime());
                    s_diagnosticListener.StopActivity(
                        requestActivity, 
                        new { Response = response, Exception = exception }
                    );
                }
            }

            return response;
        }

        #region private
        private static readonly DiagnosticListener s_diagnosticListener = new DiagnosticListener(HttpHandlerLoggingStrings.DiagnosticListenerName);

        private Guid LogHttpRequestCore(HttpRequestMessage request)
        {
            if (s_diagnosticListener.IsEnabled(HttpHandlerLoggingStrings.RequestWriteName))
            {
                Guid loggingRequestId = Guid.NewGuid();
                long timestamp = Stopwatch.GetTimestamp();

                s_diagnosticListener.Write(
                    HttpHandlerLoggingStrings.RequestWriteName,
                    new
                    {
                        Request = request,
                        LoggingRequestId = loggingRequestId,
                        Timestamp = timestamp
                    }
                );

                return loggingRequestId;
            }
            return Guid.Empty;
        }

        private void LogHttpResponseCore(HttpResponseMessage response, Guid loggingRequestId)
        {
            // An empty loggingRequestId signifies that the request was not logged, so do
            // not attempt to log response.
            if (s_diagnosticListener.IsEnabled(HttpHandlerLoggingStrings.ResponseWriteName) && loggingRequestId != Guid.Empty)
            {
                long timestamp = Stopwatch.GetTimestamp();

                s_diagnosticListener.Write(
                    HttpHandlerLoggingStrings.ResponseWriteName,
                    new
                    {
                        Response = response,
                        LoggingRequestId = loggingRequestId,
                        TimeStamp = timestamp
                    }
                );
            }
        }

        private const string CorrelationContextHeaderName = "Correlation-Context";
        private const string RequestIdHeaderName = "Request-Id";

        private List<string> FormatBaggageHeader(IEnumerable<KeyValuePair<string, string>> baggage)
        {
            List<string> baggageHeader = new List<string>();
            foreach (var pair in baggage)
            {
                baggageHeader.Add(new NameValueHeaderValue(pair.Key, pair.Value).ToString());
            }

            return baggageHeader;
        }

        //TODO: move to stopwatch
        private static class DateTimeStopwatch
        {
            //last machine boot time if Stopwatch is HighResolution
            private static DateTime _stopwatchStartTime = Stopwatch.IsHighResolution ?
                DateTime.UtcNow.AddSeconds(-1 * Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency) :
                DateTime.UtcNow;

            public static DateTime GetTime()
            {
                return Stopwatch.IsHighResolution
                    ? _stopwatchStartTime.AddSeconds(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency)
                    : DateTime.UtcNow;
            }
        }
        #endregion
    }
}
