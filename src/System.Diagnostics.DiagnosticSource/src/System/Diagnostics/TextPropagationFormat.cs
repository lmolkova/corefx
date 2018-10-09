namespace System.Diagnostics
{
    /// <summary>
    /// ITextPropagationFormat implementation is reponsible Id extraction and validation
    /// It knows how to encode Activity into Http headers (or other text format of the tracecontext)
    ///
    /// Interface is useful iff we decide to support custom  propagation formats
    /// or disable propagation without loosing ability to trace calls.
    /// </summary>
    /// <typeparam name="T">Metadata carrier type</typeparam>
    public interface ITextPropagationFormat<T>
    {
        /// <summary>
        /// Extracts context from the metadata and sets it on activity.
        /// </summary>
        /// <param name="carrier">Metadata map</param>
        /// <param name="getter">Function that gets particular metadata field</param>
        /// <param name="activity">Activity to apply context to (must not be started yet)</param>/// 
        /// <returns>Activity for chaining</returns>
        Activity Extract(T carrier, Func<T, string, string> getter, Activity activity);

        /// <summary>
        /// Injects context into the metadata of request.
        /// </summary>
        /// <param name="activity">Activity to get context from</param>
        /// <param name="carrier">Metadata map</param>
        /// <param name="setter">Action that adds context to the request metadata</param>
        void Inject(Activity activity, T carrier, Action<T, string, string> setter);
    }

    /// <summary>
    /// Tracecontext (w3c distributed tracing implementation).
    /// It is reused by AspNetCore, AspNetClassic and some other SDKs like AMQP client SDKs
    /// or other protocols that support headers/string metadata.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class TraceContextPropagationTextFormat<T> : ITextPropagationFormat<T>
    {
        /// <summary>
        /// Extracts traceparent and tracestate headers and set them on the Activity.
        /// </summary>
        /// <param name="carrier">Request headers/metadata</param>
        /// <param name="getter">Function that gets particular metadata field</param>
        /// <param name="activity">Activity to apply context to (must not be started yet)</param>
        /// <returns></returns>
        public Activity Extract(T carrier, Func<T, string, string> getter, Activity activity)
        {
            // TODO: validation

            // only library that implements particular protocol knows how to 
            // extract header from the request, so it supplies a lambda
            var traceparent = getter.Invoke(carrier, "traceparent");

            if (traceparent != null)
            {
                ParseTraceparent(activity, traceparent);

                activity.Tracestate = getter.Invoke(carrier, "tracestate");
            }
            else
            {
                activity.TraceId = new TraceId();
                activity.SpanId = new SpanId();
            }

            return activity;
        }

        /// <summary>
        /// Injects traceparent and tracestate into outgoing requests
        /// </summary>
        /// <param name="activity">Activity to get context from</param>
        /// <param name="carrier">Request headers/metadata</param>
        /// <param name="setter">Action that adds context to the request headers/metadata</param>
        public void Inject(Activity activity, T carrier, Action<T, string, string> setter)
        {
            var traceparent = string.Concat(
                Activity.ProtocolVersion,
                "-",
                activity.TraceId.ToString(),
                "-",
                activity.SpanId.ToString(),
                "-",
                activity.TraceFlags.ToString("x2"));

            setter(carrier, "traceparent", traceparent);

            if (activity.Tracestate != null)
            {
                setter(carrier, "tracestate", activity.Tracestate);
            }
        }

        private void ParseTraceparent(Activity activity, string traceparent)
        {
            // TODO validation
            var segments = traceparent.Split('-');

            // we actually don't care about version: even if it's not 1
            // we should continue
            string version = segments[0];

            string traceid = segments[1];
            string spanid = segments[2];
            string sampled = segments[3];

            activity.TraceId = new TraceId(traceid);
            activity.ParentSpanId = new SpanId(spanid);
            activity.TraceFlags = Convert.ToByte(sampled, 16);
        }

    }
}
