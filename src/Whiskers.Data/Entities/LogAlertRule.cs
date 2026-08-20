namespace Whiskers.Models;

public class LogAlertRuleEntity
{
    public long Id { get; set; }
    public string RuleId { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "";
    // Container filter: BOTH null = all containers. The UI dialog and the MCP tool only ever set
    // ContainerName (by design — a name outlives a recreated container), so neither field alone can
    // stand for "no filter" — see LogMonitorService.RuleApplies.
    public string? ContainerId { get; set; }
    public string? ContainerName { get; set; }
    public string Pattern { get; set; } = "";    // Regex oder Plaintext
    public bool IsRegex { get; set; }
    public string Severity { get; set; } = "warning"; // info, warning, error, critical
    public bool NotifyMatrix { get; set; } = true;
    public bool NotifyMattermost { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public int CooldownMinutes { get; set; } = 10;
    public DateTime? LastTriggered { get; set; }
    public int TriggerCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
