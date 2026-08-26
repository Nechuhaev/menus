// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Menus.Abstract.Models;
using Menus.Domain;
using Menus.Web.Models;
using Menus.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Menus.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly Core _core;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly EmailService _email;
        private readonly IHostingEnvironment _hostingEnvironment;
        public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, Core core, IHostingEnvironment hostingEnvironment, EmailService email)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _core = core;
            _hostingEnvironment = hostingEnvironment;
            _email = email;
        }
        //public IActionResult Index()
        //{
        //    return View();
        //}

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ExternalLogin(string provider, string returnUrl = null)
        {
            var redirectUrl = Url.Action("", "Account", new { returnUrl });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            properties.RedirectUri = $"https://{Request.Host}/Account/ExternalLoginCallback";
            return Challenge(properties, provider);
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ExternalLoginCallback(string returnUrl = null, string remoteError = null)
        {
            if (remoteError != null)
            {
                return RedirectToAction("Index", "Home");
            }
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                return RedirectToAction("Index", "Home");
            }

            // Sign in the user with this external login provider if the user already has a login.
            var result = await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: true, bypassTwoFactor: true);
            if (result.Succeeded)
            {
                return RedirectToAction("Index", "Home");
            }
            else
            {
                // If the user does not have an account, then ask the user to create an account.
                string username = "";
                if (info.LoginProvider == "Facebook" || info.LoginProvider == "Google") username = info.Principal.Claims.FirstOrDefault(x => x.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")?.Value ?? info.Principal.Claims.First(x => x.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier").Value;

                string firstname = "";
                if (info.LoginProvider == "Facebook" || info.LoginProvider == "Google") firstname = info.Principal.Claims.FirstOrDefault(x => x.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname")?.Value ?? "";

                string lastname = "";
                if (info.LoginProvider == "Facebook" || info.LoginProvider == "Google") lastname = info.Principal.Claims.FirstOrDefault(x => x.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname")?.Value ?? "";

                string fbId = "";
                if(info.LoginProvider == "Facebook") fbId = info.Principal.Claims.FirstOrDefault(x => x.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value ?? "";

                ApplicationUser user = await _core.Users.FindByUsername(username) as ApplicationUser;

                if (user == null) {
                    user = new ApplicationUser
                    {
                        UserName = username,
                        SecurityStamp = Guid.NewGuid().ToString(),
                        FirstName = firstname,
                        LastName = lastname
                    };

                    if (!string.IsNullOrEmpty(fbId))
                    {
                        var client = new HttpClient {BaseAddress = new Uri("https://graph.facebook.com")};

                        var image = await client.GetByteArrayAsync($"/{fbId}/picture?type=large&width=720&height=720");

                        client.Dispose();

                        var file = new DbFile
                        {
                            Id = Guid.NewGuid().ToString(),
                            Data = image,
                            Mime = "image/jpg",
                            Size = image.Length
                        };

                        await _core.Files.Create(file);

                        user.ImageUrl = $"{file.Id}.jpg";
                    }

                    var iUser = _core.Users.ProcessUserWithUsername(user);
                    await _userManager.CreateAsync((ApplicationUser)iUser);
                    var id = iUser.Id;
                    user = await _userManager.FindByIdAsync(id);
                }

                await _userManager.AddLoginAsync(user, info);
                await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: true, bypassTwoFactor: true);

                if (!string.IsNullOrEmpty(Request.Cookies[".Menus.RedirectUrl"]))
                {
                    byte[] data = Convert.FromBase64String(Request.Cookies[".Menus.RedirectUrl"]);
                    string decodedString = Encoding.UTF8.GetString(data);
                    var redirectUrl = WebUtility.UrlDecode(decodedString);
                    Response.Cookies.Delete(".Menus.RedirectUrl");
                    return Redirect(redirectUrl);
                }

                return RedirectToAction("Index", "Home");
            }
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> UploadUserPhoto(string base64, string userId)
        {
            var user = await _core.Users.GetUserById(userId);
            if (user == null) return BadRequest("user not found");

            if (user.ImageUrl != null)
            {
                await _core.Files.Delete(GetGuid(user.ImageUrl));
            }

            base64 = base64.Substring(base64.IndexOf(',') + 1);
            base64 = base64.Trim('\0');
            byte[] bytes = Convert.FromBase64String(base64);
            var file = new DbFile
            {
                Data = bytes,
                Id = Guid.NewGuid().ToString(),
                Mime = "image/png",
                Size = bytes.Length
            };

            await _core.Files.Create(file);

            user.ImageUrl = file.Id + ".png";

            await _core.Users.Update(user);

            return Ok(new { id = file.Id, name = file.Id + ".png", approved = true });
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> RemoveUserPhoto(string userId)
        {
            var user = await _core.Users.GetUserById(userId);
            if (user == null) return BadRequest("user not found");

            await _core.Files.Delete(GetGuid(user.ImageUrl));

            user.ImageUrl = null;

            await _core.Users.Update(user);

            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password, bool remember)
        {

            if (!IsValidEmail(username) && !IsValidPhone(username)) return BadRequest(new { error = "username" });

            var user = await _core.Users.FindByUsername(username) as ApplicationUser;
            if(user == null) return BadRequest(new { error = "username" });
            if(!user.EmailConfirmed) return BadRequest(new { error = "not-confirmed" });
            if(user.Status == UserStatus.Blocked) return BadRequest(new { error = "blocked" });

            var signInResult = await _signInManager.PasswordSignInAsync(user, password, remember, lockoutOnFailure: false);
            if (signInResult.Succeeded)
            {
                var redirectToRestaurants = await _userManager.IsInRoleAsync(user, "restaurant") || await _userManager.IsInRoleAsync(user, "restaurant-manager");

                if (redirectToRestaurants)
                {
                    return Redirect("Index");
                }
                return Ok();
            }

            return BadRequest(new { error = "password" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(string username, string password, bool remember, string usermode)
        {
            if (await _core.Users.IsUsernameExist(username)) return BadRequest("Username exist");
            if (!IsValidEmail(username) && !IsValidPhone(username)) return BadRequest("Invalid username");

            var user = new ApplicationUser {
                UserName = username,
                EmailConfirmed = false
            };

            var iUser = _core.Users.ProcessUserWithUsername(user);
            var creationResult = await _userManager.CreateAsync((ApplicationUser) iUser);
            var id = iUser.Id;
            user = await _userManager.FindByIdAsync(id);
            await _userManager.AddPasswordAsync(user, password);
            if (usermode == UserMode.Restaurant.ToString().ToLower())
            {
                await _userManager.AddToRoleAsync((ApplicationUser)iUser, UserRole.Restaurant.ToString());
            }

            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

            if (IsValidEmail(username))
            {
                var mailText = System.IO.File.ReadAllText(_hostingEnvironment.ContentRootPath + "/EmailTemplates/register.html");
                var confirmationUrl = $"https://{Request.Host}/Account/ConfirmEmail/?email={WebUtility.UrlEncode(username)}&token={WebUtility.UrlEncode(token)}";
                mailText = mailText.Replace("{{confirmationUrl}}", confirmationUrl);

                _email.SendEmail(user.Email, "Email confirmation", mailText);

                if (username.Contains("@test-kwfcx.te")) return Ok(new { isEmail = IsValidEmail(username), link = confirmationUrl });
            }
            else
            {
                var signInResult = await _signInManager.PasswordSignInAsync(user, password, remember, lockoutOnFailure: false);
                if (signInResult.Succeeded)
                {
                    return Ok(new { isEmail = false });
                }
            }

            return Ok(new { isEmail = IsValidEmail(username) });
        }

        [HttpGet]
        public async Task<IActionResult> ConfirmEmail(string email, string token)
        {
            var user = await _core.Users.FindByUsername(email) as ApplicationUser;
            var result = await _userManager.ConfirmEmailAsync(user, token);

            if (result.Succeeded)
            {
                await _signInManager.SignInAsync(user, true);
            }

            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            Response.Cookies.Delete(".Menus.SelectedRestaurant");
            Response.Cookies.Delete(".Menus.UserId");

            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestorePassword(string email)
        {
            var user = await _core.Users.FindByEmail(email);
            if(user == null) return BadRequest(new { error = "user not exist" });

            var token = await _userManager.GeneratePasswordResetTokenAsync(user as ApplicationUser);

            var mailText = System.IO.File.ReadAllText(_hostingEnvironment.ContentRootPath + "/EmailTemplates/reset.html");
            var confirmationUrl = $"https://{Request.Host}/?email={WebUtility.UrlEncode(email)}&token={WebUtility.UrlEncode(token)}";
            mailText = mailText.Replace("{{confirmationUrl}}", confirmationUrl);

            _email.SendEmail(user.Email, "Password reset", mailText);

            if (email.Contains("@test-kwfcx.te")) return Ok(new { link = confirmationUrl });
            return Ok(new {});
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public IActionResult Share(string link, string email)
        {
            //var mailText = System.IO.File.ReadAllText(_hostingEnvironment.ContentRootPath + "/EmailTemplates/share.html");
            //mailText = mailText.Replace("{{url}}", link);

            //await _email.SendEmailAsync(user.Email, "Password reset", mailText);

            return Ok();
        }

        [Authorize]
        public async Task<IActionResult> Index(string culture, bool updateSuccess)
        {
            //var cities = await _core.Restaurants.GetCities();
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();
            //ViewData["cities"] = JsonConvert.SerializeObject(cities.Select(x => new { name = x.Name.Get(), id = x.Id }).OrderBy(x => x.name));
            ViewData["selectedCity"] = selectedCity;
            ViewData["updateSuccess"] = updateSuccess;
            var userInfo = await _userManager.FindByIdAsync(User.Identity.Name);
            return View(userInfo);
        }

        [Authorize]
        public IActionResult Password()
        {
            //var cities = await _core.Restaurants.GetCities();
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();
            //ViewData["cities"] = JsonConvert.SerializeObject(cities.Select(x => new { name = x.Name.Get(), id = x.Id }).OrderBy(x => x.name));
            ViewData["selectedCity"] = selectedCity;
            return View();
        }

        [Authorize]
        public async Task<IActionResult> Activity(int page = 0)
        {
            //var cities = await _core.Restaurants.GetCities();
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();
            //ViewData["cities"] = JsonConvert.SerializeObject(cities.Select(x => new { name = x.Name.Get(), id = x.Id }).OrderBy(x => x.name));
            ViewData["selectedCity"] = selectedCity;
            MyActivityPageModel result = new MyActivityPageModel();
            result.Ratings = (await _core.Ratings.GetUserRatings(User.Identity.Name)).OrderByDescending(x => x.UpdateDate).Skip(page * 12).Take(12); 
            result.UserInfo = await _userManager.FindByIdAsync(User.Identity.Name);
            return page == 0 ? View(result) : View("_ActivityAjax", result);
        }

        private string GetSelectedCityIdAndSetSelectedCityCookie()
        {
            var selectedCity = Request.Cookies[".Menus.SelectedCity"];
            if (string.IsNullOrEmpty(selectedCity))
            {
                Response.Cookies.Append(
                    ".Menus.SelectedCity",
                    "e2655e24-92dc-4179-abf6-c3908278a4de",
                    new CookieOptions {Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true}
                );

                Response.Cookies.Append(
                    ".Menus.SelectedCityName",
                    "kyiv",
                    new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true }
                );

                selectedCity = "e2655e24-92dc-4179-abf6-c3908278a4de";
            }

            return selectedCity;
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> EditRatingTextAsync(MyActivityPageModel model)
        {
            if (model != null && model.EditableRate != null && model.EditableRate.Id != null)
            {
                var editableRate = await _core.Ratings.GetRatingById(model.EditableRate.Id);
                editableRate.Text = model.EditableRate.Text;
                editableRate.Status = Abstract.Models.RatingStatus.NotApproved;
                await _core.Ratings.Update(editableRate);
                return RedirectToAction("Activity", "Account");
            } else
            {
                return RedirectToAction("Activity", "Account");
            }
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> DeleteRate(MyActivityPageModel model)
        {
            if (model != null && model.DeleteModel != null && model.DeleteModel.Id != null)
            {
                await _core.Ratings.Delete(model.DeleteModel.Id);
                return RedirectToAction("Activity", "Account");
            }
            else
            {
                return BadRequest();
            }
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> EditUserInfo(ApplicationUser user)
        {
            var userEntity = await _userManager.FindByIdAsync(User.Identity.Name);
            userEntity.FirstName = user.FirstName;
            userEntity.LastName = user.LastName;
            userEntity.PhoneNumber = user.PhoneNumber;
            await _core.Users.Update(userEntity);
            return RedirectToAction("Index", new { updateSuccess = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePassword(string email, string token, string password)
        {
            token = token.Replace(" ", "+");
            var user = await _core.Users.FindByEmail(email);
            if (user == null) return RedirectToAction("Index", "Home");

            var result = await _userManager.ResetPasswordAsync(user as ApplicationUser, token, password);
            if (result.Succeeded) {
                await _signInManager.PasswordSignInAsync(user as ApplicationUser, password, false, lockoutOnFailure: false);
            }

            return RedirectToAction("Index", "Account");
        }

        public async Task<string> UploadBase64Avatar(string base64)
        {
            base64 = base64.Substring(base64.IndexOf(',') + 1);
            base64 = base64.Trim('\0');
            byte[] bytes = Convert.FromBase64String(base64);
            var file = new DbFile
            {
                Data = bytes,
                Id = Guid.NewGuid().ToString(),
                Mime = "image/png",
                Size = bytes.Length
            };

            await _core.Files.Create(file);

            return file.Id + ".png";
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordModel model)
        {
            if (model.NewPassword != model.RepeatNewPassword)
            {
                return BadRequest("Passwords does not match!");
            }
            var user = await _userManager.FindByIdAsync(User.Identity.Name);
            if (user == null) return RedirectToAction("Index", "Home");
            var result = await _userManager.ChangePasswordAsync(user, model.OldPassword, model.NewPassword);
            if (result.Succeeded)
            {
                return RedirectToAction("Index", "Account");
            }
            return View("Password", model);
        }

        private static bool IsValidEmail(string input)
        {
            try
            {
                var address = new System.Net.Mail.MailAddress(input);
                return true;
            }
            catch {
                return false;
            }
        }

        private static string GetGuid(string input)
        {
            if (input == null) return "";
            Regex regex = new Regex("[a-z0-9]{8}-([a-z0-9]{4}-){3}[a-z0-9]{12}");
            Match match = regex.Match(input);
            if (match.Success)
            {
                return match.Value;
            }

            return "";
        }
        private static bool IsValidPhone(string input)
        {
            return PhoneNumbers.PhoneNumberUtil.IsViablePhoneNumber(input);
        }
    }

    public class DeleteRateModel
    {
        public string Id { get; set; }
    }

    public class ResetPasswordModel
    {
        public string OldPassword { get; set; }
        public string NewPassword { get; set; }
        public string RepeatNewPassword { get; set; }
    }
    
    public class MyActivityPageModel
    {
        public ApplicationUser UserInfo { get; set; }
        public Rating EditableRate { get; set; }
        public DeleteRateModel DeleteModel { get; set; }
        public IEnumerable<Menus.Abstract.Models.IUserRating> Ratings { get; set; }
    }

}