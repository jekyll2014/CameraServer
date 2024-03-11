using CameraLib;

using CameraServer.Auth;
using CameraServer.Controllers;
using CameraServer.Models;
using CameraServer.Services.CameraHub;
using CameraServer.Services.MotionDetection;
using CameraServer.Services.VideoRecording;

using Emgu.CV;
using Emgu.CV.Structure;

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
        private const string ExternalHostUriSection = "ExternalHostUrl";
        private const string TelegramStreamId = "telegram";

        private const string SnapShotCommand = "/image";
        private const string SnapShotCommandDescription = "Get picture";
        private const string VideoCommand = "/video";
        private const string VideoCommandDescription = "Get video";
        private const string LinkCommand = "/link";
        private const string LinkCommandDescription = "Get url to a video stream";
        private const string VideoRecordCommand = "/record";
        private const string VideoRecordCommandDescription = "Record video";
        private const string MotionDetectorCommand = "/motion";
        private const string MotionDetectorCommandDescription = "Motion detector";
        private const string RefreshCommand = "/refresh";
        private const string RefreshCommandDescription = "Refresh camera list";

        private const uint VideoRecordMaxTime = 120;
        private readonly char[] _separator = new[] { ' ', ',' };
        private readonly IUserManager _manager;
        private readonly CameraHubService _collection;
        private readonly IServiceProvider _serviceProvider;
        public readonly TelegeramSettings Settings;
        private readonly string _externalHostUrl;
        private CancellationTokenSource? _cts;
        private TelegramBotClient? _botClient;
        private bool _disposedValue;

        public TelegramService(IConfiguration configuration,
            IUserManager manager,
            CameraHubService collection,
            IServiceProvider serviceProvider)
        {
            Settings = configuration.GetSection(TelegramConfigSection)?.Get<TelegeramSettings>() ?? new TelegeramSettings();
            _manager = manager;
            _collection = collection;
            _serviceProvider = serviceProvider;
            _externalHostUrl = configuration.GetValue(ExternalHostUriSection, string.Empty) ?? string.Empty;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(Settings.Token))
            {
                Console.WriteLine("Telefram service not setup.");

                return;
            }

            Console.WriteLine("Starting Telefram service...");
            _cts = new CancellationTokenSource();
            _botClient = new TelegramBotClient(Settings.Token);
            // StartReceiving does not block the caller thread. Receiving is done on the ThreadPool.
            ReceiverOptions receiverOptions = new()
            {
                AllowedUpdates = new UpdateType[] { UpdateType.Message, UpdateType.CallbackQuery } // receive all update types except ChatMember related updates
            };

            try
            {
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
                    Command = SnapShotCommand.TrimStart('/'),
                    Description = SnapShotCommandDescription
                },
                new BotCommand()
                {
                    Command = VideoCommand.TrimStart('/'),
                    Description = VideoCommandDescription
                },
                new BotCommand()
                {
                    Command = LinkCommand.TrimStart('/'),
                    Description = LinkCommandDescription
                },
                new BotCommand()
                {
                    Command = VideoRecordCommand.TrimStart('/'),
                    Description = VideoRecordCommandDescription
                },
                new BotCommand()
                {
                    Command = MotionDetectorCommand.TrimStart('/'),
                    Description = MotionDetectorCommandDescription
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

                var me = await _botClient.GetMeAsync(cancellationToken);
                Console.WriteLine($"...listening for @{me.Username} [{me.Id}]");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"...connection failed: {ex}");
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

        public async Task<Message?> SendText(ChatId chatId,
            string text,
            CancellationToken cancellationToken)
        {
            if (_botClient == null)
                return null;

            Console.WriteLine($"Sending text to [{chatId}]: \"{text}\"");

            try
            {
                return await _botClient.SendTextMessageAsync(chatId, text, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Telegram exception: {ex}");
                return null;
            }
        }

        public async Task<Message?> SendImage(ChatId chatId,
            Mat image,
            string caption,
            CancellationToken cancellationToken)
        {
            if (_botClient == null)
                return null;

            Console.WriteLine($"Sending image to [{chatId}]: \"{caption}\"");
            try
            {
                using (var ms = new MemoryStream())
                {
                    var jpegBuffer = image.ToImage<Rgb, byte>().ToJpegData(Settings.DefaultImageQuality);
                    if (jpegBuffer != null)
                    {
                        await ms.WriteAsync(jpegBuffer, cancellationToken);
                        ms.Position = 0;
                        image.Dispose();
                        var pic = InputFile.FromStream(ms);

                        return await _botClient.SendPhotoAsync(chatId: chatId,
                            photo: pic,
                            caption: caption,
                            cancellationToken: cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Telegram exception: {ex}");
                return null;
            }

            return null;
        }

        public async Task<Message?> SendVideo(ChatId chatId,
            string fileName,
            string caption,
            CancellationToken cancellationToken)
        {
            if (_botClient == null)
                return null;

            Console.WriteLine($"Sending video to [{chatId}]: {fileName} \"{caption}\"");
            try
            {
                await using (var stream = File.OpenRead(fileName))
                {
                    var videoFileStream = InputFile.FromStream(stream, fileName);

                    return await _botClient.SendVideoAsync(
                        chatId: chatId,
                        video: videoFileStream,
                        caption: caption,
                        cancellationToken: cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Telegram exception: {ex}");
                return null;
            }
        }

        public async Task<Message?> SendMenu(ChatId chatId,
            string text,
            IReplyMarkup menu,
            CancellationToken cancellationToken)
        {
            if (_botClient == null)
                return null;

            Console.WriteLine($"Sending nenu to [{chatId}]: \"{text}\"");

            try
            {
                return await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: text,
                    parseMode: ParseMode.Html,
                    replyMarkup: menu,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Telegram exception: {ex}");
                return null;
            }
        }
        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update,
            CancellationToken cancellationToken)
        {
            string messageText;
            ChatId chatId;
            long senderId;
            string senderName;
            if (update.Message is { } message)
            {
                messageText = update.Message.Text ?? string.Empty;
                chatId = message.Chat.Id;
                senderId = message.From?.Id ?? -1;
                senderName = message.From?.Username ?? string.Empty;
            }
            else if (update.CallbackQuery is { } query)
            {
                messageText = query.Data ?? string.Empty;
                chatId = query.Message?.Chat.Id ?? -1;
                senderId = query.From.Id;
                senderName = query.From.Username ?? string.Empty;
            }
            else
            {
                return;
            }

            Console.WriteLine($"Received a '{messageText}' message from \"@{senderName}\"[{senderId}].");

            var currentTelegramUser = _manager.GetUserInfo(senderId);
            if (currentTelegramUser == null)
            {
                await SendText(chatId: chatId, text: $"Non authorized users are not allowed", cancellationToken);

                return;
            }

            // Echo received message text
            await SendText(chatId: chatId, text: $"Requested: \"{messageText}\"", cancellationToken);

            Task.Run(async () =>
            {
                // search for the available cameras
                // return snapshots of the requested cameras
                if (messageText.StartsWith(SnapShotCommand, StringComparison.OrdinalIgnoreCase))
                    await SendImageMessage(chatId, currentTelegramUser, messageText, cancellationToken);
                // return video clip
                else if (messageText.StartsWith(VideoCommand, StringComparison.OrdinalIgnoreCase))
                    await SendVideoMessage(chatId, currentTelegramUser, messageText, cancellationToken);
                // return video stream link
                else if (messageText.StartsWith(LinkCommand, StringComparison.OrdinalIgnoreCase))
                    await SendLinkMessage(chatId, currentTelegramUser, messageText, cancellationToken);
                else if (messageText.StartsWith(VideoRecordCommand, StringComparison.OrdinalIgnoreCase))
                    await ManageVideoRecorder(chatId, currentTelegramUser, messageText, cancellationToken);
                else if (messageText.StartsWith(MotionDetectorCommand, StringComparison.OrdinalIgnoreCase))
                    await ManageMotionDetector(chatId, currentTelegramUser, messageText, cancellationToken);
                else if (messageText.Equals(RefreshCommand, StringComparison.OrdinalIgnoreCase))
                    await RefreshCameraListMessage(chatId, currentTelegramUser, cancellationToken);
                // return help message on unknown command
                else
                    await SendHelpMessage(chatId, cancellationToken);
            }, cancellationToken);
        }

        private async Task SendImageMessage(ChatId chatId,
            ICameraUser user,
            string messageText,
            CancellationToken cancellationToken)
        {
            var tokens = messageText.Split(_separator, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (tokens.Count < 2)
            {
                var buttons = new List<InlineKeyboardButton[]>();
                var buttonsRow = new List<InlineKeyboardButton>();
                var n = 0;
                foreach (var camera in _collection.Cameras
                             .Where(m => m.AllowedRoles
                                 .Intersect(user.Roles)
                                 .Any()))
                {
                    var format = camera.Camera.Description.FrameFormats.MaxBy(n => n.Height * n.Width);
                    buttonsRow.Add(new InlineKeyboardButton($"{n}:{camera.Camera.Description.Name}[{format?.Width ?? 0}x{format?.Height ?? 0}]")
                    {
                        CallbackData = $"{SnapShotCommand} {n}"
                    });
                    buttons.Add(buttonsRow.ToArray());
                    buttonsRow.Clear();
                    n++;
                }

                var inline = new InlineKeyboardMarkup(buttons);
                await SendMenu(chatId, "Get image from camera:", inline, cancellationToken);
            }
            else if (tokens.Count >= 2)
            {
                var cameraNumber = tokens[1];
                if (!int.TryParse(cameraNumber, out var n))
                    return;

                ServerCamera camera;
                try
                {
                    camera = _collection.GetCamera(n, user);
                }
                catch (Exception ex)
                {
                    await SendText(chatId: chatId, text: ex.Message, cancellationToken);

                    return;
                }

                var image = await camera.Camera.GrabFrame(cancellationToken);
                if (image != null)
                    await SendImage(chatId, image, $"Camera[{n}]: {camera.Camera.Description.Name}", cancellationToken);
                else
                    await SendText(chatId: chatId, text: $"Can't get image from camera: \"{n}\"", cancellationToken);
            }
            else
                await SendText(chatId: chatId, text: $"Incorrect command", cancellationToken);
        }

        private async Task SendVideoMessage(ChatId chatId,
            ICameraUser user,
            string messageText,
            CancellationToken cancellationToken)
        {
            if (_botClient == null)
                return;

            var tokens = messageText.Split(_separator, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (tokens.Count < 3)
            {
                var buttons = new List<InlineKeyboardButton[]>();
                var buttonsRow = new List<InlineKeyboardButton>();
                var cameraNumber = 0;
                foreach (var camera in _collection.Cameras
                             .Where(m => m.AllowedRoles
                                 .Intersect(user.Roles)
                                 .Any()))
                {
                    var format = camera.Camera.Description.FrameFormats.MaxBy(n => n.Height * n.Width);
                    buttonsRow.Add(new InlineKeyboardButton($"{cameraNumber}:{camera.Camera.Description.Name}[{format?.Width ?? 0}x{format?.Height ?? 0}]")
                    {
                        CallbackData = $"{VideoCommand} {cameraNumber} {Settings.DefaultVideoTime}"
                    });
                    buttons.Add(buttonsRow.ToArray());
                    buttonsRow.Clear();
                    cameraNumber++;
                }

                var inline = new InlineKeyboardMarkup(buttons);
                await SendMenu(chatId, "Get video from camera:", inline, cancellationToken);
            }
            else if (tokens.Count >= 3
                    && int.TryParse(tokens[1], out var cameraNumber)
                    && uint.TryParse(tokens[2], out var recordTime))
            {
                ServerCamera camera;
                try
                {
                    camera = _collection.GetCamera(cameraNumber, user);
                }
                catch (Exception ex)
                {
                    await SendText(chatId, ex.Message, cancellationToken);

                    return;
                }

                if (recordTime <= 0)
                    return;

                if (recordTime >= VideoRecordMaxTime)
                    recordTime = VideoRecordMaxTime;

                try
                {
                    if (_serviceProvider.GetService(typeof(VideoRecorderService)) is not VideoRecorderService videoRecorderService)
                        return;

                    var queueId = $"{TelegramStreamId}-{chatId}";
                    var fileName = await videoRecorderService.RecordVideoFile(camera,
                        queueId,
                        TelegramStreamId,
                        recordTime,
                        null,
                        Settings.DefaultVideoQuality);
                    await SendVideo(chatId, fileName, $"Camera#{cameraNumber} record",
                        cancellationToken: cancellationToken);
                    File.Delete(fileName);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Can not record video for Telegram user {chatId}: {ex}");
                }
            }
            else
                await SendText(chatId, $"Incorrect command", cancellationToken);
        }

        private async Task SendLinkMessage(ChatId chatId,
            ICameraUser user,
            string messageText,
            CancellationToken cancellationToken)
        {
            var tokens = messageText.Split(_separator, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (tokens.Count < 2)
            {
                var buttons = new List<InlineKeyboardButton[]>();
                var buttonsRow = new List<InlineKeyboardButton>();
                var n = 0;
                foreach (var camera in _collection.Cameras
                             .Where(m => m.AllowedRoles
                                 .Intersect(user.Roles)
                                 .Any()))
                {
                    var format = camera.Camera.Description.FrameFormats.MaxBy(n => n.Height * n.Width);
                    buttonsRow.Add(new InlineKeyboardButton($"{n}:{camera.Camera.Description.Name}[{format?.Width ?? 0}x{format?.Height ?? 0}]")
                    {
                        CallbackData = $"{LinkCommand} {n}"
                    });
                    buttons.Add(buttonsRow.ToArray());
                    buttonsRow.Clear();
                    n++;
                }

                var inline = new InlineKeyboardMarkup(buttons);
                await SendMenu(chatId, "Get Url for camera:", inline, cancellationToken);
            }
            else if (tokens.Count >= 2)
            {
                var cameraNumber = tokens[1];
                if (!int.TryParse(cameraNumber, out var n))
                    return;

                ServerCamera camera;
                try
                {
                    camera = _collection.GetCamera(n, user);
                }
                catch (Exception ex)
                {
                    await SendText(chatId, ex.Message, cancellationToken);

                    return;
                }

                var linklabel = $"Url: {camera.Camera.Description.Name}";
                var linkUrl = _externalHostUrl.Trim('/') + CameraController.GenerateCameraUrl(n);
                var keyboard = new InlineKeyboardMarkup(InlineKeyboardButton.WithUrl(linklabel, linkUrl));
                await SendMenu(
                     chatId,
                     "Link to video stream:",
                     keyboard,
                     cancellationToken);
            }
            else
            {
                await SendText(chatId, $"Incorrect command", cancellationToken);
            }
        }

        //{VideoRecordCommand} [n] [start/stop]
        private async Task ManageVideoRecorder(ChatId chatId,
            ICameraUser user,
            string messageText,
            CancellationToken cancellationToken)
        {
            if (_botClient == null)
                return;

            if (_serviceProvider.GetService(typeof(VideoRecorderService)) is not VideoRecorderService videoRecorderService)
                return;

            var tokens = messageText.Split(_separator, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (tokens.Count < 2)
            {
                var buttons = new List<InlineKeyboardButton[]>();
                var buttonsRow = new List<InlineKeyboardButton>();
                var cameraNumber = 0;
                foreach (var camera in _collection.Cameras
                             .Where(m => m.AllowedRoles
                                 .Intersect(user.Roles)
                                 .Any()))
                {
                    var taskId = VideoRecorderService.GenerateTaskId(camera.Camera.Description.Path, 0, 0);
                    var running = videoRecorderService.TaskList.Any(n => n == taskId) ? "running" : "stopped";
                    var action = videoRecorderService.TaskList.Any(n => n == taskId) ? "stop" : "start";
                    buttonsRow.Add(new InlineKeyboardButton($"[{running}] {cameraNumber}:{camera.Camera.Description.Name}")
                    {
                        CallbackData = $"{VideoRecordCommand} {cameraNumber} {action}"
                    });
                    buttons.Add(buttonsRow.ToArray());
                    buttonsRow.Clear();
                    cameraNumber++;
                }

                var inline = new InlineKeyboardMarkup(buttons);
                await SendMenu(chatId, "Start/stop video record for camera:", inline, cancellationToken);
            }
            else if (tokens.Count >= 3)
            {
                if (!int.TryParse(tokens[1], out var cameraNumber))
                    return;

                ServerCamera camera;
                try
                {
                    camera = _collection.GetCamera(cameraNumber, user);
                }
                catch (Exception ex)
                {
                    await SendText(chatId, ex.Message, cancellationToken);

                    return;
                }

                var message = "Incorrect command";
                if (tokens[2] == "start")
                {
                    try
                    {
                        videoRecorderService.Start(camera.Camera.Description.Path, user.Login, new FrameFormatDto(),
                            95);
                        message = $"Record started for camera {camera.Camera.Description.Name}";
                    }
                    catch (Exception ex)
                    {
                        message = $"Can't start record: {ex.Message}";
                    }

                }
                else if (tokens[2] == "stop")
                {
                    var taskId = VideoRecorderService.GenerateTaskId(camera.Camera.Description.Path, 0, 0);
                    videoRecorderService.Stop(taskId);
                    message = $"Record stopped for camera {camera.Camera.Description.Name}";
                }

                await SendText(chatId, message, cancellationToken);
            }
            else
            {
                await SendText(chatId, $"Incorrect command", cancellationToken);
            }
        }

        //{MotionDetectorCommand} [n] [start/stop] [text/image/video]
        private async Task ManageMotionDetector(ChatId chatId,
            ICameraUser user,
            string messageText,
            CancellationToken cancellationToken)
        {
            if (_botClient == null)
                return;

            if (_serviceProvider.GetService(typeof(MotionDetectionService)) is not MotionDetectionService motionDetectionService)
                return;

            var tokens = messageText.Split(_separator, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (tokens.Count < 2)
            {
                var buttons = new List<InlineKeyboardButton[]>();
                var buttonsRow = new List<InlineKeyboardButton>();
                var cameraNumber = 0;
                foreach (var camera in _collection.Cameras
                             .Where(m => m.AllowedRoles
                                 .Intersect(user.Roles)
                                 .Any()))
                {
                    var taskId = MotionDetectionService.GenerateTaskId(camera.Camera.Description.Path, user.Login);
                    var running = motionDetectionService.TaskList.Any(n => n == taskId) ? "running" : "stopped";
                    var action = motionDetectionService.TaskList.Any(n => n == taskId) ? " stop" : " start";
                    buttonsRow.Add(new InlineKeyboardButton($"[{running}] {cameraNumber}:{camera.Camera.Description.Name}")
                    {
                        CallbackData = $"{MotionDetectorCommand} {cameraNumber}{action}"
                    });
                    buttons.Add(buttonsRow.ToArray());
                    buttonsRow.Clear();
                    cameraNumber++;
                }

                var inline = new InlineKeyboardMarkup(buttons);
                await SendMenu(chatId, "Start/stop motion detector for camera:", inline, cancellationToken);
            }
            else if (tokens.Count == 3)
            {
                var buttons = new List<InlineKeyboardButton[]>();
                var buttonsRow = new List<InlineKeyboardButton>();
                int.TryParse(tokens[1], out var cameraNumber);

                ServerCamera camera;
                try
                {
                    camera = _collection.GetCamera(cameraNumber, user);
                }
                catch (Exception ex)
                {
                    await SendText(chatId, ex.Message, cancellationToken);

                    return;
                }

                var taskId = MotionDetectionService.GenerateTaskId(camera.Camera.Description.Path, user.Login);

                if (tokens[2] == "stop")
                {
                    motionDetectionService.Stop(taskId);
                    await SendText(chatId, $"Motion detect stopped for camera {camera.Camera.Description.Name}", cancellationToken);
                }
                else if (tokens[2] == "start")
                {
                    var started = motionDetectionService.TaskList.Any(n => n == taskId) ? "stop" : "start";
                    buttonsRow.Add(new InlineKeyboardButton("text")
                    {
                        CallbackData = $"{MotionDetectorCommand} {cameraNumber} {started} text"
                    });
                    buttons.Add(buttonsRow.ToArray());
                    buttonsRow.Clear();

                    buttonsRow.Add(new InlineKeyboardButton("image")
                    {
                        CallbackData = $"{MotionDetectorCommand} {cameraNumber} {started} image"
                    });
                    buttons.Add(buttonsRow.ToArray());
                    buttonsRow.Clear();

                    buttonsRow.Add(new InlineKeyboardButton("video")
                    {
                        CallbackData = $"{MotionDetectorCommand} {cameraNumber} {started} video"
                    });
                    buttons.Add(buttonsRow.ToArray());
                    buttonsRow.Clear();

                    var inline = new InlineKeyboardMarkup(buttons);
                    await SendMenu(chatId, "Send notification on movement detection as:", inline, cancellationToken);
                }
                else
                    await SendText(chatId, $"Incorrect command", cancellationToken);
            }
            else if (tokens.Count >= 4)
            {
                var cameraNumber = tokens[1];
                if (!int.TryParse(cameraNumber, out var n))
                    return;

                ServerCamera camera;
                try
                {
                    camera = _collection.GetCamera(n, user);
                }
                catch (Exception ex)
                {
                    await SendText(chatId, ex.Message, cancellationToken);

                    return;
                }

                var message = "Incorrect command";
                if (tokens[2] == "start")
                {
                    var messageType = MotionDetection.MessageType.Text;
                    if (tokens[3] == "image")
                        messageType = MotionDetection.MessageType.Image;
                    else if (tokens[3] == "video")
                        messageType = MotionDetection.MessageType.Video;

                    try
                    {
                        motionDetectionService.Start(camera.Camera.Description.Path,
                            user.Login,
                            new FrameFormatDto(),
                            motionDetectionService.Settings.DefaultMotionDetectParameters,
                            new List<NotificationParameters>()
                            {
                            new NotificationParameters()
                            {
                                Message = $"Movement detected at camera {camera.Camera.Description.Name}",
                                MessageType = messageType,
                                Destination = chatId.ToString(),
                                Transport = NotificationTransport.Telegram,
                                VideoLengthSec = Settings.DefaultVideoTime
                            }
                            });

                        message = $"Motion detect started for camera {camera.Camera.Description.Name}";
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                        message = $"Can't start motion detector {ex.Message}";
                    }
                }
                else if (tokens[2] == "stop")
                {
                    var taskId = MotionDetectionService.GenerateTaskId(camera.Camera.Description.Path, user.Login);
                    motionDetectionService.Stop(taskId);
                    message = $"Motion detect stopped for camera {camera.Camera.Description.Name}";
                }

                await SendText(chatId, message, cancellationToken);
            }
            else
            {
                await SendText(chatId, $"Incorrect command", cancellationToken);
            }
        }

        private async Task RefreshCameraListMessage(ChatId chatId,
            ICameraUser user,
            CancellationToken cancellationToken)
        {
            if (user.Roles.Contains(Roles.Admin))
            {
                await _collection.RefreshCameraCollection(cancellationToken);
                await SendText(chatId, "Camera list refreshed", cancellationToken);
            }
            else
            {
                await SendText(chatId, "Only allowed to Admin", cancellationToken);
            }
        }

        private async Task SendHelpMessage(ChatId chatId,
            CancellationToken cancellationToken)
        {
            await SendText(chatId,
                 $"Usage tips:\r\n" +
                      $"\t{SnapShotCommand} n - get image from camera[n]\r\n" +
                      $"\t{VideoCommand} n s - get video from camera [n], duration [s] seconds" +
                      $"\t{LinkCommand} n s - get url of the video from camera [n]" +
                      $"\t{VideoRecordCommand} n k - video record from camera [n] k=[start/stop]" +
                      $"\t{MotionDetectorCommand} n k m - motion detection camera [n] k=[start/stop] reporting with m=[text/image/video]" +
                      $"\t{RefreshCommand} - refresh camera list on the server\r\n",
                 cancellationToken);
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient,
            Exception exception,
            CancellationToken cancellationToken)
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiRequestException
                    => $"Telegram API Error: [{apiRequestException.ErrorCode}] {apiRequestException.Message}",
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
