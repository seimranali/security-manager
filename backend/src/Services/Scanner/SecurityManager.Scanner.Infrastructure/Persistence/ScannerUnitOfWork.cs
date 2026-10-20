using SecurityManager.Scanner.Application.Abstractions;
using SecurityManager.Scanner.Domain.Entities;
using SecurityManager.Shared.Infrastructure.Persistence;
using SecurityManager.Shared.Kernel.Abstractions;

namespace SecurityManager.Scanner.Infrastructure.Persistence;

public sealed class ScannerUnitOfWork(ScannerDbContext context)
    : UnitOfWork<ScannerDbContext>(context), IScannerUnitOfWork
{
    public IRepository<ScanTarget> Targets => Repository<ScanTarget>();
    public IRepository<ScanSchedule> Schedules => Repository<ScanSchedule>();
    public IRepository<ScanJob> Jobs => Repository<ScanJob>();
    public IRepository<ScanFinding> Findings => Repository<ScanFinding>();
}
