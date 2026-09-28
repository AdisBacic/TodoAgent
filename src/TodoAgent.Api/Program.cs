// Program.cs is the composition root: it wires dependencies, maps endpoints and
// starts the host. It contains no behaviour of its own. Every feature lives in
// a slice under Features/, and every slice is registered here by name so this
// file doubles as the table of contents for the whole API.

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
   .WithName("Health");

app.Run();

// Exposes the implicit Program class so the test project can host the app
// in-memory with WebApplicationFactory<Program>.
public partial class Program;
