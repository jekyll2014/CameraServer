using CameraServer.Auth;
using CameraServer.Models;
using CameraServer.Services.Helpers;

using Emgu.CV;
using Emgu.CV.Structure;

using System.Collections.Concurrent;

using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

using File = System.IO.File;

namespace CameraServer.Services.Telegram
{
    public class TelegramService : IHostedService, IDisposable
    {
        private const string TelegramConfigSection = "Telegram";
        private const string StartCommand = "/start";
        private const string StartCommandDescription = "start new command";
        private const string RefreshCommand = "/refresh";
        private const string RefreshCommandDescription = "refresh camera list";
        private const string SnapShotCommand = "/image";
        private const string SnapShotCommandDescription = "get picture";
        private const string VideoRecordCommand = "/video";
        private const string VideoRecordCommandDescription = "get video";
        private const int VideoRecordMaxTime = 120;
        private readonly char[] _separator = new char[] { ' ', ',' };
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
                AllowedUpdates = new UpdateType[] { UpdateType.Message, UpdateType.CallbackQuery } // receive all update types except ChatMember related updates
            };

            if (!await _botClient.TestApiAsync(cancellationToken))
            {
                Console.WriteLine($"Telegram connection failed.");
                _botClient = null;

                return;
            }

            await _botClient.SetMyCommandsAsync(new[]
            {
                new BotCommand()
                {
                    Command = StartCommand.TrimStart('/'),
                    Description = StartCommandDescription
                },
                new BotCommand()
                {
                    Command = SnapShotCommand.TrimStart('/'),
                    Description = SnapShotCommandDescription
                },
                new BotCommand()
                {
                    Command = VideoRecordCommand.TrimStart('/'),
                    Description = VideoRecordCommandDescription
                },
                new BotCommand()
                {
                    Command = RefreshCommand.TrimStart('/'),
                    Description = RefreshCommandDescription
                }
            }, cancellationToken: cancellationToken);

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
                _cts.Cancel();

            if (_botClient != null)
                await _botClient.CloseAsync(cancellationToken);

