using Microsoft.EntityFrameworkCore;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

var builder = WebApplication.CreateBuilder(args);

var workshopOsConnectionString = builder.Configuration.GetConnectionString("WorkshopOS");
if (string.IsNullOrWhiteSpace(workshopOsConnectionString))
{
    throw new InvalidOperationException(
        "Connection string 'WorkshopOS' is not configured. Set ConnectionStrings:WorkshopOS via User Secrets for local development.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(workshopOsConnectionString));

builder.Services.AddScoped<IOrganizationContext, UnresolvedOrganizationContext>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
