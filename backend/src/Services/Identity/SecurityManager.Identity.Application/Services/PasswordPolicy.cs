using SecurityManager.Shared.Kernel.Exceptions;

namespace SecurityManager.Identity.Application.Services;

public static class PasswordPolicy
{
    public const int MinLength = 8;

    public static void Validate(string password)
    {
        var errors = new List<string>();
        if (password.Length < MinLength) errors.Add($"at least {MinLength} characters");
        if (!password.Any(char.IsUpper)) errors.Add("an uppercase letter");
        if (!password.Any(char.IsLower)) errors.Add("a lowercase letter");
        if (!password.Any(char.IsDigit)) errors.Add("a digit");
        if (password.All(char.IsLetterOrDigit)) errors.Add("a symbol");
        if (errors.Count > 0)
            throw new BusinessRuleException($"Password must contain {string.Join(", ", errors)}.");
    }
}
