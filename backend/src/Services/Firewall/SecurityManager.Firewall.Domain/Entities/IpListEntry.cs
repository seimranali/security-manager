using System.Net;
using System.Net.Sockets;
using SecurityManager.Firewall.Domain.Enums;
using SecurityManager.Firewall.Domain.Services;
using SecurityManager.Shared.Kernel.Entities;

namespace SecurityManager.Firewall.Domain.Entities;

/// <summary>An address (IP, CIDR block or range) on the allowlist or blocklist, optionally expiring.</summary>
public class IpListEntry : BaseEntity
{
    /// <summary>Blocklist entries broader than this are rejected to prevent accidentally blocking the internet.</summary>
    public const int MinBlockPrefixV4 = 8;
    public const int MinBlockPrefixV6 = 16;

    public IpListType ListType { get; set; }
    public string Address { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IpListSource Source { get; set; } = IpListSource.Manual;
    /// <summary>External reference, e.g. the detection id that triggered an automatic block.</summary>
    public string? SourceReference { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsEnabled { get; set; } = true;
    public long HitCount { get; set; }
    public DateTime? LastHitAt { get; set; }
    public string? CreatedBy { get; set; }

    public bool IsExpired(DateTime now) => ExpiresAt is { } e && e <= now;
    public bool IsActive(DateTime now) => IsEnabled && !IsExpired(now);
    public bool Matches(string ip) => AddressMatcher.IsMatch(Address, ip);

    public void RegisterHit(DateTime at)
    {
        HitCount++;
        LastHitAt = at;
    }

    /// <summary>Re-activates the entry and extends (never shortens) its expiry. Permanent entries stay permanent.</summary>
    public void ExtendUntil(DateTime expiresAt, DateTime now)
    {
        IsEnabled = true;
        if (ExpiresAt is null) return;
        if (IsExpired(now) || expiresAt > ExpiresAt) ExpiresAt = expiresAt;
    }

    /// <summary>Normalises an address spec ("10.0.0.0/24", "1.2.3.4", "a-b") for storage and duplicate detection.</summary>
    public static string Normalize(string address) =>
        string.Join('-', address.Trim().ToLowerInvariant().Split('-', StringSplitOptions.TrimEntries));

    /// <summary>Returns an error message, or null when the address is acceptable for the given list.</summary>
    public static string? Validate(string address, IpListType listType)
    {
        var a = Normalize(address);
        if (a.Length == 0) return "Address is required.";
        if (a.Contains(',')) return "Use one address, CIDR block or range per entry (bulk import accepts many).";
        if (AddressMatcher.IsAny(a)) return "'any' is not allowed in an IP list.";
        if (!AddressMatcher.IsValid(a)) return $"'{address.Trim()}' is not a valid IP address, CIDR block or range.";

        if (listType == IpListType.Block && a.Contains('/'))
        {
            var parts = a.Split('/');
            var family = IPAddress.Parse(parts[0]).AddressFamily;
            var prefix = int.Parse(parts[1]);
            var min = family == AddressFamily.InterNetwork ? MinBlockPrefixV4 : MinBlockPrefixV6;
            if (prefix < min) return $"Blocklist CIDR /{prefix} is too broad (minimum /{min}).";
        }
        return null;
    }
}
