using SecurityManager.Identity.Application.Abstractions;
using SecurityManager.Identity.Application.DTOs;
using SecurityManager.Identity.Domain.Entities;
using SecurityManager.Shared.Kernel.Exceptions;
using SecurityManager.Shared.Kernel.Security;

namespace SecurityManager.Identity.Application.Services;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> GetAllAsync(CancellationToken ct = default);
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, Guid actingUserId, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Guid actingUserId, CancellationToken ct = default);
    Task<int> RevokeSessionsAsync(Guid id, CancellationToken ct = default);
}

public sealed class UserService(IIdentityUnitOfWork uow, IPasswordHasher hasher) : IUserService
{
    public async Task<IReadOnlyList<UserDto>> GetAllAsync(CancellationToken ct = default)
    {
        var users = await uow.Users.ListAsync(orderBy: o => o.OrderBy(u => u.UserName), ct: ct);
        return users.Select(UserMapping.ToDto).ToList();
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest r, CancellationToken ct = default)
    {
        ValidateRole(r.Role);
        PasswordPolicy.Validate(r.Password);

        var userName = User.Normalize(r.UserName);
        var email = r.Email.Trim().ToLowerInvariant();
        if (await uow.Users.AnyAsync(u => u.UserName == userName, ct)) throw new ConflictException($"User name '{userName}' is taken.");
        if (await uow.Users.AnyAsync(u => u.Email == email, ct)) throw new ConflictException($"Email '{email}' is already registered.");

        var user = new User { UserName = userName, Email = email, FullName = r.FullName.Trim(), Role = r.Role };
        user.SetPassword(hasher.Hash(user, r.Password), DateTime.UtcNow);
        await uow.Users.AddAsync(user, ct);
        await uow.SaveChangesAsync(ct);
        return UserMapping.ToDto(user);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest r, Guid actingUserId, CancellationToken ct = default)
    {
        ValidateRole(r.Role);
        var user = await LoadAsync(id, ct);
        var now = DateTime.UtcNow;

        if (id == actingUserId && (r.Role != Roles.Admin || !r.IsActive))
            throw new BusinessRuleException("You cannot remove your own admin role or disable your own account.");
        if (user.Role == Roles.Admin && (r.Role != Roles.Admin || !r.IsActive))
            await EnsureAnotherActiveAdminAsync(id, ct);

        var email = r.Email.Trim().ToLowerInvariant();
        if (await uow.Users.AnyAsync(u => u.Email == email && u.Id != id, ct)) throw new ConflictException($"Email '{email}' is already registered.");

        var revokeSessions = (user.IsActive && !r.IsActive) || user.Role != r.Role;
        user.Email = email;
        user.FullName = r.FullName.Trim();
        user.Role = r.Role;
        user.IsActive = r.IsActive;

        if (!string.IsNullOrEmpty(r.NewPassword))
        {
            PasswordPolicy.Validate(r.NewPassword);
            user.SetPassword(hasher.Hash(user, r.NewPassword), now);
            revokeSessions = true;
        }

        // Role/status/password changes invalidate existing refresh tokens; access tokens expire within minutes.
        if (revokeSessions) await AuthService.RevokeAllForUserAsync(uow, id, now, RevocationReasons.AdminRevoked, ct);
        await uow.SaveChangesAsync(ct);
        return UserMapping.ToDto(user);
    }

    public async Task DeleteAsync(Guid id, Guid actingUserId, CancellationToken ct = default)
    {
        if (id == actingUserId) throw new BusinessRuleException("You cannot delete your own account.");
        var user = await LoadAsync(id, ct);
        if (user.Role == Roles.Admin) await EnsureAnotherActiveAdminAsync(id, ct);

        uow.Users.Remove(user);
        await uow.SaveChangesAsync(ct);
    }

    public async Task<int> RevokeSessionsAsync(Guid id, CancellationToken ct = default)
    {
        await LoadAsync(id, ct);
        var count = await AuthService.RevokeAllForUserAsync(uow, id, DateTime.UtcNow, RevocationReasons.AdminRevoked, ct);
        await uow.SaveChangesAsync(ct);
        return count;
    }

    private async Task<User> LoadAsync(Guid id, CancellationToken ct) =>
        await uow.Users.GetByIdAsync(id, ct: ct) ?? throw new NotFoundException("User", id);

    private async Task EnsureAnotherActiveAdminAsync(Guid excludingId, CancellationToken ct)
    {
        if (!await uow.Users.AnyAsync(u => u.Role == Roles.Admin && u.IsActive && u.Id != excludingId, ct))
            throw new BusinessRuleException("At least one active administrator must remain.");
    }

    private static void ValidateRole(string role)
    {
        if (!Roles.UserRoles.Contains(role))
            throw new BusinessRuleException($"Role must be one of: {string.Join(", ", Roles.UserRoles)}.");
    }
}
