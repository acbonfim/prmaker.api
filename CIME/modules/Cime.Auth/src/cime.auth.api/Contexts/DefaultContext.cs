using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using cliqx.auth.api.Dtos;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using cliqx.auth.api.Models.Identity;
using cliqx.auth.api.Models;

namespace ProSales.Repository.Contexts
{
    public class DefaultContext : IdentityDbContext<User, Role, int,
    IdentityUserClaim<int>, UserRole, IdentityUserLogin<int>, IdentityRoleClaim<int>, IdentityUserToken<int>>
    {
        private readonly IMapper _mapper;

        public DefaultContext(DbContextOptions<DefaultContext> options,
            IMapper mapper) : base(options)
        {
            _mapper = mapper;
        }

        public DbSet<UserForgetCode> UserForgetCodes { get; set; }
        public DbSet<UserService> UserServices { get; set; }
        public DbSet<MyService> Services { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<UserRole>(ur =>
            {
                ur.HasKey(k => new { k.UserId, k.RoleId });

                ur
                .HasOne(x => x.Role)
                .WithMany(r => r.UserRoles)
                .HasForeignKey(fk => fk.RoleId)
                .IsRequired();

                ur
                .HasOne(x => x.User)
                .WithMany(r => r.UserRoles)
                .HasForeignKey(fk => fk.UserId)
                .IsRequired();
            });

            modelBuilder.Entity<UserService>(us =>
            {
                us.HasKey(k => new { k.ServiceId, k.UserId });

                us
                .HasOne(x => x.Service)
                .WithMany(r => r.UserServices)
                .HasForeignKey(fk => fk.ServiceId)
                .IsRequired();

                us
                .HasOne(x => x.User)
                .WithMany(r => r.UserServices)
                .HasForeignKey(fk => fk.UserId)
                .IsRequired();
            });

            modelBuilder.Entity<Role>(r =>
            {
                r.HasData(
                    new Role() { Id = 1, Name = "admin", NormalizedName = "ADMIN", ConcurrencyStamp = Guid.NewGuid().ToString().ToUpper() }
                    , new Role() { Id = 2, Name = "user", NormalizedName = "USER", ConcurrencyStamp = Guid.NewGuid().ToString().ToUpper() }
                    , new Role() { Id = 3, Name = "external_client", NormalizedName = "EXTERNALCLIENT", ConcurrencyStamp = Guid.NewGuid().ToString().ToUpper() }
                    , new Role() { Id = 4, Name = "support", NormalizedName = "SUPPORT", ConcurrencyStamp = Guid.NewGuid().ToString().ToUpper() }
                );
            });
            
            modelBuilder.Entity<User>(u =>
            {
                var hasher = new PasswordHasher<User>();
                
                var adminUser = new User
                {
                    Id = 1,
                    UserName = "admin",
                    NormalizedUserName = "ADMIN",
                    Email = "admin@cime.com.br",
                    NormalizedEmail = "ADMIN@CIME.COM.BR",
                    EmailConfirmed = true,
                    FullName = "Admin",
                    Departamento = "ADMIN",
                    SecurityStamp = Guid.NewGuid().ToString().ToUpper(),
                    CompanyId = 1,
                    ExternalId = Guid.NewGuid(),
                    ChannelOrigin = "WEB",
                    Active = true,
                    DataUltimoLogin = DateTime.Now,
                    PhoneNumberConfirmed = false,
                    TwoFactorEnabled = false,
                    LockoutEnabled = false,
                    AccessFailedCount = 0
                };
                
                adminUser.PasswordHash = hasher.HashPassword(adminUser, "Copa#2026");
                
                u.HasData(adminUser);
            });
            
            modelBuilder.Entity<UserRole>(ur =>
            {
                ur.HasData(
                    new UserRole
                    {
                        UserId = 1,
                        RoleId = 1
                    }
                );
            });

        }
    }


}