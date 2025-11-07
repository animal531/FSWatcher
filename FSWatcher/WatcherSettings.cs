using System;
using System.IO;
using System.Threading;
using System.Runtime.CompilerServices;




namespace FSWatcher
{
	internal class SettingsReader
	{
		private static bool _canDetectDirectoryCreate;
		private static bool _canDetectDirectoryDelete;
		private static bool _canDetectFileCreate;
		private static bool _canDetectFileChange;
		private static bool _canDetectFileDelete;


		public static WatcherSettings GetSettings()
		{
			// If we're not forcing a re-check and the passed in settings are ok, then skip the new check for a faster startup
			//if (!recheckFullSupport && !cachedSettings.IsDefault)
			//{
			//	return (cachedSettings);
			//}
			

			var maxWaitTime = 3000;

			var file2Deleted = false;
			var file3Created = false;

			var dir2Deleted = false;
			var dir3Created = false;

			//changeDir = "C:\\Users\\animal\\AppData\\Local\\Temp\\tmp2F7B.tmp"
			var changeDir = Path.GetTempFileName();
			//changeDir = "C:\\Users\\animal\\AppData\\Local\\Temp\\changedir_tmp2F7B"
			changeDir = Path.Combine(Path.GetDirectoryName(changeDir), "changedir_" + Path.GetFileNameWithoutExtension(changeDir));
			Directory.CreateDirectory(changeDir);

			var subdir = Path.Combine(changeDir, "subdir");
			//subdir = "C:\\Users\\animal\\AppData\\Local\\Temp\\changedir_tmp2F7B\\subdir"
			var subdirDelete = Path.Combine(changeDir, "subdirdelete");
			//dir2 = "C:\\Users\\animal\\AppData\\Local\\Temp\\changedir_tmp2F7B\\subdir1"
			var dir2 = Path.Combine(changeDir, "subdir1");
			//dir3 = "C:\\Users\\animal\\AppData\\Local\\Temp\\changedir_tmp2F7B\\subdir2"
			var dir3 = Path.Combine(changeDir, "subdir2");
			//file = "C:\\Users\\animal\\AppData\\Local\\Temp\\changedir_tmp2F7B\\subdir\\myfile.txt"
			var file = Path.Combine(subdir, "myfile.txt");
			//file2 = "C:\\Users\\animal\\AppData\\Local\\Temp\\changedir_tmp2F7B\\MovedFile.txt"
			var file2 = Path.Combine(changeDir, "MovedFile.txt");
			//file3 = "C:\\Users\\animal\\AppData\\Local\\Temp\\changedir_tmp2F7B\\MovedFile.txt.again"
			var file3 = file2 + ".again";
			//fileContentChange = "C:\\Users\\animal\\AppData\\Local\\Temp\\changedir_tmp2F7B\\contentToChange.txt"
			var fileContentChange = Path.Combine(changeDir, "contentToChange.txt");
			
			Directory.CreateDirectory(dir2);
			Directory.CreateDirectory(subdirDelete);
			File.WriteAllText(fileContentChange, "to be changed");
			File.WriteAllText(file2, "hey");

			var cache = new Cache(changeDir, () => false, null);
			cache.Initialize(false);

			Func<bool> fullSupport = () =>
			{
				return
					_canDetectDirectoryCreate &&
					_canDetectDirectoryDelete &&
					dir2Deleted && dir3Created &&
					_canDetectFileCreate &&
					_canDetectFileChange &&
					_canDetectFileDelete &&
					file2Deleted && file3Created;
			};

			var fsw = new FSW(
				changeDir,
				(s) =>
				{
					cache.Patch(new Change(ChangeType.DirectoryCreated, s));
					_canDetectDirectoryCreate = true;
					if (s == dir3)
						dir3Created = true;
				},
				(s) =>
				{
					cache.Patch(new Change(ChangeType.DirectoryDeleted, s));
					_canDetectDirectoryDelete = true;
					if (s == dir2)
						dir2Deleted = true;
				},
				(s) =>
				{
					_canDetectFileCreate = true;
					if (s == file3)
						file3Created = true;
				},
				(s) => _canDetectFileChange = true,
				(s) =>
				{
					_canDetectFileDelete = true;
					if (s == file2)
						file2Deleted = true;
				},
				(s) => { },
					 cache);
			fsw.Start();

			var fileChanges = new Thread(() =>
			{
				var startTime = DateTime.Now;

				// subdir = "C:\\Users\\animal\\AppData\\Local\\Temp\\changedir_tmp2F7B\\subdir"
				Directory.CreateDirectory(subdir);
				// file = "C:\\Users\\animal\\AppData\\Local\\Temp\\changedir_tmp2F7B\\subdir\\myfile.txt"
				File.WriteAllText(file, "hey");
				//fileContentChange = "C:\\Users\\animal\\AppData\\Local\\Temp\\changedir_tmp2F7B\\contentToChange.txt"
				using (var writer = File.AppendText(fileContentChange))
				{
					writer.Write("moar content");
				}
				// From file2 = "C:\\Users\\animal\\AppData\\Local\\Temp\\changedir_tmp2F7B\\MovedFile.txt"
				//   To file3 = "C:\\Users\\animal\\AppData\\Local\\Temp\\changedir_tmp2F7B\\MovedFile.txt.again"
				File.Move(file2, file3);
				// file = "C:\\Users\\animal\\AppData\\Local\\Temp\\changedir_tmp2F7B\\subdir\\myfile.txt"
				File.Delete(file);
				// From dir2 = "C:\\Users\\animal\\AppData\\Local\\Temp\\changedir_tmp2F7B\\subdir1"
				//   To dir3 = "C:\\Users\\animal\\AppData\\Local\\Temp\\changedir_tmp2F7B\\subdir2"
				Directory.Move(dir2, dir3);
				// subdirDelete = "C:\\Users\\animal\\AppData\\Local\\Temp\\changedir_tmp2F7B\\subdirdelete"
				Directory.Delete(subdirDelete);

				while (!fullSupport() && TimeSince(startTime) < maxWaitTime)
				{
					Thread.Sleep(10);
				}
			});

			fileChanges.Start();
			fileChanges.Join();

			fsw.Stop();

			Directory.Delete(dir3);
			if (Directory.Exists(subdirDelete))
			{
				Directory.Delete(subdirDelete);
			}
			File.Delete(fileContentChange);
			File.Delete(file3);
			Directory.Delete(subdir);
			Directory.Delete(changeDir);

			Console.WriteLine($"WatcherSettings: \r\n_canDetectDirectoryCreate {_canDetectDirectoryCreate}\r\n_canDetectDirectoryDelete {_canDetectDirectoryDelete}\r\n_canDetectFileCreate {_canDetectFileCreate}\r\n_canDetectFileChange {_canDetectFileChange}\r\n_canDetectFileDelete {_canDetectFileDelete}");

			cache.AdjustFilters(true);

			return (new WatcherSettings(
						_canDetectDirectoryCreate,
						_canDetectDirectoryDelete,
						dir2Deleted && dir3Created,
						_canDetectFileCreate,
						_canDetectFileChange,
						_canDetectFileDelete,
						file2Deleted && file3Created));
		}

