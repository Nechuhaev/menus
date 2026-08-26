using System;

namespace Menus.Abstract.Models
{
    public interface ICustomerRequest
    {
        string Id { get; set; }
        string RestaurantName { get; set; }
        string Email { get; set; }
        string Phone { get; set; }
        string Package { get; set; }
        CustomerRequestStatus Status { get; set; }
        DateTime CreateDate { get; set; }
        DateTime UpdateDate { get; set; }
    }

    public enum CustomerRequestStatus
    {
        New,
        Processed,
        NewRequest,
    }
}
