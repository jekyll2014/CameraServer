using CameraLib;

using CameraServer.Auth;
using CameraServer.Controllers;
using CameraServer.Models;
using CameraServer.Services.MotionDetection;
using CameraServer.Services.VideoRecorder;

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
        private const string ExternalHostUriSection = "ExternalHostUri";
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
        private readonly CameraHub.CameraHubService _collection;
        private readonly VideoRecorderService _videoRecorderService;
        private readonly IServiceProvider _serviceProvider;
        private readonly TelegeramSettings _settings;
        private readonly string _externalHostUri;
        private CancellationTokenSource? _cts;
        private TelegramBotClient? _botClient;
        private bool _disposedValue;

        public TelegramService(IConfiguration configuration,
            IUserManager manager,
            CameraHub.CameraHubService collection,
            VideoRecorderService videoRecorderService,
            IServiceProvider serviceProvider)
        {
            _settings = configuration.GetSection(TelegramConfigSection)?.Get<TelegeramSettings>() ?? new TelegeramSettings();
            _manager = manager;
            _collection = collection;
            _videoRecorderService = videoRecorderService;
            _serviceProvider = serviceProvider;
            _externalHostUri = configuration.GetValue(ExternalHostUriSection, string.Empty) ?? string.Empty;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_settings.Token))
            {
                Console.WriteLine("Telefram service not setup.");

                return;
            }

            Console.WriteLine("Starting Telefram service...");
            _cts = new CancellationTokenSource();
            _botClient = new TelegramBotClient(_settings.Token);
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
                    var jpegBuffer = image.ToImage<Rgb, byte>().ToJpegData();
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
            catch (Exception ex)
            {
                Console.WriteLine($"Telegram exception: {ex}");
                return null;
            }
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
            long chatId;
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
                currentTelegramUser = new UserDto()
                {
                    TelegramId = senderId,
                    Roles = _settings.DefaultRoles
                };
            }

            // Echo received message text
            await SendText(chatId: chatId, text: $"Requested: \"{messageText}\"", cancellationToken);

            await Task.Run(async () =>
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
                else if (messageText.Equals(RefreshCommand, StringComparison.OrdinalIgnoreCase)
                    && currentTelegramUser.Roles.Contains(Roles.Admin))
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
                        CallbackData = $"{VideoCommand} {cameraNumber} {_settings.DefaultVideoTime}"
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

                var queueId = $"{TelegramStreamId}-{chatId}";
                var frameFormat = new FrameFormatDto();
                var imageQueue = new ConcurrentQueue<Mat>();
                var cameraCancellationToken = await _collection.HookCamera(camera.Camera.Description.Path,
                    queueId,
                    imageQueue,
                    frameFormat);
                if (cameraCancellationToken == CancellationToken.None)
                {
                    await SendText(chatId, $"Can not connect to camera#{cameraNumber}", cancellationToken);

                    return;
                }

                if (recordTime <= 0)
                    return;

                if (recordTime >= VideoRecordMaxTime)
                    recordTime = VideoRecordMaxTime;

                //record video
                var currentTime = DateTime.Now;
                var fileName = VideoRecorder.VideoRecorder.SanitizeFileName($"{TelegramStreamId}-" +
                                                                            $"{chatId}-" +
                                                                            $"Cam{cameraNumber}-" +
                                                                            $"{currentTime.ToString("yyyy-MM-dd")}-" +
                                                                            $"{currentTime.ToString("HH-mm-ss")}.mp4");
                try
                {
                    var fps = camera.Camera.Description.FrameFormats.FirstOrDefault()?.Fps ?? 0;
                    using (var recorder = new VideoRecorder.VideoRecorder(fileName,
                               new FrameFormatDto { Width = 0, Height = 0, Format = string.Empty, Fps = fps }))
                    {
                        var timeOut = DateTime.Now.AddSeconds(recordTime);
                        while (DateTime.Now < timeOut && !cancellationToken.IsCancellationRequested)
                        {
                            if (imageQueue.TryDequeue(out var image))
                            {
                                recorder.SaveFrame(image);
                                image.Dispose();
                            }
                            else
                                await Task.Delay(10);
                        }

                        await _collection.UnHookCamera(camera.Camera.Description.Path, queueId, frameFormat);
                    }

                    await SendVideo(chatId, fileName, $"Camera#{cameraNumber} record",
                        cancellationToken: cancellationToken);
                    File.Delete(fileName);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Can not record video for Telegram user {chatId}: {ex}");
                }
                finally
                {
                    while (imageQueue.TryDequeue(out var image))
                    {
                        image.Dispose();
                    }

                    imageQueue.Clear();
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive);
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
                var linkUrl = _externalHostUri.Trim('/') + CameraController.GenerateCameraUrl(n);
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
                    var running = _videoRecorderService.TaskList.Any(n => n == taskId) ? "running" : "stopped";
                    var action = _videoRecorderService.TaskList.Any(n => n == taskId) ? "stop" : "start";
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
                        _videoRecorderService.Start(camera.Camera.Description.Path, user.Login, new FrameFormatDto(),
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
                    _videoRecorderService.Stop(taskId);
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

            var motionDetectionService = (MotionDetectionService)_serviceProvider.GetService(typeof(MotionDetectionService));
            if (motionDetectionService == null)
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
                                Destination = chatId.Identifier?.ToString() ?? chatId.Username ?? "0",
                                Transport = NotificationTransport.Telegram,
                                VideoLengthSec = _settings.DefaultVideoTime
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
            await _collection.RefreshCameraCollection(cancellationToken);
            await SendText(chatId, "Camera list refreshed", cancellationToken);
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
