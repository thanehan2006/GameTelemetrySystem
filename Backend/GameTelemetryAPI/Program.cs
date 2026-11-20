using Microsoft.EntityFrameworkCore;
using GameTelemetryAPI.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Kết nối Database SQL Server
builder.Services.AddDbContext<GameTelemetryContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);

var app = builder.Build();

// Tự động đảm bảo cột PasswordHash tồn tại trong bảng Players khi khởi động Backend
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<GameTelemetryContext>();
        db.Database.ExecuteSqlRaw(@"
            IF OBJECT_ID(N'dbo.Players', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Players', N'PasswordHash') IS NULL
            BEGIN
                ALTER TABLE dbo.Players
                ADD PasswordHash NVARCHAR(256) NOT NULL CONSTRAINT DF_Players_PasswordHash DEFAULT '';
            END");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Không thể tự động kiểm tra/cập nhật cột PasswordHash khi khởi động.");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // Bật Swagger UI truyền thống
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "GameTelemetryAPI v1");
        c.RoutePrefix = "swagger";
    });
}

// Cho phép phục vụ file tĩnh và file mặc định từ wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
