using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using SecurityManager.Alert.Application.Abstractions;
using SecurityManager.Alert.Application.DTOs;
using SecurityManager.Alert.Application.Services;
using SecurityManager.Alert.Domain.Entities;
using SecurityManager.Shared.Infrastructure.Security;

namespace SecurityManager.Alert.Infrastructure.Notifications;

/// <summary>SMTP email and HTTP webhook delivery.</summary>
public sealed class NotificationSender(HttpClient http, IChannelSecretProtector secrets) : INotificationSender
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public Task SendAsync(NotificationChannel channel, AlertDto alert, bool escalation, CancellationToken ct = default) =>
        channel.Type == ChannelType.Email ? SendEmailAsync(channel, alert, escalation, ct) : SendWebhookAsync(channel, alert, escalation, ct);

    public static string Subject(AlertDto a, bool escalation) =>
        $"[{a.Severity.ToString().ToUpperInvariant()}]{(escalation ? " Escalated:" : string.Empty)} {a.Title}";

    public static string Body(AlertDto a)
    {
        var category = a.Category is null ? string.Empty : " / " + a.Category;
        return string.Join('\n',
            a.Title,
            string.Empty,
            $"Severity:   {a.Severity}",
            $"Source:     {a.Source}{category}",
            $"Status:     {a.Status}",
            $"First seen: {a.CreatedAt:u}",
            $"Last seen:  {a.LastOccurredAt:u} ({a.OccurrenceCount} occurrence(s))",
            string.Empty,
            a.Message,
            string.Empty,
            "-- Security Manager");
    }

    private async Task SendEmailAsync(NotificationChannel c, AlertDto alert, bool escalation, CancellationToken ct)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(c.FromAddress!),
            Subject = Subject(alert, escalation),
            Body = Body(alert),
            IsBodyHtml = false
        };
        foreach (var to in NotificationChannelService.ParseRecipients(c.Recipients)) message.To.Add(to);

        using var smtp = new SmtpClient(c.SmtpHost!, c.SmtpPort) { EnableSsl = c.SmtpUseTls, Timeout = 15000 };
        if (!string.IsNullOrEmpty(c.SmtpUserName))
            smtp.Credentials = new NetworkCredential(c.SmtpUserName, c.SmtpPassword is null ? string.Empty : secrets.Unprotect(c.SmtpPassword));
        await smtp.SendMailAsync(message, ct);
    }

    private async Task SendWebhookAsync(NotificationChannel c, AlertDto alert, bool escalation, CancellationToken ct)
    {
        var subject = Subject(alert, escalation);
        object payload = c.WebhookFormat switch
        {
            WebhookFormat.Slack => new { text = $"*{subject}*\n{alert.Message}\n_Source: {alert.Source}_" },
            WebhookFormat.Teams => new { title = subject, text = $"{alert.Message}\n\nSource: {alert.Source}" },
            _ => new { @event = escalation ? "alert.escalated" : "alert.created", alert }
        };
        var body = JsonSerializer.Serialize(payload, Json);

        using var request = new HttpRequestMessage(HttpMethod.Post, c.WebhookUrl) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.UserAgent.ParseAdd("SecurityManager/1.0");
        if (c.WebhookSecret is not null)
        {
            // Receivers verify: lowercase hex(HMAC-SHA256(secret, timestamp + "." + body)) == X-SM-Signature.
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            request.Headers.Add("X-SM-Timestamp", timestamp);
            request.Headers.Add("X-SM-Signature", Sign(secrets.Unprotect(c.WebhookSecret), timestamp, body));
        }

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Webhook returned {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    public static string Sign(string secret, string timestamp, string body) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"))).ToLowerInvariant();
}

/// <summary>Encrypts SMTP passwords and webhook secrets at rest (AES-256-GCM).</summary>
public sealed class ChannelSecretProtector(IConfiguration config) : IChannelSecretProtector
{
    private readonly SecretBox _box = new(config, "Alert.Channels");

    public string Protect(string value) => _box.ProtectString(value);
    public string Unprotect(string value) => _box.UnprotectString(value);
}
