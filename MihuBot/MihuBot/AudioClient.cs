﻿using Discord;
using Discord.Audio;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace MihuBot
{
    public class AudioClient
    {
        private static readonly Dictionary<ulong, AudioClient> _audioClients = new Dictionary<ulong, AudioClient>();


        private static readonly HttpClient HttpClient = new HttpClient();

        private readonly SocketGuild _guild;
        private readonly AudioOutStream _audioStream;

        private readonly Queue<AudioSource> _sourcesQueue;
        private AudioSource _activeStream;

        private AudioClient(SocketGuild guild)
        {
            _guild = guild;
            _audioStream = guild.AudioClient.CreatePCMStream(AudioApplication.Music);
            _sourcesQueue = new Queue<AudioSource>();
        }

        public static async Task<AudioClient> TryGetOrJoinAsync(SocketGuild guild, SocketVoiceChannel channelToJoin)
        {
            lock (_audioClients)
            {
                if (_audioClients.TryGetValue(guild.Id, out var audioClient))
                {
                    return audioClient;
                }
            }

            if (channelToJoin is null)
                return null;

            await channelToJoin.ConnectAsync();

            lock (_audioClients)
            {
                if (_audioClients.TryGetValue(guild.Id, out var audioClient))
                {
                    return audioClient;
                }

                audioClient = new AudioClient(guild);
                _audioClients.Add(guild.Id, audioClient);
                return audioClient;
            }
        }

        public async Task TryQueueContentAsync(SocketMessage message)
        {
            Uri uri = null;
            string url = message.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(p => Uri.TryCreate(p, UriKind.Absolute, out uri));
            if (url is null)
            {
                await message.ReplyAsync("Please supply a valid url", mention: true);
                return;
            }

            if (!uri.AbsolutePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                await message.ReplyAsync("Only mp3 rn");
                return;
            }

            if (uri.Host.Equals("dropbox.com", StringComparison.OrdinalIgnoreCase))
            {
                AddAudioSource(new DropboxAudioSource(uri));
            }
            else
            {
                await message.ReplyAsync("Only dropbox rn");
            }
        }

        private void AddAudioSource(AudioSource source)
        {
            lock (_audioStream)
            {
                if (_activeStream is null)
                {
                    _activeStream = source;
                    _ = Task.Run(async () => await SourceRelayTaskAsync());
                }
                else
                {
                    _sourcesQueue.Enqueue(source);
                }
            }
        }

        private async Task SourceRelayTaskAsync()
        {
            byte[] buffer = new byte[32 * 1024];

            while (true)
            {
                Debug.Assert(_activeStream != null);

                try
                {
                    await _activeStream.InitAsync();

                    int read;
                    while ((read = await _activeStream.ReadAsync(buffer)) > 0)
                    {
                        await _audioStream.WriteAsync(buffer.AsMemory(0, read));
                    }
                }
                catch (Exception ex)
                {
                    await Program.DebugAsync(ex.ToString());
                }

                try
                {
                    await _activeStream.CleanupAsync();
                }
                catch { }

                lock (_sourcesQueue)
                {
                    if (_sourcesQueue.Count == 0)
                    {
                        _activeStream = null;
                        break;
                    }

                    _activeStream = _sourcesQueue.Dequeue();
                    continue;
                }
            }
        }




        public sealed class DropboxAudioSource : AudioSource
        {
            private readonly Uri _uri;
            private string _tempFilePath;

            private Stream _tempFileReadStream;

            public DropboxAudioSource(Uri uri)
            {
                _uri = uri;
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer) => _tempFileReadStream.ReadAsync(buffer);

            public override async Task InitAsync()
            {
                _tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3");

                var response = await HttpClient.GetAsync(_uri, HttpCompletionOption.ResponseHeadersRead);
                var stream = await response.Content.ReadAsStreamAsync();

                using var fs = File.OpenWrite(_tempFilePath);
                await stream.CopyToAsync(fs);

                _tempFileReadStream = File.OpenRead(_tempFilePath);
            }

            public override Task CleanupAsync()
            {
                try
                {
                    _tempFileReadStream.Dispose();
                }
                catch { }

                File.Delete(_tempFilePath);
                return Task.CompletedTask;
            }
        }

        public abstract class AudioSource
        {
            public abstract Task InitAsync();
            public abstract ValueTask<int> ReadAsync(Memory<byte> buffer);
            public virtual Task CleanupAsync() => Task.CompletedTask;
        }
    }
}
