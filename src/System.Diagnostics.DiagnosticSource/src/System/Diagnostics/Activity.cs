// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
#if ALLOW_PARTIALLY_TRUSTED_CALLERS
    using System.Security;
#endif

namespace System.Diagnostics
{
    /// <summary>
    /// Activity represents operation with context to be used for logging.
    /// Activity has operation name, Id, start time and duration, tags and baggage.
    ///  
    /// Current activity can be accessed with static AsyncLocal variable Activity.Current.
    /// 
    /// Activities should be created with constructor, configured as necessary
    /// and then started with Activity.Start method which maintains parent-child
    /// relationships for the activities and sets Activity.Current.
    /// 
    /// When activity is finished, it should be stopped with static Activity.Stop method.
    /// 
    /// No methods on Activity allow exceptions to escape as a response to bad inputs.
    /// They are thrown and caught (that allows Debuggers and Monitors to see the error)
    /// but the exception is suppressed, and the operation does something reasonable (typically
    /// doing nothing).  
    /// </summary>
    public partial class Activity
    {
        /// <summary>
        /// An operation name is a COARSEST name that is useful grouping/filtering. 
        /// The name is typically a compile-time constant.   Names of Rest APIs are
        /// reasonable, but arguments (e.g. specific accounts etc), should not be in
        /// the name but rather in the tags.  
        /// </summary>
        public string OperationName { get; }

        /// <summary>
        /// Is the ID of the whole trace forest. It is represented as a 16-bytes array,
        /// for example, 4bf92f3577b34da6a3ce929d0e0e4736.
        /// </summary>
        public TraceId TraceId { get; internal set; }

        /// <summary>
        /// Is the ID of the caller span (parent). It is represented as an 8-byte array,
        /// for example, 00f067aa0ba902b7
        /// </summary>
        public SpanId ParentSpanId { get; internal set; }

        /// <summary>
        /// Is the ID of the current span. It is represented as an 8-byte array,
        /// for example, 00f067aa0ba902b7
        /// </summary>
        public SpanId SpanId { get; internal set; }

        /// <summary>
        /// An 8-bit field that controls tracing flags such as sampling, trace level.
        /// </summary>
        public byte TraceFlags { get; set; } = 0;

        /// <summary>
        /// Conveys information about request position in multiple distributed
        /// tracing graphs and tracing-system specific context.
        ///
        /// Default .NET behavior is to blindly propagate tracestate to child activities and
        /// outside the process. It's similar to Baggage, but cannot be set there because baggage
        /// is sent in Correlation-Context and we can't make everyone update to newer .NET Core and new HttpClient.
        ///
        /// </summary>
        
        //  The downside of this implementation (string) that
        // Tracestate will be parsed and validated on each outgoing call.
        //
        // we should consider
        // 1. implement tracestate in Activity with lazy intitialization.
        // then common tracestate validation code could live here and be used by all http libs
        // (we have at least 4 now for Http and few more non-http in Azure).
        //
        // 2. let tracing system parse it in Start event and set it on the first-level Activity once
        // and then use oject on the child Activities as needed. Then Tracestate might look like
        // class Tracestate<T>
        // {
        //    public readonly string TracestateString;
        //    public T TracestateObject;
        // }
        // then tracing system is reponsible to validate and parse state and synchronizestate string and object.
        public string Tracestate { get; set; }

        /// <summary>
        /// This is an ID that is specific to a particular request.   Filtering
        /// to a particular ID insures that you get only one request that matches.  
        /// Id has a hierarchical structure: '|root-id.id1_id2.id3_' Id is generated when 
        /// <see cref="Start"/> is called by appending suffix to Parent.Id
        /// or ParentId; Activity has no Id until it started
        /// <para/>
        /// See <see href="https://github.com/dotnet/corefx/blob/master/src/System.Diagnostics.DiagnosticSource/src/ActivityUserGuide.md#id-format"/> for more details
        /// </summary>
        /// <example>
        /// Id looks like '|a000b421-5d183ab6.1.8e2d4c28_1.':<para />
        ///  - '|a000b421-5d183ab6.' - Id of the first, top-most, Activity created<para />
        ///  - '|a000b421-5d183ab6.1.' - Id of a child activity. It was started in the same process as the first activity and ends with '.'<para />
        ///  - '|a000b421-5d183ab6.1.8e2d4c28_' - Id of the grand child activity. It was started in another process and ends with '_'<para />
        /// 'a000b421-5d183ab6' is a <see cref="RootId"/> for the first Activity and all its children
        /// </example>
        [Obsolete]
        public string Id {
            get
            {
                if (TraceId == null || SpanId == null)
                    return null;

                // use traceid and spanid to form valid request id for backward compatibility:
                // we shoud expect old HttpClient  (2.0) to use fresh diagnostic source and 
                // send meaningful Ids
                return string.Concat("|", TraceId.ToString(), ".", SpanId.ToString(), ".");
            }
        }

