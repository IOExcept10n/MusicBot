using Discord;
using Discord.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Victoria;
using Victoria.EventArgs;
using Victoria.Enums;
using Victoria.Responses.Search;
using System.Collections.Concurrent;
using System.Globalization;

namespace MusicBot.Modules
{
    public class MusicModule : ModuleBase<SocketCommandContext>
    {
        private LavaNode lavaNode;

        private MusicService musicService;

        [Command("ping")]
        public async Task PingAsync()
        {
            await ReplyQuickEmbedAsync("Pong!");
        }

        public MusicModule(LavaNode lavaNode, MusicService musicService)
        {
            this.lavaNode = lavaNode;
            this.musicService = musicService;
        }

        private async Task ReplyQuickEmbedAsync(string message, Color color = default)
        {
            var builder = new EmbedBuilder()
                .WithAuthor(Context.User)
                .WithDescription(message)
                .WithColor(color);
            var embed = builder.Build();
            await ReplyAsync(embed: embed);
        }

        [Command("join")]
        public async Task JoinAsync()
        {
            if (lavaNode.HasPlayer(Context.Guild))
            {
                await ReplyQuickEmbedAsync("Бот уже подключён к каналу!", Color.Red);
                return;
            }

            var voiceState = Context.User as IVoiceState;
            if (voiceState?.VoiceChannel == null)
            {
                await ReplyQuickEmbedAsync("Вы должны быть подключены к голосовому каналу!", Color.Red);
                return;
            }

            try
            {
                await lavaNode.JoinAsync(voiceState.VoiceChannel, Context.Channel as ITextChannel);
                await ReplyQuickEmbedAsync($":white_check_mark: Успешно подключился к {voiceState.VoiceChannel.Name}!", color: Color.Green);
            }
            catch (Exception exception)
            {
                await ReplyQuickEmbedAsync(exception.Message);
            }
        }

        [Command("Play")]
        public async Task PlayAsync()
        {
            if (lavaNode.TryGetPlayer(Context.Guild, out LavaPlayer player))
            {
                if (player.PlayerState == PlayerState.Paused)
                {
                    await musicService.CancelDisconnectAsync(player);
                    await player.ResumeAsync();
                    await ReplyQuickEmbedAsync(":arrow_forward: Плеер запущен успешно.", color: new Color(0x00aaaa));
                    return;
                }
                else if (player.PlayerState == PlayerState.Stopped)
                {
                    await ReplyQuickEmbedAsync(":x: Напишите мне, что я должен играть.", Color.LightGrey);
                    return;
                }
                await ReplyQuickEmbedAsync("Невозможно запустить плеер на данный момент.", Color.Red);
                return;
            }
            await ReplyQuickEmbedAsync(":x: Напишите мне, что я должен играть.", Color.LightGrey);
            return;
        }

