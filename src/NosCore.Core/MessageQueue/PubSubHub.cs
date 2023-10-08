﻿//  __  _  __    __   ___ __  ___ ___
// |  \| |/__\ /' _/ / _//__\| _ \ __|
// | | ' | \/ |`._`.| \_| \/ | v / _|
// |_|\__|\__/ |___/ \__/\__/|_|_\___|
// 
// Copyright (C) 2019 - NosCore
// 
// NosCore is a free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NosCore.Core.MessageQueue.Messages;
using NosCore.Data.Enumerations.I18N;
using NosCore.Data.WebApi;
using NosCore.Shared.I18N;

namespace NosCore.Core.MessageQueue
{
    public class PubSubHub(ILogger<PubSubHub> logger, MasterClientList masterClientList, ILogLanguageLocalizer<LogLanguageKey> logLanguage)
        : Hub, IPubSubHub
    {
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var data = masterClientList.Channels.TryGetValue(Context.ConnectionId, out var channel) ? channel : null;
            if (data != null)
            {
                logger.LogDebug(logLanguage[LogLanguageKey.CHANNEL_CONNECTION_LOST],
                    data.Id.ToString(CultureInfo.CurrentCulture),
                    data.Name);
                masterClientList.Channels.Remove(Context.ConnectionId, out _);
                masterClientList.ConnectedAccounts.Remove(Context.ConnectionId, out _);
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, data.Type.ToString());
            }

            await base.OnDisconnectedAsync(exception);
        }

        public Task Bind(Channel data)
        {
            var id = ++masterClientList.ConnectionCounter;
            logger.LogDebug(logLanguage[LogLanguageKey.AUTHENTICATED_SUCCESS],
                id.ToString(CultureInfo.CurrentCulture),
                data.ClientName);
            masterClientList.ConnectedAccounts.TryAdd(Context.ConnectionId,
                new ConcurrentDictionary<long, Subscriber>());
            var serv = new ChannelInfo
            {
                Name = data.ClientName,
                Host = data.Host,
                Port = data.Port,
                DisplayPort = (ushort?)data.DisplayPort,
                DisplayHost = data.DisplayHost,
                IsMaintenance = data.StartInMaintenance,
                Id = id,
                ConnectedAccountLimit = data.ConnectedAccountLimit,
                WebApi = data.WebApi,
                Type = data.ClientType,
            };
            masterClientList.Channels.AddOrUpdate(Context.ConnectionId, serv, (_, _) => serv);
            return Task.CompletedTask;
        }

        public Task<List<ChannelInfo>> GetCommunicationChannels()
        {
            return Task.FromResult(masterClientList.Channels.Values.ToList());
        }

        public Task<List<IMessage>> ReceiveMessagesAsync()
        {
            return Task.FromResult(masterClientList.Messages.Values.ToList());
        }

        public Task<bool> DeleteMessageAsync(Guid messageId)
        {
            return Task.FromResult(masterClientList.Messages.TryRemove(messageId, out _));
        }

        public Task<bool> SendMessageAsync(IMessage message)
        {
            return Task.FromResult(masterClientList.Messages.TryAdd(message.Id, message));
        }

        public Task<bool> SendMessagesAsync(List<IMessage> messages)
        {
            return Task.FromResult(messages.Select(message => masterClientList.Messages.TryAdd(message.Id, message)).All(x => x));
        }

        public Task<List<Subscriber>> GetSubscribersAsync()
        {
            return Task.FromResult(masterClientList.ConnectedAccounts.SelectMany(x => x.Value.Values).ToList());
        }

        public Task<bool> SubscribeAsync(Subscriber subscriber)
        {
            subscriber.ChannelId = masterClientList.Channels[Context.ConnectionId].Id;
            masterClientList.ConnectedAccounts[Context.ConnectionId].AddOrUpdate(subscriber.Id, subscriber, (_, _) => subscriber);
            return Task.FromResult(true);
        }

        public Task<bool> UnsubscribeAsync(long id)
        {
            return Task.FromResult(masterClientList.ConnectedAccounts[Context.ConnectionId].TryRemove(id, out _));
        }
    }
}