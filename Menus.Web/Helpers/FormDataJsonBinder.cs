using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Newtonsoft.Json;

namespace Menus.Web.Helpers
{
    public class FormDataJsonBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext == null) throw new ArgumentNullException(nameof(bindingContext));

            // Fetch the value of the argument by name and set it to the model state
            string fieldName = bindingContext.FieldName;
            var valueProviderResult = bindingContext.ValueProvider.GetValue(fieldName);
            if (valueProviderResult == ValueProviderResult.None) return Task.CompletedTask;
            else bindingContext.ModelState.SetModelValue(fieldName, valueProviderResult);

            // Do nothing if the value is null or empty
            string value = valueProviderResult.FirstValue;
            if (string.IsNullOrEmpty(value)) return Task.CompletedTask;

            try
            {
                // Deserialize the provided value and set the binding result
                object result = JsonConvert.DeserializeObject(value, bindingContext.ModelType);
                bindingContext.Result = ModelBindingResult.Success(result);
            }
            catch (JsonException)
            {
                bindingContext.Result = ModelBindingResult.Failed();
            }

            return Task.CompletedTask;
        }
    }

    public class FormDataJsonBinderProvider : IModelBinderProvider
    {
        public IModelBinder GetBinder(ModelBinderProviderContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Do not use this provider for binding simple values
            if (!context.Metadata.IsComplexType) return null;

            // Do not use this provider if the binding target is not a property
            var propName = context.Metadata.PropertyName;
            var propInfo = context.Metadata.ElementType == null ? context.Metadata.ContainerType?.GetProperty(propName) : context.Metadata.ContainerType?.GetProperty(propName, context.Metadata.ElementType);
            if (propName == null || propInfo == null) return null;

            // Do not use this provider if the target property type implements IFormFile
            if (propInfo.PropertyType.IsAssignableFrom(typeof(IFormFile))) return null;

            // Do not use this provider if this property does not have the FromForm attribute
            if (!propInfo.GetCustomAttributes(typeof(FromFormAttribute), false).Any()) return null;

            // All criteria met; use the FormDataJsonBinder
            return new FormDataJsonBinder();
        }
    }
}