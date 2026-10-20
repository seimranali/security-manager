using SecurityManager.Shared.Kernel.Entities;
using SecurityManager.Shared.Kernel.Enums;

namespace SecurityManager.Alert.Domain.Entities;

public enum ChannelType
{
    Email,
    Webhook
}

/// <summary>Payload shape for webhooks: generic JSON, or the message format Slack / Microsoft Teams incoming webhooks expect.</summary>
public enum WebhookFormat
{
    Generic,
    Slack,
    Teams
}

/// <summary>Where alert notifications are delivered (an email recipient list or a webhook endpoint).</summary>
public class NotificationChannel : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public ChannelType Type { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>Only alerts at or above this severity are sent.</summary>
    public Severity MinSeverity { get; set; } = Severity.High;
    /// <summary>Also notify when an existing alert re-occurs with a higher severity.</summary>
    public bool NotifyOnEscalation { get; set; } = true;

    // ---- Email
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseTls { get; set; } = true;
    public string? SmtpUserName { get; set; }
    /// <summary>Encrypted at rest.</summary>
    public string? SmtpPassword { get; set; }
    public string? FromAddress { get; set; }
    /// <summary>Comma- or semicolon-separated recipients.</summary>
    public string? Recipients { get; set; }

    // ---- Webhook
    public string? WebhookUrl { get; set; }
    public WebhookFormat WebhookFormat { get; set; } = WebhookFormat.Generic;
    /// <summary>Optional shared secret (encrypted at rest); requests carry an HMAC-SHA256 signature header.</summary>
    public string? WebhookSecret { get; set; }

    // ---- Delivery status
    public DateTime? LastSentAt { get; set; }
    public DateTime? LastFailureAt { get; set; }
    public string? LastError { get; set; }
    public int SentCount { get; set; }

    public bool Accepts(Severity severity) => IsEnabled && severity >= MinSeverity;
}
