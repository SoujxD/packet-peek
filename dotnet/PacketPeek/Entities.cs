using Microsoft.AspNetCore.Identity;

namespace PacketPeek;

public class User : IdentityUser { }

public class Rule
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Metric { get; set; } = "packets";
    public string Grouping { get; set; } = "src_ip";
    public int WindowSeconds { get; set; }
    public long Threshold { get; set; }
    public bool Active { get; set; } = true;
}

public class Alert
{
    public long Id { get; set; }
    public int RuleId { get; set; }
    public Rule Rule { get; set; } = null!;
    public DateTimeOffset FiredAt { get; set; }
    public string GroupKey { get; set; } = "";
    public long Value { get; set; }
}

public class FlowSummary
{
    public long Id { get; set; }
    public string SrcIp { get; set; } = "";
    public string DstIp { get; set; } = "";
    public int SrcPort { get; set; }
    public int DstPort { get; set; }
    public int Protocol { get; set; }
    public long FirstUs { get; set; }
    public long LastUs { get; set; }
    public long Packets { get; set; }
    public long Bytes { get; set; }
}
