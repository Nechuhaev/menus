// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.ComponentModel.DataAnnotations.Schema;
using Menus.Abstract.Models;
using Newtonsoft.Json;

namespace Menus.Web.Models
{
    public class Page : IPage
    {
        public string Id { get; set; }
        public string Photo { get; set; }
        public string Title { get; set; }
        public string ShortContent { get; set; }
        public string Content { get; set; }
        public PageCategory Category { get; set; }
        public PageStatus Status { get; set; }
        public DateTime Timestamp { get; set; }
        public string Url { get; set; }
        public string SeoTitle { get; set; }
        public string SeoDescription { get; set; }
        public string SeoKeywords { get; set; }
        public string SeoText { get; set; }


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
        private LocalizableString _titleCached;
        [NotMapped]
        ILocalizableString IPage.Title
        {
            get => _title; set => _title = value as LocalizableString;
        }


        [NotMapped]
        private LocalizableString _shortContent
        {
            get
            {
                if (_shortContentCached != null) return _shortContentCached;
                var data = JsonConvert.DeserializeObject<LocalizableString>(ShortContent);
                _shortContentCached = data;
                return data;
            }
            set
            {
                _shortContentCached = value;
                ShortContent = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _shortContentCached;
        [NotMapped]
        ILocalizableString IPage.ShortContent
        {
            get => _shortContent; set => _shortContent = value as LocalizableString;
        }


        [NotMapped]
        private LocalizableString _content
        {
            get
            {
                if (_contentCached != null) return _contentCached;
                var data = JsonConvert.DeserializeObject<LocalizableString>(Content);
                _contentCached = data;
                return data;
            }
            set
            {
                _contentCached = value;
                Content = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _contentCached;
        [NotMapped]
        ILocalizableString IPage.Content
        {
            get => _content; set => _content = value as LocalizableString;
        }


        [NotMapped]
        private LocalizableString _seoTitle
        {
            get
            {
                if (_seoTitleCached != null) return _seoTitleCached;
                var data = JsonConvert.DeserializeObject<LocalizableString>(SeoTitle);
                _seoTitleCached = data;
                return data;
            }
            set
            {
                _seoTitleCached = value;
                SeoTitle = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _seoTitleCached;
        [NotMapped]
        ILocalizableString IPage.SeoTitle
        {
            get => _seoTitle; set => _seoTitle = value as LocalizableString;
        }



        [NotMapped]
        private LocalizableString _seoDescription
        {
            get
            {
                if (_seoDescriptionCached != null) return _seoDescriptionCached;
                var data = JsonConvert.DeserializeObject<LocalizableString>(SeoDescription);
                _seoDescriptionCached = data;
                return data;
            }
            set
            {
                _seoDescriptionCached = value;
                SeoDescription = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _seoDescriptionCached;
        [NotMapped]
        ILocalizableString IPage.SeoDescription
        {
            get => _seoDescription; set => _seoDescription = value as LocalizableString;
        }



        [NotMapped]
        private LocalizableString _seoKeywords
        {
            get
            {
                if (_seoKeywordsCached != null) return _seoKeywordsCached;
                var data = JsonConvert.DeserializeObject<LocalizableString>(SeoKeywords);
                _seoKeywordsCached = data;
                return data;
            }
            set
            {
                _seoKeywordsCached = value;
                SeoKeywords = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _seoKeywordsCached;
        [NotMapped]
        ILocalizableString IPage.SeoKeywords
        {
            get => _seoKeywords; set => _seoKeywords = value as LocalizableString;
        }



        [NotMapped]
        private LocalizableString _seoText
        {
            get
            {
                if (_seoTextCached != null) return _seoTextCached;
                var data = JsonConvert.DeserializeObject<LocalizableString>(SeoText);
                _seoTextCached = data;
                return data;
            }
            set
            {
                _seoTextCached = value;
                SeoText = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _seoTextCached;
        [NotMapped]
        ILocalizableString IPage.SeoText
        {
            get => _seoText; set => _seoText = value as LocalizableString;
        }
    }
}
