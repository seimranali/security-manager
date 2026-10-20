using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using SecurityManager.Alert.Application.Abstractions;
using SecurityManager.Alert.Application.DTOs;
using SecurityManager.Alert.Domain.Entities;
using SecurityManager.Alert.Domain.Enums;
using SecurityManager.Shared.Kernel.Enums;
using SecurityManager.Shared.Kernel.Exceptions;

namespace SecurityManager.Alert.Application.Services;

/// <summary>Secrets never leave the service: DTOs only say whether one is set.</summary>
public sealed record NotificationChannelDto(
    Guid Id, string Name, ChannelType Type, bool IsEnabled, Severity MinSeverity, bool NotifyOnEscalation,
    string? SmtpHost, int SmtpPort, bool SmtpUseTls, string? SmtpUserName, bool HasSmtpPassword, string? FromAddress, string? Recipients,
    string? WebhookUrl, WebhookFormat WebhookFormat, bool HasWebhookSecret,
    DateTime? LastSentAt, DateTime? LastFailureAt, string? LastError, int SentCount, DateTime CreatedAt);

public sealed class SaveNotificationChannelRequest
{
    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    public ChannelType Type { get; set; }
    public bool IsEnabled { get; set; } = true;
    public Severity MinSeverity { get; set; } = Severity.High;
    public bool NotifyOnEscalation { get; set; } = true;

    [StringLength(255)] public string? SmtpHost { get; set; }
    [Range(1, 65535)] public int SmtpPort { get; set; } = 587;
    public bool SmtpUseTls { get; set; } = true;
    [StringLength(200)] public string? SmtpUserName { get; set; }
    /// <summary>Null keeps the stored password; empty string clears it.</summary>
    [StringLength(500)] public string? SmtpPassword { get; set; }
    [StringLength(200)] public string? FromAddress { get; set; }
    [StringLength(1000)] public string? Recipients { get; set; }

    [StringLength(1000)] public string? WebhookUrl { get; set; }
    public WebhookFormat WebhookFormat { get; set; } = WebhookFormat.Generic;
    /// <summary>Null keeps the stored secret; empty string clears it.</summary>
    [StringLength(500)] public string? WebhookSecret { get; set; }
}

