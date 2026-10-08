

namespace Nur.Store2026.Observability.Tracing;

internal class TracingProvider : ITracingProvider
{
    private string _correlationId;
    private string _spanId;
    private string _traceId;

    public TracingProvider()
    {
        _correlationId = Guid.NewGuid().ToString();
        _spanId = Guid.NewGuid().ToString();
        _traceId = Guid.NewGuid().ToString();
    }
    public string GetCorrelationId()
    {
        return _correlationId;
    }

    public string GetSpanId()
    {
        return _spanId;
    }

    public string GetTraceId()
    {
        return _traceId;
    }

    public void SetCorrelationId(string correlationId)
    {
        _correlationId = correlationId;
    }

    public void SetSpanId(string spanId)
    {
        _spanId = spanId;
    }

    public void SetTraceId(string traceId)
    {
        _traceId = traceId;
    }
}
