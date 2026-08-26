using Menus.Abstract.Models;
using Menus.Web.Data.Repositories;
using Menus.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Data
{
    public class CustomerRequestRepository : EfRepository<CustomerRequest, ICustomerRequest>
    {
        public CustomerRequestRepository(DbContext context) : base(context)
        {
        }
    }
}
