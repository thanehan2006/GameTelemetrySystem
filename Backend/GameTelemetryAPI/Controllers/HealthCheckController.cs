using Microsoft.AspNetCore.Mvc;
using GameTelemetryAPI.Data;

namespace GameTelemetryAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HealthCheckController : ControllerBase
    {
        private readonly GameTelemetryContext _context;

        public HealthCheckController(GameTelemetryContext context)
        {
            _context = context;
        }

        [HttpGet("db")]
        public async Task<IActionResult> CheckDatabaseConnection()
        {
            try
            {
                bool canConnect = await _context.Database.CanConnectAsync();
                if (canConnect)
                {
                    return Ok(new { status = "Success", message = "Connected to SQL Server successfully!" });
                }
                return StatusCode(500, new { status = "Failed", message = "Cannot connect to SQL Server." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = "Error", error = ex.Message });
            }
        }
    }
}
