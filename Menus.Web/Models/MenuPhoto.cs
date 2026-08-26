// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Menus.Abstract.Models;
using Newtonsoft.Json;

namespace Menus.Web.Models
{
    public class MenuPhoto : IMenuPhoto
    {
        public string Id { get; set; }
        public string RestaurantId { get; set; }
        public string UserId { get; set; }
        public DateTime Timestamp { get; set; }
        public string Photos { get; set; }
        public int Index { get; set; }

        [NotMapped]
        private IEnumerable<string> _photos;
        [NotMapped]
        IEnumerable<string> IMenuPhoto.Photos
        {
            get
            {
                if (_photos != null) return _photos;
                try
                {
                    _photos = (string.IsNullOrEmpty(Photos)
                        ? new List<string>()
                        : JsonConvert.DeserializeObject<IEnumerable<string>>(Photos));
                }
                catch (JsonReaderException)
                {
                    _photos = new List<string> { Photos };
                }

                return _photos;
            }
            set
            {
                _photos = value;
                Photos = JsonConvert.SerializeObject(value);
            }
        }
    }
}
