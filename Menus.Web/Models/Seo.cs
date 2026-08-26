// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;
using Newtonsoft.Json;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Menus.Web.Models
{
    public class Seo : ISeo
    {
        public string Id { get; set; }
        public string ItemId { get; set; }
        public string Url { get; set; }
        public string Header { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Keywords { get; set; }
        public string SeoText { get; set; }
        public string SearchTerms { get; set; }

        [NotMapped]
        private LocalizableString _header
        {
            get
            {
                if (_headerCached != null) return _headerCached;
                var data = JsonConvert.DeserializeObject<LocalizableString>(Header);
                _headerCached = data;
                return data;
            }
            set
            {
                _headerCached = value;
                Header = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _headerCached = null;
        [NotMapped]
        ILocalizableString ISeo.Header { get => _header; set => _header = value as LocalizableString; }

        [NotMapped]
        private LocalizableString _title
        {
            get
            {
                if (_titleCached != null) return _titleCached;
                var data = JsonConvert.DeserializeObject<LocalizableString>(Title);
                _titleCached = data;
                return data;
            }
            set
            {
                _titleCached = value;
                Title = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _titleCached = null;
        [NotMapped]
        ILocalizableString ISeo.Title { get => _title; set => _title = value as LocalizableString; }


        [NotMapped]
        private LocalizableString _description
        {
            get
            {
                if (_descriptionCached != null) return _descriptionCached;
                var data = JsonConvert.DeserializeObject<LocalizableString>(Description);
                _descriptionCached = data;
                return data;
            }
            set
            {
                _descriptionCached = value;
                Description = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _descriptionCached = null;
        [NotMapped]
        ILocalizableString ISeo.Description { get => _description; set => _description = value as LocalizableString; }

        public virtual object Clone()
        {
            throw new InvalidOperationException();
        }
    }
}
