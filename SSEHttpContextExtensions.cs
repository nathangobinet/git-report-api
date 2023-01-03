using System.Text.Json;

public static class SSEHttpContextExtensions
{
  public static async Task SSEInitAsync(this HttpContext ctx)
  {
    ctx.Response.Headers.Add("Cache-Control", "no-cache");
    ctx.Response.Headers.Add("Content-Type", "text/event-stream");
    await ctx.Response.Body.FlushAsync();
  }

  public static async Task SSESendEventAsync(this HttpContext ctx, SSEEvent e)
  {
    if (String.IsNullOrWhiteSpace(e.Id) is false)
      await ctx.Response.WriteAsync("id: " + e.Id + "\n");

    if (e.Retry is not null)
      await ctx.Response.WriteAsync("retry: " + e.Retry + "\n");

    await ctx.Response.WriteAsync("event: " + e.Name + "\n");

    var lines = e.Data switch
    {
      null => new[] { String.Empty },
      string s => s.Split('\n').ToArray(),
      _ => new[] { JsonSerializer.Serialize(e.Data) }
    };

    foreach (var line in lines)
      await ctx.Response.WriteAsync("data: " + line + "\n");

    await ctx.Response.WriteAsync("\n");
    await ctx.Response.Body.FlushAsync();
  }
}