using System.Collections;
using System.Reflection;

namespace OpenTelemetry.Proxy;

public static class ActivityExtensions
{
    private static readonly ConcurrentDictionary<Type, Action<Activity, string, object>?> CacheDictionary = new();
    private static readonly MethodInfo EnumerateMethod = typeof(ActivityExtensions).GetMethod(nameof(Enumerate), BindingFlags.Static | BindingFlags.NonPublic)!;

    public static Activity SetTag(this Activity activity, IEnumerable<KeyValuePair<string, object?>> tags)
    {
        foreach (var kv in tags) activity.SetTagEnumerable(kv.Key, kv.Value);

        return activity;
    }

    public static Activity SetTagEnumerable(this Activity activity, string key, object? value)
    {
        if (value is string || value is not IEnumerable enumerable) return activity.SetTag(key, value);

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry kv in dictionary)
                activity.SetTagEnumerable($"{key}.{kv.Key}", kv.Value);

            return activity;
        }

        var action = CacheDictionary.GetOrAdd(value.GetType(), static type => (from i in type.GetInterfaces()
            where i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            select i.GetGenericArguments()[0] into argument
            where argument.IsGenericType && argument.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)
            select (Action<Activity, string, object>)EnumerateMethod.MakeGenericMethod(argument.GetGenericArguments())
                .CreateDelegate(typeof(Action<Activity, string, object>))).FirstOrDefault());

        if (action != null) action(activity, key, value);
        else
        {
            var index = 0;
            foreach (var v in enumerable)
                activity.SetTagEnumerable($"{key}.{index++}", v);
        }

        return activity;
    }

    private static void Enumerate<TKey, TValue>(Activity activity, string key, object dictionary)
    {
        foreach (var kv in (IEnumerable<KeyValuePair<TKey, TValue>>)dictionary)
            activity.SetTagEnumerable($"{key}.{kv.Key}", kv.Value);
    }
}
