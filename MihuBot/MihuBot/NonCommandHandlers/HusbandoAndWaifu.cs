﻿using Discord;
using MihuBot.Helpers;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MihuBot.NonCommandHandlers
{
    public sealed class HusbandoAndWaifu : NonCommandHandler
    {
        protected override TimeSpan Cooldown => TimeSpan.FromMinutes(1);

        public override async ValueTask HandleAsync(MessageContext ctx)
        {
            var content = ctx.Content;

            bool waifu = content.StartsWith("@waifu", StringComparison.OrdinalIgnoreCase);

            bool husbando = !waifu &&
                (content.StartsWith("@husband", StringComparison.OrdinalIgnoreCase) &&
                (content.Length == 8 || (content[8] | 0x20) == 'o'));

            if (waifu || husbando)
            {
                string redisPrefix = waifu ? "waifu-" : "husbando-";

                if (!await TryEnterOrWarnAsync(ctx))
                    return;

                if (ctx.IsFromAdmin)
                {
                    string[] parts = content.Split(' ');
                    if (parts.Length > 2 && ulong.TryParse(parts[2], out ulong argId1))
                    {
                        ulong argId2;
                        switch (parts[1].ToLowerInvariant())
                        {
                            case "add":
                                if (parts.Length > 3 && ulong.TryParse(parts[3], out argId2))
                                {
                                    await ctx.Redis.SetAddAsync(redisPrefix + argId1, argId2.ToString());
                                    return;
                                }
                                break;

                            case "remove":
                                if (parts.Length > 3 && ulong.TryParse(parts[3], out argId2))
                                {
                                    await ctx.Redis.SetRemoveAsync(redisPrefix + argId1, argId2.ToString());
                                    return;
                                }
                                break;

                            case "list":
                                ulong[] partners = (await ctx.Redis.SetScanAsync(redisPrefix + argId1).ToArrayAsync())
                                    .Select(p => ulong.Parse(p))
                                    .ToArray();
                                await ctx.ReplyAsync($"```\n{string.Join('\n', partners.Select(p => ctx.Discord.GetUser(p).Username))}\n```");
                                return;
                        }
                    }
                }

                RedisValue partner = await ctx.Redis.SetRandomMemberAsync(redisPrefix + ctx.AuthorId);

                if (partner.IsNullOrEmpty || !ulong.TryParse(partner, out ulong partnerId))
                {
                    await ctx.ReplyAsync($"{Emotes.PepePoint}");
                }
                else
                {
                    await ctx.ReplyAsync(MentionUtils.MentionUser(partnerId));
                }
            }
        }
    }
}
