using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;



namespace FSWatcher
{
	public enum ChangeType
	{
		DirectoryCreated,
		DirectoryDeleted,
		FileCreated,
		FileChanged,
		FileDeleted
	}

	internal class Change
	{
		public ChangeType Type
		{
			get; private set;
		}
		public string Item
		{
			get; private set;
		}

		public Change(ChangeType type, string item)
		{
			Type = type;
			Item = item;
		}
	}

	internal class Cache
	{
		private Func<bool> _abortCheck;
		private string _dir;
		private Action<string, Exception> _onError = null;
		private Dictionary<string, string> _directories = new Dictionary<string, string>();
		private Dictionary<string, FileEx> _files = new Dictionary<string, FileEx>();
		private HashSet<string> _fileTypesAllowedDisabled;
		private HashSet<string> _fileTypesAllowed;
		private int millisecondIgnoreTime;
		private TimeSpan ignoreTime;
		private List<(DateTime ignoreUntil, string fileName)> ignoreByTimeList = new List<(DateTime ignoreUntil, string fileName)>();


		public Cache(string dir, Func<bool> abortCheck, HashSet<string> fileTypesAllowed, int millisecondIgnoreTime = WatcherSettings.DefaultMillisecondIgnoreTime)
		{
			_dir = dir;
			_abortCheck = abortCheck;
			this.millisecondIgnoreTime = millisecondIgnoreTime;
			ignoreTime = new TimeSpan(0, 0, 0, 0, millisecondIgnoreTime);

			_fileTypesAllowed = fileTypesAllowed == null ?
										new HashSet<string>() :
										new HashSet<string>(fileTypesAllowed.Select(f => f.ToLowerInvariant()));
			_fileTypesAllowedDisabled = _fileTypesAllowed;
		}

		public void Initialize(bool enableFilters = false)
		{
			GetSnapshot(_dir, ref _directories, ref _files);
			if (!enableFilters)
			{
				AdjustFilters(false);
			}
		}

		public bool IsDirectory(string dir)
		{
			return _directories.ContainsKey(dir.ToString());
		}

		public void ErrorNotifier(Action<string, Exception> notifier)
		{
			_onError = notifier;
		}

		public bool RefreshFromDisk(Action<ChangeType, string> directoryCreated, Action<ChangeType, string> directoryDeleted, Action<ChangeType, string> fileCreated, Action<ChangeType, string> fileChanged, Action<ChangeType, string> fileDeleted)
		{
			while (true)
			{
				try
				{
					var dirs = new Dictionary<string, string>();
					var files = new Dictionary<string, FileEx>();
					GetSnapshot(_dir, ref dirs, ref files);


					return (HandleDeleted(false, _directories, dirs, directoryDeleted) |
								HandleCreated(false, _directories, dirs, directoryCreated) |
								HandleDeleted(true, _files, files, fileDeleted) |
								HandleCreated(true, _files, files, fileCreated) |
								HandleChanged(true, _files, files, fileChanged));

				} catch (Exception ex)
				{
					if (_onError != null)
					{
						_onError(_dir, ex);
					}

					System.Threading.Thread.Sleep(100);
				}
			}
		}

		public bool Patch(Change item)
		{
			if (item == null)
			{
				return (false);
			}

			if (item.Type == ChangeType.DirectoryCreated)
			{
				return (Add(item.Item, _directories));
			}

			if (item.Type == ChangeType.DirectoryDeleted)
			{
				return (Remove(item.Item.ToString(), _directories));
			}

			if (item.Type == ChangeType.FileCreated)
			{
				bool allowed = FileAllowed(item.Item) && Add(GetFile(item.Item), _files);
				if (allowed)
				{
					ignoreByTimeList.Add((DateTime.Now + ignoreTime, item.Item));
				}
				return (allowed);
			}

			if (item.Type == ChangeType.FileChanged)
			{
				bool allowed = FileAllowed(item.Item) && Update(GetFile(item.Item), _files);
				if (allowed)
				{
					ignoreByTimeList.Add((DateTime.Now + ignoreTime, item.Item));
				}
				return (allowed);
			}

			if (item.Type == ChangeType.FileDeleted)
			{
				bool allowed = FileAllowed(item.Item) && Remove(GetFile(item.Item).ToString(), _files);
				if (allowed)
				{
					ignoreByTimeList.Add((DateTime.Now + ignoreTime, item.Item));
				}
				return (allowed);
			}

			return (false);
		}

		private FileEx GetFile(string file)
		{
			return (new FileEx(file, System.IO.Path.GetDirectoryName(file).GetHashCode()));
		}

		private void GetSnapshot(string directory, ref Dictionary<string, string> dirs, ref Dictionary<string, FileEx> files)
		{
			if (_abortCheck())
			{
				return;
			}

			try
			{
				foreach (var dir in System.IO.Directory.GetDirectories(directory))
				{
					if (!dirs.ContainsKey(dir.ToString()))
					{
						dirs.Add(dir.ToString(), dir);
					}
					GetSnapshot(dir, ref dirs, ref files);
				}

				foreach (var filepath in System.IO.Directory.GetFiles(directory))
				{
					var file = GetFile(filepath);
					try
					{
						if (!files.ContainsKey(file.ToString()))
						{
							files.Add(file.ToString(), file);
						}

					} catch (Exception ex)
					{
						if (_onError != null)
						{
							_onError(filepath, ex);
						}
					}

					if (_abortCheck())
					{
						return;
					}
				}

			} catch (Exception ex)
			{
				if (_onError != null)
				{
					_onError(directory, ex);
				}
			}
		}


