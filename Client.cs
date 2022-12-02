class Client {
  public string Id;
  public HttpContext Context;
  public MemoryStream? CommitsFile { get; set; }

  public Client(string id, HttpContext context)
  {
    this.Id = id;
    this.Context = context;
  }
}