            _cts?.Dispose();
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update,
            CancellationToken cancellationToken)
        {
            var messageText = "";
            long chatId;
            long senderId;
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

            var currentTelegramUser = _settings.AutorizedUsers
                .Find(n => n.UserId == senderId)
                                      ?? new TelegeramUser(senderId)
                                      {
                                          Roles = _settings.DefaultRoles
                                      };

            // Echo received message text
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"Requested: \"{messageText}\"",
                cancellationToken: cancellationToken);

            Task.Run(async () =>
            {
                // search for the available cameras
                if (messageText.Equals(RefreshCommand, StringComparison.OrdinalIgnoreCase)
                    && currentTelegramUser.Roles.Contains(Roles.Admin))
                {
                    await _collection.RefreshCameraCollection(cancellationToken);

                    messageText = StartCommand;
                }

                // generate new command
                if (messageText.Equals(StartCommand, StringComparison.OrdinalIgnoreCase))
                {
                    var buttons = new List<InlineKeyboardButton[]>
                    {
                        new InlineKeyboardButton[]
                        {
                            new ("Get picture")
                            {
                                CallbackData = SnapShotCommand
                            }
                        },
                        new InlineKeyboardButton[]
                        {
                            new ("Get video")
                            {
                                CallbackData = VideoRecordCommand
                            }
                        }
                    };

                    if (currentTelegramUser.Roles.Contains(Roles.Admin))
                    {
                        buttons.Add(new InlineKeyboardButton[]
                        {
                            new ("Referesh camera list")
                            {
                                CallbackData = RefreshCommand
                            }
                        });
                    }

                    var inline = new InlineKeyboardMarkup(buttons);

                    _botClient?.SendTextMessageAsync(chatId, "Camera list", null, parseMode: ParseMode.Html,
                        replyMarkup: inline, cancellationToken: cancellationToken);

                    return;
                }

                // return snapshots of the requested cameras
                if (messageText.StartsWith(SnapShotCommand, StringComparison.OrdinalIgnoreCase))
                {
                    var tokens = messageText.Split(_separator, StringSplitOptions.RemoveEmptyEntries).ToList();
                    if (tokens.Count < 2)
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
                            buttonsRow.Add(new InlineKeyboardButton($"{n}:{camera.Camera.Description.Name}[{format?.Width ?? 0}x{format?.Heigth ?? 0}]")
                            {
                                CallbackData = $"{SnapShotCommand} {n}"
                            });
                            buttons.Add(buttonsRow.ToArray());
                            buttonsRow.Clear();
                            n++;
                        }

                        if (currentTelegramUser.Roles.Contains(Roles.Admin))
                        {
                            buttons.Add(new InlineKeyboardButton[]
                            {
                                new ("Referesh")
                                {
                                    CallbackData = RefreshCommand
                                }
                            });
                        }

                        var inline = new InlineKeyboardMarkup(buttons);

                        _botClient?.SendTextMessageAsync(chatId, "Get image from camera:", null, parseMode: ParseMode.Html,
                            replyMarkup: inline, cancellationToken: cancellationToken);
                    }
                    else if (tokens.Count >= 2)
                    {
                        var cameraNumber = tokens[1];
                        tokens.RemoveAt(0);
                        if (!int.TryParse(cameraNumber, out var n))
                            return;

                        ServerCamera camera;
                        try
                        {
                            camera = GetCamera(n, currentTelegramUser);
                        }
                        catch (Exception ex)
                        {
                            await botClient.SendTextMessageAsync(
                                chatId: chatId,
                                text: ex.Message,
                                cancellationToken: cancellationToken);

                            return;
                        }

                        var image = await camera.Camera.GrabFrame(cancellationToken);
                        if (image != null)
                        {
                            using (var ms = new MemoryStream())
                            {
                                var jpegBuffer = image.ToImage<Rgb, byte>().ToJpegData();
                                await ms.WriteAsync(jpegBuffer, cancellationToken);
                                ms.Position = 0;
                                image.Dispose();
                                var pic = InputFile.FromStream(ms);

                                await botClient.SendPhotoAsync(
                                    chatId: chatId,
                                    photo: pic,
                                    caption: $"Camera[{n}]: {camera.Camera.Description.Name}",
                                    cancellationToken: cancellationToken);
                            }

                            image.Dispose();
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(
                                chatId: chatId,
                                text: $"Can't get image from camera: \"{n}\"",
                                cancellationToken: cancellationToken);
                        }
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: $"Incorrect command",
                            cancellationToken: cancellationToken);
                    }

                    return;
                }

                // return video clip
                if (messageText.StartsWith(VideoRecordCommand, StringComparison.OrdinalIgnoreCase))
                {
                    var tokens = messageText.Split(_separator, StringSplitOptions.RemoveEmptyEntries).ToList();
                    if (tokens.Count < 2)
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
                            buttonsRow.Add(new InlineKeyboardButton($"{n}:{camera.Camera.Description.Name}[{format?.Width ?? 0}x{format?.Heigth ?? 0}]")
                            {
                                CallbackData = $"{VideoRecordCommand} {n} {_settings.DefaultVideoTime}"
                            });
                            buttons.Add(buttonsRow.ToArray());
                            buttonsRow.Clear();
                            n++;
                        }

                        var inline = new InlineKeyboardMarkup(buttons);
                        _botClient?.SendTextMessageAsync(chatId, "Get video from camera:", null, parseMode: ParseMode.Html,
                            replyMarkup: inline, cancellationToken: cancellationToken);
                    }
                    else if (tokens.Count >= 2
                            && int.TryParse(tokens[1], out var cameraNumber)
                            && int.TryParse(tokens[2], out var recordTime))
                    {
                        ServerCamera camera;
                        try
                        {
                            camera = GetCamera(cameraNumber, currentTelegramUser);
                        }
                        catch (Exception ex)
                        {
                            await botClient.SendTextMessageAsync(
                                chatId: chatId,
                                text: ex.Message,
                                cancellationToken: cancellationToken);

                            return;
                        }

                        var userId = $"{currentTelegramUser.UserId}{DateTime.Now.Ticks}";
                        var imageQueue = new ConcurrentQueue<Mat>();
                        var cameraCancellationToken = await _collection.HookCamera(camera.Camera.Description.Path,
                            userId,
                            imageQueue,
                            0,
                            0,
                            "");
                        if (cameraCancellationToken == CancellationToken.None)
                        {
                            await botClient.SendTextMessageAsync(
                                chatId: chatId,
                                text: $"Can not connect to camera#{cameraNumber}",
                                cancellationToken: cancellationToken);

                            return;
                        }

                        if (recordTime <= 0)
                            return;

                        if (recordTime >= VideoRecordMaxTime)
                            recordTime = VideoRecordMaxTime;

                        //record video
                        var fileName = $"{userId}.mp4";
                        try
                        {
                            var fps = camera.Camera.Description.FrameFormats.FirstOrDefault()?.Fps ?? -1;
                            var recorder = new VideoRecorder(fileName, fps, 90);
                            var timeOut = DateTime.Now.AddSeconds(recordTime);
                            while (DateTime.Now < timeOut && !cancellationToken.IsCancellationRequested)
                            {
                                if (imageQueue.TryDequeue(out var image))
                                {
                                    recorder.SaveFrame(image);
                                    image.Dispose();
                                }
                                else
                                    await Task.Delay(10, CancellationToken.None);
                            }

                            recorder.Stop();
                            recorder.Dispose();
                            var stream = File.OpenRead(fileName);

                            var videoFile = InputFile.FromStream(stream,
                                $"Cam{cameraNumber}-{DateTime.Now.ToShortTimeString().Replace(':', '-')}");
                            await botClient.SendVideoAsync(
                                chatId: chatId,
                                video: videoFile,
                                caption: $"Camera#{cameraNumber} record", cancellationToken: cancellationToken);
                            await stream.DisposeAsync();

                            File.Delete(fileName);
                        }
                        catch
                        {
                        }

                        await _collection.UnHookCamera(camera.Camera.Description.Path, userId);
                        while (imageQueue.TryDequeue(out var image))
                        {
                            image.Dispose();
                        }

                        imageQueue.Clear();
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: $"Incorrect command",
                            cancellationToken: cancellationToken);
                    }

                    return;
                }

                // return help message on unknown command
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: $"Usage tips:\r\n" +
                          $"\t{StartCommand} - generate new command\r\n" +
                          $"\t{RefreshCommand} - refresh camera list on the server\r\n" +
                          $"\t{SnapShotCommand} n - get image from camera[n]\r\n" +
                          $"\t{VideoRecordCommand} n s - get video from camera [n], duration [s] seconds",
                    cancellationToken: cancellationToken);
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

        private ServerCamera GetCamera(int cameraNumber, TelegeramUser currentTelegramUser)
        {
            if (cameraNumber < 0 || cameraNumber >= _collection.Cameras.Count())
                throw new ArgumentOutOfRangeException($"No camera available: \"{cameraNumber}\"");

            var camera = _collection.Cameras.ToArray()[cameraNumber];
            if (!camera.AllowedRoles.Intersect(currentTelegramUser.Roles).Any())
                throw new ArgumentOutOfRangeException($"No camera available: \"{cameraNumber}\"");

            return camera;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    if (!(_cts?.IsCancellationRequested ?? true))
                        _cts?.Cancel();

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