        /// <summary>
        /// Root Id is substring from Activity.Id (or ParentId) between '|' (or beginning) and first '.'.
        /// Filtering by root Id allows to find all Activities involved in operation processing.
        /// RootId may be null if Activity has neither ParentId nor Id.
        /// See <see href="https://github.com/dotnet/corefx/blob/master/src/System.Diagnostics.DiagnosticSource/src/ActivityUserGuide.md#id-format"/> for more details
        /// </summary>
        [Obsolete]
        public string RootId => _rootId ?? TraceId?.ToString();
        // assuming AspNetCore2.0 is used with fresh DiagnosticSource, it will use SetParentId and we need
        // tracing system to know there was a legacy root Id. 

        /// <summary>
        /// The time that operation started.  It will typically be initialized when <see cref="Start"/>
        /// is called, but you can set at any time via <see cref="SetStartTime(DateTime)"/>.
        /// </summary>
        public DateTime StartTimeUtc { get; private set; }

        /// <summary>
        /// If the Activity that created this activity is from the same process you can get 
        /// that Activity with Parent.  However, this can be null if the Activity has no
        /// parent (a root activity) or if the Parent is from outside the process.
        /// </summary>
        /// <seealso cref="ParentId"/>
        public Activity Parent { get; private set; }

        /// <summary>
        /// If the parent for this activity comes from outside the process, the activity
        /// does not have a Parent Activity but MAY have a ParentId (which was deserialized from
        /// from the parent).   This accessor fetches the parent ID if it exists at all.  
        /// Note this can be null if this is a root Activity (it has no parent)
        /// <para/>
        /// See <see href="https://github.com/dotnet/corefx/blob/master/src/System.Diagnostics.DiagnosticSource/src/ActivityUserGuide.md#id-format"/> for more details
        /// </summary>
        [Obsolete]
        public string ParentId { get; private set; }


        /// <summary>
        /// Tags are string-string key-value pairs that represent information that will
        /// be logged along with the Activity to the logging system.   This information
        /// however is NOT passed on to the children of this activity.
        /// </summary>
        /// <seealso cref="Baggage"/>
        public IEnumerable<KeyValuePair<string, string>> Tags
        {
            get
            {
                for (var tags = _tags; tags != null; tags = tags.Next)
                    yield return tags.keyValue;
            }
        }

        /// <summary>
        /// Baggage is string-string key-value pairs that represent information that will
        /// be passed along to children of this activity.   Baggage is serialized 
        /// when requests leave the process (along with the ID).   Typically Baggage is
        /// used to do fine-grained control over logging of the activity and any children.  
        /// In general, if you are not using the data at runtime, you should be using Tags 
        /// instead. 
        /// </summary> 
        public IEnumerable<KeyValuePair<string, string>> Baggage
        {
            get
            {
                for (var activity = this; activity != null; activity = activity.Parent)
                    for (var baggage = activity._baggage; baggage != null; baggage = baggage.Next)
                        yield return baggage.keyValue;
            }
        }

        /// <summary>
        /// Returns the value of the key-value pair added to the activity with <see cref="AddBaggage(string, string)"/>.
        /// Returns null if that key does not exist.  
        /// </summary>
        public string GetBaggageItem(string key)
        {
            foreach (var keyValue in Baggage)
                if (key == keyValue.Key)
                    return keyValue.Value;
            return null;
        }

        /* Constructors  Builder methods */

