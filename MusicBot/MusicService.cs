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
using Discord;
using Discord.Commands;
using System.Globalization;

namespace MusicBot
{
    public class MusicService
    {
        private LavaNode lavaNode;

        public ConcurrentDictionary<ulong, GuildSessionConfiguration> ActiveGuilds { get; } = new ConcurrentDictionary<ulong, GuildSessionConfiguration>();

        public GuildSessionConfiguration GetGuildSession(ulong channelId)
        {
            if (ActiveGuilds.TryGetValue(channelId, out var guildSession)) return guildSession;
            return null;
        }

        public bool TryGetGuildSession(ulong channelId, out GuildSessionConfiguration guildSession)
        {
            return ActiveGuilds.TryGetValue(channelId, out guildSession);
        }

        public MusicService(LavaNode lavaNode)
        {
            this.lavaNode = lavaNode;
            lavaNode.OnTrackEnded += OnTrackEnded;
            lavaNode.OnTrackStarted += OnTrackStarted;
        }

        internal async Task CancelDisconnectAsync(LavaPlayer player)
        {
            if (!TryGetGuildSession(player.VoiceChannel?.Guild.Id ?? 0, out var value))
            {
                return;
            }
            var tokenSource = value.CancellationTokenSource;
            if (tokenSource.IsCancellationRequested)
            {
                return;
            }

            tokenSource.Cancel(true);
            await Task.CompletedTask;
            return;
        }

        private Task OnTrackStarted(TrackStartEventArgs arg)
        {
            if (arg.Player == null) return Task.CompletedTask;
            if (!TryGetGuildSession(arg.Player.VoiceChannel.Guild.Id, out var value))
            {
                value = new GuildSessionConfiguration();
                value.GuildID = arg.Player.VoiceChannel.Guild.Id;
                value.ChannelId = arg.Player.VoiceChannel.Id;
                value.CurrentPlaylist = arg.Player.Queue;
                value.CurrentPlayOrder = PlayOrder.Direct;
                ActiveGuilds.TryAdd(arg.Player.VoiceChannel.Guild.Id, value);
                return Task.CompletedTask;
            }
            var tokenSource = value.CancellationTokenSource;
            if (tokenSource.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            tokenSource.Cancel(true);
            return Task.CompletedTask;
            //await arg.Player!.TextChannel.SendMessageAsync("Авто-отключение было отменено!");
        }

        internal async Task InitiateDisconnectAsync(LavaPlayer player, TimeSpan timeSpan)
        {
            if (player.PlayerState != PlayerState.Stopped) return;
            try
            {
                if (!TryGetGuildSession(player.VoiceChannel.Guild.Id, out var value))
                {
                    value = new GuildSessionConfiguration
                    {
                        CancellationTokenSource = new CancellationTokenSource()
                    };
                    ActiveGuilds.TryAdd(player.VoiceChannel.Guild.Id, value);
                }
                else if (value.CancellationTokenSource.IsCancellationRequested)
                {
                    value.CancellationTokenSource = new CancellationTokenSource();
                }

                //await player.TextChannel.SendMessageAsync($"Запускаю авто-отключение от канала! Отключение через {timeSpan}...");
                var isCancelled = SpinWait.SpinUntil(() => value.CancellationTokenSource.IsCancellationRequested, timeSpan);
                isCancelled |= player.VoiceChannel == null;
                if (isCancelled)
                {
                    return;
                }
                ActiveGuilds.TryRemove(player.VoiceChannel.Guild.Id, out _);
                await lavaNode.LeaveAsync(player.VoiceChannel);
                await ReplyQuickEmbedAsync(player.TextChannel, "Пригласите меня снова, когда я понадоблюсь. :smile:", Color.Magenta);
            }
            catch (Exception ex)
            {
                CommandServiceHandler.logger.Warn(ex.Message);
            }
            finally
            {
                
            }
        }

        private async Task OnTrackEnded(TrackEndedEventArgs args)
        {
            var player = args.Player;

            if (!(args.Reason == TrackEndReason.Finished || args.Reason == TrackEndReason.LoadFailed)) return;

            var playOrder = GetGuildSession(player.VoiceChannel.Guild.Id)?.CurrentPlayOrder ?? PlayOrder.Direct;

            switch (playOrder)
            {
                case PlayOrder.Direct:
                    break;
                case PlayOrder.Loop:
                    player.Queue.Enqueue(args.Track);
                    break;
                case PlayOrder.Random:
                    player.Queue.Shuffle();
                    player.Queue.Enqueue(args.Track);
                    break;
                case PlayOrder.Repeat:
                    await args.Player.PlayAsync(args.Track);
                    await ReplyQuickEmbedAsync(args.Player.TextChannel, $"{args.Reason}: {args.Track.Title}\nСейчас играет: {args.Track.Title}", Color.LighterGrey);
                    return;
            }


            if (!player.Queue.TryDequeue(out var queueable))
            {
                if (player.PlayerState != PlayerState.Stopped) return;
                await ReplyQuickEmbedAsync(args.Player.TextChannel, ":cd: Очередь завершена! Можете добавить побольше музыки, чтобы не скучать!", Color.DarkBlue);
                _ = InitiateDisconnectAsync(args.Player, ReadFormattedTimeSpan(EnvironmentVariablesHandler.Variables["timeout"]) ?? TimeSpan.FromSeconds(15));
                return;
            }
            
            if (queueable is not LavaTrack track)
            {
                await ReplyQuickEmbedAsync(args.Player.TextChannel, ":x: Далее в очереди находится что-то не музыкальное.", Color.Red);
                return;
            }
            await args.Player.PlayAsync(track);
            await ReplyQuickEmbedAsync(args.Player.TextChannel, $"{args.Reason}: {args.Track.Title}\nСейчас играет: {track.Title}", Color.LighterGrey);
        }

        private static async Task ReplyQuickEmbedAsync(ITextChannel channel, string message, Color color = default)
        {
            var builder = new EmbedBuilder()
                .WithDescription(message)
                .WithColor(color);
            var embed = builder.Build();
            await channel.SendMessageAsync(embed: embed);
        }

        public static TimeSpan? ReadFormattedTimeSpan(string data)
        {
            if (string.IsNullOrEmpty(data)) return null;
            data = data.Trim().ToLower();
            TimeSpan output = new TimeSpan();
            if (data.Contains('h'))
            {
                int pos = data.IndexOf('h') + 1;
                output += TimeSpan.ParseExact(data.Substring(0, pos), @"h\h", CultureInfo.InvariantCulture);
                data = data.Substring(pos);
            }
            if (data.Contains('m'))
            {
                int pos = data.IndexOf('m') + 1;
                output += TimeSpan.ParseExact(data.Substring(0, pos), @"m\m", CultureInfo.InvariantCulture);
                data = data.Substring(pos);
            }
            if (data.Contains('s'))
            {
                int pos = data.IndexOf('s') + 1;
                output += TimeSpan.ParseExact(data.Substring(0, pos), @"s\s", CultureInfo.InvariantCulture);
                //data = data.Substring(pos);
            }
            return output;
        }
    }

    public enum PlayOrder
    {
        Direct,
        Loop, 
        Random,
        Repeat
    }
}
