using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Victoria;

namespace MusicBot
{
    public class GuildSessionConfiguration
    {
        public ulong GuildID { get; set; }

        public ulong ChannelId { get; set; }

        public CancellationTokenSource CancellationTokenSource { get; set; }

        public PlayOrder CurrentPlayOrder { get; set; }
         
        public DefaultQueue<LavaTrack> CurrentPlaylist { get; set; }

        public GuildSessionConfiguration()
        {
            CancellationTokenSource = new CancellationTokenSource();
            CurrentPlaylist = new DefaultQueue<LavaTrack>();
        }
    }
}
