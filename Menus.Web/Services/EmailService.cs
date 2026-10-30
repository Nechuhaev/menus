// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using Menus.Domain;
using Menus.Web.Models;
using MimeKit;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;

namespace Menus.Web.Services
{
    public class EmailService
    {
        private readonly Core _core;
        private readonly IConfiguration _configuration;

        public EmailService(Core core, IConfiguration configuration)
        {
            _core = core;
            _configuration = configuration;
        }

        public void SendEmail(string email, string subject, string message)
        {
            Send(email, subject, message);
        }

        // null - надіслано, інакше текст помилки (розсилка пише його в журнал).
        // unsubscribeUrl - заголовок List-Unsubscribe: поштові сервіси показують кнопку «відписатися»
        public string Send(string email, string subject, string message, string unsubscribeUrl = null)
        {

            //From Address 
            string FromAddress = "admin@tipmymenu.com";
            string FromAdressTitle = "Tipmymenus";
            //To Address 
            string ToAddress = email;
            string ToAdressTitle = "";
            string Subject = subject;
            string BodyContent = message;

            //Smtp Server 
            string SmtpServer = _configuration["Smtp:Server"];
            //Smtp Port Number 
            int SmtpPortNumber = int.Parse(_configuration["Smtp:Port"]);

            var mimeMessage = new MimeMessage();
            mimeMessage.From.Add(new MailboxAddress(FromAdressTitle, FromAddress));
            mimeMessage.To.Add(new MailboxAddress(ToAdressTitle, ToAddress));
            mimeMessage.Subject = Subject;
            if (!string.IsNullOrEmpty(unsubscribeUrl)) mimeMessage.Headers.Add("List-Unsubscribe", $"<{unsubscribeUrl}>");
            mimeMessage.Body = new TextPart("html")
            {
                Text = BodyContent
            };

            LogEvent("EmailSend", new
            {
                recepient = email,
                subject = subject
            }).Wait();

            using (var client = new SmtpClient())
            {
                try
                {
                    // Disable SSL validation
                    client.ServerCertificateValidationCallback = (s, c, h, e) => true;

                    client.Connect(SmtpServer, SmtpPortNumber, false);
                    // Note: only needed if the SMTP server requires authentication 
                    // Error 5.5.1 Authentication  
                    client.Authenticate(_configuration["Smtp:User"], _configuration["Smtp:Password"]);
                    client.Send(mimeMessage);
                    client.Disconnect(true);
                    return null;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"EmailService SEND FAILED to {ToAddress}: {ex}");
                    return ex.Message;
                }
            }
        }

        private async Task LogEvent(string name, object data)
        {
            var logItem = new LogItem
            {
                Id = Guid.NewGuid().ToString(),
                UserId = "system",
                DeviceId = "system",
                Event = name,
                EventData = JsonConvert.SerializeObject(data),
                Timestamp = DateTime.Now
            };
            await _core.LogItems.Create(logItem);
        }
    }
}
