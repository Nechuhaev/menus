// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract;
using Menus.Abstract.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Menus.Domain.Users
{
    public class CoreUsers
    {
        private IDbContext _db;
        private Core _core;
        public CoreUsers(IDbContext db, Core core)
        {
            _db = db;
            _core = core;
        }

        public async Task<IUser> Create(IUser user)
        {
            user.PhoneNumber = NormalizePhone(user.PhoneNumber);
            user.NormalizedEmail = user.Email.ToUpper();
            user.Id = Guid.NewGuid().ToString();
            user.UserName = user.Id;
            user.NormalizedUserName = user.UserName.ToUpper();

            await _db.Users.Add(user);

            return user;
        }

        public async Task<string> CreateUserWithUsername(IUser user)
        {
            user.Id = Guid.NewGuid().ToString();
            if (IsStringEmail(user.UserName))
            {
                user.Email = user.UserName;
                user.NormalizedEmail = user.Email.ToUpper();
                user.UserName = user.Id;
                user.NormalizedUserName = user.UserName.ToUpper();
                user.EmailConfirmed = false;
            }
            else
            {
                user.PhoneNumber = NormalizePhone(user.UserName);
                user.PhoneNumberConfirmed = false;
                user.UserName = user.Id;
                user.NormalizedUserName = user.UserName.ToUpper();
            }
            user.TwoFactorEnabled = false;
            await _db.Users.Add(user);
            return user.Id;
        }

        public IUser ProcessUserWithUsername(IUser user)
        {
            user.Id = Guid.NewGuid().ToString();
            if (IsStringEmail(user.UserName))
            {
                user.Email = user.UserName;
                user.NormalizedEmail = user.Email.ToUpper();
                user.UserName = user.Id;
                user.NormalizedUserName = user.UserName.ToUpper();
                user.EmailConfirmed = false;
            }
            else
            {
                user.PhoneNumber = NormalizePhone(user.UserName);
                user.PhoneNumberConfirmed = false;
                user.UserName = user.Id;
                user.NormalizedUserName = user.UserName.ToUpper();
            }
            user.TwoFactorEnabled = false;
            return user;
        }

        public async Task<bool> IsUsernameExist(string username)
        {
            if (IsStringEmail(username))
            {
                var user = await Task.Run(() => _db.Users.FirstOrDefault(x => x.NormalizedEmail == username.ToUpper()));
                return user != null;
            }
            else
            {
                var phone = NormalizePhone(username);
                var user = await Task.Run(() => _db.Users.FirstOrDefault(x => x.PhoneNumber == phone));
                return user != null;
            }
        }

        public async Task<IEnumerable<IUser>> GetAllUsers()
        {
            return await Task.Run(() => _db.Users
                .OrderBy(x => x.PhoneNumber).ThenBy(x => x.Email).ToList());
        }

        public async Task<IEnumerable<IUser>> Search(string query, UserStatus? status, UserRole? role)
        {
            IQueryable<IUser> result = _db.Users;

            query = query?.ToLower();

            if (status != null) {
                result = result.Where(x =>
                    x.Status == status
                );
            }

            if (role != null)
            {
                result = result.Where(x =>
                    x.Role == role
                );
            }

            if (!string.IsNullOrEmpty(query)) {
                result = result.Where(x =>
                    x.Email.ToLower().Contains(query) ||
                    x.PhoneNumber.ToLower().Contains(query) ||
                    x.FirstName.ToLower().Contains(query) ||
                    x.LastName.ToLower().Contains(query)
                );
            }

            result = result.OrderBy(x => x.PhoneNumber).ThenBy(x => x.Email);
            return await Task.Run(() => result.ToList());
        }

        public async Task<IUser> GetUserById(string id)
        {
            return await Task.Run(() => _db.Users.FirstOrDefault(x => x.Id == id));
        }

        public async Task<IUser> FindByUsername(string username)
        {
            if (IsStringEmail(username))
            {
                var user = await Task.Run(() => _db.Users.FirstOrDefault(x => x.NormalizedEmail == username.ToUpper()));
                return user;
            }
            else
            {
                var phone = NormalizePhone(username);
                var user = await Task.Run(() => _db.Users.FirstOrDefault(x => x.PhoneNumber == phone));
                return user;
            }
        }

        public async Task<IUser> FindByPhone(string phone)
        {
            phone = NormalizePhone(phone);

            return phone == "" ? null :
                await Task.Run(() => _db.Users
                .FirstOrDefault(x => x.PhoneNumber == phone));
        }

        public async Task<IUser> FindByEmail(string email)
        {
            email = !string.IsNullOrEmpty(email) ? email.ToUpper() : "";

            return email == "" ? null :
                await Task.Run(() => _db.Users
                .FirstOrDefault(x => x.NormalizedEmail == email));
        }

        public async Task Update(IUser user)
        {
            user.PhoneNumber = NormalizePhone(user.PhoneNumber);
            await _db.Users.Update(user);
        }

        public async Task DeleteUser(string id)
        {
            var user = await GetUserById(id);

            await _db.Users.Remove(user);
        }


        private static bool IsStringEmail(string input)
        {
            return input.Contains("@");
        }


        private static string NormalizePhone(string phone)
        {
            if (!string.IsNullOrEmpty(phone))
            {
                var regex = new Regex(@"[\s\(\)-]");
                phone = regex.Replace(phone, "");
            }

            return phone;
        }
    }
}
