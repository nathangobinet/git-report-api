var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
  options.IdleTimeout = TimeSpan.FromSeconds(10);
  options.Cookie.HttpOnly = true;
  options.Cookie.IsEssential = true;
});

builder.Services.AddCors(options =>
{
  options.AddPolicy(name: MyAllowSpecificOrigins, builder =>
  {
    builder.AllowAnyOrigin();
  });
});


var app = builder.Build();

app.MapGet("/status", async (context) =>
{
  await context.Response.WriteAsJsonAsync(new
  {
    status = "OK",
    date = DateTime.Now,
    sessionId = context.Session.Id,
  });
});

// curl -v -H "EventStreamId: eesId" -F commits=@commits.csv http://localhost:5058/commits
app.MapPost("/commits", async (context) =>
{
  string id = context.Request.Headers["EventStreamId"];
  var commitsFile = context.Request.Form.Files["commits"];
  if (string.IsNullOrEmpty(id)) throw new InvalidDataException("No header for EventStreamId");
  if (commitsFile == null) throw new InvalidDataException("No commits");

  using var memoryStream = new MemoryStream();
  await commitsFile.CopyToAsync(memoryStream);

  var commits = System.Text.Encoding.UTF8.GetString(memoryStream.ToArray());

  await context.Response.WriteAsJsonAsync(new
  {
    id = id,
    commits = commits,
  });
});

app.MapGet("/see", async (context) =>
{
  await context.Session.LoadAsync();
  string id = context.Session.Id;
  await context.SSEInitAsync();
  app.Logger.LogInformation($"User {id} opened event stream");
  await context.SSESendEventAsync(new SSEEvent("init") { Id = id, Retry = 10 });
  while (true)
  {
    await Task.Delay(10000);
    if (context.RequestAborted.IsCancellationRequested == true)
    {
      app.Logger.LogInformation($"User {id} closed event stream");
      break;
    }
    await context.SSESendEventAsync(new SSEEvent("sup") { Id = id, Retry = 10 });
  }
});

app.UseCors(MyAllowSpecificOrigins);

app.UseSession();

app.Run();
