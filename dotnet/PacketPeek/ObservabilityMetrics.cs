using Prometheus;

namespace PacketPeek;

/// <summary>Application metrics use fixed names and no user or packet-derived labels.</summary>
public static class ObservabilityMetrics
{
    public static int ChannelCapacityValue { get; private set; }
    static ObservabilityMetrics() { }
    public static void Initialize() { }

    public static readonly Counter PacketsReceived = Metrics.CreateCounter(
        "packetpeek_engine_packets_received_total", "Packet events read from the native engine.");
    public static readonly Counter FlowsPersisted = Metrics.CreateCounter(
        "packetpeek_flows_persisted_total", "Flow summaries persisted to PostgreSQL.");
    public static readonly Counter PacketsProcessed = Metrics.CreateCounter(
        "packetpeek_packets_processed_total", "Packet events evaluated by the rule worker.");
    public static readonly Counter FlowsCreated = Metrics.CreateCounter(
        "packetpeek_flows_created_total", "Flow summaries received from the native engine.");
    public static readonly Counter AlertsTriggered = Metrics.CreateCounter(
        "packetpeek_alerts_triggered_total", "Alerts triggered by rule evaluation.");
    public static readonly Counter RuleEvaluations = Metrics.CreateCounter(
        "packetpeek_rule_evaluations_total", "Individual active rule evaluations performed.");
    public static readonly Counter EngineErrors = Metrics.CreateCounter(
        "packetpeek_engine_errors_total", "Unexpected native engine failures.");
    public static readonly Gauge EngineRunning = Metrics.CreateGauge(
        "packetpeek_engine_running", "Whether the native capture engine process is running.");
    public static readonly Gauge EngineCaptureExpected = Metrics.CreateGauge(
        "packetpeek_engine_capture_expected", "1 only when enabled live-interface capture is expected to remain running; 0 for disabled capture and finite PCAP replay.");
    public static readonly Gauge ChannelDepth = Metrics.CreateGauge(
        "packetpeek_channel_depth", "Packet events currently buffered in the bounded processing channel.");
    public static readonly Gauge ChannelCapacity = Metrics.CreateGauge(
        "packetpeek_channel_capacity", "Configured capacity of the bounded packet processing channel.");
    public static readonly Gauge ChannelUtilization = Metrics.CreateGauge(
        "packetpeek_channel_utilization_ratio", "Buffered packet events divided by channel capacity (0 to 1).");
    public static readonly Gauge ActiveRules = Metrics.CreateGauge(
        "packetpeek_active_rules", "Number of active rules loaded during the most recent packet evaluation.");
    public static readonly Histogram RuleEvaluationDuration = Metrics.CreateHistogram(
        "packetpeek_rule_evaluation_duration_seconds", "Time spent evaluating all active rules for one packet.",
        new HistogramConfiguration { Buckets = Histogram.ExponentialBuckets(0.0005, 2, 15) });
    public static readonly Histogram DbOperationDuration = Metrics.CreateHistogram(
        "packetpeek_db_operation_duration_seconds", "Duration of instrumented database operations.",
        new HistogramConfiguration { Buckets = Histogram.ExponentialBuckets(0.001, 2, 12) });
    public static readonly Counter DbOperationFailures = Metrics.CreateCounter(
        "packetpeek_db_operation_failures_total", "Failures from instrumented database operations.");

    public static void SetChannelDepth(int depth, int capacity)
    {
        ChannelCapacityValue = capacity;
        ChannelCapacity.Set(capacity);
        ChannelDepth.Set(depth);
        ChannelUtilization.Set(capacity == 0 ? 0 : Math.Clamp((double)depth / capacity, 0, 1));
    }

    public static async Task MeasureDbOperationAsync(Func<Task> operation)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try { await operation().ConfigureAwait(false); }
        catch { DbOperationFailures.Inc(); throw; }
        finally { DbOperationDuration.Observe(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds); }
    }

    public static async Task<T> MeasureDbOperationAsync<T>(Func<Task<T>> operation)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try { return await operation().ConfigureAwait(false); }
        catch { DbOperationFailures.Inc(); throw; }
        finally { DbOperationDuration.Observe(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds); }
    }
}
