using CameraServer.Auth;

using System.Drawing.Imaging;

using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace CameraServer.Services.Telegram
{
    public class TelegramService : IHostedService, IDisposable
    {
        private const string TelegramConfigSection = "Telegram";
        private const string ListCommand = "?";
        private const string RefreshCommand = "RefreshCameraList";
        private readonly char[] _separator = [' ', ','];
        private readonly CameraHub.CameraHubService _collection;
        private readonly TelegeramSettings _settings;
        private CancellationTokenSource? _cts;
        private TelegramBotClient? _botClient;
        private bool _disposedValue;

        public TelegramService(IConfiguration configuration, CameraHub.CameraHubService collection)
        {
            _collection = collection;
            _settings = configuration.GetSection(TelegramConfigSection)?.Get<TelegeramSettings>() ?? new TelegeramSettings();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (_settings == null || string.IsNullOrEmpty(_settings.Token))
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _botClient = new TelegramBotClient(_settings.Token);
            // StartReceiving does not block the caller thread. Receiving is done on the ThreadPool.
            ReceiverOptions receiverOptions = new()
            {
                AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery] // receive all update types except ChatMember related updates
            };

            if (!await _botClient.TestApiAsync(cancellationToken))
            {
                Console.WriteLine($"Telegram connection failed.");
                _botClient = null;

                return;
            }

            _botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                pollingErrorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: _cts.Token
            );

            try
            {
                var me = await _botClient.GetMeAsync(cancellationToken);
                Console.WriteLine($"Start listening for @{me.Username}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Telegram connection failed: {ex}");
                if (ex is ApiRequestException apiEx && apiEx.ErrorCode == 401)
                {
                    Console.WriteLine("Check your \"Telegram\": { \"Token\" } .");
                }
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_cts != null)
                await _cts.CancelAsync();

            if (_botClient != null)
                await _botClient.CloseAsync(cancellationToken);

            _cts?.Dispose();
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update,
            CancellationToken cancellationToken)
        {
            var messageText = "";
            long chatId = -1;
            long senderId = -1;
            var senderName = "";
            if (update.Message is { } message)
            {
                messageText = update.Message.Text ?? "";
                chatId = message.Chat.Id;
                senderId = message.From?.Id ?? -1;
                senderName = message.From?.Username ?? "";
            }
            else if (update.CallbackQuery is { } query)
            {
                messageText = query.Data ?? "";
                chatId = query.Message?.Chat.Id ?? -1;
                senderId = query.From.Id;
                senderName = query.From.Username ?? "";
            }
            else
            {
                return;
            }

            Console.WriteLine($"Received a '{messageText}' message from \"@{senderName}\"[{senderId}].");

            var currentTelegramUser = _settings?.AutorizedUsers
                .Find(n => n.UserId == senderId)
                                      ?? new TelegeramUser(senderId)
                                      {
                                          Roles = _settings?.DefaultRoles ?? []
                                      };

            // Echo received message text
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"Requested: \"{messageText}\"",
                cancellationToken: cancellationToken);

            Task.Run(async () =>
            {
                // search for the available cameras
                if (messageText == RefreshCommand && currentTelegramUser.Roles.Contains(Roles.Admin))
                {
                    await _collection.RefreshCameraCollection(cancellationToken);

                    messageText = ListCommand;
                }

                // return cameras list as virtual keyboard
                if (messageText == ListCommand)
                {
                    var buttons = new List<InlineKeyboardButton[]>();
                    var buttonsRow = new List<InlineKeyboardButton>();
                    var n = 0;
                    foreach (var camera in _collection.Cameras
                                 .Where(m => m.AllowedRoles
                                     .Intersect(currentTelegramUser.Roles)
                                     .Any()))
                    {
                        var format = camera.Camera.Description.FrameFormats.MaxBy(n => n.Heigth * n.Width);
                        buttonsRow.Add(new InlineKeyboardButton($"{n}:{camera.Camera.Name}[{format?.Width ?? 0}x{format?.Heigth ?? 0}]")
                        {
                            CallbackData = n.ToString()
                        });
                        buttons.Add(buttonsRow.ToArray());
                        buttonsRow.Clear();
                        n++;
                    }

                    if (currentTelegramUser.Roles.Contains(Roles.Admin))
                    {
                        buttons.Add([new InlineKeyboardButton("Referesh")
                    {
                        CallbackData = RefreshCommand
                    }]);
                    }

                    var inline = new InlineKeyboardMarkup(buttons);

                    _botClient?.SendTextMessageAsync(chatId, "Camera list", null, parseMode: ParseMode.Html,
                        replyMarkup: inline, cancellationToken: cancellationToken);

                    return;
                }

                // return help message on unknown command
                if (!messageText.All(n => char.IsDigit(n) || _separator.Contains(n)))
                {
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: $"""
                               Usage tips:
                               "{ListCommand}" - camera list
                               "{RefreshCommand}" - refresh camera list on the server
                               n - get image from camera[n]
                               "n,m" - get images from cameras[n] and [m]
                               "n m" - get images from cameras [n] and[m]
                               """,
                        cancellationToken: cancellationToken);

                    return;
                }

                // return snapshots of the requested cameras
                var cameraNumbers = messageText.Split(_separator, StringSplitOptions.RemoveEmptyEntries).ToList();
                while (cameraNumbers.Count != 0)
                {
                    var cameraNumber = cameraNumbers[0];
                    cameraNumbers.RemoveAt(0);
                    if (!int.TryParse(cameraNumber, out var n))
                        continue;

                    if (n < 0 || n >= _collection.Cameras.Count())
                    {
                        await botClient.SendTextMessageAsync(
                           chatId: chatId,
                           text: $"No camera available: \"{n}\"",
                           cancellationToken: cancellationToken);

                        continue;
                    }

                    var camera = _collection.Cameras.ToArray()[n];
                    if (!camera.AllowedRoles.Intersect(currentTelegramUser.Roles).Any())
                    {
                        await botClient.SendTextMessageAsync(
                           chatId: chatId,
                           text: $"No camera available: \"{n}\"",
                           cancellationToken: cancellationToken);

                        continue;
                    }

                    var image = await camera.Camera.GrabFrame(cancellationToken);
                    if (image != null)
                    {
                        using (var ms = new MemoryStream())
                        {
                            image.Save(ms, ImageFormat.Jpeg);
                            image.Dispose();
                            ms.Position = 0;
                            var pic = InputFile.FromStream(ms);

                            await botClient.SendPhotoAsync(
                               chatId: chatId,
                               photo: pic,
                               caption: $"Camera[{n}]: {camera.Camera.Name}",
                               cancellationToken: cancellationToken);
                        }
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(
                          chatId: chatId,
                          text: $"Can't get image from camera: \"{n}\"",
                          cancellationToken: cancellationToken);
                    }
                }
            });
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiRequestException
                    => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.WriteLine(errorMessage);

            return Task.CompletedTask;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    if (!(_cts?.IsCancellationRequested ?? true))
                        _cts.Cancel();

                    _botClient?.CloseAsync();
                    _botClient = null;
                    _cts?.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
