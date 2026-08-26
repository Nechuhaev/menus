using System;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Newtonsoft.Json;

namespace Menus.Web.Helpers
{
    public static class EfModelConfigfExtensions
    {
        public static PropertyBuilder<TProperty> HasJsonConversion<TProperty>(this PropertyBuilder<TProperty> pb)
        {
            return pb.HasConversion(
                d => JsonConvert.SerializeObject(d),
                m => string.IsNullOrEmpty(m) ? Activator.CreateInstance<TProperty>() : JsonConvert.DeserializeObject<TProperty>(m)
            );
        }
    }
}