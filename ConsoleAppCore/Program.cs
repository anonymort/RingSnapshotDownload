using System;
using KoenZomers.Ring.Api;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Diagnostics;
using SixLabors.ImageSharp;
using System.Configuration;
using KoenZomers.Ring.Api.Exceptions;
using KoenZomers.Ring.Api.Entities;
using System.Threading;

namespace KoenZomers.Ring.SnapshotDownload
{
    class Program
    {
        /// <summary>
        /// The hardware id of the device running this application.
        /// </summary>
        public const string HardwareId = nameof(HardwareId);

        private static readonly Random RetryJitter = new Random();

        /// <summary>
        /// Gets the location of the settings file
        /// </summary>
        private static readonly string SettingsFilePath = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "Settings.json");

        /// <summary>
        /// Configuration used by this application
        /// </summary>
        public static Configuration Configuration { get; set; }

        static async Task Main(string[] args)
        {
            using var cancellationSource = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellationSource.Cancel();
            };
            var cancellationToken = cancellationSource.Token;

            Console.WriteLine();

            var appVersion = Assembly.GetExecutingAssembly().GetName().Version;

            Console.WriteLine($"Ring Snapshot Download Tool v{appVersion.Major}.{appVersion.Minor}.{appVersion.Build}.{appVersion.Revision} by Koen Zomers");
            Console.WriteLine();

            if (args.Contains("-help") || args.Contains("--help") || args.Contains("-h"))
            {
                DisplayHelp();
                Environment.Exit(0);
            }

            // Load the configuration
            Configuration = await Configuration.Load(SettingsFilePath);

            // Parse the provided arguments
            try
            {
                ParseArguments(args);
            }
            catch (FormatException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                DisplayHelp();
                Environment.Exit(1);
            }

            // Ensure we have the required configuration
            if (string.IsNullOrWhiteSpace(Configuration.Username) && string.IsNullOrWhiteSpace(Configuration.RefreshToken))
            {
                Console.WriteLine("Error: -username is required");
                DisplayHelp();
                Environment.Exit(1);
            }

            if (string.IsNullOrWhiteSpace(Configuration.Password) && string.IsNullOrWhiteSpace(Configuration.RefreshToken))
            {
                Console.WriteLine("Error: -password is required");
                DisplayHelp();
                Environment.Exit(1);
            }

            if (!Configuration.DeviceId.HasValue && !Configuration.ListBots)
            {
                Console.WriteLine("Error: -deviceid or -list is required");
                DisplayHelp();
                Environment.Exit(1);
            }

            // Connect to Ring
            Console.WriteLine("Connecting to Ring services");
            Session session;
            if (!string.IsNullOrWhiteSpace(Configuration.RefreshToken))
            {
                // Use refresh token from previous session
                Console.WriteLine("Authenticating using refresh token from previous session");

                try
                {
                session = await Session.GetSessionByRefreshToken(Configuration.RefreshToken, GetHardwareIdOrDefault(), cancellationToken: cancellationToken);
                }
                catch (Api.Exceptions.AuthenticationFailedException)
                {
                    Console.WriteLine("Authentication failed: the saved Ring refresh token is no longer valid.");
                    Console.WriteLine($"Delete {SettingsFilePath} or run again with fresh credentials.");
                    Environment.Exit(1);
                    return;
                }
            }
            else
            {
                // Use the username and password provided
                Console.WriteLine("Authenticating using provided username and password");

                session = new Session(Configuration.Username, Configuration.Password, GetHardwareIdOrDefault());

                try
                {
                    await session.Authenticate(cancellationToken: cancellationToken);
                }
                catch (Api.Exceptions.TwoFactorAuthenticationRequiredException)
                {
                    // Two factor authentication is enabled on the account. The above Authenticate() will trigger a text or e-mail message to be sent. Ask for the token sent in that message here.
                    Console.WriteLine($"Two factor authentication enabled on this account, please enter the Ring token from the e-mail, text message or authenticator app:");
                    var token = Console.ReadLine();

                    // Authenticate again using the two factor token
                    await session.Authenticate(twoFactorAuthCode: token, cancellationToken: cancellationToken);
                }
                catch(Api.Exceptions.ThrottledException)
                {
                    Console.WriteLine("Two factor authentication is required, but too many tokens have been requested recently. Wait for a few minutes and try connecting again.");
                    Environment.Exit(1);
                }
                catch (Api.Exceptions.AuthenticationFailedException e)
                {
                    Console.WriteLine(e.Message);
                    Console.WriteLine("Check your Ring email and password, then try again.");
                    Environment.Exit(1);
                }
                catch (WebException)
                {
                    Console.WriteLine("Connection failed. Validate your credentials.");
                    Environment.Exit(1);
                }
            }

