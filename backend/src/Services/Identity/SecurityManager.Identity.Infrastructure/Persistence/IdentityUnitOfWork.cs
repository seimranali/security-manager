using SecurityManager.Identity.Application.Abstractions;
using SecurityManager.Identity.Domain.Entities;
using SecurityManager.Shared.Infrastructure.Persistence;
using SecurityManager.Shared.Kernel.Abstractions;

namespace SecurityManager.Identity.Infrastructure.Persistence;

public sealed class IdentityUnitOfWork(IdentityDbContext context)
    : UnitOfWork<IdentityDbContext>(context), IIdentityUnitOfWork
{
    public IRepository<User> Users => Repository<User>();
    public IRepository<RefreshToken> RefreshTokens => Repository<RefreshToken>();
    public IRepository<LoginAttempt> LoginAttempts => Repository<LoginAttempt>();
}
