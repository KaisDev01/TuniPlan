using System.Linq.Expressions;
using DAL.Context;
using DAO.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DAO.Repositories;

public class GenericRepository<T>(TuniPlanDbContext context) : IGenericRepository<T> where T : class
{
    protected readonly TuniPlanDbContext Context = context;
    protected DbSet<T> Set => Context.Set<T>();

    public IQueryable<T> Query() => Set;
    public IQueryable<T> QueryNoTracking() => Set.AsNoTracking();

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default) => await Set.FindAsync([id], ct);

    public Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(predicate, ct);

    public Task<List<T>> ListAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default) =>
        (predicate is null ? Set.AsNoTracking() : Set.AsNoTracking().Where(predicate)).ToListAsync(ct);

    public Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) => Set.AnyAsync(predicate, ct);

    public Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default) =>
        predicate is null ? Set.CountAsync(ct) : Set.CountAsync(predicate, ct);

    public async Task AddAsync(T entity, CancellationToken ct = default) => await Set.AddAsync(entity, ct);
    public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default) => Set.AddRangeAsync(entities, ct);
    public void Update(T entity) => Set.Update(entity);
    public void Remove(T entity) => Set.Remove(entity);
    public void RemoveRange(IEnumerable<T> entities) => Set.RemoveRange(entities);
}
