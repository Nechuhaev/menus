using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Menus.Abstract.Models;
using Menus.Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Types.InputFiles;

namespace Menus.TelegramBot
{
    class Program
    {
        static ITelegramBotClient _botClient;
        static Dictionary<string, string> _state = new Dictionary<string, string>();
        static Controller _controller = new Controller();
        static HttpClient _http = new HttpClient
        {
            BaseAddress = new Uri("https://tipmymenu.com")
        };

    static void Main()
    {
        _botClient = new TelegramBotClient(Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN"));

        var me = _botClient.GetMeAsync().Result;
        Console.WriteLine(
            $"Hello, World! I am user {me.Id} and my name is {me.FirstName}."
        );

        ////////////
        _controller.AddSequence()
            .Next("/start", async (e, state) =>
            {
                await _botClient.SendTextMessageAsync(
                    chatId: e.Message.Chat,
                    text: "Попробуйте /search для поиска по названию, или /nearby для поиска поблизости",
                    replyMarkup: new ReplyKeyboardMarkup(new List<KeyboardButton>
                    {
                        new KeyboardButton("/search"),
                        new KeyboardButton("/nearby")
                    }, true, true)
                );
            });

        _controller.AddSequence()
            .Next("/nearby", async (e, state) =>
            {
                await _botClient.SendTextMessageAsync(
                    chatId: e.Message.Chat,
                    text: "Предоставьте своё местоположение для поиска",
                    replyMarkup: new ReplyKeyboardMarkup(new List<KeyboardButton>
                    {
                        KeyboardButton.WithRequestLocation("Предоставить моё местоположение")
                    }, true, true)
                );
            })
            .Next("/nearby-location", async (e, state) =>
            {
                var lat = e.Message.Location.Latitude.ToString("F5", new CultureInfo("en-US"));
                var lon = e.Message.Location.Longitude.ToString("F5", new CultureInfo("en-US"));
                var result = await _http.GetStringAsync($"/ru/telegrambot/getnearestrestaurants?lat={lat}&lon={lon}");

                if (result.Length < 10)
                {
                    await _botClient.SendTextMessageAsync(
                        chatId: e.Message.Chat,
                        text: "К сожалению ничего не найдено. Попробуйте /search для поиска по названию",
                        replyMarkup: new ReplyKeyboardMarkup(new List<KeyboardButton>
                        {
                            new KeyboardButton("/search")
                        }, true, true)
                    );

                    return;
                }

                var restaurants =
                    JsonConvert.DeserializeObject<List<CompleteRestaurant>>(JObject.Parse(result)["restaurants"].Value<string>());

                foreach (var restaurant in restaurants)
                {
                    await _botClient.SendPhotoAsync(
                        chatId: e.Message.Chat,
                        photo: new InputOnlineFile(GetRestaurantPhoto(restaurant)),
                        replyMarkup: new InlineKeyboardMarkup(new InlineKeyboardButton
                        {
                            Text = $"{restaurant.Name}, {restaurant.Address}",
                            Url = GetRestaurantUrl(restaurant)
                        })
                    );
                }
            });

            _controller.AddSequence()
                .Next("/search", async (e, state) =>
                {
                    await _botClient.SendTextMessageAsync(
                        chatId: e.Message.Chat,
                        text: "Введите название ресторана:"
                    );
                })
                .Next("/search-name", async (e, state) =>
                {
                    var name = e.Message.Text.StartsWith("/") ? e.Message.Text.Split(" ")[1] : e.Message.Text;
                    var result = await _http.GetStringAsync($"/ru/telegrambot/getcitiesforrestaurant?name={name}");

                    if (result.Length < 10)
                    {
                        state.StateObj = null;

                        await _botClient.SendTextMessageAsync(
                            chatId: e.Message.Chat,
                            text: "К сожалению ничего не найдено. Попробуйте /search для поиска по другому названию, или /nearby для поиска поблизости",
                            replyMarkup: new ReplyKeyboardMarkup(new List<KeyboardButton>
                            {
                                new KeyboardButton("/search"),
                                new KeyboardButton("/nearby")
                            }, true, true)
                        );

                        return;
                    }

                    var cities =
                        JsonConvert.DeserializeObject<List<string>>(JObject.Parse(result)["cities"].Value<string>());
                    state.StateObj = JObject.FromObject(new { name });

                    await _botClient.SendTextMessageAsync(
                        chatId: e.Message.Chat,
                        text: "Выберите город:",
                        replyMarkup: new ReplyKeyboardMarkup(cities.Select(x => new KeyboardButton(x)), true, true)
                    );
                })
                .Next("/search-city", async (e, state) =>
                {
                    var city = e.Message.Text.StartsWith("/") ? e.Message.Text.Split(" ")[1] : e.Message.Text;
                    var name = (state.StateObj as JObject)?["name"];
                    var result = await _http.GetStringAsync($"/ru/telegrambot/getrestaurants?name={name}&city={city}");

                    if (result.Length < 10)
                    {
                        state.StateObj = null;

                        await _botClient.SendTextMessageAsync(
                            chatId: e.Message.Chat,
                            text: "К сожалению ничего не найдено. Попробуйте /search для поиска по другому названию, или /nearby для поиска поблизости",
                            replyMarkup: new ReplyKeyboardMarkup(new List<KeyboardButton>
                            {
                                new KeyboardButton("/search"),
                                new KeyboardButton("/nearby")
                            }, true, true)
                        );

                        return;
                    }

                    var restaurants =
                        JsonConvert.DeserializeObject<List<CompleteRestaurant>>(JObject.Parse(result)["restaurants"].Value<string>());

                    foreach (var restaurant in restaurants)
                    {
                        await _botClient.SendPhotoAsync(
                            chatId: e.Message.Chat,
                            photo: new InputOnlineFile(GetRestaurantPhoto(restaurant)),
                            replyMarkup: new InlineKeyboardMarkup(new InlineKeyboardButton
                            {
                                Text = $"{restaurant.Name}, {restaurant.Address}",
                                Url = GetRestaurantUrl(restaurant)
                            })
                        );
                    }
                });

            _controller.SetDefaultStep("/start", async (e, state) =>
            {
                await _botClient.SendTextMessageAsync(
                    chatId: e.Message.Chat,
                    text: "Не могу распознать ваш запрос, попробуйте /search для поиска по названию, или /nearby для поиска поблизости",
                    replyMarkup: new ReplyKeyboardMarkup(new List<KeyboardButton>
                    {
                        new KeyboardButton("/search"),
                        new KeyboardButton("/nearby")
                    }, true, true)
                );
            });
            ////////////

            _botClient.OnMessage += Bot_OnMessage;
            _botClient.StartReceiving();
            Thread.Sleep(int.MaxValue);
        }

        static async void Bot_OnMessage(object sender, MessageEventArgs e)
        {
            await _controller.ProcessRequest(e);
        }

        private static string GetRestaurantUrl(ICompleteRestaurant restaurant)
        {
            return $"https://tipmymenu.com/ru/kyiv/restaurant/{restaurant.Url}";
        }

        private static string GetRestaurantPhoto(ICompleteRestaurant restaurant)
        {
            var photos = string.IsNullOrEmpty(restaurant.Photos) ? new List<string>() : Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(restaurant.Photos);
            return $"https://tipmymenu.com/files/{photos.FirstOrDefault()}";
        }
    }

