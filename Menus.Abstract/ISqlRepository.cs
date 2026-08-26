using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Menus.Abstract
{
    public interface ISqlRepository<TEntity>
    {
        Task<IEnumerable<TEntity>> FromSql(string sql, IDictionary<string, object> parameters);

        IQueryable<TEntity> From(string sql, IDictionary<string, object> parameters);
    }
}
