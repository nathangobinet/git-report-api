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
    if (context.RequestAborted.IsCancellationRequested == true) {
      app.Logger.LogInformation($"User {id} closed event stream");
      break;
    }
    await context.SSESendEventAsync(new SSEEvent("sup") { Id = id, Retry = 10 });
  }
});

app.UseCors(MyAllowSpecificOrigins);

app.UseSession();

app.Run();
