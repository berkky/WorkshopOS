using WorkshopOS.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWorkshopOsWebServices(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseWorkshopOsWebPipeline();

app.Run();