            // If we have a refresh token, update the config file with it so we don't need to authenticate again next time
            if (session.OAuthToken != null)
            {
                Configuration.RefreshToken = session.OAuthToken.RefreshToken;
                await Configuration.Save();
            }

            if (Configuration.ListBots)
            {
                // Retrieve all available Ring devices and list them
                Console.Write("Retrieving all devices... ");
                
                var devices = await session.GetRingDevices(cancellationToken);

                Console.WriteLine($"{devices.Doorbots.Count + devices.AuthorizedDoorbots.Count + devices.StickupCams.Count} found");
                Console.WriteLine();

                if (devices.AuthorizedDoorbots.Count > 0)
                {
                    Console.WriteLine("Authorized Doorbells");
                    foreach (var device in devices.AuthorizedDoorbots)
                    {
                        Console.WriteLine($"{device.Id} - {device.Description}");
                    }
                    Console.WriteLine();
                }
                if (devices.Doorbots.Count > 0)
                {
                    Console.WriteLine("Doorbells");
                    foreach (var device in devices.Doorbots)
                    {
                        Console.WriteLine($"{device.Id} - {device.Description}");
                    }
                    Console.WriteLine();
                }
                if (devices.StickupCams.Count > 0)
                {
                    Console.WriteLine("Stickup cams");
                    foreach (var device in devices.StickupCams)
                    {
                        Console.WriteLine($"{device.Id} - {device.Description}");
                    }
                    Console.WriteLine();
                }
            }
            else
            {
                if (Configuration.DownloadAllHistoricalSnapshots)
                {
                    await DownloadAllHistoricalRecordings(session, cancellationToken);
                    Console.WriteLine("Done");
                    Environment.Exit(0);
                }

                Directory.CreateDirectory(Configuration.OutputPath);

                // By default the screenshot will be tagged with the current date/time unless we can retrieve information from Ring when the latest snapshot was really taken
                var timeStamp = DateTime.Now;

                // Retrieve when the latest available snapshot was taken
                try
                {
                    var doorbotTimeStamps = await session.GetDoorbotSnapshotTimestamp(Configuration.DeviceId.Value, cancellationToken);

                    // Validate if we received timestamps
                    if(doorbotTimeStamps.Timestamp.Count > 0)
                    {
                        // Filter out timestamps which are not for the doorbot we are requesting and take the most recent snapshot only
                        var latestDoorbotTimeStamp = doorbotTimeStamps.Timestamp.Where(t => t.DoorbotId.HasValue && t.DoorbotId.Value == Configuration.DeviceId.Value).OrderByDescending(t => t.TimestampEpoch).FirstOrDefault();

                        // If we have a result and the result has an Epoch timestamp on it, use that as the marker for when the screenshot has been taken
                        if (latestDoorbotTimeStamp != null && latestDoorbotTimeStamp.TimestampEpoch.HasValue)
                        {
                            // Convert from the Epoch time to a DateTime we can use
                            timeStamp = latestDoorbotTimeStamp.Timestamp.Value;
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e) when (e is DeviceUnknownException || e is HttpRequestException)
                {
                    Console.WriteLine($"Unable to retrieve snapshot timestamp, using the current time instead: {e.Message}");
                }

                // Construct the filename and path where to save the file
                var downloadFileName = $"{Configuration.DeviceId} - {timeStamp:yyyy-MM-dd HH-mm-ss}.jpg";
                var downloadFullPath = Path.Combine(Configuration.OutputPath, downloadFileName);

                // Retrieve the snapshot                
                short attempt = 0;
                var downloadSucceeded = false;
                var imageValidationSucceeded = true;
                var savingSucceeded = false;
                do
                {
                    attempt++;
                    downloadSucceeded = false;
                    imageValidationSucceeded = true;
                    savingSucceeded = false;
                    try
                    {
                        Console.Write($"Downloading snapshot from Ring device with ID {Configuration.DeviceId}... ");

                        await session.DownloadLatestSnapshot(Configuration.DeviceId.Value, downloadFullPath, Configuration.ForceUpdateSnapshot, cancellationToken: cancellationToken);
                        downloadSucceeded = true;
                        
                        Console.WriteLine("OK");
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception e) when (IsRetryableSnapshotError(e))
                    {
                        if (attempt < Configuration.MaximumRetries)
                        {
                            Console.WriteLine($"Failed: {e.Message}, retrying ({attempt}/{Configuration.MaximumRetries})");
                            await Task.Delay(GetSnapshotRetryDelay(attempt), cancellationToken);
                            continue;
                        }

                        Console.WriteLine($"Failed: {e.Message}, reached retry limit ({attempt}/{Configuration.MaximumRetries})");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Failed: {e.Message}, no retry configured.");
                        break;
                    }

                    // Check if the image should be validated to not be corrupt
                    if(downloadSucceeded && Configuration.ValidateImage)
                    {
                        Console.Write("Validating image... ");
                        try
                        {
                            await using var downloadedImage = File.OpenRead(downloadFullPath);
                            await Image.DetectFormatAsync(downloadedImage);

                            Console.WriteLine("OK");
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch(InvalidImageContentException)
                        {
                            Console.WriteLine($"Failed: image content corrupt, retrying ({attempt}/{Configuration.MaximumRetries})");
                            imageValidationSucceeded = false;
                            TryDeletePartialFile(downloadFullPath);
                        }
                        catch(UnknownImageFormatException)
                        {
                            Console.WriteLine($"Failed: image content not recognized, retrying ({attempt}/{Configuration.MaximumRetries})");
                            imageValidationSucceeded = false;
                            TryDeletePartialFile(downloadFullPath);
                        }
                    }

                    if(downloadSucceeded && imageValidationSucceeded)
                    {
                        Console.Write($"Saving image to {downloadFullPath}... ");
                        try
                        {
                            savingSucceeded = true;
                            Console.WriteLine("OK");
                        }
                        catch(Exception e)
                        {
                            Console.WriteLine($"Failed: {e.Message}, retrying ({attempt}/{Configuration.MaximumRetries})");
                            savingSucceeded = false;
                            TryDeletePartialFile(downloadFullPath);
                        }
                    }

                } while ((!downloadSucceeded || !imageValidationSucceeded || !savingSucceeded) && attempt < Configuration.MaximumRetries);

                if (!savingSucceeded)
                {
                    Environment.Exit(1);
                }
            }
            
            Console.WriteLine("Done");

            Environment.Exit(0);
        }

        /// <summary>
        /// Parses all provided arguments
        /// </summary>
        /// <param name="args">String array with arguments passed to this console application</param>
        private static void ParseArguments(IList<string> args)
        {
            for (var argIndex = 0; argIndex < args.Count; argIndex++)
            {
                switch (args[argIndex].ToLowerInvariant())
                {
                    case "-out":
                        Configuration.OutputPath = ConsumeOptionValue(args, ref argIndex, "-out");
                        break;
                    case "-username":
                        Configuration.Username = ConsumeOptionValue(args, ref argIndex, "-username");
                        break;
                    case "-password":
                        Configuration.Password = ConsumeOptionValue(args, ref argIndex, "-password");
                        break;
                    case "-deviceid":
                        var deviceIdValue = ConsumeOptionValue(args, ref argIndex, "-deviceid");
                        if (!int.TryParse(deviceIdValue, out int deviceId))
                        {
                            throw new FormatException($"Could not parse -deviceid value '{deviceIdValue}' as a numeric ID.");
                        }
                        Configuration.DeviceId = deviceId;
                        break;
                    case "-maxretries":
                        var maxRetriesValue = ConsumeOptionValue(args, ref argIndex, "-maxretries");
                        if (!short.TryParse(maxRetriesValue, out short maxRetries))
                        {
                            throw new FormatException($"Could not parse -maxretries value '{maxRetriesValue}' as a number.");
                        }
                        if (maxRetries < 1)
                        {
                            throw new FormatException("-maxretries must be at least 1.");
                        }
                        Configuration.MaximumRetries = maxRetries;
                        break;
                    case "-dlalldays":
                        var dlAllDaysValue = ConsumeOptionValue(args, ref argIndex, "-dlalldays");
                        if (!int.TryParse(dlAllDaysValue, out int downloadAllDays))
                        {
                            throw new FormatException($"Could not parse -dlalldays value '{dlAllDaysValue}' as a number.");
                        }
                        if (downloadAllDays < 1)
                        {
                            throw new FormatException("-dlalldays must be at least 1.");
                        }
                        Configuration.DownloadAllDays = Math.Min(downloadAllDays, 180);
                        break;
                    case "-list":
                        Configuration.ListBots = true;
                        break;
                    case "-forceupdate":
                        Configuration.ForceUpdateSnapshot = true;
                        break;
                    case "-validateimage":
                        Configuration.ValidateImage = true;
                        break;
                    case "-dlall":
                        Configuration.DownloadAllHistoricalSnapshots = true;
                        break;
                    default:
                        throw new FormatException($"Unrecognized argument '{args[argIndex]}'.");
                }
            }

            if (string.IsNullOrEmpty(Configuration.OutputPath))
            {
                Configuration.OutputPath = Environment.CurrentDirectory;
            }
        }

        private static string ConsumeOptionValue(IList<string> args, ref int index, string option)
        {
            if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                throw new FormatException($"{option} requires a value.");
            }

            return args[++index];
        }

        private static bool IsRetryableSnapshotError(Exception exception)
        {
            if (exception is DeviceUnknownException || exception is ThrottledException || exception is TimeoutException || exception is HttpRequestException)
            {
                return true;
            }

            if (exception is WebException webException && webException.Status == WebExceptionStatus.Timeout)
            {
                return true;
            }

            return false;
        }

        private static async Task DownloadAllHistoricalRecordings(Session session, CancellationToken cancellationToken)
        {
            var outputPath = Path.Combine(Configuration.OutputPath, $"{Configuration.DeviceId} - historical recordings");
            Directory.CreateDirectory(outputPath);

            var manifest = new List<object>();
            var totalDownloaded = 0;
            var now = DateTime.Now;
            var start = now.Date.AddDays(-Configuration.DownloadAllDays + 1);

            Console.WriteLine($"Historical Ring recording download for device {Configuration.DeviceId}");
            Console.WriteLine($"Requesting {Configuration.DownloadAllDays} day(s), from {start:yyyy-MM-dd} through {now:yyyy-MM-dd}");
            Console.WriteLine($"Saving files to {outputPath}");
            Console.WriteLine("These are event recordings returned by Ring video history. Use local frame extraction to create JPG snapshots from the MP4 files.");
            Console.WriteLine();

            for (var day = start.Date; day <= now.Date; day = day.AddDays(1))
            {
                var dayStart = day;
                var dayEnd = day == now.Date ? now : day.AddDays(1).AddMilliseconds(-1);
                totalDownloaded += await DownloadVideoHistoryForDay(session, outputPath, manifest, dayStart, dayEnd, cancellationToken);
            }

            var manifestPath = Path.Combine(outputPath, "manifest.json");
            await File.WriteAllTextAsync(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), cancellationToken);

            Console.WriteLine();
            Console.WriteLine($"Downloaded {totalDownloaded} historical recording clip(s).");
            Console.WriteLine("To create JPG snapshots from these MP4 files, rerun the wizard with --dlall-extract.");
            Console.WriteLine($"Manifest saved to {manifestPath}");
        }

