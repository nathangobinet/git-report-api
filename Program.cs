using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
  options.IdleTimeout = TimeSpan.FromSeconds(10);
  options.Cookie.HttpOnly = true;
  options.Cookie.IsEssential = true;
});

var app = builder.Build();

string script = System.IO.File.ReadAllText("get-commits.sh");
var clients = new Dictionary<string, Client>();

app.MapGet("/status", async (context) =>
{
  await context.Response.WriteAsJsonAsync(new
  {
    status = "OK",
    date = DateTime.Now,
    sessionId = context.Session.Id,
    clients = clients.Count,
    memory = System.GC.GetTotalMemory(true) / 1000,
  });
});

var clearClient = (string id) =>
{
  var client = clients[id];
  if (client == null)
  {
    app.Logger.LogWarning($"No client found for {id}");
    return;
  }
  if (client.CommitsFile != null)
  {
    client.CommitsFile.Dispose();
    app.Logger.LogInformation($"Deleted commits for {id}");
  }
  else
  {
    app.Logger.LogInformation($"No commits to delete for {id}");
  }
  clients.Remove(id);
};

app.MapGet("/see", async (context) =>
{
  await context.Session.LoadAsync();
  string id = context.Session.Id;
  await context.SSEInitAsync();
  app.Logger.LogInformation($"User {id} opened event stream");
  await context.SSESendEventAsync(new SSEEvent("init") { Id = id, Retry = 10 });
  clients.Add(id, new Client(context));
  while (true)
  {
    await Task.Delay(10000);
    if (context.RequestAborted.IsCancellationRequested == true)
    {
      app.Logger.LogInformation($"User {id} closed event stream");
      clearClient(id);
      break;
    }
    await context.SSESendEventAsync(new SSEEvent("waiting-commits") { Id = id, Retry = 10 });
  }
});


app.MapGet("/script/{id}", async (HttpContext context, string id) =>
{
  var userScript = script.Replace("{{ID}}", id);
  await context.Response.WriteAsync(userScript);
});


app.MapPost("/commits", async (context) =>
{
  string id = context.Request.Headers["EventStreamId"];

  if (string.IsNullOrEmpty(id)) { await Results.BadRequest().ExecuteAsync(context); return; }
  var client = clients[id];
  if (client == null) { await Results.Unauthorized().ExecuteAsync(context); return; }

  var commitsFile = context.Request.Form.Files[0];
  if (commitsFile == null) { await Results.BadRequest().ExecuteAsync(context); return; }

  await client.Context.SSESendEventAsync(
    new SSEEvent("commits-received") { Id = id, Retry = 10 }
  );

  // Copy received file to the client and reset stream position for reading
  client.CommitsFile = new MemoryStream();
  await commitsFile.CopyToAsync(client.CommitsFile);
  client.CommitsFile.Position = 0;

  app.Logger.LogInformation($"Commits received from {id}");

  await client.Context.SSESendEventAsync(
    new SSEEvent("commits-ready") { Id = id, Retry = 10 }
  );
});

app.MapGet("/get-commits/{id}", async (HttpContext context, string id) =>
{
  var client = clients[id];
  if (client == null) { await Results.Unauthorized().ExecuteAsync(context); return; }
  if (client.CommitsFile == null) { await Results.NotFound().ExecuteAsync(context); return; }

  await Results.File(client.CommitsFile, "application/octet-stream").ExecuteAsync(context);
});

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
  ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseSession();

app.Run();
