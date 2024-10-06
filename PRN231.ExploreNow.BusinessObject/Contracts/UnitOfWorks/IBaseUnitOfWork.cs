﻿using PRN231.ExploreNow.BusinessObject.Contracts.Repositories;
using PRN231.ExploreNow.BusinessObject.Entities;

namespace PRN231.ExploreNow.BusinessObject.Contracts.UnitOfWorks;

public interface IBaseUnitOfWork : IDisposable
{
    IBaseRepository<TEntity> GetRepositoryByEntity<TEntity>() where TEntity : BaseEntity;

    TRepository GetRepository<TRepository>() where TRepository : IBaseRepository;

    Task<bool> SaveChangesAsync(CancellationToken cancellationToken = default);
}