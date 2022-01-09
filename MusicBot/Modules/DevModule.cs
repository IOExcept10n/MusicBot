using Discord;
using Discord.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot.Modules
{
    public class DevModule : ModuleBase<SocketCommandContext>
    {
        [Command("SetPrefix")]
        [RequireOwner]
        public async Task SetPrefix(char prefix)
        {
            CommandServiceHandler.Prefix = prefix;
            EnvironmentVariablesHandler.Variables["prefix"] = prefix.ToString();
            EnvironmentVariablesHandler.Save();
            await ReplyAsync($":white_check_mark: Префикс успешно изменён на {prefix}! Теперь начинайте команды с этого символа!");
        }

        [Command("SetDebug")]
        [RequireOwner]
        public async Task SetDebug(bool debug)
        {
            CommandServiceHandler.DebugMode = debug;
            await ReplyAsync($":white_check_mark: Режим отладки теперь имеет значение {debug}!");
        }

        [Alias("AddBind")]
        [RequireUserPermission(GuildPermission.Administrator)]
        [Command("SetBind")]
        public async Task SetBind([Remainder]string bindData)
        {
            string[] sp = bindData.Split(';');
            if (bindData != null && sp.Length == 2)
            {
                CommandServiceHandler.Binds.Add(sp[0], sp[1]);
                await ReplyAsync($"Привязка успешно создана! Теперь на команду `{sp[0]}` будет осуществляться реакция `{sp[1]}`!");
            }
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [Command("RemoveBind")]
        public async Task RemoveBind(string bindData)
        {
            if (bindData != null && CommandServiceHandler.Binds.ContainsKey(bindData))
            {
                CommandServiceHandler.Binds.Remove(bindData);
                await ReplyAsync($"Привязка успешно удалена!");
            }
        }

        [Alias("binds")]
        [RequireUserPermission(GuildPermission.Administrator)]
        [Command("GetBinds")]
        public async Task GetBindsAsync()
        {
            string binds = "";
            foreach (var bind in CommandServiceHandler.Binds)
            {
                binds += $"{bind.Key} - {bind.Value}\n";
            }
            var builder = new EmbedBuilder()
                .WithAuthor(Context.User)
                .WithColor(Color.DarkMagenta)
                .WithThumbnailUrl("https://c.tenor.com/QvWdVYDh_YMAAAAC/riina-headphones.gif")
                .WithDescription(binds);
            var embed = builder.Build();
            await ReplyAsync(embed: embed);
        }

        [Alias("i", "h", "info", "help", "information")]
        [Command("Help")]
        public async Task ShowHelpMessageAsync()
        {
            var builder = new EmbedBuilder()
                .WithAuthor(Context.User)
                .WithColor(Color.Teal)
                .WithTitle(":information_source: Список доступных вам команд:")
                .WithThumbnailUrl("https://c.tenor.com/QvWdVYDh_YMAAAAC/riina-headphones.gif")//или https://cdn.discordapp.com/avatars/926000897572618281/65908264754d77c0a59ded0e75062c3e.png?size=256
                .AddField(":musical_note: Команды для работы с треками: ",
                ":arrow_forward: `play *[string|url tracks]` - запускает/доабавляет в очередь один или несколько треков (через ;). \n" +
                ":pause_button: `pause` - приостанавливает плеер.\n" +
                ":stop_button: `stop` - останваливает плеер, очищает очередь и выходит из канала.\n" +
                ":level_slider: `seek (timespan time)` - перематывает трек на указанное время.\n" +
                ":track_next: `skip [int number]` - пропускает указанное число треков.\n" +
                ":arrow_up: `insert (int position) *[string|url tracks]` - вставляет один или несколько треков в очередь начиная с указанной позиции.\n" +
                ":white_large_square: `clear` - очищает очередь воспроизведения.\n" +
                ":wastebasket: `remove (int position)` - удаляет трек под указанным номером из очереди.\n" +
                ":mag: `search (string query) [int maxresults]` - ищет указанное число треков по запросу на YouTube (не более 25).")
                .AddField(":control_knobs: Прочие команды для управления плеером: ",
                ":inbox_tray: `join` - подключает бота к каналу.\n" +
                ":outbox_tray: `leave` - отключает бота от канала.\n" +
                ":electric_plug: `reconnect` - переподключает бота к каналу.\n" +
                ":minidisc: `playlist [int page]` - показывает указанную страницу списка воспроизводимой музыки.\n" +
                ":bar_chart: `state` - отображает данные о текущем треке.\n" +
                ":cyclone: `setorder (PlayOrder order)` - задаёт порядок воспроизведения (Direct = 0, Loop = 1, Random = 2 или Repeat = 3).\n" +
                ":sound: `setvolume (ushort volume)` - задаёт гроомкость бота.")
                .AddField(":man_technologist: Функции для администраторов:",
                ":heavy_plus_sign: `setbind (string bindData)` - добавляет привязку для авто-ответа на указанную фразу. Формат строки: key;value. Также допустим автовызов команд, не требующих аргументов (начинайте с $).\n" +
                ":heavy_minus_sign: `removebind (string bind)` - убирает указанную привязку для авто-ответа.\n" +
                ":clipboard: `getbinds` - отображает список привязок.");
            var embed = builder.Build();
            await ReplyAsync(embed: embed);
        }
    }
}
