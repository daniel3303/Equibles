using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Equibles.Data;

public abstract class BaseRepository<TEntity> where TEntity : class {
    protected readonly EquiblesDbContext DbContext;

    protected BaseRepository(EquiblesDbContext dbContext) {
        DbContext = dbContext;
    }

    public virtual async Task<TEntity> Get(params object[] key) {
        return await DbContext.Set<TEntity>().FindAsync(key);
    }

    public virtual IQueryable<TEntity> GetAll() {
        return DbContext.Set<TEntity>().AsQueryable();
    }

    public virtual TEntity Add(TEntity entity) {
        DbContext.Set<TEntity>().Add(entity);
        return entity;
    }

    public virtual void AddRange(IEnumerable<TEntity> entities) {
        DbContext.Set<TEntity>().AddRange(entities);
    }

    public virtual void Update(TEntity entity) {
        DbContext.Set<TEntity>().Update(entity);
    }

    public virtual void Delete(TEntity entity) {
        DbContext.Set<TEntity>().Remove(entity);
    }

    public virtual void Delete(IEnumerable<TEntity> entities) {
        foreach (var entity in entities) {
            Delete(entity);
        }
    }

    protected DbSet<TEntity> GetDbSet() {
        return DbContext.Set<TEntity>();
    }

    protected EquiblesDbContext GetDbContext() {
        return DbContext;
    }

    public async Task ExecuteDeleteAll(CancellationToken cancellationToken = default) {
        await DbContext.Set<TEntity>().ExecuteDeleteAsync(cancellationToken);
    }

    public void ClearChangeTracker() {
        DbContext.ChangeTracker.Clear();
    }

    public virtual Task SaveChanges() {
        return DbContext.SaveChangesAsync();
    }

    public async Task<IDbContextTransaction> CreateTransaction(IsolationLevel isolationLevel, CancellationToken cancellationToken = new CancellationToken()) {
        return await DbContext.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
    }

    public IDbContextTransaction GetCurrentTransaction() {
        return DbContext.Database.CurrentTransaction;
    }

    public bool HasActiveTransaction() {
        return DbContext.Database.CurrentTransaction != null;
    }
}
