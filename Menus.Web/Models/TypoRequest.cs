// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Abstract.Models;

namespace Menus.Web.Models
{
    public class TypoRequest : ITypoRequest
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string Text { get; set; }
        public string Url { get; set; }
        public string Email { get; set; }
        public DateTime CreateDate { get; set; }
        public TypoRequestStatus Status { get; set; }
    }
}
