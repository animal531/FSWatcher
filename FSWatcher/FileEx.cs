using System;
using System.IO;



namespace FSWatcher
{
	internal class FileEx
	{
		public string Path
		{
			get; private set;
		}
		public long Hash
		{
			get; private set;
		}
		public int Directory
		{
			get; private set;
		}

		public FileEx(string file, int dir)
		{
			Path = file;
			Directory = dir;
			Hash = GetContentHash();
		}

		public void SetHash(long newHash)
		{
			Hash = newHash;
		}

		public override bool Equals(object obj)
		{
			if (obj.GetType() == typeof(FileEx))
			{
				return (Path.Equals(((FileEx)obj).Path));
			}

			return (false);
		}

		public override string ToString()
		{
			return (Path);
		}

		public override int GetHashCode()
		{
			return (0);
		}

		private long GetContentHash()
		{
			try
			{
				if (!File.Exists(Path))
				{
					return (0);
				}

				var info = new FileInfo(Path);
				// Overflow is fine, just wrap
				unchecked
				{
					long hash = 17;
					hash = hash * 23 + info.Length;
					hash = hash * 23 + info.LastWriteTimeUtc.Ticks;
					return (hash);
				}

			} catch
			{
				return (0);
			}
		}
	}
}
