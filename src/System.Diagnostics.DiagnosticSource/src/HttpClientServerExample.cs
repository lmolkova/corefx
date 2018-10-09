using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace AspNetCore.Hosting
{

    /// <summary>
    /// Example of Http server instrumentation.
    /// </summary>
    class HttpServerExample
    {
        private static readonly DiagnosticListener MySource = new DiagnosticListener("HttpInExample");

        // TraceContextPropagationTextFormat implements Id extraction and validation
        // It knows how to encode Activity into Http headers (or other text format of the tracecontext)
        // It is reused by AspNetCore, AspNetClassic and some other SDKs (like AMQP client SDKs
        // or other protocols that support headers/string metadata
        private readonly ITextPropagationFormat<HttpRequestHeaders> defaultPropagationFormat =
            new TraceContextPropagationTextFormat<HttpRequestHeaders>();

        private void HttpIn(HttpRequest request)
        {
            Activity activity = new Activity("httpin");
            activity = defaultPropagationFormat.Extract(
                request.Headers,
                (headers, s) =>
                {
                    headers.TryGetValue(s, out string value);

                    return value;
                }, 
                activity);

            MySource.StartActivity(activity, new { });

            // process

            MySource.StopActivity(activity, new {});
        }
    }

    class HttpClientExample
    {
        private static readonly DiagnosticListener MySource = new DiagnosticListener("HttpOutExample");
        private readonly ITextPropagationFormat<HttpRequestHeaders> defaultPropagationFormat = 
            new TraceContextPropagationTextFormat<HttpRequestHeaders>();

        private void HttpOut(HttpRequest request)
        {
            Activity activity = new Activity("httpout");
            
            MySource.StartActivity(activity, new { });

            defaultPropagationFormat.Inject(
                activity,
                request.Headers,
                (headers, k, v) =>
                {
                    if (!headers.ContainsKey(k))
                        headers.Add(k,v );
                });
            // process request

            MySource.StopActivity(activity, new { });
        }
    }

    class TracingSystem
    {
        public void OnEvent(KeyValuePair<string, object> diagSourceEvent)
        {
            switch (diagSourceEvent.Key)
            {
                case "httpin.Start":
                    // validate, parse, read and update tracestate if needed
                    var tracestateIn = new Tracestate(Activity.Current.Tracestate);
                    // get control properties, etc
                    break;
                case "httpout.Start":
                    var tracestateOut = new Tracestate(Activity.Current.Tracestate);

                    tracestateOut.Remove("az");
                    tracestateOut.Prepend("az", "newValue");

                    Activity.Current.Tracestate = tracestateOut.ToString();
                    // we'll inject tracestate AFTER start event is handled.
                    break;
                case "httpin.Stop":
                case "httpout.Stop":
                    // report telemetry
                    break;
            }
        }

        /// <summary>
        /// Tracing-system specific implementation of Tracestate. Just an example.
        /// </summary>
        class Tracestate : IEnumerable<KeyValuePair<string, string>>
        {
            private readonly string tracestateString;
            private readonly 
                Lazy<LinkedList<KeyValuePair<string,string>>> state;

            public Tracestate(string tracestateString)
            {
                // do lazy init. If it's never modified - no need to parse it
                this.tracestateString = tracestateString;
                this.state = new Lazy<LinkedList<KeyValuePair<string, string>>>(Initialize);
            }

            private LinkedList<KeyValuePair<string, string>> Initialize()
            {
                if (Validate(tracestateString))
                {
                    //...

                }
                return new LinkedList<KeyValuePair<string, string>>();
            }

            public void Prepend(string key, string value)
            {
                // as per spec, updated/new tracestate should appear first in the list
                if (ValidateKey(key) && ValidateValue(value))
                {
                    if (GetItem(key) == null)
                        state.Value.AddFirst(new KeyValuePair<string, string>(key, value));
                }
            }

            public string Remove(string key)
            {
                var toRemove = GetItem(key);
                if (toRemove != null)
                {
                    state.Value.Remove(toRemove.Value);
                    return toRemove.Value.Value;
                }

                return null;
            }

            public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
            {
                return state.Value.GetEnumerator();
            }

            public override string ToString()
            {
                if (!state.IsValueCreated)
                    return tracestateString;

                var sb = new StringBuilder();
                foreach (var kvp in state.Value)
                {
                    sb.Append(kvp.Key).Append("=").Append(kvp.Value).Append(",");
                }

                if (sb.Length > 0)
                {
                    sb.Remove(sb.Length - 1, 1);
                }

                return sb.ToString();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            private KeyValuePair<string, string>? GetItem(string key)
            {
                foreach (var kvp in state.Value)
                {
                    if (kvp.Key == key)
                        return kvp;
                }

                return null;
            }


            private static bool Validate(string tracestate)
            {
                return true;
            }

            private static bool ValidateKey(string tracestateKey)
            {
                return true;
            }

            private static bool ValidateValue(string tracestateValue)
            {
                return true;
            }
        }
    }

    class HttpRequestHeaders : Dictionary<string, string>
    {
    }

    class HttpRequest
    {
        public HttpRequestHeaders Headers { get; set; }
    }

    class HttpRequestMessage
    {
        public HttpRequestHeaders Headers { get; set; }
    }
}
