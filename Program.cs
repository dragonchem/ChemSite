using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

Log.Logger = new LoggerConfiguration()
    .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

var app = builder.Build();

app.UseSerilogRequestLogging();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "gallery",
    pattern: "Gallery/{*path}",
    defaults: new { controller = "Gallery", action = "Index" }
);

app.MapControllerRoute(
    name: "nsfwgallery",
    pattern: "NsfwGallery/{*path}",
    defaults: new { controller = "NsfwGallery", action = "Index" }
);

app.MapControllerRoute(
    name: "oembed",
    pattern: "oembed/{*path}",
    defaults: new { controller = "OEmbed", action = "Index" }
);

app.MapControllerRoute(
    name: "default",
    pattern: "{*path}",
    defaults: new { controller = "StaticPage", action = "Index" }
);

app.Run();
