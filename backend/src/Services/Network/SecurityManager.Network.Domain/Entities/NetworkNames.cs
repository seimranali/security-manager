using System.Text.RegularExpressions;

namespace SecurityManager.Network.Domain.Entities;

public static partial class NetworkNames
{
    /// <summary>Linux interface names: max 15 characters (IFNAMSIZ − 1).</summary>
    public static bool IsInterfaceName(string? name) => !string.IsNullOrWhiteSpace(name) && InterfaceRegex().IsMatch(name);

    public static bool IsHostname(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 253 && HostnameRegex().IsMatch(name);

    /// <summary>WireGuard keys are 32 bytes, base64 encoded (44 characters ending in '=').</summary>
    public static bool IsWireGuardKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length != 44) return false;
        try { return Convert.FromBase64String(key).Length == 32; }
        catch (FormatException) { return false; }
    }

    /// <summary>Routing table ids usable for custom tables (main = 254).</summary>
    public const int MainTable = 254;
    public static bool IsRoutingTable(int table) => table is >= 1 and <= 252 or MainTable;

    public static IEnumerable<string> SplitList(string? csv) =>
        (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9._-]{0,14}$")]
    private static partial Regex InterfaceRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$")]
    private static partial Regex HostnameRegex();
}
