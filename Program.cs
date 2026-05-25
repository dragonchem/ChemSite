using ImageMagick;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();

Log.Logger = new LoggerConfiguration()
    .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();
builder.Host.UseSerilog();

var app = builder.Build();

app.UseSerilogRequestLogging();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

OpenCL.IsEnabled = true;
OpenCL.SetCacheDirectory(".");

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// Search routes
app.MapControllerRoute("gallery-search", "gallery/search",
    new { controller = "Search", action = "GallerySearch" });
app.MapControllerRoute("nsfwgallery-search", "nsfwgallery/search",
    new { controller = "Search", action = "NsfwGallerySearch" });

// Admin routes (gallery prefix)
app.MapControllerRoute("gallery-admin-upload-get", "gallery/admin/upload",
    new { controller = "Admin", action = "Upload" });
app.MapControllerRoute("gallery-admin-artists-create", "gallery/admin/artists/create",
    new { controller = "Admin", action = "ArtistCreate" });
app.MapControllerRoute("gallery-admin-artists-edit", "gallery/admin/artists/{artistName}",
    new { controller = "Admin", action = "ArtistEdit" });
app.MapControllerRoute("gallery-admin-artists-delete", "gallery/admin/artists/{artistName}/delete",
    new { controller = "Admin", action = "ArtistDelete" });
app.MapControllerRoute("gallery-admin-artists", "gallery/admin/artists",
    new { controller = "Admin", action = "Artists" });
app.MapControllerRoute("gallery-admin-tags-section-create", "gallery/admin/tags/section/create",
    new { controller = "Admin", action = "TagSectionCreate" });
app.MapControllerRoute("gallery-admin-tags-section-delete", "gallery/admin/tags/section/{sectionName}/delete",
    new { controller = "Admin", action = "TagSectionDelete" });
app.MapControllerRoute("gallery-admin-tags-add", "gallery/admin/tags/section/{sectionName}/add-tag",
    new { controller = "Admin", action = "TagAdd" });
app.MapControllerRoute("gallery-admin-tags-remove", "gallery/admin/tags/section/{sectionName}/remove-tag",
    new { controller = "Admin", action = "TagRemove" });
app.MapControllerRoute("gallery-admin-tags", "gallery/admin/tags",
    new { controller = "Admin", action = "Tags" });
app.MapControllerRoute("gallery-admin-create-folder", "gallery/admin/create-folder",
    new { controller = "Admin", action = "CreateFolder" });
app.MapControllerRoute("gallery-admin-rename-folder", "gallery/admin/rename-folder",
    new { controller = "Admin", action = "RenameFolder" });
app.MapControllerRoute("gallery-admin-delete-folder", "gallery/admin/delete-folder",
    new { controller = "Admin", action = "DeleteFolder" });
app.MapControllerRoute("gallery-admin-edit", "gallery/admin/edit/{*path}",
    new { controller = "Admin", action = "Edit" });
app.MapControllerRoute("gallery-admin-delete", "gallery/admin/delete/{*path}",
    new { controller = "Admin", action = "Delete" });
app.MapControllerRoute("gallery-admin-artists-list", "gallery/admin/artists-list",
    new { controller = "Admin", action = "ArtistsList" });
app.MapControllerRoute("gallery-admin-rebuild-thumbs",          "gallery/admin/rebuild-thumbnails",          new { controller = "Admin", action = "RebuildThumbnails" });
app.MapControllerRoute("gallery-admin-rebuild-thumbs-progress", "gallery/admin/rebuild-thumbnails/progress", new { controller = "Admin", action = "RebuildProgress" });
app.MapControllerRoute("gallery-admin-rebuild-thumbs-status",   "gallery/admin/rebuild-thumbnails/status",   new { controller = "Admin", action = "RebuildStatus" });
app.MapControllerRoute("gallery-admin-themes",              "gallery/admin/themes",               new { controller = "Admin", action = "Themes" });
app.MapControllerRoute("gallery-admin-theme-set",           "gallery/admin/themes/set",           new { controller = "Admin", action = "ThemeSet" });
app.MapControllerRoute("gallery-admin-theme-delete",        "gallery/admin/themes/delete",        new { controller = "Admin", action = "ThemeDelete" });
app.MapControllerRoute("gallery-admin-theme-folder",        "gallery/admin/themes/folder",        new { controller = "Admin", action = "ThemeEditFolder" });
app.MapControllerRoute("gallery-admin-browse",              "gallery/admin/browse",               new { controller = "Admin", action = "BrowseFolder" });
app.MapControllerRoute("gallery-admin-theme-suggest",       "gallery/admin/themes/suggest-colors", new { controller = "Admin", action = "SuggestThemeColors" });
app.MapControllerRoute("nsfwgallery-admin-themes",          "nsfwgallery/admin/themes",           new { controller = "Admin", action = "Themes" });
app.MapControllerRoute("nsfwgallery-admin-theme-set",       "nsfwgallery/admin/themes/set",       new { controller = "Admin", action = "ThemeSet" });
app.MapControllerRoute("nsfwgallery-admin-theme-delete",    "nsfwgallery/admin/themes/delete",    new { controller = "Admin", action = "ThemeDelete" });
app.MapControllerRoute("nsfwgallery-admin-theme-folder",    "nsfwgallery/admin/themes/folder",    new { controller = "Admin", action = "ThemeEditFolder" });
app.MapControllerRoute("nsfwgallery-admin-browse",          "nsfwgallery/admin/browse",           new { controller = "Admin", action = "BrowseFolder" });
app.MapControllerRoute("nsfwgallery-admin-theme-suggest",   "nsfwgallery/admin/themes/suggest-colors", new { controller = "Admin", action = "SuggestThemeColors" });

