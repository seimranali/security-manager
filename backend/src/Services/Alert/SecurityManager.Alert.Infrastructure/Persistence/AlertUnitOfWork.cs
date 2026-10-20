using SecurityManager.Alert.Application.Abstractions;
using SecurityManager.Alert.Domain.Entities;
using SecurityManager.Shared.Infrastructure.Persistence;
using SecurityManager.Shared.Kernel.Abstractions;

namespace SecurityManager.Alert.Infrastructure.Persistence;

public sealed class AlertUnitOfWork(AlertDbContext context)
    : UnitOfWork<AlertDbContext>(context), IAlertUnitOfWork
{
    public IRepository<SecurityAlert> Alerts => Repository<SecurityAlert>();
    public IRepository<NotificationChannel> Channels => Repository<NotificationChannel>();
}
