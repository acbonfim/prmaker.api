using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using cliqx.auth.api.Models.Identity;
using cliqx.auth.api.Models.Types;
using Microsoft.OpenApi.Expressions;

namespace cliqx.auth.api.Services
{
    public static class EmailService
    {
        public static async void SendEmail(User user, string code, TypeCodeEnum type)
        {
            using (SmtpClient smtpClient = new SmtpClient())
            {
                using (MailMessage message = new MailMessage())
                {

                    MailAddress fromAddress = new MailAddress("cancelamento@snog.com.br", "SNOG - Primeiro acesso");

                    smtpClient.UseDefaultCredentials = false;
                    smtpClient.Host = "smtp.gmail.com";
                    smtpClient.Port = 587;
                    smtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;
                    smtpClient.EnableSsl = true;
                    smtpClient.Credentials = new NetworkCredential("noreply@snog.com.br", "17PmCHQ5FHISPr6");

                    message.From = fromAddress;

                    string Assunto = "SNOG - Primeiro acesso ";
                    message.Body = $@" <h3>Olá {user.FullName.Split(" ").First()},</h3>";

                    if (type == TypeCodeEnum.RegisterNewUser)
                    {
                        Assunto = "SNOG - Primeiro acesso";
                        
                        message.Body += @$"<br> Segue o seu código para desbloqueio de primeiro acesso: {code}. Esse código expira em {DateTime.Now.AddMinutes(5)} 
                        <br> Clique no link para confirmar sua conta: https://app.snog.com.br/auth/api/User/ConfirmEmail?username={user.UserName}&code={code}";
                    }

                    if (type == TypeCodeEnum.ForgetPassword)
                    {
                        Assunto = "SNOG - Esqueci minha senha";
                        message.Body += $@"<br> Segue o seu código para desbloqueio da senha: {code}. Esse código expira em {DateTime.Now.AddMinutes(5)} 
                        <br> Clique no link para resetar sua senha: https://app.snog.com.br/auth/api/User/ConfirmEmail?username={user.UserName}&code={code}";
                    }

                    message.Body += $"<br> Caso esse codigo expire, acesse o link a seguir para gerar um novo codigo: https://app.snog.com.br/auth/api/Password/GenerateForgetCodeByUsername?username={user.UserName}";

                    message.Subject = Assunto;

                    message.IsBodyHtml = true;

                    message.To.Add(user.Email);
                    message.Bcc.Add("alex.bonfim@snog.com.br");
                    //message.To.Add("marcus@cliqx.com.br");
                    //message.To.Add("guilherme.pires@cliqx.com.br");

                    try
                    {
                        await smtpClient.SendMailAsync(message);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                        Console.WriteLine(ex.StackTrace);

                    }
                }
            }
        }
    }

}