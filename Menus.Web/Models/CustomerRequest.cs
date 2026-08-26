using System;
using Menus.Abstract.Models;

namespace Menus.Web.Models
{
    public class CustomerRequest : ICustomerRequest
    {
        public string Id { get; set; }
        public string RestaurantName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Package { get; set; }
        public CustomerRequestStatus Status { get; set; }
        public DateTime CreateDate { get; set; }
        public DateTime UpdateDate { get; set; }
    }
}
