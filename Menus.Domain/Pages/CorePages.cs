// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Menus.Abstract;
using Menus.Abstract.Models;

namespace Menus.Domain.Pages
{
    public class CorePages
    {
        private IDbContext _db;
        private Core _core;
        public CorePages(IDbContext db, Core core)
        {
            _db = db;
            _core = core;
        }

        public async Task Create(IPage page)
        {
            await _db.Pages.Add(page);
        }

        public async Task<IEnumerable<IPage>> GetAll()
        {
            return await Task.Run(() => _db.Pages);
        }

        public async Task<IEnumerable<IPage>> GetPages()
        {
            return await Task.Run(() => _db.Pages.Where(x => x.Category == PageCategory.Content));
        }

        public async Task<IEnumerable<IPage>> GetBlog()
        {
            return await Task.Run(() => _db.Pages.Where(x => x.Category == PageCategory.News));
        }

        public async Task<IEnumerable<IPage>> Search(string query, PageStatus? status, PageCategory? category)
        {
            IEnumerable<IPage> result = _db.Pages;
            query = query?.ToLower();

            if (status != null)
            {
                result = (IQueryable<IPage>)result.Where(x =>
                    x.Status == status
                );
            }

            if (category != null)
            {
                result = (IQueryable<IPage>)result.Where(x =>
                    x.Category == category
                );
            }

            var result1 = result; // Solves "Access to modified closure"
            result = await Task.Run(() => result1.ToList());

            if (!string.IsNullOrEmpty(query))
            {
                result = result.Where(x =>
                    x.Title.Contains(query)
                );
            }

            return result;
        }

        public async Task<IPage> GetById(string id)
        {
            return await Task.Run(() => _db.Pages.FirstOrDefault(x => x.Id == id));
        }

        public async Task<IPage> GetByUrl(string url)
        {
            return await Task.Run(() => _db.Pages.FirstOrDefault(x => x.Url == url));
        }

        public async Task<IPage> GetPublishedByUrl(string url, PageCategory category)
        {
            return await Task.Run(() => _db.Pages.FirstOrDefault(x => x.Url == url && x.Status == PageStatus.Published && x.Category == category));
        }

        public async Task Update(IPage page)
        {
            await _db.Pages.Update(page);
        }

        public async Task Delete(string id)
        {
            var page = await GetById(id);
            await _db.Pages.Remove(page);
        }
    }
}
