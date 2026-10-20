using SecurityManager.Security.Application.Abstractions;
using SecurityManager.Security.Domain.Entities;
using SecurityManager.Shared.Infrastructure.Persistence;
using SecurityManager.Shared.Kernel.Abstractions;

namespace SecurityManager.Security.Infrastructure.Persistence;

public sealed class SecurityUnitOfWork(SecurityDbContext context)
    : UnitOfWork<SecurityDbContext>(context), ISecurityUnitOfWork
{
    public IRepository<SecurityEvent> Events => Repository<SecurityEvent>();
    public IRepository<DetectionRule> Rules => Repository<DetectionRule>();
    public IRepository<Detection> Detections => Repository<Detection>();
}
