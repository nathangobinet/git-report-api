class Client {
  public HttpContext Context;
  public MemoryStream? CommitsFile { get; set; }

  public Client(HttpContext context)
  {
    this.Context = context;
  }
}