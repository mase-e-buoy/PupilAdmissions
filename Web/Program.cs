using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PupilAdmissions.Application;
using PupilAdmissions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddApplication();

// Program.cs is the sole composition root permitted to reference
// Infrastructure directly (AD-8). No page or controller does.
var configuredConnectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' not found.");

// Resolve the SQLite file path against the site's physical content root,
// not the process's current working directory: under IIS in-process
// hosting (the architecture's stated deployment target) CWD is not the
// site's physical path, so a bare relative "Data Source" could silently
// open/create the database file in the wrong location.
var sqliteConnectionStringBuilder = new SqliteConnectionStringBuilder(configuredConnectionString);
if (!Path.IsPathRooted(sqliteConnectionStringBuilder.DataSource))
{
    sqliteConnectionStringBuilder.DataSource = Path.Combine(builder.Environment.ContentRootPath, sqliteConnectionStringBuilder.DataSource);
}

// A fresh checkout/deploy may not have the database directory yet.
Directory.CreateDirectory(Path.GetDirectoryName(sqliteConnectionStringBuilder.DataSource)!);

builder.Services.AddInfrastructure(sqliteConnectionStringBuilder.ToString());

var app = builder.Build();

// Apply pending EF Core migrations and enable WAL mode (AD-4: single
// always-on instance is the only process that opens this file) before
// serving any request.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

// No authorization middleware/policies yet (Epic 1 is deferred this
// sprint) — Pupil pages ship with no authorization gate for now.

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
