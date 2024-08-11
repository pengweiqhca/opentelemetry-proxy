namespace OpenTelemetry.StaticProxy.SourceTransformer;

internal interface ILineNumber
{
    int Line { get; }

    string File { get; }
}

internal sealed class LineNumber(int line, string file) : ILineNumber
{
    public int Line { get; } = line;

    public string File { get; } = file;

    public override string ToString() => $"#line {Line} \"{File}\"";
}
