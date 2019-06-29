using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Mail;

namespace FB.BanChecker
{
    public class Mailer
    {
        IConfiguration _config;
        public Mailer()
        {
            _config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .Build();
        }

        public void SendEmailNotification(string subj, string msg)
        {
            using (var message = new MailMessage())
            {
                message.To.Add(new MailAddress(_config.GetValue<string>("mail_to"), "To Name"));
                message.From = new MailAddress(_config.GetValue<string>("mail_login"), "FB Domain Checker");
                message.Subject = subj;
                message.Body = msg;
                message.IsBodyHtml = false;

                using (var client = new SmtpClient(_config.GetValue<string>("mail_server")))
                {
                    client.Port = _config.GetValue<int>("mail_port");
                    client.Credentials = new NetworkCredential(
                        _config.GetValue<string>("mail_login"),
                        _config.GetValue<string>("mail_password"));
                    client.EnableSsl = _config.GetValue<bool>("mail_usessl");
                    client.Send(message);
                }
            }
        }
    }
}