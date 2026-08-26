// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Abstract;
using Menus.Abstract.Models;
using Menus.Domain;
using Menus.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Menus.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class UsersController : Controller
    {
        private readonly Core _core;
        private readonly IDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public UsersController(Core core, UserManager<ApplicationUser> userManager, IDbContext db)
        {
            _core = core;
            _userManager = userManager;
            _db = db;
        }

        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Index(string q, UserStatus? status, UserRole? role)
        {
            IEnumerable<IUser> users;
            if(string.IsNullOrEmpty(q) && status == null && role == null) users = await _core.Users.GetAllUsers();
            else users = await _core.Users.Search(q, status, role);

            ViewData["q"] = q;
            ViewData["status"] = status;
            ViewData["role"] = role;
            ViewData["hideRoles"] = false;

            return View(users.Take(50));
        }

        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Log(string id, int page = 0)
        {
            var logItems = _db.LogItems.Where(x => x.UserId == id).OrderByDescending(x => x.Timestamp).Skip(page * 20).Take(20).ToList();

            ViewData["page"] = page;
            ViewData["id"] = id;

            return View(logItems);
        }

        [Authorize(Roles = "admin, manager")]
        public async Task<IActionResult> Managers(string q, UserStatus? status)
        {

            var role = UserRole.Restaurant;
            var users = await _core.Users.Search(q, status, role);

            ViewData["q"] = q;
            ViewData["status"] = status;
            ViewData["role"] = role;
            ViewData["hideRoles"] = true;

            return View("Index", users.Take(50));
        }

        [Authorize(Roles = "admin, manager")]
        public async Task<IActionResult> Edit(string id)
        {
            var user = await _core.Users.GetUserById(id);
            var restaurants = await _core.Restaurants.GetRestaurantsForUser(id);
            ViewData["restaurants"] = restaurants;
            return View(user);
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        [Authorize(Roles = "admin, manager")]
        public async Task<IActionResult> Edit(ApplicationUser user)
        {
            var existingUser = await _core.Users.GetUserById(user.Id);

            existingUser.FirstName = user.FirstName;
            existingUser.LastName = user.LastName;
            existingUser.PhoneNumber = user.PhoneNumber;
            existingUser.Email = user.Email;
            
            if (User.IsInRole("admin") && existingUser.Role != user.Role)
            {
                var identityUser = await _userManager.FindByIdAsync(existingUser.Id);
                
                await _userManager.RemoveFromRoleAsync(identityUser, "admin");
                await _userManager.RemoveFromRoleAsync(identityUser, "manager");
                await _userManager.RemoveFromRoleAsync(identityUser, "restaurant");
                await _userManager.RemoveFromRoleAsync(identityUser, "restaurant-manager");

                if (user.Role == UserRole.Admin)
                {
                    await _userManager.AddToRoleAsync(identityUser, "admin");
                }
                else if (user.Role == UserRole.Manager)
                {
                    await _userManager.AddToRoleAsync(identityUser, "manager");
                }
                else if (user.Role == UserRole.Restaurant)
                {
                    await _userManager.AddToRoleAsync(identityUser, "restaurant");
                }
                else if (user.Role == UserRole.RestaurantManager)
                {
                    await _userManager.AddToRoleAsync(identityUser, "restaurant-manager");
                }

                existingUser.Role = user.Role;
            }

            await _core.Users.Update(existingUser);
            return RedirectToAction("Index");
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        [Authorize(Roles = "admin, manager")]
        public async Task<IActionResult> ChangeStatus(string id, UserStatus status)
        {
            var user = await _core.Users.GetUserById(id);
            user.Status = status;
            await _core.Users.Update(user);
            return Ok();
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        [Authorize(Roles = "admin, manager")]
        public async Task<IActionResult> ChangeEmailStatus(string id)
        {
            var user = await _core.Users.GetUserById(id);
            user.EmailConfirmed = !user.EmailConfirmed;
            await _core.Users.Update(user);
            return Ok();
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> ChangeRole(string id, UserRole role)
        {
            var user = await _core.Users.GetUserById(id) as ApplicationUser;

            if (user.Role == role) return Ok();

            await _userManager.RemoveFromRoleAsync(user, "admin");
            await _userManager.RemoveFromRoleAsync(user, "manager");
            await _userManager.RemoveFromRoleAsync(user, "restaurant");
            await _userManager.RemoveFromRoleAsync(user, "restaurant-manager");

            if (role == UserRole.Admin) await _userManager.AddToRoleAsync(user, "admin");
            else if (role == UserRole.Manager) await _userManager.AddToRoleAsync(user, "manager");
            else if (role == UserRole.Restaurant) await _userManager.AddToRoleAsync(user, "restaurant");
            else if (role == UserRole.RestaurantManager) await _userManager.AddToRoleAsync(user, "restaurant-manager");

            user.Role = role;
            await _core.Users.Update(user);
            return Ok();
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(string id)
        {
            await _core.Users.DeleteUser(id);
            return Ok();
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteSeveral(IEnumerable<string> ids)
        {
            foreach (var id in ids) {
                await _core.Users.DeleteUser(id);
            }
            return Ok();
        }
    }
}