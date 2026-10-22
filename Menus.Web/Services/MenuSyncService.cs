using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using Menus.Abstract.Models;
using Menus.Domain;
using Menus.Web.Data;
using Menus.Web.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Menus.Web.Services
{
    // Пропозиція content-bot (sync_restaurants.py): додати страву з сайту ресторану
    // або прибрати нашу страву, якої на сайті вже немає
    public class MenuSyncProposal
    {
        public string Id { get; set; }
        public string RestaurantId { get; set; }
        public string Kind { get; set; } // add | remove | price
        public string DishId { get; set; }
        public string Name { get; set; }
        public string NameLang { get; set; }
        public string Category { get; set; }
        public decimal? Price { get; set; }
        public decimal? OldPrice { get; set; } // лише для price
        public string Weight { get; set; }
        public string Description { get; set; }
        public string ImageUrl { get; set; }
        public string Status { get; set; } // new | applied | rejected
        public DateTime CreatedAt { get; set; }
    }

    public class MenuSyncRestaurantSummary
    {
        public string RestaurantId { get; set; }
        public string Name { get; set; }
        public int ToAdd { get; set; }
        public int ToRemove { get; set; }
        public int ToChangePrice { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class MenuSyncBranch
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
        public string Website { get; set; }
    }

    public class MenuSyncResult
    {
        public int Added { get; set; }
        public int Removed { get; set; }
        public int Repriced { get; set; }
        public int Branches { get; set; }
    }

    public class MenuSyncService
    {
        // Таблицю створює й content-bot; тут - щоб адмінка працювала й до першого запуску бота
        private const string CreateTableSql = @"
            CREATE TABLE IF NOT EXISTS MenuSyncProposals (
                Id VARCHAR(36) PRIMARY KEY,
                RestaurantId VARCHAR(255) NOT NULL,
                Kind VARCHAR(16) NOT NULL,
                DishId VARCHAR(255) NULL,
                Name TEXT NOT NULL,
                NameLang VARCHAR(2) NULL,
                Category TEXT NULL,
                Price DECIMAL(10,2) NULL,
                Weight VARCHAR(50) NULL,
                Description TEXT NULL,
                ImageUrl TEXT NULL,
                Status VARCHAR(16) NOT NULL DEFAULT 'new',
                CreatedAt DATETIME NOT NULL,
                INDEX IX_MenuSyncProposals_Restaurant (RestaurantId, Status)
            );
            ALTER TABLE MenuSyncProposals ADD COLUMN IF NOT EXISTS OldPrice DECIMAL(10,2) NULL;";

        private readonly Core _core;
        private readonly UrlGenerateService _urls;
        private readonly IDbConnection _connection;

        public MenuSyncService(Core core, UrlGenerateService urls, EfDbContext dbContext)
        {
            _core = core;
            _urls = urls;
            _connection = dbContext.Database.GetDbConnection();
        }

        public async Task<List<MenuSyncRestaurantSummary>> GetPendingSummary()
        {
            await _connection.ExecuteAsync(CreateTableSql);
            var rows = await _connection.QueryAsync<MenuSyncRestaurantSummary>(@"
                SELECT p.RestaurantId, r.Name,
                       CAST(SUM(p.Kind = 'add') AS SIGNED) AS ToAdd, CAST(SUM(p.Kind = 'remove') AS SIGNED) AS ToRemove,
                       CAST(SUM(p.Kind = 'price') AS SIGNED) AS ToChangePrice,
                       MAX(p.CreatedAt) AS CreatedAt
                FROM MenuSyncProposals p
                JOIN Restaurants r ON r.Id = p.RestaurantId
                WHERE p.Status = 'new'
                GROUP BY p.RestaurantId, r.Name
                ORDER BY MAX(p.CreatedAt) DESC");
            return rows.ToList();
        }

        public async Task<List<MenuSyncProposal>> GetPendingForRestaurant(string restaurantId)
        {
            await _connection.ExecuteAsync(CreateTableSql);
            var rows = await _connection.QueryAsync<MenuSyncProposal>(
                "SELECT * FROM MenuSyncProposals WHERE RestaurantId = @restaurantId AND Status = 'new' ORDER BY Kind, Category, Name",
                new { restaurantId });
            return rows.ToList();
        }

        public async Task Reject(string restaurantId, IEnumerable<string> ids)
        {
            await _connection.ExecuteAsync(
                "UPDATE MenuSyncProposals SET Status = 'rejected' WHERE RestaurantId = @restaurantId AND Status = 'new' AND Id IN @ids",
                new { restaurantId, ids = ids.ToList() });
        }

        // Філії мережі: інші активні ресторани з тим самим сайтом (pesto-family.com - 8 філій Pesto Cafe)
        public async Task<List<MenuSyncBranch>> GetBranches(string restaurantId)
        {
            var all = (await _connection.QueryAsync<MenuSyncBranch>(@"
                SELECT r.Id, r.Name, r.Address, ri.Website
                FROM Restaurants r JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
                WHERE r.Status = 0 AND ri.Website IS NOT NULL AND ri.Website <> ''")).ToList();
            var own = all.FirstOrDefault(x => x.Id == restaurantId);
            if (own == null) return new List<MenuSyncBranch>();
            var site = NormalizeSite(own.Website);
            return all.Where(x => x.Id != restaurantId && NormalizeSite(x.Website) == site).OrderBy(x => x.Address).ToList();
        }

        public async Task<MenuSyncResult> Apply(string restaurantId, IEnumerable<string> ids, bool includeBranches)
        {
            var idList = ids.ToList();
            var proposals = (await GetPendingForRestaurant(restaurantId)).Where(x => idList.Contains(x.Id)).ToList();
            var result = new MenuSyncResult();

            await ApplyToRestaurant(restaurantId, proposals, result, byDishId: true);
            await MarkApplied(proposals.Select(x => x.Id));

            if (includeBranches)
            {
                foreach (var branch in await GetBranches(restaurantId))
                {
                    await ApplyToRestaurant(branch.Id, proposals, result, byDishId: false);
                    // власні пропозиції філії з тими ж стравами більше не потрібні
                    var branchProposals = await GetPendingForRestaurant(branch.Id);
                    var keys = new HashSet<string>(proposals.Select(x => x.Kind + "|" + Normalize(x.Name)));
                    await MarkApplied(branchProposals.Where(x => keys.Contains(x.Kind + "|" + Normalize(x.Name))).Select(x => x.Id));
                    result.Branches++;
                }
            }

            return result;
        }

        // Для свого ресторану страва береться за DishId пропозиції, для філій - за назвою будь-якою мовою
        private async Task ApplyToRestaurant(string restaurantId, List<MenuSyncProposal> proposals, MenuSyncResult result, bool byDishId)
        {
            var categories = (await _core.Restaurants.GetAllMenuCategoriesForRestaurant(restaurantId)).ToList();
            var dishesByName = byDishId ? null : await GetActiveDishesByName(restaurantId);

            foreach (var proposal in proposals)
            {
                var dishId = byDishId ? proposal.DishId : dishesByName.TryGetValue(Normalize(proposal.Name), out var found) ? found : null;

                if (proposal.Kind == "add")
                {
                    if (!byDishId && dishId != null) continue; // у філії така страва вже є
                    var category = await FindOrCreateCategory(restaurantId, proposal, categories);
                    await AddDish(restaurantId, proposal, category);
                    result.Added++;
                }
                else if (proposal.Kind == "remove" && dishId != null)
                {
                    // не видаляємо: Pending ховає страву з сайту, повернути можна в адмінці ресторану
                    result.Removed += await _connection.ExecuteAsync(
                        "UPDATE Dishes SET Status = @status WHERE Id = @dishId AND RestaurantId = @restaurantId",
                        new { status = (int)DishStatus.Pending, dishId, restaurantId });
                }
                else if (proposal.Kind == "price" && dishId != null && proposal.Price > 0)
                {
                    var changed = await _connection.ExecuteAsync(
                        "UPDATE Dishes SET Price = @price WHERE Id = @dishId AND RestaurantId = @restaurantId AND IsPriceMultiple = 0",
                        new { price = proposal.Price, dishId, restaurantId });
                    if (changed > 0)
                    {
                        await _connection.ExecuteAsync("UPDATE DishVariations SET Price = @price WHERE DishId = @dishId",
                            new { price = proposal.Price, dishId });
                        result.Repriced++;
                    }
                }
            }
        }

        private async Task<Dictionary<string, string>> GetActiveDishesByName(string restaurantId)
        {
            var rows = await _connection.QueryAsync<DishNameRow>(
                "SELECT Id, Name FROM Dishes WHERE RestaurantId = @restaurantId AND Status = 0", new { restaurantId });
            var result = new Dictionary<string, string>();
            foreach (var row in rows)
            {
                LocalizableString name;
                try { name = JsonConvert.DeserializeObject<LocalizableString>(row.Name ?? ""); }
                catch (JsonException) { continue; }
                foreach (var value in name?.Values ?? new List<LocalizableStringValue>())
                {
                    var key = Normalize(value.Value);
                    if (key.Length > 0 && !result.ContainsKey(key)) result[key] = row.Id;
                }
            }
            return result;
        }

        private class DishNameRow
        {
            public string Id { get; set; }
            public string Name { get; set; }
        }

        private async Task MarkApplied(IEnumerable<string> ids)
        {
            var list = ids.ToList();
            if (list.Any())
                await _connection.ExecuteAsync("UPDATE MenuSyncProposals SET Status = 'applied' WHERE Id IN @list", new { list });
        }

        private static string NormalizeSite(string url) =>
            Regex.Replace((url ?? "").Trim().ToLowerInvariant(), @"^https?://(www\.)?|[?#].*$", "").TrimEnd('/');

        private async Task AddDish(string restaurantId, MenuSyncProposal proposal, IMenuCategory category)
        {
            var lang = string.IsNullOrEmpty(proposal.NameLang) ? "uk" : proposal.NameLang;
            var weight = ParseWeight(proposal.Weight, out var unit);

            var dish = (IDish)new Dish();
            dish.Id = Guid.NewGuid().ToString();
            dish.RestaurantId = restaurantId;
            dish.Name = Localizable(lang, proposal.Name);
            dish.Description = string.IsNullOrWhiteSpace(proposal.Description) ? new LocalizableString() : Localizable(lang, proposal.Description);
            dish.Ingredients = new LocalizableString();
            dish.ShortDescription = new LocalizableString();
            dish.Photos = new List<string>(); // фото допише бот при наступній перевірці ресторану
            dish.Price = proposal.Price ?? 0;
            dish.Weight = weight.ToString();
            dish.WeightType = unit;
            dish.IsPriceMultiple = false;
            dish.Status = DishStatus.Active;
            await _core.Dishes.Create(dish);

            await _core.Dishes.CreateDishVariations(new List<IDishVariation>
            {
                new DishVariation { Id = Guid.NewGuid().ToString(), DishId = dish.Id, Price = dish.Price, Weight = weight, Type = unit }
            });

            if (category != null)
            {
                await _core.Dishes.AddDishToCategory(new DishCategory { Id = Guid.NewGuid().ToString(), DishId = dish.Id, MenuCategoryId = category.Id });
            }

            var seo = new DishSeo() as IDishSeo;
            seo.Id = Guid.NewGuid().ToString();
            seo.ItemId = dish.Id;
            seo.RestaurantId = restaurantId;
            seo.Url = await _urls.GetNewUrlForDish(seo, dish);
            seo.SearchTerms = dish.Name.GetAll() + " " + UrlGenerateService.TranslitWithCaseSave(dish.Name.Get());
            await _core.Dishes.CreateSeo(seo);
        }

        // Категорія з такою ж назвою (будь-якою мовою); якщо немає - нова підкатегорія
        // під першою верхньою категорією ресторану, як це робить кабінет ресторатора
        private async Task<IMenuCategory> FindOrCreateCategory(string restaurantId, MenuSyncProposal proposal, List<IMenuCategory> categories)
        {
            if (string.IsNullOrWhiteSpace(proposal.Category)) return categories.FirstOrDefault(x => !x.IsTopLevelCategory);

            var wanted = Normalize(proposal.Category);
            var existing = categories.FirstOrDefault(x => !x.IsTopLevelCategory &&
                new[] { "uk", "ru", "en" }.Any(l => Normalize(x.Name.Get(l)) == wanted));
            if (existing != null) return existing;

            var lang = string.IsNullOrEmpty(proposal.NameLang) ? "uk" : proposal.NameLang;
            var category = new MenuCategory() as IMenuCategory;
            category.Id = Guid.NewGuid().ToString();
            category.RestaurantId = restaurantId;
            category.IsTopLevelCategory = false;
            category.Index = categories.Count(x => !x.IsTopLevelCategory);
            category.Name = Localizable(lang, proposal.Category.Trim().ToLowerInvariant());
            category.Description = new LocalizableString();
            category.Active = true;
            await _core.Restaurants.CreateMenuCategory(category);

            var parent = categories.Where(x => x.IsTopLevelCategory).OrderBy(x => x.Index).FirstOrDefault();
            if (parent != null)
            {
                await _connection.ExecuteAsync("INSERT INTO MenuCategoryRelations (ParentId, ChildId) VALUES (@ParentId, @ChildId)",
                    new { ParentId = parent.Id, ChildId = category.Id });
            }

            categories.Add(category);
            return category;
        }

        private static LocalizableString Localizable(string locale, string value) =>
            new LocalizableString { Values = new List<LocalizableStringValue> { new LocalizableStringValue { Locale = locale, Value = value.Trim() } } };

        private static string Normalize(string value) =>
            Regex.Replace((value ?? "").ToLowerInvariant().Replace('ё', 'е'), @"[^\p{L}\p{N}]+", " ").Trim();

        // "250 г", "0,5 л", "300ml" -> число й одиниця; без ваги - 0 і пробіл, як у кабінеті ресторатора
        private static int ParseWeight(string value, out string unit)
        {
            unit = " ";
            var match = Regex.Match(value ?? "", @"(\d+(?:[.,]\d+)?)\s*([^\d\s]*)");
            if (!match.Success) return 0;

            unit = string.IsNullOrEmpty(match.Groups[2].Value) ? " " : match.Groups[2].Value;
            var number = double.Parse(match.Groups[1].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
            // вага в базі - ціле число: «0,5 л» зберігаємо як «500 мл»
            if (number < 10 && (unit == "л" || unit == "l" || unit == "кг" || unit == "kg"))
            {
                number *= 1000;
                unit = unit == "л" ? "мл" : unit == "l" ? "ml" : unit == "кг" ? "г" : "g";
            }
            return (int)Math.Round(number);
        }
    }
}
