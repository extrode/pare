var builder = WebApplication.CreateSlimBuilder(args);
// CreateBuilder does this in Development; CreateSlimBuilder does not. Without it, an unpublished
// run answers Accept-Encoding requests with the build's compressed copies it cannot find: 200 and
// an empty body, so browsers show a blank page. A no-op in published output.
builder.WebHost.UseStaticWebAssets();
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
