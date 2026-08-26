// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

extern alias MySqlConnectorAlias;
using MySqlParameter = MySqlConnectorAlias::MySql.Data.MySqlClient.MySqlParameter;

using Menus.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Menus.Web.Data.Repositories
{
    public abstract class EfRepository<TEntity, TInterface> : IRepository<TInterface> where TEntity : class, TInterface, new()
    {
        protected EfRepository(DbContext context)
        {
            _context = context;
            context.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
        }

        protected DbContext _context;

        public Type ElementType => typeof(TEntity);

        public virtual Expression Expression => _context.Set<TEntity>().Cast<TInterface>().Expression;

        public virtual IQueryProvider Provider => _context.Set<TEntity>().Cast<TInterface>().Provider;

        public TInterface Create()
        {
            return new TEntity();
        }

        public virtual async Task<IEnumerable<TInterface>> FromSql(string sql, IDictionary<string, object> parameters, bool isQuery = true)
        {
            if (isQuery) return await _context.Query<TEntity>().AsNoTracking().FromSql(sql,
                parameters.Select(x => new MySqlParameter(x.Key, x.Value)).ToArray()).Cast<TInterface>().ToListAsync();
            return await _context.Set<TEntity>().AsNoTracking().FromSql(sql,
                parameters.Select(x => new MySqlParameter(x.Key, x.Value)).ToArray()).Cast<TInterface>().ToListAsync();
        }

        public virtual async Task Add(TInterface entity)
        {
            _context.Add(entity as TEntity);
            await _context.SaveChangesAsync();
        }

        public virtual async Task Add(IEnumerable<TInterface> entities)
        {
            _context.AddRange(entities.Cast<TEntity>());
            await _context.SaveChangesAsync();
        }

        public virtual async Task Remove(TInterface entity)
        {
            _context.Entry(entity as TEntity).State = EntityState.Deleted;
            await _context.SaveChangesAsync();
        }

        public virtual async Task Remove(IEnumerable<TInterface> entities)
        {
            foreach (var item in entities)
            {
                _context.Entry(item as TEntity).State = EntityState.Deleted;
            }
            await _context.SaveChangesAsync();
        }

        public virtual async Task Update(TInterface entity)
        {
            _context.Entry(entity as TEntity).State = EntityState.Modified;
            await _context.SaveChangesAsync();
        }

        public virtual async Task Update(IEnumerable<TInterface> entities)
        {
            foreach (var item in entities)
            {
                _context.Entry(item as TEntity).State = EntityState.Modified;
            }
            await _context.SaveChangesAsync();
        }

        public virtual IEnumerator<TInterface> GetEnumerator()
        {
            return _context.Set<TEntity>().Cast<TInterface>().GetEnumerator();
        }

        public virtual IEnumerator GetDefaultEnumerator()
        {
            return _context.Set<TEntity>().Cast<TInterface>().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetDefaultEnumerator();
        }
    }
}
