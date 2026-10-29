using PacketPeek.Protocol;
using Xunit;

namespace PacketPeek.Tests;

public class EvaluatorTests
{
    private static PacketEvent Packet(ulong seconds, string dst = "198.51.100.1", uint port = 80, string src = "192.0.2.1") =>
        new() { TimestampUs = seconds * 1000000, SrcIp = src, DstIp = dst, DstPort = port, Bytes = 10 };

    [Theory]
    [InlineData("packets", "src_ip", 2)]
    [InlineData("packets", "src_ip+dst_port", 2)]
    [InlineData("bytes", "src_ip", 20)]
    [InlineData("bytes", "src_ip+dst_port", 20)]
    [InlineData("distinct_dst_ips", "src_ip", 2)]
    [InlineData("distinct_dst_ips", "src_ip+dst_port", 2)]
    public void All_six_shapes_fire_only_above_threshold_and_rearm_after_expiry(string metric, string grouping, long threshold)
    {
        var evaluator = new RuleEvaluator();
        var rule = new Rule { Id = 1, Metric = metric, Grouping = grouping, WindowSeconds = 10, Threshold = threshold };
        Assert.Empty(evaluator.Evaluate(Packet(1), [rule]));
        Assert.Empty(evaluator.Evaluate(Packet(2, "198.51.100.2"), [rule]));
        var alert = Assert.Single(evaluator.Evaluate(Packet(3, "198.51.100.3"), [rule]));
        Assert.True(alert.Value > threshold);
        Assert.Empty(evaluator.Evaluate(Packet(4, "198.51.100.4"), [rule]));
        Assert.Empty(evaluator.Evaluate(Packet(20), [rule]));
        Assert.Empty(evaluator.Evaluate(Packet(21, "198.51.100.2"), [rule]));
        Assert.Single(evaluator.Evaluate(Packet(22, "198.51.100.3"), [rule]));
    }

    [Fact]
    public void Groups_are_isolated_and_distinct_metric_deduplicates()
    {
        var evaluator = new RuleEvaluator();
        var rule = new Rule { Id = 1, Metric = "distinct_dst_ips", Grouping = "src_ip+dst_port", WindowSeconds = 10, Threshold = 1 };
        Assert.Empty(evaluator.Evaluate(Packet(1), [rule]));
        Assert.Empty(evaluator.Evaluate(Packet(2), [rule]));
        Assert.Empty(evaluator.Evaluate(Packet(3, "198.51.100.2", port: 443), [rule]));
        Assert.Empty(evaluator.Evaluate(Packet(4, "198.51.100.2", src: "192.0.2.2"), [rule]));
        Assert.Single(evaluator.Evaluate(Packet(5, "198.51.100.2"), [rule]));
    }

    [Fact]
    public void Window_is_open_at_start_and_edits_and_deactivation_reset_state()
    {
        var evaluator = new RuleEvaluator();
        var rule = new Rule { Id = 1, Metric = "packets", Grouping = "src_ip", WindowSeconds = 10, Threshold = 1 };
        Assert.Empty(evaluator.Evaluate(Packet(1), [rule]));
        Assert.Empty(evaluator.Evaluate(Packet(11), [rule]));
        Assert.Single(evaluator.Evaluate(Packet(12), [rule]));
        rule.Active = false; Assert.Empty(evaluator.Evaluate(Packet(13), [rule]));
        rule.Active = true; Assert.Empty(evaluator.Evaluate(Packet(14), [rule]));
        rule.Threshold = 2; Assert.Empty(evaluator.Evaluate(Packet(15), [rule]));
        Assert.Empty(evaluator.Evaluate(Packet(16), [rule]));
        Assert.Single(evaluator.Evaluate(Packet(17), [rule]));
        Assert.Empty(evaluator.Evaluate(Packet(1), [rule]));
    }
}
