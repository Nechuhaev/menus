using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Menus.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Services
{
    public class MenuLinkProposal
    {
        public int Id { get; set; }
        public string RestaurantId { get; set; }
        public string RestaurantName { get; set; }
        public string Address { get; set; }
        public string Platform { get; set; }
        public string Url { get; set; }
        public string PageTitle { get; set; }
        public string PageDescription { get; set; }
        public decimal NameScore { get; set; }
        public bool Recommended { get; set; }
        public string Reason { get; set; }
    }

    // Пропозиції посилань на меню в Expirenza/ChoiceQR. Таблицю наповнює content-bot/find_menu_links.py,
    // адмін підтверджує посилання (воно йде в RestaurantMenuLinks) або відхиляє у «Звітах»
    public class MenuLinkProposalService
    {
        private const int StatusNew = 0;
        private const int StatusApproved = 1;
        private const int StatusRejected = 2;

        private const string CreateTableSql = @"
            CREATE TABLE IF NOT EXISTS MenuLinkProposals (
                Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                RestaurantId VARCHAR(255) NOT NULL,
                Platform VARCHAR(20) NOT NULL,
                Url VARCHAR(255) NOT NULL,
                PageTitle VARCHAR(500) NOT NULL,
                PageDescription TEXT NOT NULL,
                NameScore DECIMAL(4,2) NOT NULL,
                Recommended TINYINT(1) NOT NULL DEFAULT 0,
                Reason VARCHAR(255) NOT NULL,
                Status TINYINT NOT NULL DEFAULT 0,
                CreatedAt DATETIME NOT NULL,
                UNIQUE KEY UX_MenuLinkProposals_Restaurant_Url (RestaurantId, Url),
                KEY IX_MenuLinkProposals_Status (Status)
            ) DEFAULT CHARSET=utf8mb4";

        // Нові пропозиції ресторанів, у яких посилання ще немає (вказане вручну в картці теж рахується)
        private const string PendingWhere = @"
            FROM MenuLinkProposals p
            JOIN Restaurants r ON r.Id = p.RestaurantId
            LEFT JOIN RestaurantMenuLinks ml ON ml.RestaurantId = p.RestaurantId
            WHERE p.Status = 0 AND r.Status = 0 AND ml.Url IS NULL";

        private readonly IDbConnection _connection;
        private readonly RestaurantMenuLinkService _menuLinks;

        public MenuLinkProposalService(EfDbContext dbContext, RestaurantMenuLinkService menuLinks)
        {
            _connection = dbContext.Database.GetDbConnection();
            _menuLinks = menuLinks;
        }

        private async Task EnsureTables()
        {
            await _menuLinks.Get("-"); // створює RestaurantMenuLinks, якщо її ще немає
            await _connection.ExecuteAsync(CreateTableSql);
        }

        public async Task<int> GetPendingRestaurantsCount()
        {
            await EnsureTables();
            return await _connection.ExecuteScalarAsync<int>("SELECT COUNT(DISTINCT p.RestaurantId) " + PendingWhere);
        }

        // Рекомендовані ресторани першими, у ресторані - рекомендоване й найсхожіше першим
        public async Task<List<MenuLinkProposal>> GetPending()
        {
            await EnsureTables();
            var rows = await _connection.QueryAsync<MenuLinkProposal>(@"
                SELECT p.Id, p.RestaurantId, r.Name AS RestaurantName, r.Address, p.Platform, p.Url, p.PageTitle,
                       p.PageDescription, p.NameScore, p.Recommended, p.Reason " + PendingWhere);
            return rows
                .GroupBy(x => x.RestaurantId)
                .OrderByDescending(g => g.Any(x => x.Recommended))
                .ThenBy(g => g.First().RestaurantName)
                .SelectMany(g => g.OrderByDescending(x => x.Recommended).ThenByDescending(x => x.NameScore))
                .ToList();
        }

        // Посилання йде в картку ресторану, решта пропозицій цього ресторану відхиляється
        public async Task<bool> Approve(int id)
        {
            await EnsureTables();
            var proposal = await _connection.QueryFirstOrDefaultAsync<MenuLinkProposal>(
                "SELECT Id, RestaurantId, Url FROM MenuLinkProposals WHERE Id = @id AND Status = @StatusNew", new { id, StatusNew });
            if (proposal == null) return false;
            await _menuLinks.Set(proposal.RestaurantId, proposal.Url);
            await _connection.ExecuteAsync("UPDATE MenuLinkProposals SET Status = @StatusApproved WHERE Id = @id", new { id, StatusApproved });
            await _connection.ExecuteAsync(
                "UPDATE MenuLinkProposals SET Status = @StatusRejected WHERE RestaurantId = @RestaurantId AND Status = @StatusNew",
                new { proposal.RestaurantId, StatusRejected, StatusNew });
            return true;
        }

        public async Task Reject(int id)
        {
            await EnsureTables();
            await _connection.ExecuteAsync("UPDATE MenuLinkProposals SET Status = @StatusRejected WHERE Id = @id AND Status = @StatusNew",
                new { id, StatusRejected, StatusNew });
        }

        // Усі рекомендовані: по одній на ресторан, Expirenza перед ChoiceQR (там ваги й варіанти страв)
        public async Task<int> ApproveRecommended()
        {
            var chosen = (await GetPending())
                .Where(x => x.Recommended)
                .GroupBy(x => x.RestaurantId)
                .Select(g => g.OrderBy(x => x.Platform == "expz" ? 0 : 1).First())
                .ToList();
            foreach (var proposal in chosen)
            {
                await Approve(proposal.Id);
            }
            return chosen.Count;
        }
    }
}
