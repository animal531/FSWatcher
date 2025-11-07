using System;
using System.Collections.Generic;



namespace FSWatcher.Test
{

	// Save WatcherSettings and reuse
	// Picking up combos of delete/create/etc.; check what its saving/for how long
	// 
	// 
	// 
	class Program
	{
		public static void Main(string[] args)
		{
			HashSet<string> fileTypesAllowed = new HashSet<string>()
			{
				".cs"
			};

			var watcher = new Watcher(
					//Environment.CurrentDirectory,
					@"D:\dev\Projects\Holo\_Game",
					//(s) => System.Console.WriteLine("Dir created " + s),
					//(s) => System.Console.WriteLine("Dir deleted " + s),
					null,
					null,
					(changeType, fileName) => System.Console.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] {changeType} {fileName}"),
					(changeType, fileName) => System.Console.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] {changeType} {fileName}"),
					(changeType, fileName) => System.Console.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] {changeType} {fileName}"),
					fileTypesAllowed
					, new WatcherSettings(true, true, true, true, true, true, true, 200)
					);

			var settings = watcher.Settings;
			// Overriding the polling frequency in milliseconds
			//settings.SetPollFrequencyTo(1000);
			//settings.ContinuousPolling = false;

			watcher.ErrorNotifier((path, ex) => 
			{ 
				System.Console.WriteLine("{0}\n{1}", path, ex); 
			});

			// Print strategy
			System.Console.WriteLine("Will poll continuously: {0}", watcher.Settings.ContinuousPolling);
			System.Console.WriteLine("Poll frequency: {0} milliseconds", watcher.Settings.PollFrequency);
			
			//System.Console.WriteLine("Evented directory create: {0}", watcher.Settings.CanDetectEventedDirectoryCreate);
			//System.Console.WriteLine("Evented directory delete: {0}", watcher.Settings.CanDetectEventedDirectoryDelete);
			//System.Console.WriteLine("Evented directory rename: {0}", watcher.Settings.CanDetectEventedDirectoryRename);
			System.Console.WriteLine("Evented file create: {0}", watcher.Settings.CanDetectEventedFileCreate);
			System.Console.WriteLine("Evented file change: {0}", watcher.Settings.CanDetectEventedFileChange);
			System.Console.WriteLine("Evented file delete: {0}", watcher.Settings.CanDetectEventedFileDelete);
			System.Console.WriteLine("Evented file rename: {0}", watcher.Settings.CanDetectEventedFileRename);

			watcher.Watch();
			var command = System.Console.ReadLine();

			if (command == "refresh")
				watcher.ForceRefresh();

			watcher.StopWatching();
		}
	}
}