        /// <summary>
        /// Note that Activity has a 'builder' pattern, where you call the constructor, a number of 'Set*' and 'Add*' APIs and then
        /// call <see cref="Start"/> to build the activity.  You MUST call <see cref="Start"/> before using it.
        /// </summary>
        /// <param name="operationName">Operation's name <see cref="OperationName"/></param>
        public Activity(string operationName)
        {
            if (string.IsNullOrEmpty(operationName))
            {
                NotifyError(new ArgumentException($"{nameof(operationName)} must not be null or empty"));
                return;
            }

            OperationName = operationName;
        }

        /// <summary>
        /// Update the Activity to have a tag with an additional 'key' and value 'value'.
        /// This shows up in the <see cref="Tags"/>  enumeration.   It is meant for information that
        /// is useful to log but not needed for runtime control (for the latter, <see cref="Baggage"/>)
        /// </summary>
        /// <returns>'this' for convenient chaining</returns>
        public Activity AddTag(string key, string value)
        {
            _tags = new KeyValueListNode() { keyValue = new KeyValuePair<string, string>(key, value), Next = _tags };
            return this;
        }

        /// <summary>
        /// Update the Activity to have baggage with an additional 'key' and value 'value'.
        /// This shows up in the <see cref="Baggage"/> enumeration as well as the <see cref="GetBaggageItem(string)"/>
        /// method.
        /// Baggage is meant for information that is needed for runtime control.   For information 
        /// that is simply useful to show up in the log with the activity use <see cref="Tags"/>.
        /// Returns 'this' for convenient chaining.
        /// </summary>
        /// <returns>'this' for convenient chaining</returns>
        public Activity AddBaggage(string key, string value)
        {
            _baggage = new KeyValueListNode() { keyValue = new KeyValuePair<string, string>(key, value), Next = _baggage };
            return this;
        }

        /// <summary>
        /// Updates the Activity To indicate that the activity with ID <paramref name="parentId"/>
        /// caused this activity.   This is intended to be used only at 'boundary' 
        /// scenarios where an activity from another process logically started 
        /// this activity. The Parent ID shows up the Tags (as well as the ParentID 
        /// property), and can be used to reconstruct the causal tree.  
        /// Returns 'this' for convenient chaining.
        /// </summary>
        /// <param name="parentId">The id of the parent operation.</param>
        [Obsolete]
        public Activity SetParentId(string parentId)
        {
            if (Parent != null)
            {
                NotifyError(new InvalidOperationException($"Trying to set {nameof(ParentId)} on activity which has {nameof(Parent)}"));
            }
            else if (ParentId != null)
            {
                NotifyError(new InvalidOperationException($"{nameof(ParentId)} is already set"));
            }
            else if (string.IsNullOrEmpty(parentId))
            {
                NotifyError(new ArgumentException($"{nameof(parentId)} must not be null or empty"));
            }
            else
            {
                // we have to expect older ASP.NET (Core) versions calling it with Request-Id string
                // and in some cases it might have |traceid.spanid. pattern
                if (parentId[0] == '|')
                {
                    int rootEnd = parentId.IndexOf('.');
                    if (rootEnd == 33)
                    {
                        // TODO: validate 
                        TraceId = new TraceId(parentId.Substring(1, 32));

                        int spanEnd = parentId.IndexOf('.', rootEnd + 1);
                        if (spanEnd == 33 + 1 + 16)
                        {
                            ParentSpanId = new 
                                SpanId(parentId.Substring(rootEnd + 1, 16));
                        }
                    }
                    else if (rootEnd > 0)
                    {
                        _rootId = parentId.Substring(1, rootEnd);
                    }
                }

                ParentId = parentId;
            }

            return this;
        }

        /// <summary>
        /// Update the Activity to set start time
        /// </summary>
        /// <param name="startTimeUtc">Activity start time in UTC (Greenwich Mean Time)</param>
        /// <returns>'this' for convenient chaining</returns>
        public Activity SetStartTime(DateTime startTimeUtc)
        {
            if (startTimeUtc.Kind != DateTimeKind.Utc)
            {
                NotifyError(new InvalidOperationException($"{nameof(startTimeUtc)} is not UTC"));
            }
            else
            {
                StartTimeUtc = startTimeUtc;
            }
            return this;
        }

