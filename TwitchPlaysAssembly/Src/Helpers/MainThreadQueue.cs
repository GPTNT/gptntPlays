using System;
using System.Collections.Generic;
using System.Threading;
using log4net;

/// <summary>
/// Allows the execution of a function with the guarantee that it will run on Unity's mainthread.
/// </summary>
static class MainThreadQueue
{
	static readonly Queue<Action> ActionQueue = new Queue<Action>();
	static int MainThreadID;

	private static ILog log = LogManager.GetLogger("MainThreadQueue");

	/// <summary>
	/// Stores the current thread ID, allowing enqueued functions to execute immediately if they were already on Unity's mainthread.
	/// Must be called from Unity's mainthread to work properly.
	/// </summary>
	public static void Initialize()
	{
		MainThreadID = Thread.CurrentThread.ManagedThreadId;
	}

	public static void Enqueue(Action action)
	{
		if (Thread.CurrentThread.ManagedThreadId == MainThreadID)
		{
			action();
		}
		else
		{
			lock (ActionQueue)
			{
				log.Debug("Action Queue length: " + ActionQueue.Count);
				ActionQueue.Enqueue(action);
			}
		}
	}

	/// <summary>
	/// Runs all enqueued functions.
	/// Must be called from Unity's mainthread to work properly.
	/// </summary>
	public static void ProcessQueue()
	{
		if (Thread.CurrentThread.ManagedThreadId != MainThreadID) throw new Exception("ProcessQueue() called outside the mainthread.");

		if (ActionQueue.Count != 0)
		{
			lock (ActionQueue)
			{
				while (ActionQueue.Count != 0)
				{
					ActionQueue.Dequeue().Invoke();
				}
			}
		}
	}
}