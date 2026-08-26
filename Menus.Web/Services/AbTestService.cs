
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Web.Data;
using Menus.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Services
{
    public class AbTestService
    {
        private readonly EfDbContext _dbContext;
        private static ConcurrentDictionary<string, AbTest> _abTests;

        public AbTestService(EfDbContext dbContext)
        {
            _dbContext = dbContext;
            if (_abTests == null)
            {
                _abTests = new ConcurrentDictionary<string, AbTest>(dbContext.AbTests.Include(x => x.Parameters).AsNoTracking()
                .Select(x => new KeyValuePair<string, AbTest>(x.Name, x)));
            }
        }

        public async Task<AbTestParameter> FireParameterForTest(AbTest test)
        {
            var smallestParameterFired = test.Parameters.Min(x => x.TimesFired);
            var smallestParameter = test.Parameters.FirstOrDefault(x => x.TimesFired == smallestParameterFired);

            if (smallestParameter == null) return null;

            smallestParameter.TimesFired++;
            _dbContext.Entry(smallestParameter).State = EntityState.Modified;
            await _dbContext.SaveChangesAsync();
            return smallestParameter;
        }

        public AbTest GetTestByName(string name)
        {
            if (_abTests.ContainsKey(name))
            {
                return _abTests[name];
            } else
            {
                return null;
            }
        }

        public async Task CreateTest(AbTest test)
        {
            _abTests.GetOrAdd(test.Name, test);
            _dbContext.AbTests.Add(test);
            await _dbContext.SaveChangesAsync();
        }

        public async Task UpdateTest(AbTest test)
        {
            _dbContext.Entry(test).State = EntityState.Modified;
            await _dbContext.SaveChangesAsync();
        }

        public async Task RemoveTest(AbTest test)
        {
            _abTests.Remove(test.Name, out test);
            _dbContext.AbTests.Remove(test);
            await _dbContext.SaveChangesAsync();
        }
    }
}
