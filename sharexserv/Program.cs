using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Runtime.Caching;
using Force.Crc32;

namespace sharexserv
{
	class Program
	{
		private const string listener_prefix = "http://+:80/upload/";
		private const string key = "";
		private const string path = "./files/";
		private const string address = "http://localhost/";
		private const string fail_address = "http://localhost/failed.jpg";
		private const int store_duration = 14; // days
		private static readonly string[] file_ignore_list = new string[] // never delete these
		{
			"index.html",
			"style.css",
			"failed.jpg"
		};

		private static CacheEntryRemovedCallback cachedFileRemove = new CacheEntryRemovedCallback(RemoveCachedFile);
		private static MemoryCache cache = MemoryCache.Default;

		static void Main(string[] args)
		{
			Cleanup(); // perform cleanup as soon as we start

			HttpListener listener = new HttpListener();

#if DEBUG
			listener.Prefixes.Add("http://+:80/Temporary_Listen_Addresses/"); // windows' free to use prefix
#else
			listener.Prefixes.Add(listener_prefix);
#endif

			listener.Start();
			Console.WriteLine("Listening...");

			while (true)
			{
#if !DEBUG
				try
#endif
				{
					// GetContext is blocking
					HttpListenerContext context = listener.GetContext();
					if (context.Request.HasEntityBody)
					{
						Response(context.Request, context.Response);
					}
				}
#if !DEBUG
				catch (Exception e) { Console.WriteLine(e.Message); }
#endif

			}

			//listener.Stop();
		}

		private static void Response(HttpListenerRequest request, HttpListenerResponse response)
		{
			bool success = false;
			string fileName = string.Empty;
			if (request.Headers.Get("key") == key) // password check
			{
				string fileType = string.Empty;
				using (MemoryStream fullReq = new MemoryStream())
				{
					request.InputStream.CopyTo(fullReq);
					fullReq.Position = 0;

					bool shouldSave = false;

					using (StreamReader reader = new StreamReader(fullReq))
					{
						// iterate through first 4 lines to find the file extension
						for (int i = 0; i < 3; i++)
						{
							// Content-Type
							if (i == 2)
							{
								string contentType = reader.ReadLine().Split(':')[1].TrimStart(' ');
								if (contentType == "image/png" || contentType == "image/jpeg")
								{
									// only save if it's a picture
									shouldSave = true;
									if (contentType == "image/png")
									{
										fileType = ".png";
									}
									else if (contentType == "image/jpeg")
									{
										fileType = ".jpg";
									}
								}
							}
							else
							{
								reader.ReadLine();
							}
						}

						// save data
						if (shouldSave)
						{
							fullReq.Position = 0;
							using (MemoryStream data = GetFile(request.ContentEncoding, GetBoundary(request.ContentType), fullReq))
							{
								data.Position = 0;
								byte[] buf = new byte[data.Length];
								data.Read(buf, 0, (int)data.Length);

								// use CRC32 as an unique file name
								fileName = Crc32Algorithm.Compute(buf).ToString("X") + fileType;

								if (Directory.Exists(path))
								{
									File.WriteAllBytes(path + fileName, buf);
									Console.WriteLine($"Wrote {fileName}");

									// cache file name to remove it after store_duration days
									CacheItemPolicy policy = new CacheItemPolicy()
									{
										AbsoluteExpiration = DateTimeOffset.Now.AddDays(store_duration),
										RemovedCallback = cachedFileRemove
									};

									cache.Add(fileName, fileName, policy);
									success = true;
								}
							}
						}
					}
				}
			}
			// write file address in the response
			string responsestring = fail_address;
			if (success)
				responsestring = address + fileName;

			byte[] buffer = Encoding.UTF8.GetBytes(responsestring);
			response.ContentLength64 = buffer.Length;

			Stream output = response.OutputStream;
			output.Write(buffer, 0, buffer.Length);
			output.Close();
		}

		#region File Removal
		private static void RemoveCachedFile(CacheEntryRemovedArguments arguments)
		{
			// iterate through all file in the directory and delete cached file
			string fileName = arguments.CacheItem.Key;
			var files = Directory.EnumerateFiles(path);
			foreach (string dirFile in files)
			{
				if (Path.GetFileName(dirFile) == fileName)
				{
					Console.WriteLine($"Removed {fileName}");
					File.Delete(path + fileName);
					return;
				}
			}
		}

		private static void Cleanup()
		{
			// delete all files in the directory that are older than store_duration
			foreach (string filePath in Directory.EnumerateFiles(path))
			{
				if (!file_ignore_list.Contains(Path.GetFileName(filePath)) && File.GetLastWriteTimeUtc(filePath).AddDays(store_duration) < DateTime.UtcNow)
				{
					Console.WriteLine($"Removed {filePath}");
					File.Delete(filePath);
				}
			}
		}
		#endregion

		#region Data Detection

		// Based on https://stackoverflow.com/questions/8466703/httplistener-and-file-upload

		private static string GetBoundary(string ctype)
		{
			return "--" + ctype.Split(';')[1].Split('=')[1];
		}

		private static MemoryStream GetFile(Encoding enc, string boundary, Stream input)
		{
			byte[] boundaryBytes = enc.GetBytes(boundary);
			int boundaryLen = boundaryBytes.Length;
			MemoryStream output = new MemoryStream();

			byte[] buffer = new byte[1024];
			int len = input.Read(buffer, 0, 1024);
			int startPos = -1;

			// Find start boundary
			while (true)
			{
				if (len != 0)
				{
					startPos = IndexOf(buffer, len, boundaryBytes);
					if (startPos >= 0)
					{
						break;
					}
					else
					{
						Array.Copy(buffer, len - boundaryLen, buffer, 0, boundaryLen);
						len = input.Read(buffer, boundaryLen, 1024 - boundaryLen);
					}
				}
			}

			// Skip four lines (Boundary, Content-Disposition, Content-Type, and a blank)
			for (int i = 0; i < 4; i++)
			{
				while (true)
				{
					if (len != 0)
					{
						startPos = Array.IndexOf(buffer, enc.GetBytes("\n")[0], startPos);
						if (startPos >= 0)
						{
							startPos++;
							break;
						}
						else
						{
							len = input.Read(buffer, 0, 1024);
						}
					}
				}
			}

			Array.Copy(buffer, startPos, buffer, 0, len - startPos);
			len = len - startPos;

			while (true)
			{
				int endPos = IndexOf(buffer, len, boundaryBytes);
				if (endPos >= 0)
				{
					if (endPos > 0) output.Write(buffer, 0, endPos - 2);
					break;
				}
				else if (len <= boundaryLen)
				{
					throw new Exception("End Boundary Not Found");
				}
				else
				{
					output.Write(buffer, 0, len - boundaryLen);
					Array.Copy(buffer, len - boundaryLen, buffer, 0, boundaryLen);
					len = input.Read(buffer, boundaryLen, 1024 - boundaryLen) + boundaryLen;
				}
			}

			return output;
		}

		private static int IndexOf(byte[] buffer, int len, byte[] boundarybytes)
		{
			for (int i = 0; i <= len - boundarybytes.Length; i++)
			{
				bool match = true;
				for (int j = 0; j < boundarybytes.Length && match; j++)
				{
					match = buffer[i + j] == boundarybytes[j];
				}

				if (match)
				{
					return i;
				}
			}

			return -1;
		}
		#endregion
	}
}
