﻿using PRN231.ExploreNow.BusinessObject.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PRN231.ExploreNow.BusinessObject.Contracts.Repositories.Interfaces
{
    public interface IUserRepository : IBaseRepository
    {
        Task<ApplicationUser> GetUserByEmailAsync(string email);
        Task Update(ApplicationUser applicationUser);
    }
}