		private static double TimeSince(DateTime startTime)
		{
			return (DateTime.Now.Subtract(startTime).TotalMilliseconds);
		}
	}


	public struct WatcherSettings
	{
		private static readonly WatcherSettings IDefault = new WatcherSettings(false, false, false, false, false, false, false, -1);
		public static WatcherSettings Default
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get
			{
				return (IDefault);
			}
		}


		public bool ContinuousPolling
		{
			get
			{
				var supportsAll =
					 CanDetectEventedDirectoryCreate &&
					 CanDetectEventedDirectoryDelete &&
					 CanDetectEventedDirectoryRename &&
					 CanDetectEventedFileCreate &&
					 CanDetectEventedFileChange &&
					 CanDetectEventedFileDelete &&
					 CanDetectEventedFileRename;

				return (!supportsAll);
			}
		}

		/// <summary>
		/// Is this a default instance?
		/// </summary>
		public bool IsDefault
		{
			get
			{
				return (PollFrequency == Default.PollFrequency);
			}
		}


		public bool CanDetectEventedDirectoryCreate
		{
			get; private set;
		}
		public bool CanDetectEventedDirectoryDelete
		{
			get; private set;
		}
		public bool CanDetectEventedDirectoryRename
		{
			get; private set;
		}
		public bool CanDetectEventedFileCreate
		{
			get; private set;
		}
		public bool CanDetectEventedFileChange
		{
			get; private set;
		}
		public bool CanDetectEventedFileDelete
		{
			get; private set;
		}
		public bool CanDetectEventedFileRename
		{
			get; private set;
		}
		public int PollFrequency
		{
			get; private set;
		}



		public WatcherSettings(bool canDetectDirectoryCreate, bool canDetectDirectoryDelete, bool canDetectDirectoryRename, bool canDetectFileCreate, bool canDetectFileChange, bool canDetectFileDelete, bool canDetectFileRename, int pollFrequency = 100)
		{
			CanDetectEventedDirectoryCreate = canDetectDirectoryCreate;
			CanDetectEventedDirectoryDelete = canDetectDirectoryDelete;
			CanDetectEventedDirectoryRename = canDetectDirectoryRename;
			CanDetectEventedFileCreate = canDetectFileCreate;
			CanDetectEventedFileChange = canDetectFileChange;
			CanDetectEventedFileDelete = canDetectFileDelete;
			CanDetectEventedFileRename = canDetectFileRename;
			PollFrequency = Math.Max(100, pollFrequency);
		}

		/// <summary>
		/// Polling Frequency
		/// </summary>
		/// <param name="milliseconds">Must be at least 100 milliseconds</param>
		public void SetPollFrequencyTo(int milliseconds)
		{
			PollFrequency = Math.Max(100, milliseconds);
		}

	}
}
