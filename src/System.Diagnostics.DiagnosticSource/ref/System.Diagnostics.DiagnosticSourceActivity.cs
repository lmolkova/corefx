// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
// ------------------------------------------------------------------------------
// Changes to this file must follow the http://aka.ms/api-review process.
// ------------------------------------------------------------------------------

namespace System.Diagnostics {
  public partial class Activity {
    public Activity(string operationName) {}      
    public string OperationName { get { throw null; } }

    [System.ObsoleteAttribute]
    public string Id {get { throw null; } private set {} }

    public DateTime StartTimeUtc {get { throw null; } private set {} }
    public Activity Parent {get { throw null; } private set {} }
  
    [System.ObsoleteAttribute]
    public string ParentId {get { throw null; } private set {} }

    [System.ObsoleteAttribute]    
    public string RootId {get { throw null; } private set {} }    
    
    public TraceId TraceId { get { throw null; } private set { } }
    public SpanId SpanId { get { throw null; } private set { } }
    public SpanId ParentSpanId { get { throw null; } private set { } }
    public byte TraceFlags {get { throw null; } set {} }    
    public string Tracestate {get { throw null; } set {} }    
    public string Traceparent {get { throw null; } set {} }    
    public TimeSpan Duration {get { throw null; } private set {} }    
    public System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, string>> Tags { get { throw null; } }    
    public System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, string>> Baggage { get { throw null; } }
    public string GetBaggageItem(string key) {throw null;}
    public Activity AddTag(string key, string value) {throw null;}
    public Activity AddBaggage(string key, string value) {throw null;}

    [System.ObsoleteAttribute]
    public Activity SetParentId(string parentId) {throw null;}

    public Activity SetStartTime(DateTime startTimeUtc) {throw null;}
    public Activity SetEndTime(DateTime endTimeUtc) {throw null;}
    public Activity Start() {throw null;}
    public void Stop() {}
    public static Activity Current 
    {
#if ALLOW_PARTIALLY_TRUSTED_CALLERS
        [System.Security.SecuritySafeCriticalAttribute]
#endif
        get { throw null; } 
#if ALLOW_PARTIALLY_TRUSTED_CALLERS
       [System.Security.SecuritySafeCriticalAttribute]
#endif
        set {}
    }
  }

  public abstract partial class DiagnosticSource {
    public Activity StartActivity(Activity activity, object args) {throw null;}
    public void StopActivity(Activity activity, object args) {}
  }

  public class SpanId
  {
    public SpanId(string hex) { }
    public SpanId(byte[] bytes) { }
    public SpanId() { }
    public byte[] Bytes { get { throw null; } private set { } }
    public override string ToString() { throw null; }
  }

  public class TraceId
  {
    public TraceId(string hex) {}      
    public TraceId(byte [] bytes) {}
    public TraceId() { }
    public byte[] Bytes { get { throw null; } private set { } }
    public override string ToString() { throw null; }
  }

    public interface ITextPropagationFormat<T>
    {
        Activity Extract(T carrier, Func<T, string, string> getter, Activity activity);
        void Inject(Activity activity, T carrier, Action<T, string, string> setter);
    }
}
