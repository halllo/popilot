using System.Text.Json;

namespace popilot
{
	public class Cached
	{
		public static async Task<T> Do<T>(string cacheFile, Func<Task<T>> @do) where T : class
		{
			var cached = default(T);
			if (File.Exists(cacheFile))
			{
				cached = JsonSerializer.Deserialize<T>(File.ReadAllText(cacheFile));
			}
			else
			{
				cached = await @do();
				File.WriteAllText(cacheFile, JsonSerializer.Serialize(cached));
			}

			return cached!;
		}
	}
}
