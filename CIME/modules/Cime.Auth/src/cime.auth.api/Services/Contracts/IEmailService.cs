using System;
using System.Threading.Tasks;
using cliqx.auth.api.Models.Identity;

namespace ProAuth.Services.Contracts
{
    public interface IEmailService
    {
        Task SendFirstAccessEmail(User user, string code, DateTime expiration);
        Task SendResetPasswordEmail(User user, string code, DateTime expiration);
    }
}
