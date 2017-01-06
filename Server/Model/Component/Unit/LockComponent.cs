﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Base;

namespace Model
{
	public enum LockStatus
	{
		LockedNot,
		LockRequesting,
		Locked,
	}

	/// <summary>
	/// 分布式锁组件,Unit对象可能在不同进程上有镜像,访问该对象的时候需要对他加锁
	/// </summary>
	[EntityEvent(typeof(LockComponent))]
	public class LockComponent: Component
	{
		private LockStatus status = LockStatus.LockedNot;
		private string address;
		private int lockCount;
		private readonly Queue<TaskCompletionSource<bool>> queue = new Queue<TaskCompletionSource<bool>>();

		public void Awake(string addr)	
		{
			this.address = addr;
		}
		
		public async Task Lock()
		{
			++this.lockCount;

			if (this.status == LockStatus.Locked)
			{
				return;
			}
			if (this.status == LockStatus.LockRequesting)
			{
				await WaitLock();
				return;
			}
			
			this.status = LockStatus.LockRequesting;

			// 真身直接本地请求锁,镜像需要调用Rpc获取锁
			MasterComponent masterComponent = this.GetComponent<MasterComponent>();
			if (masterComponent != null)
			{
				await masterComponent.Lock(this.address);
			}
			else
			{
				RequestLock();
				await WaitLock();
			}
		}

		private Task<bool> WaitLock()
		{
			TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
			if (this.status == LockStatus.Locked)
			{
				tcs.SetResult(true);
				return tcs.Task;
			}

			this.queue.Enqueue(tcs);
			return tcs.Task;
		}

		private async void RequestLock()
		{
			try
			{
				Session session = Game.Scene.GetComponent<NetInnerComponent>().Get(this.address);
				string serverAddress = Game.Scene.GetComponent<StartConfigComponent>().StartConfig.ServerIP;
				G2G_LockRequest request = new G2G_LockRequest { Id = this.Owner.Id, Address = serverAddress };
				await session.Call<G2G_LockRequest, G2G_LockResponse>(request);

				this.status = LockStatus.Locked;

				foreach (TaskCompletionSource<bool> taskCompletionSource in this.queue)
				{
					taskCompletionSource.SetResult(true);
				}
				this.queue.Clear();
			}
			catch (Exception e)
			{
				Log.Error($"获取锁失败: {this.address} {this.Owner.Id} {e}");
			}
		}

		public async Task Release()
		{
			--this.lockCount;
			if (this.lockCount != 0)
			{
				return;
			}

			this.status = LockStatus.LockedNot;
			Session session = Game.Scene.GetComponent<NetInnerComponent>().Get(this.address);
			G2G_LockReleaseRequest request = new G2G_LockReleaseRequest();
			await session.Call<G2G_LockReleaseRequest, G2G_LockReleaseResponse>(request);
		}
	}
}