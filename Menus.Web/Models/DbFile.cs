// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;

namespace Menus.Web.Models
{
    public class DbFile : IDbFile
    {
        public string Id { get; set; }
        public string Mime { get; set; }
        public string CdnLink { get; set; }
        public int Size { get; set; }
        public byte[] Data { get; set; }
    }
}