		private ChangeType CurrentChangeType(bool isFile, ChangeType crud)
		{
			if (!isFile)
			{
				if (crud == ChangeType.FileCreated)
				{
					return (ChangeType.DirectoryCreated);
				}

				return (ChangeType.DirectoryDeleted);
			}

			return (crud);
		}
		private bool HandleCreated<T>(bool isFile, Dictionary<string, T> original, Dictionary<string, T> items, Action<ChangeType, string> action)
		{
			var hasChanges = false;
			GetCreated(original, items)
				.ForEach(x =>
				{
					string xString = x.ToString();
					ChangeType type = CurrentChangeType(isFile, ChangeType.FileCreated);
					if ((!isFile && Add(x, original)) ||
						(isFile && FileAllowed(xString) && Add(x, original)))
					{
						ignoreByTimeList.Add((DateTime.Now + ignoreTime, xString));
						Notify(type, xString, action);
						hasChanges = true;
					}
				});

			return (hasChanges);
		}

		private bool HandleChanged(bool isFile, Dictionary<string, FileEx> original, Dictionary<string, FileEx> items, Action<ChangeType, string> action)
		{
			var hasChanges = false;
			GetChanged(original, items)
				.ForEach(x =>
				{
					string xString = x.ToString();
					ChangeType type = CurrentChangeType(isFile, ChangeType.FileChanged);
					if ((!isFile && Update(x, original)) ||
						(isFile && FileAllowed(xString) && Update(x, original)))
					{
						ignoreByTimeList.Add((DateTime.Now + ignoreTime, xString));
						Notify(type, x.ToString(), action);
						hasChanges = true;
					}
				});

			return (hasChanges);
		}

		private bool HandleDeleted<T>(bool isFile, Dictionary<string, T> original, Dictionary<string, T> items, Action<ChangeType, string> action)
		{
			var hasChanges = false;
			GetDeleted(original, items)
				.ForEach(x =>
				{
					string xString = x.ToString();
					ChangeType type = CurrentChangeType(isFile, ChangeType.FileDeleted);
					if ((!isFile && Remove(x.ToString(), original)) ||
						(isFile && FileAllowed(xString) && Remove(x.ToString(), original)))
					{
						ignoreByTimeList.Add((DateTime.Now + ignoreTime, xString));
						Notify(type, x.ToString(), action);
						hasChanges = true;
					}
				});

			return (hasChanges);
		}

		private bool Add<T>(T item, Dictionary<string, T> list)
		{
			lock (list)
			{
				var key = item.ToString();
				if (!list.ContainsKey(key))
				{
					list.Add(key, item);
					return (true);
				}
			}

			return (false);
		}

		private bool Remove<T>(string item, Dictionary<string, T> list)
		{
			lock (list)
			{
				if (list.ContainsKey(item))
				{
					list.Remove(item);
					return (true);
				}
			}
			return (false);
		}

		private bool Update(FileEx file, Dictionary<string, FileEx> list)
		{
			lock (list)
			{
				FileEx originalFile;
				if (list.TryGetValue(file.Path, out originalFile))
				{
					if (!originalFile.Hash.Equals(file.Hash))
					{
						originalFile.SetHash(file.Hash);
						return (true);
					}
				}
			}
			return (false);
		}

		private void Notify(ChangeType changeType, string item, Action<ChangeType, string> action)
		{
			action?.Invoke(changeType, item.ToString());
		}

		private List<T> GetCreated<T>(Dictionary<string, T> original, Dictionary<string, T> items)
		{
			var added = new List<T>();
			foreach (var item in items)
			{
				if (!original.ContainsKey(item.Key))
				{
					added.Add(item.Value);
				}
			}

			return (added);
		}

		private List<FileEx> GetChanged(Dictionary<string, FileEx> original, Dictionary<string, FileEx> items)
		{
			var changed = new List<FileEx>();
			foreach (var item in items)
			{
				FileEx val;
				if (original.TryGetValue(item.Key, out val))
				{
					if (val.Hash != item.Value.Hash)
					{
						changed.Add(item.Value);
					}
				}
			}
			return (changed);
		}

		private List<T> GetDeleted<T>(Dictionary<string, T> original, Dictionary<string, T> items)
		{
			var deleted = new List<T>();
			foreach (var item in original)
				if (!items.ContainsKey(item.Key))
					deleted.Add(item.Value);
			return deleted;
		}


		public void AdjustFilters(bool enable)
		{
			if (enable)
			{
				_fileTypesAllowed = _fileTypesAllowedDisabled;

			} else
			{
				_fileTypesAllowed = new HashSet<string>();
			}
		}

		private bool FileAllowed(string file)
		{
			int ignoredIndex = ignoreByTimeList.FindIndex(t => t.fileName == file);
			if (ignoredIndex >= 0)
			{
				var item = ignoreByTimeList[ignoredIndex];
				if (item.ignoreUntil > DateTime.Now)
				{
					return (false);
				}

				// Remove the old entry
				ignoreByTimeList.RemoveAt(ignoredIndex);
			}

			//Console.WriteLine($"07 FileAllowed check {file}");
			if (_fileTypesAllowed.Count <= 0)
			{
				return (true);
			}

			string extension = Path.GetExtension(file).ToLowerInvariant();
			if (_fileTypesAllowed.Contains(extension))
			{
				return (true);
			}

			return (false);
		}


	}
}
