using SecurityManager.Scanner.Domain.Entities;
using SecurityManager.Shared.Kernel.Enums;

namespace SecurityManager.Scanner.Domain.Services;

/// <summary>Computes a 0–100 risk score from a set of findings.</summary>
public static class RiskCalculator
{
    public static int Score(IEnumerable<ScanFinding> findings) =>
        Math.Min(100, findings.Sum(f => f.Severity switch
        {
            Severity.Critical => 40,
            Severity.High => 20,
            Severity.Medium => 8,
            Severity.Low => 2,
            _ => 0
        }));
}