        private static async Task<int> DownloadVideoHistoryForDay(Session session, string outputPath, List<object> manifest, DateTime dayStart, DateTime dayEnd, CancellationToken cancellationToken)
        {
            Console.Write($"Querying video history {dayStart:yyyy-MM-dd}... ");
            VideoSearchResponse response;
            try
            {
                response = await session.SearchVideoHistory(Configuration.DeviceId.Value, dayStart, dayEnd, cancellationToken);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed: {e.Message}");
                return 0;
            }

            var events = response?.VideoSearch?.Where(e => !string.IsNullOrWhiteSpace(e.DingId)).ToList() ?? new List<VideoSearchResult>();
            Console.WriteLine($"{events.Count} event(s)");

            var downloaded = 0;
            foreach (var historicalEvent in events)
            {
                var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(historicalEvent.CreatedAt).LocalDateTime;
                var fileName = $"{Configuration.DeviceId} - {createdAt:yyyy-MM-dd HH-mm-ss} - {SanitizeFileName(historicalEvent.Kind ?? "event")} - recording.mp4";
                var filePath = Path.Combine(outputPath, fileName);

                if (File.Exists(filePath))
                {
                    Console.WriteLine($"    Skipping existing {fileName}");
                    continue;
                }

                Console.Write($"    Downloading {fileName}... ");
                try
                {
                    await session.GetDoorbotHistoryRecording(historicalEvent.DingId, filePath, cancellationToken);
                    downloaded++;
                    manifest.Add(new
                    {
                        file = fileName,
                        source = "video_history",
                        dingId = historicalEvent.DingId,
                        historicalEvent.Kind,
                        createdAt,
                        duration = historicalEvent.Duration,
                        historicalEvent.HadSubscription
                    });
                    Console.WriteLine("OK");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed: {e.Message}");
                }
            }

            return downloaded;
        }

