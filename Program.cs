using Microsoft.Extensions.Hosting.WindowsServices;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService()
    ? AppContext.BaseDirectory
    : default,
});

builder.Host.UseWindowsService();

builder.Services.AddGrpc();
builder.Services.AddGrpcReflection();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<NoraxonService.Services.NoraxonService>();
app.MapGrpcReflectionService();

app.Run();