using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
  options.IdleTimeout = TimeSpan.FromSeconds(10);
  options.Cookie.HttpOnly = true;
  options.Cookie.IsEssential = true;
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.AddAzureWebAppDiagnostics();

var app = builder.Build();

string script = System.IO.File.ReadAllText("get-commits.sh");
string scriptStatic = System.IO.File.ReadAllText("get-local-commits.sh");
var clients = new Dictionary<string, Client>();

Func<Task<UInt32>> intializeGeneratedReports = async () =>
{
  UInt32 generatedReports = 0;
  if (!File.Exists("generated-reports-number.txt")) return generatedReports;
  var content = await File.ReadAllTextAsync("generated-reports-number.txt");
  if (content == null) return generatedReports;
  UInt32.TryParse(content, out generatedReports);
  return generatedReports;
};

UInt32 generatedReports = await intializeGeneratedReports();
app.Logger.LogInformation($"Intialize local state with {generatedReports} generated reports");

System.AppDomain.CurrentDomain.ProcessExit += (object? sender, EventArgs e) =>
{
  File.WriteAllText("generated-reports-number.txt", generatedReports.ToString());
  app.Logger.LogInformation($"Stored {generatedReports} generated reports before exit");
};

var apiGroup = app.MapGroup("/api");

apiGroup.MapGet("/status", async (context) =>
{
  await context.Response.WriteAsJsonAsync(new
  {
    status = "OK",
    date = DateTime.Now,
    sessionId = context.Session.Id,
    clients = clients.Count,
    memory = System.GC.GetTotalMemory(true) / 1000,
    generatedReports = generatedReports,
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
  
  // Azure reverse proxy doesnt correctly handle request abortion
  // So use a 5 minute timeout as a backup solution
  var timeoutCancellationTokenSource = new CancellationTokenSource();
  var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5), timeoutCancellationTokenSource.Token);

  context.RequestAborted.Register(() =>
  {
    app.Logger.LogInformation($"User {id} closed event stream");
    timeoutCancellationTokenSource.Cancel();
    clearClient(id);
  });

  // Keep connection open and send periodic message while client doesnt cancel request
  while (!context.RequestAborted.IsCancellationRequested && !timeoutTask.IsCompleted)
  {
    await context.SSESendEventAsync(new SSEEvent("waiting-commits") { Id = id, Retry = 10 });
    // ContinueWith allow to avoid error throwing
    await Task.Delay(10_000, context.RequestAborted).ContinueWith(task => { });
  }

  if (timeoutTask.IsCompleted)
  {
    app.Logger.LogInformation($"User {id} timed out after 5 minutes.");
    clearClient(id);
  }
});

apiGroup.MapGet("/script/{id}", async (HttpContext context, string id) =>
{
  var userScript = script.Replace("{{ID}}", id);
  await context.Response.WriteAsync(userScript);
});

apiGroup.MapGet("/script/static", async (HttpContext context) =>
{
  await context.Response.WriteAsync(scriptStatic);
});

apiGroup.MapPost("/commits", async (context) =>
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
  generatedReports += 1;

  await client.Context.SSESendEventAsync(
    new SSEEvent("commits-ready") { Id = id, Retry = 10 }
  );
});

apiGroup.MapGet("/get-commits/{id}", async (HttpContext context, string id) =>
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