        private static string SanitizeFileName(string value)
        {
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidCharacter, '-');
            }

            return value;
        }

        private static void TryDeletePartialFile(string downloadFullPath)
        {
            try
            {
                if (File.Exists(downloadFullPath))
                {
                    File.Delete(downloadFullPath);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }

        private static TimeSpan GetSnapshotRetryDelay(int attempt)
        {
            var baseDelaySeconds = Math.Min(10.0, Math.Pow(2.0, Math.Max(0, attempt - 1)));
            var jitter = RetryJitter.NextDouble() * 0.35;
            var delayedSeconds = baseDelaySeconds * (1 + jitter);
            return TimeSpan.FromSeconds(delayedSeconds);
        }

        /// <summary>
        /// Shows the syntax
        /// </summary>
        private static void DisplayHelp()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("   RingSnapshotDownload -username <username> -password <password> [-out <folder location> -deviceid <ring device id> -list -forceupdate]");
            Console.WriteLine();
            Console.WriteLine("username: Username of the account to use to log on to Ring");
            Console.WriteLine("password: Password of the account to use to log on to Ring");
            Console.WriteLine("out: The folder where to store the snapshot (optional, will use current directory if not specified)");
            Console.WriteLine("list: Returns the list with all Ring devices and their ids you can user with -deviceid");
            Console.WriteLine("deviceid: Id of the Ring device from wich you want to capture the screenshot. Use -list to retrieve all ids.");
            Console.WriteLine("forceupdate: Requests the Ring device to capture a new snapshot before downloading. If not provided, the latest cached snapshot will be taken.");
            Console.WriteLine("validateimage: Run a check to try to validate if the downloaded image file is valid. Will retry with the maxretries value if its not valid.");
            Console.WriteLine("maxretries: Amount of times to retry downloading the snapshot when Ring returns an error. 3 is default.");
            Console.WriteLine("dlall: Experimental. Downloads historical Ring event recordings for local frame extraction.");
            Console.WriteLine("dlalldays: Number of days to query with -dlall. 14 is default, 180 is maximum.");
            Console.WriteLine("help: Shows this help text without contacting Ring.");
            Console.WriteLine();
            Console.WriteLine("Example:");
            Console.WriteLine("   RingSnapshotDownload -username my@email.com -password mypassword -deviceid 12345 -forceupdate -out d:\\screenshots");
            Console.WriteLine("   RingSnapshotDownload -username my@email.com -password mypassword -deviceid 12345 -out d:\\screenshots");
            Console.WriteLine("   RingSnapshotDownload -username my@email.com -password mypassword -deviceid 12345");
            Console.WriteLine("   RingSnapshotDownload -username my@email.com -password mypassword -list");
            Console.WriteLine();
        }

        private static string GetHardwareIdOrDefault()
        {
            var deviceId = ConfigurationManager.AppSettings[HardwareId];
            if (string.IsNullOrEmpty(deviceId))
            {
                deviceId = Guid.NewGuid().ToString();
                var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                if (configFile.AppSettings.Settings[HardwareId] == null)
                {
                    configFile.AppSettings.Settings.Add(HardwareId, deviceId);
                }
                else
                {
                    configFile.AppSettings.Settings[HardwareId].Value = deviceId;
                }
                configFile.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
            }
            return deviceId;
        }
    }
}