// Admin routes (nsfwgallery prefix — same controller, prefix detected from URL)
app.MapControllerRoute("nsfwgallery-admin-upload-get", "nsfwgallery/admin/upload",
    new { controller = "Admin", action = "Upload" });
app.MapControllerRoute("nsfwgallery-admin-artists-create", "nsfwgallery/admin/artists/create",
    new { controller = "Admin", action = "ArtistCreate" });
app.MapControllerRoute("nsfwgallery-admin-artists-edit", "nsfwgallery/admin/artists/{artistName}",
    new { controller = "Admin", action = "ArtistEdit" });
app.MapControllerRoute("nsfwgallery-admin-artists-delete", "nsfwgallery/admin/artists/{artistName}/delete",
    new { controller = "Admin", action = "ArtistDelete" });
app.MapControllerRoute("nsfwgallery-admin-artists", "nsfwgallery/admin/artists",
    new { controller = "Admin", action = "Artists" });
app.MapControllerRoute("nsfwgallery-admin-tags-section-create", "nsfwgallery/admin/tags/section/create",
    new { controller = "Admin", action = "TagSectionCreate" });
app.MapControllerRoute("nsfwgallery-admin-tags-section-delete", "nsfwgallery/admin/tags/section/{sectionName}/delete",
    new { controller = "Admin", action = "TagSectionDelete" });
app.MapControllerRoute("nsfwgallery-admin-tags-add", "nsfwgallery/admin/tags/section/{sectionName}/add-tag",
    new { controller = "Admin", action = "TagAdd" });
app.MapControllerRoute("nsfwgallery-admin-tags-remove", "nsfwgallery/admin/tags/section/{sectionName}/remove-tag",
    new { controller = "Admin", action = "TagRemove" });
app.MapControllerRoute("nsfwgallery-admin-tags", "nsfwgallery/admin/tags",
    new { controller = "Admin", action = "Tags" });
app.MapControllerRoute("nsfwgallery-admin-create-folder", "nsfwgallery/admin/create-folder",
    new { controller = "Admin", action = "CreateFolder" });
app.MapControllerRoute("nsfwgallery-admin-rename-folder", "nsfwgallery/admin/rename-folder",
    new { controller = "Admin", action = "RenameFolder" });
app.MapControllerRoute("nsfwgallery-admin-delete-folder", "nsfwgallery/admin/delete-folder",
    new { controller = "Admin", action = "DeleteFolder" });
app.MapControllerRoute("nsfwgallery-admin-edit", "nsfwgallery/admin/edit/{*path}",
    new { controller = "Admin", action = "Edit" });
app.MapControllerRoute("nsfwgallery-admin-delete", "nsfwgallery/admin/delete/{*path}",
    new { controller = "Admin", action = "Delete" });
app.MapControllerRoute("nsfwgallery-admin-artists-list", "nsfwgallery/admin/artists-list",
    new { controller = "Admin", action = "ArtistsList" });
app.MapControllerRoute("nsfwgallery-admin-rebuild-thumbs",          "nsfwgallery/admin/rebuild-thumbnails",          new { controller = "Admin", action = "RebuildThumbnails" });
app.MapControllerRoute("nsfwgallery-admin-rebuild-thumbs-progress", "nsfwgallery/admin/rebuild-thumbnails/progress", new { controller = "Admin", action = "RebuildProgress" });
app.MapControllerRoute("nsfwgallery-admin-rebuild-thumbs-status",   "nsfwgallery/admin/rebuild-thumbnails/status",   new { controller = "Admin", action = "RebuildStatus" });

// Gallery catch-alls (must be after admin/search routes)
app.MapControllerRoute("gallery", "Gallery/{*path}",
    new { controller = "Gallery", action = "Index" });
app.MapControllerRoute("nsfwgallery", "NsfwGallery/{*path}",
    new { controller = "NsfwGallery", action = "Index" });
app.MapControllerRoute("oembed", "oembed/{*path}",
    new { controller = "OEmbed", action = "Index" });
app.MapControllerRoute("default", "{*path}",
    new { controller = "StaticPage", action = "Index" });

app.Run();
