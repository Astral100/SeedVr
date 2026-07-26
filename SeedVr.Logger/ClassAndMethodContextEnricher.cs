using Serilog.Core;
using Serilog.Events;

namespace SeedVr.Logger;

public class ClassAndMethodContextEnricher : ILogEventEnricher
{
    public ClassAndMethodContextEnricher(string callerFileName, string callerMethodName, int callerLineNumber)
    {
        CallerFileName = callerFileName;
        CallerMethodName = callerMethodName;
        CallerLineNumber = callerLineNumber;
    }

    public string CallerMethodName { get; protected set; }
    public string CallerFileName { get; protected set; }
    public int CallerLineNumber { get; protected set; }
    public static string CallerFileNamePropertyName { get; } = "CallerFileName";
    public static string CallerMethodNamePropertyName { get; } = "CallerMethodName";
    public static string CallerLineNumberPropertyName { get; } = "CallerLineNumber";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        logEvent.AddPropertyIfAbsent(new LogEventProperty(CallerFileNamePropertyName, new ScalarValue(CallerFileName)));
        logEvent.AddPropertyIfAbsent(new LogEventProperty(CallerMethodNamePropertyName, new ScalarValue(CallerMethodName)));
        logEvent.AddPropertyIfAbsent(new LogEventProperty(CallerLineNumberPropertyName, new ScalarValue(CallerLineNumber)));
    }
}