using Microsoft.EntityFrameworkCore;
using GameTelemetryAPI.Data;

// Khởi tạo ứng dụng web API và nạp cấu hình hệ thống
var builder = WebApplication.CreateBuilder(args);

// Đăng ký Controller, API Explorer và tài liệu Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Kết nối DbContext với cơ sở dữ liệu SQL Server
builder.Services.AddDbContext<GameTelemetryContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);

var app = builder.Build();

// Kiểm tra và bổ sung cột PasswordHash trong bảng Players khi khởi động nếu chưa có
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

// Bật giao diện Swagger trong môi trường phát triển
if (app.Environment.IsDevelopment())
{

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "GameTelemetryAPI v1");
        c.RoutePrefix = "swagger";
    });
}

// Phục vụ các file giao diện tĩnh từ thư mục wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

// Chuyển hướng sang HTTPS khi chạy ở môi trường triển khai
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

// Ánh xạ route đến các API Controller
app.MapControllers();

// Chạy máy chủ web API
app.Run();
