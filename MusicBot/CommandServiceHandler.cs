using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Victoria;
using NLog;

namespace MusicBot
{
    class CommandServiceHandler
    {
        public const string BotApplicationName = "IlyaApp";

        private CommandService commands;

        private DiscordSocketClient client;

        private IServiceProvider services;

        internal static Logger logger;

        internal static Dictionary<string, string> Binds { get; set; } = new Dictionary<string, string>();

        public static char Prefix { get; set; } = '.';

        public static bool DebugMode { get; set; } = false;

        internal static List<string> Commands { get; }

        static CommandServiceHandler()
        {
            Commands = new List<string>()
            {
                "$ping",
                "$join",
                "$play",
                "$pause",
                "$stop",
                "$leave",
                "$reconnect",
                "$skip",
                "$playlist",
                "$playerstate",
                "$clear",
            };
        }

        public CommandServiceHandler(IServiceProvider services)
        {
            this.services = services;
            commands = services.GetRequiredService<CommandService>();
            client = services.GetRequiredService<DiscordSocketClient>();

            commands.CommandExecuted += OnCommandExecuted;
            client.MessageReceived += OnMessageRecieved;
        }

        public async Task InitializeAsync(char? prefix = null, Logger logger = null)
        {
            Prefix = prefix ?? '.';
            CommandServiceHandler.logger = logger;
            await commands.AddModulesAsync(Assembly.GetEntryAssembly(), services);
            await client.SetActivityAsync(new Activity{ Type = ActivityType.Listening, Name = "some music for you" });
        }

        struct Activity : IActivity
        {
            public string Name { get; set; }

            public ActivityType Type { get; set; }

            public ActivityProperties Flags { get; }

            public string Details { get; }
        }

        private async Task OnMessageRecieved(SocketMessage sourceMessage)
        {
            if (sourceMessage.Source != MessageSource.User) return;
            if (sourceMessage is SocketUserMessage msg)
            {
                int argPos = 0;
                if (msg.HasCharPrefix(Prefix, ref argPos) || msg.HasMentionPrefix(client.CurrentUser, ref argPos))
                {
                    var context = new SocketCommandContext(client, msg);
                    if (context.Guild.GetChannel(context.Channel.Id).GetPermissionOverwrite(GetPersonalRole(client, context.Guild))?.SendMessages != PermValue.Deny)
                        await commands.ExecuteAsync(context, argPos, services);
                }
            }
        }

        /// <summary>
        /// Получает персональную одноимённую роль бота с помощью <see cref="System.Linq"/>-запроса.
        /// </summary>
        internal static SocketRole GetPersonalRole(DiscordSocketClient client, SocketGuild guild)
        {
            return (from role in guild.Roles where role.Name == BotApplicationName select role).First();//Получаем роль, принадлежащую боту.
        }

        private async Task OnCommandExecuted(Optional<CommandInfo> info, ICommandContext context, IResult result)
        {
            if (!result.IsSuccess)
            {
                if (DebugMode && context.Message.Author.Id == 638653302040428544) await context.Message.ReplyAsync($"{result.ErrorReason}");
                switch (result.Error)
                {
                    case CommandError.UnknownCommand:
                        {
                            string bindingTrgigger = context.Message.Content.TrimStart(Prefix).Replace(context.Client.CurrentUser.Mention, "").Split(" ").First().ToLower();
                            if (Binds.ContainsKey(bindingTrgigger))
                            {
                                string bindedValue = Binds[bindingTrgigger];
                                if (Commands.Contains(bindedValue))
                                {
                                    int argPos = bindingTrgigger.Length;
                                    var cmd = (from x in commands.Commands where x.Name.ToLower() == bindedValue[1..] select x).First();
                                    await cmd.ExecuteAsync(context, ParseResult.FromSuccess(new List<TypeReaderResult>(), new List<TypeReaderResult>()), services);
                                    return;
                                }
                                await context.Message.ReplyAsync(bindedValue);
                                return;
                            }
                            await context.Message.ReplyAsync(":x: Такой команды не существует. Пропишите `help` для списка команд.");
                            return;
                        }
                    case CommandError.BadArgCount:
                        {
                            await context.Message.ReplyAsync($":x: Неверное число аргументов.");
                            return;
                        }
                    case CommandError.ParseFailed:
                        {
                            await context.Message.ReplyAsync($":x: Произошла ошибка при преобразовании типов данных.");
                            return;
                        }
                    case CommandError.ObjectNotFound:
                        {
                            await context.Message.ReplyAsync($":x: Один/несколько запрашиваемых объектов не найдены.");
                            return;
                        }
                    case CommandError.MultipleMatches:
                        {
                            await context.Message.ReplyAsync($":x: По данному запросу может быть обработано несколько команд. Пожалуйста, уточните запрос.");
                            return;
                        }
                    case CommandError.UnmetPrecondition:
                        {
                            await context.Message.ReplyAsync($":x: У вас или у бота недостаточный уровень доступа для исполнения данной команды.");
                            return;
                        }
                    case CommandError.Exception:
                        {
                            await context.Message.ReplyAsync($":x: Произошла ошибка в процессе выполнения команды.");
                            return;
                        }
                    default:
                        {
                            await context.Message.ReplyAsync($":x: Команда не была выполнена или была выполнена неудачно.");
                            return;
                        }
                }
            }
        }
    }
}