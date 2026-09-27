using System.Linq.Expressions;
using Entities.Base;

namespace DAO.Interfaces;

/// <summary>Generic Repository pattern: common CRUD for every entity.</summary>
public interface IGenericRepository<T> where T : class
{
    /// <summary>Tracked query (for updates). Use <see cref="QueryNoTracking"/> for read-only lists.</summary>
    IQueryable<T> Query();
    IQueryable<T> QueryNoTracking();

    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    Task<List<T>> ListAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);
    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);

    Task AddAsync(T entity, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);
    void Update(T entity);
    void Remove(T entity);
    void RemoveRange(IEnumerable<T> entities);
}
