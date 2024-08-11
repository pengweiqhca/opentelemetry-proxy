using OpenTelemetry.Trace;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace OpenTelemetry.Proxy;

public static class ActivityExtensions
{
    public static Activity SetTag(this Activity activity, IEnumerable<KeyValuePair<string, object?>> tags)
    {
        foreach (var kv in tags) activity.SetTagEnumerable(kv.Key, kv.Value);

        return activity;
    }

    public static Activity SetTagEnumerable(this Activity activity, string key, object? value)
    {
        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry kv in dictionary)
                activity.SetTagEnumerable($"{key}.{kv.Key}", kv.Value);

            return activity;
        }

        if (value is not ITuple tuple) return activity.SetTag(key, value);

        for (var index = 0; index < tuple.Length; index++)
            activity.SetTagEnumerable($"{key}.Item{index + 1}", tuple[index]);

        return activity;
    }

    /// <returns>Return false always.</returns>
    public static bool SetExceptionStatus(Activity? activity, Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, UnwrapException(ex).Message).RecordException(ex.Demystify());

        return false;

        static Exception UnwrapException(Exception ex)
        {
            var counter = 100;

            while (counter-- > 0)
                if (ex is AggregateException or TargetInvocationException && ex.InnerException != null)
                    ex = ex.InnerException;
                else counter = 0;

            return ex;
        }
    }
}
