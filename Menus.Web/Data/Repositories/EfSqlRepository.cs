// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

extern alias MySqlConnectorAlias;
using MySqlParameter = MySqlConnectorAlias::MySql.Data.MySqlClient.MySqlParameter;

using Menus.Abstract;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Menus.Web.Data.Repositories
{
    public abstract class EfSqlRepository<TEntity, TInterface> : ISqlRepository<TInterface> where TEntity : class, TInterface, new()
    {
        protected EfSqlRepository(DbContext context)
        {
            Context = context;
        }

        protected DbContext Context;

        public async Task<IEnumerable<TInterface>> FromSql(string sql, IDictionary<string, object> parameters)
        {
            return await Context.Query<TEntity>().FromSql(sql,
                parameters.Select(x => new MySqlParameter(x.Key, x.Value)).ToArray()).ToListAsync();
        }

        public IQueryable<TInterface> From(string sql, IDictionary<string, object> parameters)
        {
            return Context.Query<TEntity>().FromSql(sql,
                parameters.Select(x => new MySqlParameter(x.Key, x.Value)).ToArray());
        }
    }
}
