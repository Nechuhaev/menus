using System.Collections.Generic;
using System.Linq;
using Menus.Abstract.Models;
using Menus.Web.Models;

namespace Menus.Web.Helpers
{
    public static class DishSearchFormatter
    {
        // Якщо назва страви не містить пошукового запиту (знайдено за категорією),
        // показуємо в результатах "Категорія: Страва", щоб було видно, чому страва в списку
        public static IEnumerable<ICompleteDish> AddCategoryToNames(IEnumerable<ICompleteDish> result, string search)
        {
            foreach (ICompleteDish dish in result)
            {
                if (!dish.Name.GetAll().ToLower().Contains(search.ToLower()))
                {
                    List<LocalizableStringValue> newValues = new List<LocalizableStringValue>();

                    foreach (LocalizableStringValue locStrig in dish.Name.Values)
                    {
                        string locale = locStrig.Locale;
                        string categoryName = dish.CategoryName.Get(locale);
                        string dishName = dish.Name.Get(locale);
                        if (!string.IsNullOrEmpty(categoryName))
                        {
                            LocalizableStringValue value = new LocalizableStringValue
                            {
                                Locale = locale,
                                Value = $"{categoryName}: {dish.Name.Get(locale)}"
                            };
                            newValues.Add(value);
                        }
                        else
                        {
                            newValues.Add(locStrig);
                        }
                    }

                    dish.Name.Replace(newValues);
                }
            }

            return result;
        }
    }
}
