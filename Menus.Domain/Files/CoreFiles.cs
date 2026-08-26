// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract;
using Menus.Abstract.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Menus.Domain.Files
{
    public class CoreFiles
    {
        private IDbContext _db;
        private Core _core;
        public CoreFiles(IDbContext db, Core core)
        {
            _db = db;
            _core = core;
        }

        public async Task Create(IDbFile file)
        {
            var base64 = $"{file.Id};data:{file.Mime};base64,{Convert.ToBase64String(file.Data)}";
            var http = new HttpClient();
            await http.PutAsync(_core.Settings.ImgServiceUrl, new StringContent(base64));
            http.Dispose();

            file.Data = new byte[0];
            file.CdnLink = file.Id;

            await _db.Files.Add(file);
        }

        public async Task Update(IDbFile file)
        {
            await _db.Files.Update(file);
        }

        public async Task Delete(string id)
        {
            var restaurant = await GetFileById(id);

            await _db.Files.Remove(restaurant);
        }

        public async Task<IDbFile> GetFileById(string id)
        {
            return await Task.Run(() => _db.Files.FirstOrDefault(x => x.Id == id));
        }
    }
}
