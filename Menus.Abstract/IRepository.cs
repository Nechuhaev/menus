// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Menus.Abstract
{
    public interface IRepository<TEntity> : IEnumerable<TEntity>, IQueryable<TEntity>
    {
        Task Add(TEntity entity);
        Task Add(IEnumerable<TEntity> entities);
        Task Remove(TEntity entity);
        Task Remove(IEnumerable<TEntity> entities);
        Task Update(TEntity entity);
        Task Update(IEnumerable<TEntity> entities);
        Task<IEnumerable<TEntity>> FromSql(string sql, IDictionary<string, object> parameters, bool isQuery = true);
        TEntity Create();
    }
}