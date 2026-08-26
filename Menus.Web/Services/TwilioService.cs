// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System.Threading.Tasks;
using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace Menus.Web.Services
{
    public class TwilioService
    {
        private const string AccountSid = "ACac2656d3a3756bc74f2b631e63e57e4d";
        private const string AuthToken = "018b34c2e7f75435bacf10b417153058";
        private readonly string TwilioPhone;

        private readonly TwilioRestClient _client;
        public TwilioService(string twilioPhone)
        {
            TwilioPhone = twilioPhone;
            _client = new TwilioRestClient(AccountSid, AuthToken);
        }


        public async Task SendMessage(string to, string text)
        {
            var message = await MessageResource.CreateAsync(
                from: new PhoneNumber(TwilioPhone),
                to: new PhoneNumber(to),
                body: text,
                client: _client
            );
        }
    }
}
