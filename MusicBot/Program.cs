//#define RUNPROCESS

using System;
using Discord.WebSocket;
using System.Threading.Tasks;
using Discord;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Discord.Commands;
using System.Net.Http;
using System.Globalization;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using Victoria;
using System.Diagnostics;
using NLog;
using System.Net.WebSockets;

#pragma warning disable
namespace MusicBot
{
    class Program
    {
        private DiscordSocketClient socketClient;
        private LavaNode lavaNode;
        private static Process lavalinkProc;
        private Logger logger;

        /// <summary>
        /// Точка входа программы.
        /// </summary>
        static void Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
#if RUNPROCESS
            lavalinkProc = new Process();
            lavalinkProc.StartInfo.FileName = "java.exe";
            lavalinkProc.StartInfo.Arguments = "-jar .\\LavaLink_Server\\Lavalink.jar";
            //lavalinkProc.StartInfo.CreateNoWindow = true;
            lavalinkProc.Start();
#endif
            new Program().MainAsync().GetAwaiter().GetResult();
        }
        /// <summary>
        /// Основной метод запуска бота
        /// </summary>
        public async Task MainAsync()
        {
            EnvironmentVariablesHandler.Load();
            var services = ConfigureServices();
            try
            {
                socketClient = services.GetRequiredService<DiscordSocketClient>();
                logger = LogManager.GetLogger("filedata");
                socketClient.Log += LogAsync;
                socketClient.Ready += OnReadyAsync;
                services.GetRequiredService<CommandService>().Log += LogAsync;
                
                var values = EnvironmentVariablesHandler.Variables;
                if (File.Exists(".binds")) CommandServiceHandler.Binds = EnvironmentVariablesHandler.ReadJSON(".binds") ?? new Dictionary<string, string>();
                string token = values["token"];
                await socketClient.LoginAsync(TokenType.Bot, token);
                await socketClient.StartAsync();
                lavaNode.OnLog += LogAsync;
                await services.GetRequiredService<CommandServiceHandler>().InitializeAsync(values.GetValueOrDefault("prefix")?.FirstOrDefault());
                await ConsoleRead();
                await Task.Delay(Timeout.Infinite);
            }
            catch (Exception ex)
            {
                Respond(ex.Message + ":" + ex.ToString(), ConsoleColor.Red);
            }
            finally
            {
                await services.DisposeAsync();
            }
        }

        private void LavalinkProc_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            Console.WriteLine(e.Data);
        }

        private async Task ConsoleRead()
        {
            while (true)
            {
                Respond(">", ConsoleColor.DarkGreen);
                var input = Console.ReadLine();
                if (string.IsNullOrEmpty(input))
                {
                    continue;
                }

                if (input.ToLower() == "announce")
                {
                    Respond("Что мы хотим отправить?", ConsoleColor.DarkCyan);
                    var what = Console.ReadLine();
                    foreach (var clientGuild in socketClient.Guilds)
                    {
                        try
                        {
                            await clientGuild.DefaultChannel.SendMessageAsync(what);
                            Respond($"Отправлено в {clientGuild.Name}@{clientGuild.DefaultChannel.Name} успешно.", ConsoleColor.DarkCyan);
                        }
                        catch (Exception)
                        {
                            Respond($"Не получилось отправить в {clientGuild.Name}.", ConsoleColor.Red);
                        }
                    }
                }
                else if (input.ToLower() == "dm")
                {
                    Respond("Пожалуйста, укажите номер гильдии в списке:");
                    for (var i = 0; i < socketClient.Guilds.Count; i++)
                    {
                        Console.WriteLine($"{i + 1} : {socketClient.Guilds.ElementAt(i).Name}");
                    }

                    int to = int.Parse(Console.ReadLine());
                    var guild = socketClient.Guilds.ElementAt(to - 1);
                    Respond($"{guild.Name}: ID канала: ", ConsoleColor.DarkCyan);
                    ulong id = ulong.Parse(Console.ReadLine());
                    var channel = guild.GetTextChannel(id);
                    Respond("Что отправим?", ConsoleColor.DarkCyan);
                    string text = Console.ReadLine();
                    await channel.SendMessageAsync(text);
                }
                else if (input.ToLower() == "exit")
                {
                    await socketClient.LogoutAsync();
#if RUNPROCESS
                    lavalinkProc.Close();
#endif
                    EnvironmentVariablesHandler.Save();
                    EnvironmentVariablesHandler.WriteJSON(CommandServiceHandler.Binds, ".binds");
                    try
                    {
                        await lavaNode.DisconnectAsync();
                    }
                    catch
                    {
                    }
                    await Task.Delay(1000);
                    Environment.Exit(0);
                    return;
                }
                await Task.CompletedTask;
            }
            // ReSharper disable once FunctionNeverReturns
        }

        private async Task OnReadyAsync()
        {
            if (!lavaNode.IsConnected)
            {
                await lavaNode.ConnectAsync();
            }
        }

        /// <summary>
        /// Метод для логирования клиента.
        /// </summary>
        /// <param name="logMsg">Сообщения логов.</param>
        private Task LogAsync(LogMessage logMsg)
        {
            if (logMsg.Source == "Victoria")
            {
                if (logMsg.Exception is WebSocketException) throw logMsg.Exception;
                logger.Trace(logMsg);
                return Task.CompletedTask;
            }
            logger.Debug(logMsg);
            Console.WriteLine(logMsg);
            return Task.CompletedTask;
        }
        /// <summary>
        /// Метод настройки сервисов программы.
        /// </summary>
        /// <returns>Используемые программой сервисы.</returns>
        private ServiceProvider ConfigureServices()
        {
            var provider = new ServiceCollection().AddSingleton<DiscordSocketClient>()
                .AddSingleton<CommandService>()
                .AddSingleton<CommandServiceHandler>()
                .AddSingleton<HttpClient>()
                .AddLavaNode(x =>
                {
                    x.Authorization = EnvironmentVariablesHandler.Variables["lavapassword"];
                    x.Hostname = EnvironmentVariablesHandler.Variables["lavahost"];
                    x.Port = ushort.Parse(EnvironmentVariablesHandler.Variables["lavaport"]);
                    x.SelfDeaf = true;
                })
                .AddSingleton<MusicService>()
                .BuildServiceProvider();
            lavaNode = provider.GetService<LavaNode>();
            return provider;
        }

        private static void Respond(string text, ConsoleColor fc = ConsoleColor.White)
        {
            ConsoleColor current = Console.ForegroundColor;
            Console.ForegroundColor = fc;
            Console.WriteLine(text);
            Console.ForegroundColor = current;
        }
    }
}
