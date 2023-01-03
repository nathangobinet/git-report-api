public class SSEEvent
{
  public string Name { get; set; }
  public object? Data { get; set; }
  public string? Id { get; set; }
  public int? Retry { get; set; }

  public SSEEvent(string name) { Name = name; }
  public SSEEvent(string name, object data)
  {
    Name = name;
    Data = data;
  }
}