// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using Menus.Domain;
using Menus.Web.Models;
using MimeKit;
using Newtonsoft.Json;

namespace Menus.Web.Services
{
    public class EmailService
    {
        private readonly Core _core;

        public EmailService(Core core)
        {
            _core = core;
        }

        public void SendEmail(string email, string subject, string message)
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
            string SmtpServer = "smtp.elasticemail.com";
            //Smtp Port Number 
            int SmtpPortNumber = 2525;

            var mimeMessage = new MimeMessage();
            mimeMessage.From.Add(new MailboxAddress(FromAdressTitle, FromAddress));
            mimeMessage.To.Add(new MailboxAddress(ToAdressTitle, ToAddress));
            mimeMessage.Subject = Subject;
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
                    client.Authenticate("juzzzt.dev@gmail.com", "5b4d383b-6553-4e75-a516-619837ecbd2c");
                    client.Send(mimeMessage);
                    client.Disconnect(true);
                }
                catch (MailKit.Security.AuthenticationException)
                {
                    
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
