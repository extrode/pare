var builder = WebApplication.CreateSlimBuilder(args);
var app = builder.Build();

// "/" to "/index.html" before routing picks an endpoint. MapStaticAssets serves only what the
// build registered as static web assets, so the .ts sources stay private in development too.
app.UseDefaultFiles();
app.UseRouting();
app.MapStaticAssets();

// htmx swaps this fragment into the page.
app.MapGet("/fragments/time", () =>
    Results.Content($"<time>{DateTime.UtcNow:HH:mm:ss}</time> UTC", "text/html"));

app.Run();
