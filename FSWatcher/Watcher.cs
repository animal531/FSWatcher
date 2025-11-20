using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;



namespace FSWatcher
{
	public class Watcher : IDisposable
	{
		private string _dir;
		private bool _exit = false;
		private WatcherSettings _settings;
		private Cache _cache;
		private FSW _fsw;
		private Action<ChangeType, string> _fileCreated;
		private Action<ChangeType, string> _fileChanged;
		private Action<ChangeType, string> _fileDeleted;
		private Action<ChangeType, string> _directoryCreated;
		private Action<ChangeType, string> _directoryDeleted;
		private Action<string, Exception> _onError = null;
		private Thread _watcher = null;



		private DateTime _nextCatchup = DateTime.MinValue;

		public WatcherSettings Settings
		{
			get
			{
				return _settings;
			}
		}


		public Watcher(string dir,
					int millisecondIgnoreTime,
					Action<ChangeType, string> directoryCreated, Action<ChangeType, string> directoryDeleted, Action<ChangeType, string> fileCreated, Action<ChangeType, string> fileChanged, Action<ChangeType, string> fileDeleted,
					HashSet<string> fileTypesAllowed
					)
		{
			_dir = dir;
			_cache = new Cache(_dir, () => _exit, fileTypesAllowed, millisecondIgnoreTime);
			_settings = SettingsReader.GetSettings();

			_directoryCreated = directoryCreated;
			_directoryDeleted = directoryDeleted;
			_fileCreated = fileCreated;
			_fileChanged = fileChanged;
			_fileDeleted = fileDeleted;

		}
		public Watcher(string dir,
							int millisecondIgnoreTime,
							Action<ChangeType, string> directoryCreated, Action<ChangeType, string> directoryDeleted, Action<ChangeType, string> fileCreated, Action<ChangeType, string> fileChanged, Action<ChangeType, string> fileDeleted,
							HashSet<string> fileTypesAllowed,
							WatcherSettings cachedSettings
							)
		{
			_dir = dir;
			_cache = new Cache(_dir, () => _exit, fileTypesAllowed, millisecondIgnoreTime);
			_settings = cachedSettings;
			
			_directoryCreated = directoryCreated;
			_directoryDeleted = directoryDeleted;
			_fileCreated = fileCreated;
			_fileChanged = fileChanged;
			_fileDeleted = fileDeleted;

		}

		public void Watch()
		{
			_watcher = new Thread(() =>
			{
				Initialize(true);
				_fsw = new FSW(
					_dir,
					(dir) =>
					{
						if (_cache.Patch(new Change(ChangeType.DirectoryCreated, dir)))
						{
							_directoryCreated?.Invoke(ChangeType.DirectoryCreated, dir);
						}
						SetNextCatchup();
					},
					(dir) =>
					{
						if (_cache.Patch(new Change(ChangeType.DirectoryDeleted, dir)))
						{
							_directoryDeleted?.Invoke(ChangeType.DirectoryDeleted, dir);
						}
						SetNextCatchup();
					},
					(file) =>
					{
						if (_cache.Patch(new Change(ChangeType.FileCreated, file)))
						{
							_fileCreated?.Invoke(ChangeType.FileCreated, file);
						}
						SetNextCatchup();
					},
					(file) =>
					{
						if (_cache.Patch(new Change(ChangeType.FileChanged, file)))
						{
							_fileChanged?.Invoke(ChangeType.FileChanged, file);
						}
						SetNextCatchup();
					},
					(file) =>
					{
						if (_cache.Patch(new Change(ChangeType.FileDeleted, file)))
						{
							_fileDeleted?.Invoke(ChangeType.FileDeleted, file);
						}
						SetNextCatchup();
					},
					(item) =>
					{
						SetNextCatchup();
					},
					_cache);

				_fsw.Start();

				while (!_exit)
				{
					if (_fsw.NeedsRestart)
					{
						_fsw.Start();
						SetNextCatchup();
					}

					if (WeNeedToCatchUp())
					{
						Poll();
					}

					if (_settings.ContinuousPolling && !WaitingToCatchUp())
					{
						SetNextCatchup();
					}

					Thread.Sleep(_settings.PollFrequency + 10);
				}
				_fsw.Stop();
			});

			_watcher.Priority = ThreadPriority.BelowNormal;
			_watcher.Start();
		}

		public void ForceRefresh()
		{
			Poll();
		}

		public void StopWatching()
		{
			_exit = true;

			if (_watcher == null)
			{
				return;
			}

			while (_watcher.IsAlive)
			{
				Thread.Sleep(10);
			}
		}

		public void ErrorNotifier(Action<string, Exception> notifier)
		{
			_onError = notifier;
			_cache.ErrorNotifier(notifier);
		}

		public void Dispose()
		{
			StopWatching();
		}

		private void Initialize(bool enableFilters = true)
		{
			var startTime = DateTime.Now;
			_cache.Initialize(true);
			_settings.SetPollFrequencyTo(TimeSince(startTime) * 4);
		}

		private int TimeSince(DateTime time)
		{
			return (Convert.ToInt32(DateTime.Now.Subtract(time).TotalMilliseconds));
		}

		private void Poll()
		{
			var hasChanges = _cache.RefreshFromDisk(_directoryCreated, _directoryDeleted, _fileCreated, _fileChanged, _fileDeleted);
			ClearCatchup();

			if (hasChanges)
			{
				SetNextCatchup();
			}
		}

		private bool WeNeedToCatchUp()
		{
			return (_nextCatchup != DateTime.MinValue && DateTime.Now > _nextCatchup);
		}

		private bool WaitingToCatchUp()
		{
			return (_nextCatchup != DateTime.MinValue);
		}

		private void ClearCatchup()
		{
			_nextCatchup = DateTime.MinValue;
		}

		private void SetNextCatchup()
		{
			_nextCatchup = DateTime.Now.AddMilliseconds(_settings.PollFrequency);
		}



	}
}
