using System.Linq.Expressions;
using System.Reflection;

namespace OpenTelemetry.Proxy.Demo;

public static class ActivityExtensions
{
    private static readonly Action<Activity, ActivityLink> AddLinkMethod;

    static ActivityExtensions()
    {
        var links = typeof(Activity).GetField("_links", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var ctor = links.FieldType.GetConstructors()
            .First(static ctor => ctor.GetParameters().FirstOrDefault()?.ParameterType == typeof(ActivityLink));

        var p0 = Expression.Parameter(typeof(Activity));
        var p1 = Expression.Parameter(typeof(ActivityLink));

        var field = Expression.Field(p0, links);

        var expression = Expression.IfThenElse(
            Expression.Equal(field, Expression.Constant(null)),
            Expression.Assign(field, Expression.New(ctor, p1)),
            Expression.Call(field, "Add", Type.EmptyTypes, p1));

        AddLinkMethod = Expression.Lambda<Action<Activity, ActivityLink>>(expression, p0, p1).Compile();
    }

    public static void AddLink(this Activity activity, ActivityLink link) => AddLinkMethod(activity, link);
}
