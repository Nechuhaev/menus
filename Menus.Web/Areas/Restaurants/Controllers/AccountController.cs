// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Threading.Tasks;
using Menus.Domain;
using Menus.Web.Models;
using Menus.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Menus.Web.Areas.Restaurants.Controllers
{
    [Area("Restaurants")]
    public class AccountController : Controller
    {
        private readonly Core _core;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly TwilioService _twilio;

        public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, Core core, TwilioService twilio)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _core = core;
            _twilio = twilio;
        }

        public IActionResult Login()
        {
            if (User.Identity.IsAuthenticated) return RedirectToAction("Index", "Home");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password, bool remember)
        {
            var user = await _core.Users.FindByUsername(username) as ApplicationUser;
            if (user == null) {
                return BadRequest(new { error = "username" });
            }

            if (!user.PhoneNumberConfirmed || 
                (
                !(await _userManager.IsInRoleAsync(user, "restaurant")) &&
                !(await _userManager.IsInRoleAsync(user, "restaurant-manager"))
                )
            ) {
                return BadRequest(new { error = "username" });
            }

            var signInResult = await _signInManager.PasswordSignInAsync(user, password, remember, lockoutOnFailure: false);
            if (signInResult.Succeeded)
            {
                return Ok();
            }

            return BadRequest(new { error = "password" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(string name, string lastName, string email, string phone)
        {
            if (!IsValidEmail(email) && !IsValidPhone(phone)) return BadRequest(new { error = "username" });

            var user = await _core.Users.FindByEmail(email) as ApplicationUser;
            if (user != null && !user.PhoneNumberConfirmed)
            {
                user.PhoneConfirmationCode = GenerateSmsCode();
                await _core.Users.Update(user);
                await _twilio.SendMessage(user.PhoneNumber, $"Your code is {user.PhoneConfirmationCode}");
                return BadRequest(new { error = "exist", id = user.Id });
            }
            else if (user != null) return BadRequest(new { error = "in_use"  });

            user = await _core.Users.FindByPhone(phone) as ApplicationUser;
            if (user != null && !user.PhoneNumberConfirmed)
            {
                user.PhoneConfirmationCode = GenerateSmsCode();
                await _core.Users.Update(user);
                await _twilio.SendMessage(user.PhoneNumber, $"Your code is {user.PhoneConfirmationCode}");
                return BadRequest(new { error = "exist", id = user.Id });
            }
            if (user != null) return BadRequest(new { error = "in_use" });

            user = new ApplicationUser();
            user.Email = email;
            user.FirstName = name;
            user.LastName = lastName;
            user.PhoneNumber = phone;
            user.PhoneConfirmationCode = GenerateSmsCode();

            user = await _core.Users.Create(user) as ApplicationUser;
            await _twilio.SendMessage(user.PhoneNumber, $"Your code is {user.PhoneConfirmationCode}");

            return Ok(new { id = user.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckCode(string userId, string code)
        {
            var user = await _core.Users.GetUserById(userId);
            if (user.PhoneNumberConfirmed && !string.IsNullOrEmpty(user.PasswordHash)) return BadRequest(new { error = "in_use" });

            if (user.PhoneConfirmationCode != code) return BadRequest(new { error = "wrong_code" });

            user.PhoneNumberConfirmed = true;
            await _core.Users.Update(user);

            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendCode(string userId)
        {
            var user = await _core.Users.GetUserById(userId);
            if (user.PhoneNumberConfirmed && !string.IsNullOrEmpty(user.PasswordHash)) return BadRequest(new { error = "in_use" });

            user.PhoneConfirmationCode = GenerateSmsCode();
            await _core.Users.Update(user);
            await _twilio.SendMessage(user.PhoneNumber, $"Your code is {user.PhoneConfirmationCode}");

            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FinishRegister(string userId, string password)
        {
            var user = await _core.Users.GetUserById(userId) as ApplicationUser;
            if (user.PhoneNumberConfirmed && !string.IsNullOrEmpty(user.PasswordHash)) return BadRequest(new { error = "in_use" });

            await _userManager.AddPasswordAsync(user, password);

            user.Role = Abstract.Models.UserRole.Restaurant;
            await _core.Users.Update(user);
            await _userManager.AddToRoleAsync(user, "restaurant");
            await _signInManager.SignInAsync(user, false);

            return Ok();
        }

        public IActionResult Register()
        {
            return View();
        }

        private static bool IsValidEmail(string input)
        {
            try
            {
                var address = new System.Net.Mail.MailAddress(input);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsValidPhone(string input)
        {
            return PhoneNumbers.PhoneNumberUtil.IsViablePhoneNumber(input);
        }

        private static string GenerateSmsCode()
        {
            var rand = new Random();
            var int1 = rand.Next(0, 10).ToString();
            var int2 = rand.Next(0, 10).ToString();
            var int3 = rand.Next(0, 10).ToString();
            var int4 = rand.Next(0, 10).ToString();

            return int1 + int2 + int3 + int4;
        }
    }
}