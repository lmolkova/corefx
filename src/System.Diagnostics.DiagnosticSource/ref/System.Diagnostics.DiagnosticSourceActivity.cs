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
  
    public string ParentId {get { throw null; } set {} } // TraceId, public setter for binary (e.g. AMQP) or non-w3c custom propagation (e.g.Zipkin)

    public string RootId {get { throw null; } set {} } // TraceId, public setter for binary (e.g. AMQP) or non-w3c custom propagation (e.g.Zipkin)

    public string SpanId { get { throw null; } private set { } }

    public byte TraceFlags {get { throw null; } set {} }

    public string Tracestate {get { throw null; } set {} }

    public string W3CId { get { throw null; } set { } }    // 00-traceid-spanid-01
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
    public void StopActivityIfEnabled(Activity activity, Func<object> eventPayloadFactory) {}
    public Activity StartActivityIfEnabled(Func<Activity> activityFactory, object isEnabledArg1, object isEnabledArg2,
          Func<object> eventPayloadFactory) { throw null; }

    public static ITextPropagationFormat HttpPropagationFormat = null;
  }

    public interface ITextPropagationFormat
    {
        Activity Extract<T>(T carrier, Func<T, string, string> getter, Activity activity);
        void Inject<T>(Activity activity, T carrier, Action<T, string, string> setter);
    }
}
