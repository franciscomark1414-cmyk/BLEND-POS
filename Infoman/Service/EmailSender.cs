using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace Infoman.Services
{
    public class EmailSettings
    {
        public string Host { get; set; } = "smtp.gmail.com";
        public int Port { get; set; } = 587;
        public string Username { get; set; } = "";
        public string AppPassword { get; set; } = "";
        public string FromName { get; set; } = "Andeng's Mango & More";
    }

    public interface IEmailSender
    {
        Task SendAsync(string to, string subject, string htmlBody);
    }

    public class SmtpEmailSender : IEmailSender
    {
        private readonly EmailSettings _s;
        public SmtpEmailSender(IOptions<EmailSettings> options) => _s = options.Value;

        public async Task SendAsync(string to, string subject, string htmlBody)
        {
            if (string.IsNullOrWhiteSpace(_s.Username) || string.IsNullOrWhiteSpace(_s.AppPassword))
                throw new InvalidOperationException("SMTP credentials are not configured.");

            using var client = new SmtpClient(_s.Host, _s.Port)
            {
                EnableSsl = true, // STARTTLS on port 587
                Credentials = new NetworkCredential(_s.Username, _s.AppPassword.Replace(" ", ""))
            };
            using var msg = new MailMessage
            {
                From = new MailAddress(_s.Username, _s.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            msg.To.Add(to);
            await client.SendMailAsync(msg);
        }
    }
}