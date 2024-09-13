namespace OpenTelemetry.Proxy;

public class InnerActivityContext
{
    private DateTimeOffset? _time;
    private string? _name;
    private IReadOnlyCollection<KeyValuePair<string, object?>>? _tags;

    public bool AdjustStartTime
    {
        get => _time != null;
        init => _time = value ? DateTimeOffset.UtcNow : null;
    }

    public string? Name
    {
        get => _name;
        init => _name = value;
    }

    public IReadOnlyCollection<KeyValuePair<string, object?>>? Tags
    {
        get => _tags;
        init => _tags = value;
    }

    internal void Merge(InnerActivityContext outer)
    {
        outer.Clear();

        _time ??= outer._time;

        if (string.IsNullOrEmpty(_name)) _name = outer._name;

        if (_tags == null || _tags.Count < 1)
        {
            _tags = outer._tags;

            return;
        }

        if (outer._tags == null || outer._tags.Count < 1) return;

        if (_tags is IDictionary<string, object?> tags)
        {
            foreach (var tag in outer._tags) tags[tag.Key] = tag.Value;

            return;
        }

        if (_tags is ICollection<KeyValuePair<string, object?>> collection)
        {
            foreach (var tag in outer._tags) collection.Add(tag);

            return;
        }

        tags = new Dictionary<string, object?>(_tags.Count + outer._tags.Count);

        foreach (var tag in _tags) tags[tag.Key] = tag.Value;
        foreach (var tag in outer._tags) tags[tag.Key] = tag.Value;

        _tags = (Dictionary<string, object?>)tags;
    }

    private Activity? _activity;
    private bool _disposed;

    internal bool OnStart(Activity data)
    {
        if (Interlocked.CompareExchange(ref _activity, data, null) != null) return false;

        if (_time is not { } startTimeUtc) return true;

        var diff = (data.StartTimeUtc - startTimeUtc).TotalMilliseconds;

        if (diff > 1) data.SetTag("_StartTimeOffset_", diff);

        data.SetStartTime(startTimeUtc.UtcDateTime);

        return true;
    }

    internal bool OnEnd(Activity data)
    {
        if (_activity != data || _disposed) return false;

        if (Name != null) data.DisplayName = Name;

        if (Tags != null) data.SetTag(Tags);

        return true;
    }

    private void Clear()
    {
        _activity = null;
        _disposed = true;
    }
}