public interface INotificationChannelService
{
    Task<IReadOnlyList<NotificationChannelDto>> GetAllAsync(CancellationToken ct = default);
    Task<NotificationChannelDto> CreateAsync(SaveNotificationChannelRequest request, CancellationToken ct = default);
    Task<NotificationChannelDto> UpdateAsync(Guid id, SaveNotificationChannelRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Sends a sample alert through the channel right away and reports the outcome.</summary>
    Task<NotificationChannelDto> TestAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Encrypts channel secrets before storage.</summary>
public interface IChannelSecretProtector
{
    string Protect(string value);
    string Unprotect(string value);
}

public sealed class NotificationChannelService(IAlertUnitOfWork uow, INotificationSender sender, IChannelSecretProtector secrets)
    : INotificationChannelService
{
    public async Task<IReadOnlyList<NotificationChannelDto>> GetAllAsync(CancellationToken ct = default) =>
        (await uow.Channels.ListAsync(orderBy: o => o.OrderBy(c => c.Name), ct: ct)).Select(ToDto).ToList();

    public async Task<NotificationChannelDto> CreateAsync(SaveNotificationChannelRequest r, CancellationToken ct = default)
    {
        var channel = new NotificationChannel();
        Apply(channel, r);
        await uow.Channels.AddAsync(channel, ct);
        await uow.SaveChangesAsync(ct);
        return ToDto(channel);
    }

    public async Task<NotificationChannelDto> UpdateAsync(Guid id, SaveNotificationChannelRequest r, CancellationToken ct = default)
    {
        var channel = await Load(id, ct);
        Apply(channel, r);
        await uow.SaveChangesAsync(ct);
        return ToDto(channel);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        uow.Channels.Remove(await Load(id, ct));
        await uow.SaveChangesAsync(ct);
    }

    public async Task<NotificationChannelDto> TestAsync(Guid id, CancellationToken ct = default)
    {
        var channel = await Load(id, ct);
        var now = DateTime.UtcNow;
        var sample = new AlertDto(Guid.NewGuid(), "Test notification from Security Manager",
            $"This is a test of the '{channel.Name}' notification channel. No action is needed.",
            Severity.Info, "Security Manager", null, "Test", AlertStatus.Open, 1, now, now, null, null, null, null, null);
        try
        {
            await sender.SendAsync(channel, sample, escalation: false, ct);
            channel.LastSentAt = now;
            channel.LastError = null;
        }
        catch (Exception ex)
        {
            channel.LastFailureAt = now;
            channel.LastError = Truncate(ex.Message, 1000);
        }
        await uow.SaveChangesAsync(ct);
        return ToDto(channel);
    }

    private void Apply(NotificationChannel c, SaveNotificationChannelRequest r)
    {
        c.Name = r.Name.Trim();
        c.Type = r.Type;
        c.IsEnabled = r.IsEnabled;
        c.MinSeverity = r.MinSeverity;
        c.NotifyOnEscalation = r.NotifyOnEscalation;

        if (r.Type == ChannelType.Email)
        {
            if (string.IsNullOrWhiteSpace(r.SmtpHost)) throw new BusinessRuleException("SMTP server is required.");
            var recipients = ParseRecipients(r.Recipients);
            if (recipients.Count == 0) throw new BusinessRuleException("Add at least one recipient.");
            if (string.IsNullOrWhiteSpace(r.FromAddress) || !MailAddress.TryCreate(r.FromAddress.Trim(), out _))
                throw new BusinessRuleException("A valid sender (From) address is required.");
            c.SmtpHost = r.SmtpHost.Trim();
            c.SmtpPort = r.SmtpPort;
            c.SmtpUseTls = r.SmtpUseTls;
            c.SmtpUserName = string.IsNullOrWhiteSpace(r.SmtpUserName) ? null : r.SmtpUserName.Trim();
            if (r.SmtpPassword is not null) c.SmtpPassword = r.SmtpPassword == string.Empty ? null : secrets.Protect(r.SmtpPassword);
            c.FromAddress = r.FromAddress.Trim();
            c.Recipients = string.Join(", ", recipients);
        }
        else
        {
            if (!Uri.TryCreate(r.WebhookUrl?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
                throw new BusinessRuleException("A valid http(s) webhook URL is required.");
            c.WebhookUrl = uri.ToString();
            c.WebhookFormat = r.WebhookFormat;
            if (r.WebhookSecret is not null) c.WebhookSecret = r.WebhookSecret == string.Empty ? null : secrets.Protect(r.WebhookSecret);
        }
    }

    public static List<string> ParseRecipients(string? text)
    {
        var list = (text ?? string.Empty).Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var invalid = list.FirstOrDefault(a => !MailAddress.TryCreate(a, out _));
        if (invalid is not null) throw new BusinessRuleException($"'{invalid}' is not a valid email address.");
        return list;
    }

    private async Task<NotificationChannel> Load(Guid id, CancellationToken ct) =>
        await uow.Channels.GetByIdAsync(id, ct: ct) ?? throw new NotFoundException("Notification channel", id);

    private static NotificationChannelDto ToDto(NotificationChannel c) => new(
        c.Id, c.Name, c.Type, c.IsEnabled, c.MinSeverity, c.NotifyOnEscalation,
        c.SmtpHost, c.SmtpPort, c.SmtpUseTls, c.SmtpUserName, c.SmtpPassword is not null, c.FromAddress, c.Recipients,
        c.WebhookUrl, c.WebhookFormat, c.WebhookSecret is not null,
        c.LastSentAt, c.LastFailureAt, c.LastError, c.SentCount, c.CreatedAt);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
