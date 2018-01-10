using System;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace Kroeg.EntityStore.Models
{
    public class APUser
    {
        public APUser()
        {
            Id = Guid.NewGuid().ToString();
        }
        
        public string Id { get; set; }
        public string Username { get; set;}
        public string NormalisedUsername { get; set;}
        public string Email { get; set; }
        public string PasswordHash { get; set; }
    }
}
