using System;
using System.Threading.Tasks;
using cliqx.auth.api.Models.Identity;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using ProAuth.Services.Contracts;

namespace cliqx.auth.api.Services
{
    // Envio de e-mails via MailKit (suporta porta 465 SSL/TLS implícito — SslOnConnect).
    // Configurável por appsettings (seção "Email" e "App").
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, ILogger<EmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        private string AppName => _config["App:Name"] ?? "Solvace PRForm";
        private string WebBaseUrl => (_config["App:WebBaseUrl"] ?? "").TrimEnd('/');

        public async Task SendFirstAccessEmail(User user, string code, DateTime expiration)
        {
            var firstName = FirstName(user.FullName);
            var link = $"{WebBaseUrl}/first-access?username={Uri.EscapeDataString(user.UserName)}&code={Uri.EscapeDataString(code)}&mode=welcome";

            var body = BuildTemplate(
                title: "Bem-vindo(a)!",
                greeting: $"Olá, {firstName}",
                intro: $"Sua conta no <strong>{AppName}</strong> foi criada. Para acessar pela primeira vez, defina a sua senha clicando no botão abaixo.",
                ctaLabel: "Definir minha senha",
                ctaUrl: link,
                code: code,
                expiration: expiration
            );

            await Send($"{AppName} — Primeiro acesso", body, user.Email);
        }

        public async Task SendResetPasswordEmail(User user, string code, DateTime expiration)
        {
            var firstName = FirstName(user.FullName);
            var link = $"{WebBaseUrl}/first-access?username={Uri.EscapeDataString(user.UserName)}&code={Uri.EscapeDataString(code)}&mode=reset";

            var body = BuildTemplate(
                title: "Redefinição de senha",
                greeting: $"Olá, {firstName}",
                intro: $"Recebemos uma solicitação para redefinir a sua senha no <strong>{AppName}</strong>. Clique no botão abaixo para criar uma nova senha. Se não foi você, ignore este e-mail.",
                ctaLabel: "Redefinir minha senha",
                ctaUrl: link,
                code: code,
                expiration: expiration
            );

            await Send($"{AppName} — Redefinição de senha", body, user.Email);
        }

        private async Task Send(string subject, string htmlBody, string to)
        {
            try
            {
                var host = _config["Email:Host"];
                var port = int.TryParse(_config["Email:Port"], out var p) ? p : 465;
                var useSsl = !bool.TryParse(_config["Email:UseSsl"], out var s) || s;
                var userName = _config["Email:User"];
                var password = _config["Email:Password"];
                var fromName = _config["Email:FromName"] ?? AppName;
                var bcc = _config["Email:Bcc"];

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(fromName, userName));
                message.To.Add(MailboxAddress.Parse(to));
                if (!string.IsNullOrWhiteSpace(bcc))
                    message.Bcc.Add(MailboxAddress.Parse(bcc));
                message.Subject = subject;
                message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

                using var client = new SmtpClient();

                // Desliga a checagem de revogação (CRL/OCSP): muitos hosts de e-mail
                // compartilhados falham com "incomplete certificate revocation check"
                // quando o ambiente não alcança o endpoint de revogação. A validação
                // de cadeia/nome do certificado continua ativa.
                client.CheckCertificateRevocation = false;

                // Bypass total da validação do certificado (opt-in via config), para
                // hosts com cadeia/nome de certificado inválidos. Padrão: desligado.
                if (bool.TryParse(_config["Email:AcceptAllCertificates"], out var acceptAll) && acceptAll)
                    client.ServerCertificateValidationCallback = (_, _, _, _) => true;

                // 465 = TLS implícito (SslOnConnect); demais portas caem em STARTTLS.
                var socketOptions = useSsl && port == 465
                    ? SecureSocketOptions.SslOnConnect
                    : SecureSocketOptions.StartTls;

                await client.ConnectAsync(host, port, socketOptions);
                await client.AuthenticateAsync(userName, password);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }
            catch (Exception ex)
            {
                // Envio é best-effort: não deve derrubar o fluxo de cadastro/reset.
                _logger.LogError(ex, "Falha ao enviar e-mail para {To}", to);
            }
        }

        private static string FirstName(string fullName) =>
            string.IsNullOrWhiteSpace(fullName) ? "" : fullName.Trim().Split(' ')[0];

        private string BuildTemplate(string title, string greeting, string intro, string ctaLabel, string ctaUrl, string code, DateTime expiration)
        {
            var exp = expiration.ToString("dd/MM/yyyy 'às' HH:mm");
            return $@"
<!DOCTYPE html>
<html lang=""pt-BR"">
<head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1.0""></head>
<body style=""margin:0;padding:0;background-color:#f2f4f7;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background-color:#f2f4f7;padding:24px 0;"">
    <tr>
      <td align=""center"">
        <table role=""presentation"" width=""560"" cellpadding=""0"" cellspacing=""0"" style=""width:560px;max-width:92%;background:#ffffff;border-radius:14px;overflow:hidden;box-shadow:0 2px 12px rgba(16,24,40,.06);"">
          <tr>
            <td style=""background:linear-gradient(135deg,#1565c0,#0d47a1);padding:28px 32px;"">
              <span style=""color:#ffffff;font-size:20px;font-weight:700;letter-spacing:.3px;"">{AppName}</span>
            </td>
          </tr>
          <tr>
            <td style=""padding:32px 32px 8px 32px;"">
              <h1 style=""margin:0 0 4px 0;font-size:22px;color:#101828;"">{title}</h1>
              <p style=""margin:0 0 16px 0;font-size:15px;color:#475467;"">{greeting},</p>
              <p style=""margin:0 0 24px 0;font-size:15px;line-height:1.6;color:#475467;"">{intro}</p>
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" style=""margin:0 0 24px 0;"">
                <tr><td style=""border-radius:10px;background:#1565c0;"">
                  <a href=""{ctaUrl}"" target=""_blank"" style=""display:inline-block;padding:14px 28px;font-size:15px;font-weight:600;color:#ffffff;text-decoration:none;border-radius:10px;"">{ctaLabel}</a>
                </td></tr>
              </table>
              <p style=""margin:0 0 6px 0;font-size:13px;color:#667085;"">Ou use o código de verificação:</p>
              <div style=""display:inline-block;font-family:Consolas,Menlo,monospace;font-size:20px;font-weight:700;letter-spacing:2px;color:#101828;background:#f2f4f7;border:1px dashed #cfd4dc;border-radius:10px;padding:10px 18px;margin-bottom:20px;"">{code}</div>
              <p style=""margin:0 0 24px 0;font-size:13px;color:#98a2b3;"">Este link/código expira em <strong style=""color:#667085;"">{exp}</strong>.</p>
              <hr style=""border:none;border-top:1px solid #eaecf0;margin:0 0 16px 0;"">
              <p style=""margin:0;font-size:12px;line-height:1.6;color:#98a2b3;"">Se o botão não funcionar, copie e cole este endereço no navegador:<br>
                <a href=""{ctaUrl}"" style=""color:#1565c0;word-break:break-all;"">{ctaUrl}</a>
              </p>
            </td>
          </tr>
          <tr>
            <td style=""padding:20px 32px;background:#f9fafb;"">
              <p style=""margin:0;font-size:12px;color:#98a2b3;"">Esta é uma mensagem automática do {AppName}. Por favor, não responda.</p>
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>";
        }
    }
}