        /// <summary>
        /// Update the Activity to set <see cref="Duration"/>
        /// as a difference between <see cref="StartTimeUtc"/>
        /// and <paramref name="endTimeUtc"/>.
        /// </summary>
        /// <param name="endTimeUtc">Activity stop time in UTC (Greenwich Mean Time)</param>
        /// <returns>'this' for convenient chaining</returns>
        public Activity SetEndTime(DateTime endTimeUtc)
        {
            if (endTimeUtc.Kind != DateTimeKind.Utc)
            {
                NotifyError(new InvalidOperationException($"{nameof(endTimeUtc)} is not UTC"));
            }
            else
            {
                Duration = endTimeUtc - StartTimeUtc;
                if (Duration.Ticks <= 0)
                    Duration = new TimeSpan(1); // We want Duration of 0 to mean  'EndTime not set)
            }
            return this;
        }

        /// <summary>
        /// If the Activity has ended (<see cref="Stop"/> or <see cref="SetEndTime"/> was called) then this is the delta
        /// between <see cref="StartTimeUtc"/> and end.   If Activity is not ended and <see cref="SetEndTime"/> was not called then this is 
        /// <see cref="TimeSpan.Zero"/>.
        /// </summary>
        public TimeSpan Duration { get; private set; }

        /// <summary>
        /// Starts activity
        /// <list type="bullet">
        /// <item>Sets <see cref="Parent"/> to hold <see cref="Current"/>.</item>
        /// <item>Sets <see cref="Current"/> to this activity.</item>
        /// <item>If <see cref="StartTimeUtc"/> was not set previously, sets it to <see cref="DateTime.UtcNow"/>.</item>
        /// <item>Generates a unique <see cref="Id"/> for this activity.</item>
        /// </list>
        /// Use <see cref="DiagnosticSource.StartActivity(Activity, object)"/> to start activity and write start event.
        /// </summary>
        /// <seealso cref="DiagnosticSource.StartActivity(Activity, object)"/>
        /// <seealso cref="SetStartTime(DateTime)"/>
        public Activity Start()
        {
            if (SpanId == null)
            {
                NotifyError(new InvalidOperationException("Trying to start an Activity that was already started"));
            }
            else
            {
                if (ParentSpanId == null)
                {
                    var parent = Current;
                    if (parent != null)
                    {
                        ParentSpanId = parent.SpanId;
                        Tracestate = parent.Tracestate;
                        Parent = parent;
                    }
                }

                if (StartTimeUtc == default(DateTime))
                {
                    StartTimeUtc = GetUtcNow();
                }

                if (TraceId == null)
                {
                    TraceId = new TraceId();
                }

                SpanId = new SpanId();

                SetCurrent(this);
            }
            return this;
        }

        /// <summary>
        /// Stops activity: sets <see cref="Current"/> to <see cref="Parent"/>.
        /// If end time was not set previously, sets <see cref="Duration"/> as a difference between <see cref="DateTime.UtcNow"/> and <see cref="StartTimeUtc"/>
        /// Use <see cref="DiagnosticSource.StopActivity(Activity, object)"/>  to stop activity and write stop event.
        /// </summary>
        /// <seealso cref="DiagnosticSource.StopActivity(Activity, object)"/>
        /// <seealso cref="SetEndTime(DateTime)"/>
        public void Stop()
        {
            if (SpanId == null)
            {
                NotifyError(new InvalidOperationException("Trying to stop an Activity that was not started"));
                return;
            }

            if (!isFinished)
            {
                isFinished = true;

                if (Duration == TimeSpan.Zero)
                {
                    SetEndTime(GetUtcNow());
                }

                SetCurrent(Parent);
            }
        }


        /// <summary>
        /// Gets or sets value of traceparent header on the activity (00-traceId-spanId-sampled)
        /// </summary>
        public string Traceparent
        {
            // TODO cache
            get => string.Concat(
                ProtocolVersion,
                "-",
                TraceId.ToString(),
                "-",
                SpanId.ToString(),
                "-",
                TraceFlags.ToString("x2"));
            set => ParseTraceparent(value);
        }

