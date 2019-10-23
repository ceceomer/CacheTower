﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace CacheTower.Extensions.Redis
{
	public class RedisRemoteEvictionExtension : IValueRefreshExtension
	{
		private ConnectionMultiplexer Connection { get; }
		private ISubscriber Subscriber { get; }
		private string RedisChannel { get; }

		private bool IsRegistered { get; set;  }

		private readonly object FlaggedRefreshesLockObj = new object();
		private HashSet<string> FlaggedRefreshes { get; } = new HashSet<string>();

		public RedisRemoteEvictionExtension(ConnectionMultiplexer connection, string channelPrefix = "CacheTower")
		{
			Connection = connection ?? throw new ArgumentNullException(nameof(connection));

			if (channelPrefix == null)
			{
				throw new ArgumentNullException(nameof(channelPrefix));
			}

			Subscriber = Connection.GetSubscriber();
			RedisChannel = $"{channelPrefix}.RemoteEviction";
		}

		public async Task OnValueRefreshAsync(string requestId, string cacheKey, TimeSpan timeToLive)
		{
			lock (FlaggedRefreshesLockObj)
			{
				FlaggedRefreshes.Add(cacheKey);
			}

			await Subscriber.PublishAsync(RedisChannel, cacheKey, CommandFlags.FireAndForget);
		}

		public void Register(ICacheStack cacheStack)
		{
			if (IsRegistered)
			{
				throw new InvalidOperationException($"{nameof(RedisRemoteEvictionExtension)} can only be registered to one {nameof(ICacheStack)}");
			}
			IsRegistered = true;

			Subscriber.Subscribe(RedisChannel, async (channel, value) =>
			{
				string cacheKey = value;
				var shouldEvictLocally = false;
				lock (FlaggedRefreshesLockObj)
				{
					shouldEvictLocally = FlaggedRefreshes.Remove(cacheKey) == false;
				}

				if (shouldEvictLocally)
				{
					await cacheStack.EvictAsync(cacheKey);
				}
			}, CommandFlags.FireAndForget);
		}
	}
}