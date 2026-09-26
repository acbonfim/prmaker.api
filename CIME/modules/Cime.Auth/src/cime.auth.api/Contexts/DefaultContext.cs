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

        /// <summary>Schema do PostgreSQL com todas as tabelas da autenticação (feature 0014).</summary>
        public const string Schema = "auth";

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            // As datas são gravadas com DateTime.Now (horário "de parede", como no datetime2 do SQL
            // Server). No Npgsql, DateTime vira timestamptz por padrão e exige Kind=Utc; aqui fica
            // "timestamp without time zone", que guarda o valor como está. Não grave DateTime.UtcNow
            // nessas colunas: o Npgsql recusa Kind=Utc em timestamp sem fuso.
            configurationBuilder.Properties<DateTime>().HaveColumnType("timestamp without time zone");
            configurationBuilder.Properties<DateTime?>().HaveColumnType("timestamp without time zone");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.HasDefaultSchema(Schema);

            // Sem HasData: seeds com Guid.NewGuid()/DateTime.Now/hash aleatório mudavam a cada build e
            // todo "migrations add" gerava UpdateData que resetaria o admin de produção. Papéis padrão e
            // admin inicial agora vêm do AuthSeeder (idempotente, só com o banco vazio).

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
        }
    }


}