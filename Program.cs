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

var clients = new List<Client>();

app.MapGet("/see", async (context) =>
{
  await context.Session.LoadAsync();
  string id = context.Session.Id;
  await context.SSEInitAsync();
  app.Logger.LogInformation($"User {id} opened event stream");
  await context.SSESendEventAsync(new SSEEvent("init") { Id = id, Retry = 10 });
  clients.Add(new Client(id, context));
  while (true)
  {
    await Task.Delay(10000);
    if (context.RequestAborted.IsCancellationRequested == true)
    {
      app.Logger.LogInformation($"User {id} closed event stream");
      break;
    }
    await context.SSESendEventAsync(new SSEEvent("waiting-commits") { Id = id, Retry = 10 });
  }
});

// curl -v -H "EventStreamId: eesId" -F commits=@commits.csv http://localhost:5058/commits
app.MapPost("/commits", async (context) =>
{
  string id = context.Request.Headers["EventStreamId"];
  if (string.IsNullOrEmpty(id)) { await Results.BadRequest().ExecuteAsync(context); return; }
  var client = clients.Find((client) => client.Id == id);
  if (client == null) { await Results.Unauthorized().ExecuteAsync(context); return; }

  var commitsFile = context.Request.Form.Files["commits"];
  if (commitsFile == null) { await Results.BadRequest().ExecuteAsync(context); return; }

  await client.Context.SSESendEventAsync(
    new SSEEvent("commits-received") { Id = id, Retry = 10 }
  );

  // Copy received file to the client and reset stream position for reading
  client.CommitsFile = new MemoryStream();
  await commitsFile.CopyToAsync(client.CommitsFile);
  client.CommitsFile.Position = 0;

  await client.Context.SSESendEventAsync(
    new SSEEvent("commits-ready") { Id = id, Retry = 10 }
  );
});

app.MapGet("/get-commits/{id}", async (HttpContext context, string id) => {
  var client = clients.Find((client) => client.Id == id);
  if (client == null) { await Results.Unauthorized().ExecuteAsync(context); return; }
  if (client.CommitsFile == null) { await Results.NotFound().ExecuteAsync(context); return; }

  // If user commits was already fetched by use
  //  it throw a ObjectDisposedException
  try {
    await Results.File(client.CommitsFile, "application/octet-stream").ExecuteAsync(context);
  } catch (System.ObjectDisposedException) {
    await Results.NotFound().ExecuteAsync(context); return; 
  }
});

app.UseCors(MyAllowSpecificOrigins);

app.UseSession();

app.Run();