        [Command("Play")]
        public async Task PlayAsync([Remainder] string searchQuery)
        {
            if (string.IsNullOrWhiteSpace(searchQuery))
            {
                await ReplyQuickEmbedAsync(":x: Напишите мне, что я должен играть.");
                return;
            }

            if (!lavaNode.HasPlayer(Context.Guild))
            {
                await JoinAsync();
                if (!lavaNode.HasPlayer(Context.Guild))
                {
                    return;
                }
            }

            var queries = searchQuery.Split(';');
            LavaPlayer player = lavaNode.GetPlayer(Context.Guild);
            foreach (var query in queries)
            {
                SearchResponse searchResponse;
                if (Uri.TryCreate(query, UriKind.Absolute, out Uri uriResult) && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
                {
                    searchResponse = await lavaNode.SearchAsync(SearchType.Direct, query);
                }
                else searchResponse = await lavaNode.SearchYouTubeAsync(query);
                if (searchResponse.Status == SearchStatus.LoadFailed ||
                    searchResponse.Status == SearchStatus.NoMatches)
                {
                    await ReplyQuickEmbedAsync($":x: Не удалось найти ничего по запросу `{query}`.", color: Color.Red);
                    return;
                }

                await musicService.CancelDisconnectAsync(player);
                if (player.PlayerState == PlayerState.Playing || player.PlayerState == PlayerState.Paused)
                {
                    if (!string.IsNullOrWhiteSpace(searchResponse.Playlist.Name))
                    {
                        foreach (var track in searchResponse.Tracks)
                        {
                            player.Queue.Enqueue(track);
                        }

                        await ReplyQuickEmbedAsync($":arrow_forward: Добавлено `{searchResponse.Tracks.Count}` треков.");
                    }
                    else
                    {
                        var track = searchResponse.Tracks.First();
                        player.Queue.Enqueue(track);
                        await ReplyQuickEmbedAsync($":arrow_forward: Добавлено: {track.Title}");
                    }
                }
                else
                {
                    var track = searchResponse.Tracks.First();

                    if (!string.IsNullOrWhiteSpace(searchResponse.Playlist.Name))
                    {
                        for (var i = 0; i < searchResponse.Tracks.Count; i++)
                        {
                            if (i == 0)
                            {
                                await player.PlayAsync(track);
                                await ReplyQuickEmbedAsync($":musical_note: Сейчас играет: {track.Title}");
                            }
                            else
                            {
                                player.Queue.Enqueue(searchResponse.Tracks.ElementAt(i));
                            }
                        }

                        await ReplyQuickEmbedAsync($":arrow_forward: Добавлено `{searchResponse.Tracks.Count}` треков.");
                    }
                    else
                    {
                        await player.PlayAsync(track);
                        await ReplyQuickEmbedAsync($":musical_note: Сейчас играет: {track.Title}");
                    }
                }
            }
            if (!musicService.TryGetGuildSession(Context.Guild.Id, out var value))
            {
                value = new GuildSessionConfiguration();
                value.GuildID = player.VoiceChannel.Guild.Id;
                value.ChannelId = player.VoiceChannel.Id;
                value.CurrentPlaylist = player.Queue;
                value.CurrentPlayOrder = PlayOrder.Direct;
                musicService.ActiveGuilds.TryAdd(player.VoiceChannel.Guild.Id, value);
            }
            else
            {
                value.CurrentPlaylist = player.Queue;
            }
        }

        [Command("Skip")]
        public async Task SkipAsync(int times = 1)
        {
            var voiceState = Context.User as IVoiceState;
            if (voiceState != null)
            {
                if (!lavaNode.HasPlayer(Context.Guild))
                {
                    await ReplyQuickEmbedAsync(":x: Я ещё не подключен к каналу...", color: Color.Red);
                    return;
                }
                var player = lavaNode.GetPlayer(Context.Guild);
                if (voiceState.VoiceChannel != player.VoiceChannel)
                {
                    await ReplyQuickEmbedAsync(":x: Вы находитесь в другом канале.", color: Color.Red);
                    return;
                }
                for (int i = 0; i < times; i++)
                {
                    if (player.Queue.Count == 0)
                    {
                        if (player.PlayerState == PlayerState.Playing)
                        {
                            await StopAsync();
                            await ReplyQuickEmbedAsync("Так как это был последний трек в очереди, плеер был отключен.");
                            return;
                        }
                        else
                        {
                            await ReplyQuickEmbedAsync(":x: Очередь пуста", color: Color.Red);
                            return;
                        }
                    }
                    await player.SkipAsync();
                }
                await ReplyQuickEmbedAsync($"Успешно пропущено! Сейчас играет {player.Track.Title}");
                if (!musicService.TryGetGuildSession(Context.Guild.Id, out var value))
                {
                    value = new GuildSessionConfiguration();
                    value.GuildID = player.VoiceChannel.Guild.Id;
                    value.ChannelId = player.VoiceChannel.Id;
                    value.CurrentPlaylist = player.Queue;
                    value.CurrentPlayOrder = PlayOrder.Direct;
                    musicService.ActiveGuilds.TryAdd(player.VoiceChannel.Guild.Id, value);
                }
                else
                {
                    value.CurrentPlaylist = player.Queue;
                }
            }
            else await ReplyQuickEmbedAsync(":x: Вы должны подключиться к голосовому каналу.");
        }

        [Command("Stop")]
        public async Task StopAsync()
        {
            var voiceState = Context.User as IVoiceState;
            if (voiceState != null)
            {
                if (!lavaNode.HasPlayer(Context.Guild))
                {
                    await ReplyQuickEmbedAsync(":x: Я ещё не подключен к каналу...");
                    return;
                }
                var player = lavaNode.GetPlayer(Context.Guild);
                if (voiceState.VoiceChannel != player.VoiceChannel)
                {
                    await ReplyQuickEmbedAsync(":x: Вы находитесь в другом канале.");
                    return;
                }
                if (player.Queue.Count == 0 && player.PlayerState != PlayerState.Playing && player.PlayerState != PlayerState.Paused)
                {
                    await ReplyQuickEmbedAsync(":x: Очередь пуста");
                    return;
                }
                await player.StopAsync();
                await ReplyQuickEmbedAsync($"Плеер успешно остановлен и отключен!", color: Color.DarkOrange);
                await lavaNode.LeaveAsync(player.VoiceChannel);
            }
            else await ReplyQuickEmbedAsync(":x: Вы должны подключиться к голосовому каналу.", color: Color.Red);
        }

        [Command("Pause")]
        public async Task Pause()
        {
            var voiceState = Context.User as IVoiceState;
            if (voiceState != null)
            {
                if (!lavaNode.HasPlayer(Context.Guild))
                {
                    await ReplyQuickEmbedAsync(":x: Я ещё не подключен к каналу...", color: Color.Red);
                    return;
                }
                var player = lavaNode.GetPlayer(Context.Guild);
                if (voiceState.VoiceChannel != player.VoiceChannel)
                {
                    await ReplyQuickEmbedAsync(":x: Вы находитесь в другом канале.", color: Color.Red);
                    return;
                }
                if (player.PlayerState == PlayerState.Paused || player.PlayerState == PlayerState.Stopped)
                {
                    await ReplyQuickEmbedAsync(":x: Плеер уже был остановлен", color: Color.Red);
                    return;
                }

                await player.PauseAsync();
                await ReplyQuickEmbedAsync($":pause_button: Плеер успешно приостановлен!", color: Color.DarkBlue);
                _ = musicService.InitiateDisconnectAsync(player, TimeSpan.FromSeconds(300));
            }
            else await ReplyQuickEmbedAsync(":x: Вы должны подключиться к голосовому каналу.", color: Color.Red);
        }

        [Command("Reconnect")]
        public async Task ReconnectAsync()
        {
            var player = lavaNode.GetPlayer(Context.Guild);
            var session = musicService.GetGuildSession(Context.Guild.Id);
            if (session != null && player != null)
            {
                var currentTrack = player.Track;
                Queue<LavaTrack> tracks = new(session.CurrentPlaylist);
                await lavaNode.LeaveAsync(player.VoiceChannel);
                await JoinAsync();
                player = lavaNode.GetPlayer(Context.Guild);
                foreach (var track in tracks)
                {
                    player.Queue.Enqueue(track);
                }
                TimeSpan position = currentTrack.Position;
                await player.PlayAsync(currentTrack);
                await player.SeekAsync(position - new TimeSpan(0, 0, 3));
                await ReplyQuickEmbedAsync(":white_check_mark: Успешно переподключено!");
            }
            else await ReplyQuickEmbedAsync(":x: Произошла ошибка при переподключении!", Color.Red);
        }

        [Command("Leave")]
        public async Task LeaveAsync()
        {
            var player = lavaNode.GetPlayer(Context.Guild);
            await lavaNode.LeaveAsync(player.VoiceChannel);
            await ReplyQuickEmbedAsync(":white_check_mark: Успешно покинул канал!", color: Color.Teal);
        }

        [Command("Seek")]
        public async Task SeekAsync(TimeSpan time)
        {
            var voiceState = Context.User as IVoiceState;
            if (voiceState != null)
            {
                if (!lavaNode.HasPlayer(Context.Guild))
                {
                    await ReplyQuickEmbedAsync(":x: Я ещё не подключен к каналу...", color: Color.Red);
                    return;
                }
                var player = lavaNode.GetPlayer(Context.Guild);
                if (voiceState.VoiceChannel != player.VoiceChannel)
                {
                    await ReplyQuickEmbedAsync(":x: Вы находитесь в другом канале.", color: Color.Red);
                    return;
                }
                if (player.PlayerState == PlayerState.Paused || player.PlayerState == PlayerState.Stopped)
                {
                    await ReplyQuickEmbedAsync(":x: Плеер остановлен", color: Color.Red);
                    return;
                }

                await player.SeekAsync(time);
                await ReplyQuickEmbedAsync($":fast_forward: Успешно произведена перемотка!", color: new Color(0x00ffdd));
            }
            else await ReplyQuickEmbedAsync(":x: Вы должны подключиться к голосовому каналу.", color: Color.Red);
        }

        [Alias("PL", "List")]
        [Command("PlayList")]
        public async Task GetPlayListAsync(int page = 1)
        {
            var voiceState = Context.User as IVoiceState;
            if (voiceState != null)
            {
                if (!lavaNode.HasPlayer(Context.Guild))
                {
                    await ReplyQuickEmbedAsync(":x: Я ещё не подключен к каналу...", color: Color.Red);
                    return;
                }
                var player = lavaNode.GetPlayer(Context.Guild);
                if (voiceState.VoiceChannel != player.VoiceChannel)
                {
                    await ReplyQuickEmbedAsync(":x: Вы находитесь в другом канале.", color: Color.Red);
                    return;
                }
                if (player.PlayerState == PlayerState.Stopped)
                {
                    await ReplyQuickEmbedAsync(":stop_button: Плеер остановлен, музыки нет.", color: Color.DarkOrange);
                    return;
                }

                string emoji = ":musical_note:";
                switch (player.PlayerState)
                {
                    case PlayerState.Paused:
                        emoji = ":pause_button:";
                        break;
                    case PlayerState.Stopped:
                        emoji = ":stop_button:";
                        break;
                    case PlayerState.Playing:
                        emoji = ":arrow_forward:";
                        break;
                }

                page--;
                var queue = player.Queue;
                int maxPage = queue.Count / 25;
                page = Math.Min(page, maxPage);
                TimeSpan totalDuration = TimeSpan.FromTicks(queue.Sum(x => x.Duration.Ticks));
                List<LavaTrack> tracks = queue.ToList().GetRange(page * 25, Math.Min(queue.Count - 25 * page, 25));
                var builder = new EmbedBuilder()
                    .WithAuthor(Context.User)
                    .WithTitle($":scroll: Текущий плейлист (страница {page + 1}/{maxPage + 1})")
                    .WithThumbnailUrl($"https://i.ytimg.com/vi/{player.Track.Id}/hqdefault.jpg")
                    .WithDescription($"{emoji} Сейчас играет: {player.Track.Title} \nОбщая продолжительность плейлиста: {Math.Floor(totalDuration.TotalHours)}:{totalDuration.Minutes}:{totalDuration.Seconds}\nВ очереди:")
                    .WithColor(Color.DarkGreen);
                int i = page * 25;
                foreach (var track in tracks)
                {
                    builder.AddField($"{++i}. {track.Title}", $"{track.Duration}");
                }
                var embed = builder.Build();
                await ReplyAsync(embed: embed);
            }
            else await ReplyQuickEmbedAsync(":x: Вы должны подключиться к голосовому каналу.", color: Color.Red);
        }

        [Alias("State", "st")]
        [Command("PlayerState")]
        public async Task GetPlayerState()
        {
            var voiceState = Context.User as IVoiceState;
            if (voiceState != null)
            {
                if (!lavaNode.HasPlayer(Context.Guild))
                {
                    await ReplyQuickEmbedAsync(":x: Я ещё не подключен к каналу...", color: Color.Red);
                    return;
                }
                var player = lavaNode.GetPlayer(Context.Guild);
                if (voiceState.VoiceChannel != player.VoiceChannel)
                {
                    await ReplyQuickEmbedAsync(":x: Вы находитесь в другом канале.", color: Color.Red);
                    return;
                }
                if (player.PlayerState == PlayerState.Stopped)
                {
                    await ReplyQuickEmbedAsync(":stop_button: Плеер остановлен, музыки нет.", color: Color.DarkOrange);
                    return;
                }

                string emoji = ":musical_note:";
                switch (player.PlayerState)
                {
                    case PlayerState.Paused:
                        emoji = ":pause_button:";
                        break;
                    case PlayerState.Stopped:
                        emoji = ":stop_button:";
                        break;
                    case PlayerState.Playing:
                        emoji = ":arrow_forward:";
                        break;
                }

                var builder = new EmbedBuilder()
                    .WithAuthor(Context.User)
                    .WithColor(new Color(0x0faaff))
                    .WithTitle("Статус плеера:")
                    .WithThumbnailUrl($"https://i.ytimg.com/vi/{player.Track.Id}/hqdefault.jpg")
                    .WithDescription($"{emoji} Текущий трек: {player.Track.Title}\nАвтор: {player.Track.Author}\nПримерное время: {player.Track.Position:hh\\:mm\\:ss}/{player.Track.Duration}\nГромкость: {player.Volume}\nПоследнее обновление: {player.LastUpdate}");
                var embed = builder.Build();
                await ReplyAsync(embed: embed);
            }
            else await ReplyQuickEmbedAsync(":x: Вы должны подключиться к голосовому каналу.", color: Color.Red);
        }

        [Alias("order")]
        [Command("SetOrder")]
        public async Task SetPlayingOrder(PlayOrder order)
        {
            IVoiceState voiceState = Context.User as IVoiceState;
            if (voiceState != null)
            {
                if (!lavaNode.HasPlayer(Context.Guild))
                {
                    await ReplyQuickEmbedAsync(":x: Я ещё не подключен к каналу...", color: Color.Red);
                    return;
                }
                var player = lavaNode.GetPlayer(Context.Guild);
                if (voiceState.VoiceChannel != player.VoiceChannel)
                {
                    await ReplyQuickEmbedAsync(":x: Вы находитесь в другом канале.", color: Color.Red);
                    return;
                }
                if (player.PlayerState == PlayerState.Stopped)
                {
                    await ReplyQuickEmbedAsync(":stop_button: Плеер остановлен, музыки нет.", color: Color.DarkOrange);
                    return;
                }

                var guildSession = musicService.GetGuildSession(Context.Guild.Id);
                if (guildSession != null) guildSession.CurrentPlayOrder = order;
                await ReplyQuickEmbedAsync("Порядок воспроизведения успешно задан!", Color.Green);
            }
            else await ReplyQuickEmbedAsync(":x: Вы должны подключиться к голосовому каналу.", color: Color.Red);
        }

        [Alias("volume")]
        [Command("SetVolume")]
        public async Task SetVolume(ushort volume)
        {
            var voiceState = Context.User as IVoiceState;
            if (voiceState != null)
            {
                if (!lavaNode.HasPlayer(Context.Guild))
                {
                    await ReplyQuickEmbedAsync(":x: Я ещё не подключен к каналу...", color: Color.Red);
                    return;
                }
                var player = lavaNode.GetPlayer(Context.Guild);
                if (voiceState.VoiceChannel != player.VoiceChannel)
                {
                    await ReplyQuickEmbedAsync(":x: Вы находитесь в другом канале.", color: Color.Red);
                    return;
                }

                await player.UpdateVolumeAsync(volume);
                await ReplyQuickEmbedAsync(":sound: Громкость успешно изменена! Наслаждайтесь музыкой!", Color.Gold);
            }
            else await ReplyQuickEmbedAsync(":x: Вы должны подключиться к голосовому каналу.", color: Color.Red);
        }

        [Command("Insert")]
        public async Task InsertMusic(int position, [Remainder] string tracks)
        {
            var voiceState = Context.User as IVoiceState;
            if (voiceState != null)
            {
                if (!lavaNode.HasPlayer(Context.Guild))
                {
                    await ReplyQuickEmbedAsync(":x: Я ещё не подключен к каналу...", color: Color.Red);
                    return;
                }
                var player = lavaNode.GetPlayer(Context.Guild);
                if (voiceState.VoiceChannel != player.VoiceChannel)
                {
                    await ReplyQuickEmbedAsync(":x: Вы находитесь в другом канале.", color: Color.Red);
                    return;
                }
                if (player.PlayerState == PlayerState.Stopped)
                {
                    await ReplyQuickEmbedAsync(":stop_button: Плеер остановлен, музыки нет.", color: Color.DarkOrange);
                    return;
                }
                position--;
                if (position < player.Queue.Count)
                {
                    var queries = tracks.Split(';');
                    foreach (var query in queries)
                    {
                        SearchResponse searchResponse;
                        if (Uri.TryCreate(query, UriKind.Absolute, out Uri uriResult) && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
                        {
                            searchResponse = await lavaNode.SearchAsync(SearchType.Direct, query);
                        }
                        else searchResponse = await lavaNode.SearchYouTubeAsync(query);
                        if (searchResponse.Status == SearchStatus.LoadFailed ||
                            searchResponse.Status == SearchStatus.NoMatches)
                        {
                            await ReplyQuickEmbedAsync($":x: Не удалось найти ничего по запросу `{query}`.", color: Color.Red);
                            return;
                        }

                        await musicService.CancelDisconnectAsync(player);
                        if (player.PlayerState == PlayerState.Playing || player.PlayerState == PlayerState.Paused)
                        {
                            if (!string.IsNullOrWhiteSpace(searchResponse.Playlist.Name))
                            {
                                foreach (var track in searchResponse.Tracks)
                                {
                                    Insert(player.Queue, track, position++);
                                }

                                await ReplyQuickEmbedAsync($":arrow_forward: Добавлено `{searchResponse.Tracks.Count}` треков.");
                            }
                            else
                            {
                                var track = searchResponse.Tracks.First();
                                Insert(player.Queue, track, position++);
                                await ReplyQuickEmbedAsync($":arrow_forward: Добавлено: {track.Title}");
                            }
                        }
                    }
                    await ReplyQuickEmbedAsync("Треки успешно вставлены в очередь!", Color.Green);
                    if (!musicService.TryGetGuildSession(Context.Guild.Id, out var value))
                    {
                        value = new GuildSessionConfiguration();
                        value.GuildID = player.VoiceChannel.Guild.Id;
                        value.ChannelId = player.VoiceChannel.Id;
                        value.CurrentPlaylist = player.Queue;
                        value.CurrentPlayOrder = PlayOrder.Direct;
                        musicService.ActiveGuilds.TryAdd(player.VoiceChannel.Guild.Id, value);
                    }
                    else
                    {
                        value.CurrentPlaylist = player.Queue;
                    }
                }
                else await ReplyQuickEmbedAsync(":x: Длина очереди меньше указанной позиции.", Color.Red);
            }
            else await ReplyQuickEmbedAsync(":x: Вы должны подключиться к голосовому каналу.", color: Color.Red);
        }

        private void Insert(DefaultQueue<LavaTrack> queue, LavaTrack value, int position)
        {
            if (position < queue.Count)
            {
                List<LavaTrack> list = new List<LavaTrack>();
                for (int i = 0; i < position; i++)
                {
                    queue.TryDequeue(out LavaTrack v);
                    list.Add(v);
                }
                list.Add(value);
                list.AddRange(queue);
                queue.Clear();
                queue.Enqueue(list);
            }
        }

        [Command("Clear")]
        public async Task Clear()
        {
            IVoiceState voiceState = Context.User as IVoiceState;
            if (voiceState != null)
            {
                if (!lavaNode.HasPlayer(Context.Guild))
                {
                    await ReplyQuickEmbedAsync(":x: Я ещё не подключен к каналу...", color: Color.Red);
                    return;
                }
                var player = lavaNode.GetPlayer(Context.Guild);
                if (voiceState.VoiceChannel != player.VoiceChannel)
                {
                    await ReplyQuickEmbedAsync(":x: Вы находитесь в другом канале.", color: Color.Red);
                    return;
                }
                if (player.PlayerState == PlayerState.Stopped)
                {
                    await ReplyQuickEmbedAsync(":stop_button: Плеер остановлен, музыки нет.", color: Color.DarkOrange);
                    return;
                }

                player.Queue.Clear();
                await ReplyQuickEmbedAsync("Очередь успешно очищена!", Color.DarkerGrey);
            }
            else await ReplyQuickEmbedAsync(":x: Вы должны подключиться к голосовому каналу.", color: Color.Red);
        }

        [Command("Remove")]
        public async Task RemoveAsync(int position)
        {
            IVoiceState voiceState = Context.User as IVoiceState;
            if (voiceState != null)
            {
                if (!lavaNode.HasPlayer(Context.Guild))
                {
                    await ReplyQuickEmbedAsync(":x: Я ещё не подключен к каналу...", color: Color.Red);
                    return;
                }
                var player = lavaNode.GetPlayer(Context.Guild);
                if (voiceState.VoiceChannel != player.VoiceChannel)
                {
                    await ReplyQuickEmbedAsync(":x: Вы находитесь в другом канале.", color: Color.Red);
                    return;
                }
                if (player.PlayerState == PlayerState.Stopped)
                {
                    await ReplyQuickEmbedAsync(":stop_button: Плеер остановлен, музыки нет.", color: Color.DarkOrange);
                    return;
                }

                if (position - 1 < player.Queue.Count) player.Queue.RemoveAt(position - 1);
                await ReplyQuickEmbedAsync("Трек успешно удалён из очереди!", Color.Orange);
                if (!musicService.TryGetGuildSession(Context.Guild.Id, out var value))
                {
                    value = new GuildSessionConfiguration();
                    value.GuildID = player.VoiceChannel.Guild.Id;
                    value.ChannelId = player.VoiceChannel.Id;
                    value.CurrentPlaylist = player.Queue;
                    value.CurrentPlayOrder = PlayOrder.Direct;
                    musicService.ActiveGuilds.TryAdd(player.VoiceChannel.Guild.Id, value);
                }
                else
                {
                    value.CurrentPlaylist = player.Queue;
                }
            }
            else await ReplyQuickEmbedAsync(":x: Вы должны подключиться к голосовому каналу.", color: Color.Red);
        }

        [Alias("Find")]
        [Command("Search")]
        public async Task SearchAsync(string searchQuery, int maxResults = 10)
        {
            if (string.IsNullOrWhiteSpace(searchQuery))
            {
                await ReplyQuickEmbedAsync(":x: Напишите мне, что я должен найти.");
                return;
            }


            SearchResponse searchResponse = await lavaNode.SearchYouTubeAsync(searchQuery);
            if (searchResponse.Status == SearchStatus.LoadFailed ||
                searchResponse.Status == SearchStatus.NoMatches)
            {
                await ReplyQuickEmbedAsync($":x: Не удалось найти ничего по запросу `{searchQuery}`.", color: Color.Red);
                return;
            }

            var builder = new EmbedBuilder()
                .WithAuthor(Context.User)
                .WithColor(new Color(0x0fffaa))
                .WithTitle("Вот что получилось найти: ")
                .WithDescription($"Максимум результатов: {Math.Min(25, maxResults)}.")
                .WithCurrentTimestamp();

            int i = 0;
            foreach (var track in searchResponse.Tracks)
            {
                if (i >= Math.Min(25, maxResults)) break;
                builder.AddField($"{++i}. {track.Title}", $"({track.Duration}) <{track.Url}>");
            }
            var embed = builder.Build();
            await ReplyAsync(embed: embed);
        }

        [RequireOwner]
        [Alias("timeout")]
        [Command("SetTimeout")]
        public async Task SetTimeout(TimeSpan timeout)
        {
            EnvironmentVariablesHandler.Variables["timeout"] = timeout.ToString("%h\\h%m\\ms\\s");
            await ReplyQuickEmbedAsync(":stopwatch: Таймаут успешно задан!", new Color(0x68a2ca));
        }

        [Command("Timeout")]
        public async Task GetTimeout()
        {
            await ReplyQuickEmbedAsync($":clock: Текущее значение таймаута отключения: { MusicService.ReadFormattedTimeSpan(EnvironmentVariablesHandler.Variables["timeout"]) ?? TimeSpan.FromSeconds(15)}", new Color(0x68a2ca));
        }
    }
}
