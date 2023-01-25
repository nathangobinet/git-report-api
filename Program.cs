using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
  options.IdleTimeout = TimeSpan.FromSeconds(10);
  options.Cookie.HttpOnly = true;
  options.Cookie.IsEssential = true;
});

builder.Host.ConfigureLogging(logging =>
{
  logging.ClearProviders();
  if (builder.Environment.IsProduction())
  {
    logging.AddDebug();
  }
  else
  {
    logging.AddConsole();
  }
});

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

app.MapGet("/status", async (context) =>
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

  context.RequestAborted.Register(() =>
  {
    app.Logger.LogInformation($"User {id} closed event stream");
    clearClient(id);
  });

  // Keep connection open and send periodic message while client doesnt cancel request
  while (!context.RequestAborted.IsCancellationRequested)
  {
    await context.SSESendEventAsync(new SSEEvent("waiting-commits") { Id = id, Retry = 10 });
    // ContinueWith allow to avoid error throwing
    await Task.Delay(10_000, context.RequestAborted).ContinueWith(task => { });
  }
});

app.MapGet("/script/{id}", async (HttpContext context, string id) =>
{
  var userScript = script.Replace("{{ID}}", id);
  await context.Response.WriteAsync(userScript);
});

app.MapGet("/script/static", async (HttpContext context) =>
{
  await context.Response.WriteAsync(scriptStatic);
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
  generatedReports += 1;

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