    class Controller
    {
        private Dictionary<long, UserState> _state = new Dictionary<long, UserState>();
        private List<Sequence> _sequences = new List<Sequence>();
        private Step _defaultHandler;

        public Sequence AddSequence()
        {
            var sequence = new Sequence();
            _sequences.Add(sequence);
            return sequence;
        }

        public void SetDefaultStep(string command, Func<MessageEventArgs, UserState, Task> action)
        {
            _defaultHandler = new Step
            {
                Command = command,
                Action = action
            };
        }

        public async Task ProcessRequest(MessageEventArgs e)
        {
            if (e.Message == null) return;

            var command = TryGetCommand(e.Message.Text);
            UserState userState;
            if (!_state.ContainsKey(e.Message.Chat.Id))
            {
                userState = new UserState
                {
                    Command = command ?? "",
                    StateObj = null
                };
                _state.Add(e.Message.Chat.Id, userState);
            }
            else
            {
                userState = _state[e.Message.Chat.Id];
                userState.Command = command ?? userState.Command;
            }

            var processed = false;
            foreach (var sequence in _sequences)
            {
                if(processed) continue;
                processed = await sequence.TryProcessRequest(e, userState);
            }

            if (!processed)
            {
                await _defaultHandler.Action.Invoke(e, userState);
            }
        }

        private string TryGetCommand(string text)
        {
            if (text == null || !text.StartsWith("/")) return null;

            var firstSpace = text.IndexOf(" ", StringComparison.Ordinal);
            return firstSpace == -1 ? text : text.Substring(0, firstSpace);
        }
    }

    class UserState
    {
        public string Command { get; set; }
        public object StateObj { get; set; }
    }

    class Sequence
    {
        public List<Step> Steps { get; set; } = new List<Step>();

        public async Task<bool> TryProcessRequest(MessageEventArgs e, UserState data)
        {
            if (Steps.All(x => x.Command != data.Command)) return false;
            var step = Steps.First(x => x.Command == data.Command);
            var stepIndex = Steps.IndexOf(step);

            if (e.Message.Text != null && data.Command != e.Message.Text && e.Message.Text.Contains(data.Command))
            {
                stepIndex++;
                step = Steps[stepIndex];
            }

            await step.Action.Invoke(e, data);

            data.Command = stepIndex == Steps.Count - 1 ? "" : Steps[stepIndex + 1].Command;

            return true;
        }
    }

    class Step
    {
        public string Command { get; set; }
        public Func<MessageEventArgs, UserState, Task> Action { get; set; }
    }

    static class SequenceExtensions
    {
        public static Sequence Next(this Sequence sequence, string command, Func<MessageEventArgs, UserState, Task> action)
        {
            sequence.Steps.Add(new Step
            {
                Action = action,
                Command = command
            });

            return sequence;
        }
    }
}