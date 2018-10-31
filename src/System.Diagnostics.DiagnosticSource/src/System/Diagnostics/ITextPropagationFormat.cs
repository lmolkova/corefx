namespace System.Diagnostics
{
    /// <summary>
    /// ITextPropagationFormat implementation is responsible Id extraction and validation
    /// It knows how to encode Activity into Http headers (or other text format of the tracecontext)
    ///
    /// Interface is useful iff we decide to support custom  propagation formats
    /// or disable propagation without loosing ability to trace calls.
    /// </summary>
    public interface ITextPropagationFormat
    {
        /// <summary>
        /// Extracts context from the metadata and sets it on activity.
        /// </summary>
        /// <typeparam name="T">Metadata carrier type</typeparam>
        /// <param name="carrier">Metadata map</param>
        /// <param name="getter">Function that gets particular metadata field</param>
        /// <param name="activity">Activity to apply context to (must not be started yet)</param>/// 
        /// <returns>Activity for chaining</returns>
        Activity Extract<T>(T carrier, Func<T, string, string> getter, Activity activity);


        /// <summary>
        /// Injects context into the metadata of request.
        /// </summary>
        /// <typeparam name="T">Metadata carrier type</typeparam>
        /// <param name="activity">Activity to get context from</param>
        /// <param name="carrier">Metadata map</param>
        /// <param name="setter">Action that adds context to the request metadata</param>
        void Inject<T>(Activity activity, T carrier, Action<T, string, string> setter);
    }
}
