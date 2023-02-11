using System.Diagnostics;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                                           {
                                               Args = args,
                                               ContentRootPath = WindowsServiceHelpers.IsWindowsService()
                                                                     ? AppContext.BaseDirectory
                                                                     : default,
                                           });

builder.Host.UseWindowsService();
builder.WebHost.UseUrls("http://0.0.0.0:7245");
builder.WebHost.UseSentry(o =>
                          {
                              o.Dsn              = "https://c294d4bb0b444dfcb7aa8b9c9697facb@sentry.limepage.com.hk/23";
                              o.Debug            = Debugger.IsAttached;
                              o.TracesSampleRate = 1.0;
                          });

builder.Services.AddGrpc();
builder.Services.AddGrpcReflection();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<NoraxonService.Services.NoraxonService>();
app.MapGrpcReflectionService();

app.Run();