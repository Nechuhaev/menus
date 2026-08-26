// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

extern alias MySqlConnectorAlias;
using MySqlParameter = MySqlConnectorAlias::MySql.Data.MySqlClient.MySqlParameter;

using System;
using Menus.Abstract;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;

namespace Menus.Web.Data.Repositories
{
    public abstract class EfProcedureRepository<TEntity, TInterface> : IProcedureRepository<TInterface> where TEntity : class, TInterface, new()
    {
        protected EfProcedureRepository(DbContext context, string sql, string singleSql = "")
        {
            _context = context;
            _sql = sql;
            _singleSql = singleSql;
        }

        protected DbContext _context;
        protected string _sql;
        protected string _singleSql;

        public IQueryable<TInterface> Get(IDictionary<string, object> parameters)
        {
            var result = _context.Query<TEntity>().FromSql(_sql,
                parameters.Select(x => new MySqlParameter(x.Key, x.Value)).ToArray());

            return result;
        }

        public TInterface GetSingle(IDictionary<string, object> parameters)
        {
            if(string.IsNullOrEmpty(_singleSql)) throw new InvalidOperationException();
            
            var result = _context.Query<TEntity>().FromSql(_singleSql,
                parameters.Select(x => new MySqlParameter(x.Key, x.Value)).ToArray());

            return result.FirstOrDefault();
        }
    }
}