        private void ParseTraceparent(string traceparent)
        {
            // TODO validation
            var segments = traceparent.Split('-');

            // we actually don't care about version: even if it's not 00
            // we should continue
            string version = segments[0];

            string traceid = segments[1];
            string spanid = segments[2];
            string sampled = segments[3];

            TraceId = new TraceId(traceid);
            ParentSpanId = new SpanId(spanid);
            TraceFlags = Convert.ToByte(sampled, 16);
        }

        #region private 
        private static void NotifyError(Exception exception)
        {
            // Throw and catch the exception.  This lets it be seen by the debugger
            // ETW, and other monitoring tools.   However we immediately swallow the
            // exception.   We may wish in the future to allow users to hook this 
            // in other useful ways but for now we simply swallow the exceptions.  
            try
            {
                throw exception;
            }
            catch { }
        }

        private static bool ValidateSetCurrent(Activity activity)
        {
            bool canSet = activity == null || (activity.SpanId != null && activity.TraceId != null && !activity.isFinished);
            if (!canSet)
            {
                NotifyError(new InvalidOperationException("Trying to set an Activity that is not running"));
            }

            return canSet;
        }

        internal const string ProtocolVersion = "00";
        private string _rootId;

        /// <summary>
        /// Having our own key-value linked list allows us to be more efficient  
        /// </summary>
        private partial class KeyValueListNode
        {
            public KeyValuePair<string, string> keyValue;
            public KeyValueListNode Next;
        }

        private KeyValueListNode _tags;
        private KeyValueListNode _baggage;
        private bool isFinished;
        #endregion // private
    }

    internal class ByteArrayId
    {
        // either 16 or 8
        private readonly int bytesCount;

        public ByteArrayId(byte[] bytes, int bytesCount)
        {
            Debug.Assert(bytesCount == 16 || bytesCount == 8);
            this.bytesCount = bytesCount;
            if (bytes != null && bytes.Length == bytesCount /* validate hex */)
                Bytes = bytes;
        }

        public ByteArrayId(string hexStr, int bytesCount)
        {
            //TODO: validate, optimize
            Debug.Assert(bytesCount == 16 || bytesCount == 8);
            this.bytesCount = bytesCount;
            int length = hexStr.Length;

            if (length == bytesCount * 2)
            {
                Bytes = new byte[length / 2];
                for (int i = 0; i < length; i += 2)
                    Bytes[i / 2] = Convert.ToByte(hexStr.Substring(i, 2), 16);
            }
        }

        public ByteArrayId(int bytesCount)
        {
            Debug.Assert(bytesCount == 16 || bytesCount == 8);
            this.bytesCount = bytesCount;

            Bytes = new byte[bytesCount];
            Guid.NewGuid().ToByteArray().CopyTo(Bytes, 16 - bytesCount);
        }

        internal byte[] Bytes { get; }

        public override string ToString()
        {
            //TODO: optimize, cache
            return BitConverter.ToString(Bytes).Replace("-", "").ToLower();
        }
    }


    /// <summary>
    /// 
    /// </summary>
    public class TraceId
    {
        private readonly ByteArrayId id;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bytes"></param>
        public TraceId(byte[] bytes)
        {
            id = new ByteArrayId(bytes, 16);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hexStr"></param>
        public TraceId(string hexStr)
        {
            id = new ByteArrayId(hexStr, 16);
        }

        /// <summary>
        /// 
        /// </summary>
        public TraceId() 
        {
            id = new ByteArrayId(16);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return id.ToString();
        }

        /// <summary>
        /// 
        /// </summary>
        public byte[] Bytes => id.Bytes;
    }

    /// <summary>
    /// 
    /// </summary>
    public class SpanId
    {
        private readonly ByteArrayId id;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bytes"></param>
        public SpanId(byte[] bytes)
        {
            id = new ByteArrayId(bytes, 8);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hexStr"></param>
        public SpanId(string hexStr)
        {
            id = new ByteArrayId(hexStr, 8);
        }

        /// <summary>
        /// 
        /// </summary>
        public SpanId()
        {
            id = new ByteArrayId(8);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return id.ToString();
        }

        /// <summary>
        /// 
        /// </summary>
        public byte[] Bytes => id.Bytes;
    }
}
