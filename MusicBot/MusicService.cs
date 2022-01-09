﻿using System;
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

namespace MusicBot
{
    public class MusicService
    {
        private LavaNode lavaNode;

        private readonly ConcurrentDictionary<ulong, CancellationTokenSource> _disconnectTokens;
        
        public PlayOrder Order { get; set; }

        public MusicService(LavaNode lavaNode)
        {
            this.lavaNode = lavaNode;
            lavaNode.OnTrackEnded += OnTrackEnded;
            lavaNode.OnTrackStarted += OnTrackStarted;
            _disconnectTokens = new ConcurrentDictionary<ulong, CancellationTokenSource>();
        }

        internal async Task CancelDisconnectAsync(LavaPlayer player)
        {
            if (!_disconnectTokens.TryGetValue(player.VoiceChannel?.Id ?? 0, out var value))
            {
                return;
            }

            if (value.IsCancellationRequested)
            {
                return;
            }

            value.Cancel(true);
            await Task.CompletedTask;
            return;
        }

        private Task OnTrackStarted(TrackStartEventArgs arg)
        {
            if (!_disconnectTokens.TryGetValue(arg.Player?.VoiceChannel?.Id ?? 0, out var value))
            {
                return Task.CompletedTask;
            }

            if (value.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            value.Cancel(true);
            return Task.CompletedTask;
            //await arg.Player!.TextChannel.SendMessageAsync("Авто-отключение было отменено!");
        }

        internal async Task InitiateDisconnectAsync(LavaPlayer player, TimeSpan timeSpan)
        {
            if (player.PlayerState != PlayerState.Stopped) return;
            try
            {
                if (!_disconnectTokens.TryGetValue(player.VoiceChannel.Id, out var value))
                {
                    value = new CancellationTokenSource();
                    _disconnectTokens.TryAdd(player.VoiceChannel.Id, value);
                }
                else if (value.IsCancellationRequested)
                {
                    _disconnectTokens.TryUpdate(player.VoiceChannel.Id, new CancellationTokenSource(), value);
                    value = _disconnectTokens[player.VoiceChannel.Id];
                }

                //await player.TextChannel.SendMessageAsync($"Запускаю авто-отключение от канала! Отключение через {timeSpan}...");
                var isCancelled = SpinWait.SpinUntil(() => value.IsCancellationRequested, timeSpan);
                isCancelled |= player.VoiceChannel == null;
                if (isCancelled)
                {
                    return;
                }
                await lavaNode.LeaveAsync(player.VoiceChannel);
                await ReplyQuickEmbedAsync(player.TextChannel, "Пригласите меня снова, когда я понадоблюсь. :smile:", Color.Magenta);
            }
            finally
            {

            }
        }

        private async Task OnTrackEnded(TrackEndedEventArgs args)
        {
            var player = args.Player;

            if (!(args.Reason == TrackEndReason.Finished || args.Reason == TrackEndReason.LoadFailed)) return;

            switch (Order)
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
                _ = InitiateDisconnectAsync(args.Player, TimeSpan.FromSeconds(10));
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

    }

    public enum PlayOrder
    {
        Direct,
        Loop, 
        Random,
        Repeat
    }
}
