using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using GameTelemetryAPI.Data;
using GameTelemetryAPI.DTOs;
using GameTelemetryAPI.Models;
using GameTelemetryAPI.Services;

namespace GameTelemetryAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Controller tiếp nhận sự kiện từ Unity, chống ghi trùng bằng ClientEventId/semantic key và điều phối vào 3 bảng đích
    public class EventsController : ControllerBase
    {
        private static readonly HashSet<string> CoreMilestoneEvents = new(StringComparer.OrdinalIgnoreCase)
        {
            "login",
            "game_start",
            "level_start",
            "level_complete",
            "level_failed",
            "player_dead",
            "boss_killed",
            "game_exit"
        };

        private static readonly HashSet<string> SinglePerSessionMilestones = new(StringComparer.OrdinalIgnoreCase)
        {
            "login",
            "game_start",
            "game_exit",
            "boss_killed",
            "player_dead"
        };

        private static readonly HashSet<string> LevelScopedEvents = new(StringComparer.OrdinalIgnoreCase)
        {
            "level_start",
            "level_complete",
            "level_failed",
            "player_dead",
            "boss_killed"
        };

        private readonly GameTelemetryContext _context;
        private readonly EventDeduplicationService _dedup;

        public EventsController(GameTelemetryContext context, EventDeduplicationService dedup)
        {
            _context = context;
            _dedup = dedup;
        }

        // Ghi nhận một sự kiện đơn lẻ (idempotent theo ClientEventId & cột mốc trong phiên)
        [HttpPost]
        public async Task<ActionResult<GameEventResponseDto>> RecordEvent([FromBody] CreateGameEventDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var session = await _context.GameSessions.FirstOrDefaultAsync(s => s.SessionId == dto.SessionId);
            if (session == null)
            {
                return NotFound(new { message = $"Không tìm thấy phiên chơi với SessionId {dto.SessionId}" });
            }

            string normalized = dto.EventType.Trim().ToLowerInvariant();
            DateTime eventTime = NormalizeUtc(dto.EventTime);
            string? clientEventId = NormalizeClientEventId(dto.ClientEventId);

            if (clientEventId != null && !_dedup.TryReserve(clientEventId))
            {
                return Ok(new GameEventResponseDto
                {
                    EventId = 0,
                    SessionId = session.SessionId,
                    EventType = normalized,
                    EventTime = eventTime,
                    Value = dto.Value,
                    Duplicate = true
                });
            }

            try
            {
                if (clientEventId != null)
                {
                    bool existsInEvents = await _context.GameEvents
                        .AsNoTracking()
                        .AnyAsync(e => e.ClientEventId == clientEventId);
                    bool existsInPurchases = !existsInEvents && await _context.ShopPurchases
                        .AsNoTracking()
                        .AnyAsync(p => p.ClientEventId == clientEventId);

                    if (existsInEvents || existsInPurchases)
                    {
                        return Ok(new GameEventResponseDto
                        {
                            EventId = 0,
                            SessionId = session.SessionId,
                            EventType = normalized,
                            EventTime = eventTime,
                            Value = dto.Value,
                            Duplicate = true
                        });
                    }
                }

                int? resolvedValue = ResolveEventValue(session, normalized, dto.Value, out bool invalidLevelValue);
                if (invalidLevelValue)
                {
                    return Ok(new GameEventResponseDto
                    {
                        EventId = 0,
                        SessionId = session.SessionId,
                        EventType = normalized,
                        EventTime = eventTime,
                        Value = dto.Value,
                        Duplicate = false
                    });
                }

                if (CoreMilestoneEvents.Contains(normalized))
                {
                    bool semanticDuplicate = SinglePerSessionMilestones.Contains(normalized)
                        ? await _context.GameEvents.AsNoTracking()
                            .AnyAsync(e => e.SessionId == session.SessionId && e.EventType == normalized)
                        : await _context.GameEvents.AsNoTracking()
                            .AnyAsync(e => e.SessionId == session.SessionId && e.EventType == normalized && e.Value == resolvedValue);

                    if (semanticDuplicate)
                    {
                        return Ok(new GameEventResponseDto
                        {
                            EventId = 0,
                            SessionId = session.SessionId,
                            EventType = normalized,
                            EventTime = eventTime,
                            Value = resolvedValue,
                            Duplicate = true
                        });
                    }
                }

                var createdEvent = ApplyEventToSession(session, normalized, eventTime, resolvedValue, clientEventId);
                await _context.SaveChangesAsync();

                var responseDto = new GameEventResponseDto
                {
                    EventId = createdEvent?.EventId ?? 0,
                    SessionId = session.SessionId,
                    EventType = normalized,
                    EventTime = eventTime,
                    Value = resolvedValue,
                    Duplicate = false
                };

                if (createdEvent != null)
                {
                    return CreatedAtAction(nameof(GetEventById), new { id = createdEvent.EventId }, responseDto);
                }

                return Ok(responseDto);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                _context.ChangeTracker.Clear();
                return Ok(new GameEventResponseDto
                {
                    EventId = 0,
                    SessionId = dto.SessionId,
                    EventType = normalized,
                    EventTime = eventTime,
                    Value = dto.Value,
                    Duplicate = true
                });
            }
            catch
            {
                _dedup.Release(clientEventId);
                throw;
            }
        }

        // Ghi nhận đồng loạt nhiều sự kiện từ Unity batch với khả năng chống trùng theo lô
        [HttpPost("batch")]
        public async Task<IActionResult> RecordEventsBatch([FromBody] CreateBatchGameEventsDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var sessionExists = await _context.GameSessions.AsNoTracking().AnyAsync(s => s.SessionId == dto.SessionId);
            if (!sessionExists)
            {
                return NotFound(new { message = $"Không tìm thấy phiên chơi với SessionId {dto.SessionId}" });
            }

            var reservedIds = new List<string>(dto.Events.Count);
            try
            {
                var (processedCount, skippedDuplicates) = await ProcessBatchInternalAsync(dto, reservedIds, checkInMemoryDedup: true);
                return Ok(new
                {
                    message = $"Đã ghi nhận thành công {processedCount} sự kiện",
                    count = processedCount,
                    skippedDuplicates
                });
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                _context.ChangeTracker.Clear();
                var (processedCount, skippedDuplicates) = await ProcessBatchInternalAsync(dto, null, checkInMemoryDedup: false);
                return Ok(new
                {
                    message = $"Đã ghi nhận thành công {processedCount} sự kiện",
                    count = processedCount,
                    skippedDuplicates
                });
            }
            catch
            {
                foreach (var id in reservedIds)
                {
                    _dedup.Release(id);
                }
                throw;
            }
        }

        private async Task<(int ProcessedCount, int SkippedDuplicates)> ProcessBatchInternalAsync(
            CreateBatchGameEventsDto dto,
            List<string>? reservedIds,
            bool checkInMemoryDedup)
        {
            var session = await _context.GameSessions.FirstAsync(s => s.SessionId == dto.SessionId);

            var batchClientIds = dto.Events
                .Select(e => NormalizeClientEventId(e.ClientEventId))
                .Where(id => id != null)
                .Select(id => id!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var existingClientIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (batchClientIds.Count > 0)
            {
                var dbEventIds = await _context.GameEvents
                    .AsNoTracking()
                    .Where(e => e.ClientEventId != null && batchClientIds.Contains(e.ClientEventId))
                    .Select(e => e.ClientEventId!)
                    .ToListAsync();
                foreach (var id in dbEventIds) existingClientIds.Add(id);

                var dbPurchaseIds = await _context.ShopPurchases
                    .AsNoTracking()
                    .Where(p => p.ClientEventId != null && batchClientIds.Contains(p.ClientEventId))
                    .Select(p => p.ClientEventId!)
                    .ToListAsync();
                foreach (var id in dbPurchaseIds) existingClientIds.Add(id);
            }

            var existingMilestones = await _context.GameEvents
                .AsNoTracking()
                .Where(e => e.SessionId == dto.SessionId)
                .Select(e => new { e.EventType, e.Value })
                .ToListAsync();

            var singleMilestonesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var levelMilestonesSeen = new HashSet<(string EventType, int? Value)>();
            foreach (var m in existingMilestones)
            {
                string typeKey = m.EventType.Trim().ToLowerInvariant();
                if (SinglePerSessionMilestones.Contains(typeKey))
                {
                    singleMilestonesSeen.Add(typeKey);
                }
                else if (CoreMilestoneEvents.Contains(typeKey))
                {
                    levelMilestonesSeen.Add((typeKey, m.Value));
                }
            }

            int processedCount = 0;
            int skippedDuplicates = 0;

            foreach (var item in dto.Events)
            {
                if (string.IsNullOrWhiteSpace(item.EventType)) continue;

                string normalized = item.EventType.Trim().ToLowerInvariant();
                string? clientEventId = NormalizeClientEventId(item.ClientEventId);

                if (clientEventId != null)
                {
                    if (existingClientIds.Contains(clientEventId))
                    {
                        skippedDuplicates++;
                        continue;
                    }

                    if (checkInMemoryDedup)
                    {
                        if (!_dedup.TryReserve(clientEventId))
                        {
                            skippedDuplicates++;
                            continue;
                        }
                        reservedIds?.Add(clientEventId);
                    }

                    existingClientIds.Add(clientEventId);
                }

                int? resolvedValue = ResolveEventValue(session, normalized, item.Value, out bool invalidLevelValue);
                if (invalidLevelValue)
                {
                    skippedDuplicates++;
                    continue;
                }

                if (CoreMilestoneEvents.Contains(normalized))
                {
                    if (SinglePerSessionMilestones.Contains(normalized))
                    {
                        if (!singleMilestonesSeen.Add(normalized))
                        {
                            skippedDuplicates++;
                            continue;
                        }
                    }
                    else
                    {
                        if (!levelMilestonesSeen.Add((normalized, resolvedValue)))
                        {
                            skippedDuplicates++;
                            continue;
                        }
                    }
                }

                ApplyEventToSession(session, normalized, NormalizeUtc(item.EventTime), resolvedValue, clientEventId);
                processedCount++;
            }

            if (processedCount > 0)
            {
                await _context.SaveChangesAsync();
            }

            return (processedCount, skippedDuplicates);
        }

        // Lấy thông tin chi tiết của một sự kiện cột mốc theo EventId
        [HttpGet("{id:long}")]
        public async Task<ActionResult<GameEventResponseDto>> GetEventById(long id)
        {
            var gameEvent = await _context.GameEvents.FindAsync(id);
            if (gameEvent == null)
            {
                return NotFound(new { message = $"Không tìm thấy sự kiện với EventId {id}" });
            }

            return Ok(new GameEventResponseDto
            {
                EventId = gameEvent.EventId,
                SessionId = gameEvent.SessionId,
                EventType = gameEvent.EventType,
                EventTime = gameEvent.EventTime,
                Value = gameEvent.Value,
                Duplicate = false
            });
        }

        private static int? ResolveEventValue(GameSession session, string normalized, int? rawValue, out bool invalidLevelValue)
        {
            invalidLevelValue = false;
            if (!LevelScopedEvents.Contains(normalized))
            {
                return rawValue;
            }

            if (rawValue is >= 1 and <= 3)
            {
                return rawValue.Value;
            }

            if (session.LevelId is >= 1 and <= 3)
            {
                return session.LevelId.Value;
            }

            invalidLevelValue = true;
            return null;
        }

        // Điều phối sự kiện: cột mốc vào GameEvents, mua đồ vào ShopPurchases, chỉ số chiến đấu/vàng cộng vào GameSessions
        private GameEvent? ApplyEventToSession(GameSession session, string normalized, DateTime eventTime, int? value, string? clientEventId)
        {
            int safeValue = Math.Max(0, value ?? 0);

            if (CoreMilestoneEvents.Contains(normalized))
            {
                if ((normalized == "level_start" || session.LevelId == null)
                    && (normalized == "level_start" || normalized == "level_complete" || normalized == "level_failed" || normalized == "player_dead" || normalized == "boss_killed")
                    && value is >= 1 and <= 3)
                {
                    session.LevelId = value.Value;
                }

                if (normalized == "boss_killed" || normalized == "level_complete")
                {
                    session.BossKilled = true;
                }

                var gameEvent = new GameEvent
                {
                    SessionId = session.SessionId,
                    EventType = normalized,
                    EventTime = eventTime,
                    Value = value,
                    ClientEventId = clientEventId
                };
                _context.GameEvents.Add(gameEvent);
                return gameEvent;
            }

            switch (normalized)
            {
                case "enemy_killed":
                    session.EnemiesKilled += 1;
                    break;

                case "gold_collected":
                    session.GoldEarned += safeValue;
                    break;

                case "block_hit":
                    session.BlockHits += 1;
                    session.BlockedDamage += safeValue;
                    break;

                case "perfect_parry":
                    session.PerfectParries += 1;
                    break;

                case "trap_hit":
                    session.TrapHits += 1;
                    session.TrapDamage += safeValue;
                    break;

                case "weapon_purchased":
                {
                    var (weaponName, rarity) = WeaponCatalog.ResolveByPrice(safeValue);
                    session.GoldSpent += safeValue;
                    _context.ShopPurchases.Add(new ShopPurchase
                    {
                        SessionId = session.SessionId,
                        ItemType = "Weapon",
                        ItemName = weaponName,
                        Rarity = rarity,
                        Price = safeValue,
                        PurchasedAt = eventTime,
                        ClientEventId = clientEventId
                    });
                    break;
                }

                case "potion_purchased":
                    session.GoldSpent += safeValue;
                    _context.ShopPurchases.Add(new ShopPurchase
                    {
                        SessionId = session.SessionId,
                        ItemType = "Potion",
                        ItemName = "Bình Hồi Máu Huyết Ngọc",
                        Rarity = "Tiêu Hao",
                        Price = safeValue,
                        PurchasedAt = eventTime,
                        ClientEventId = clientEventId
                    });
                    break;
            }

            return null;
        }

        private static string? NormalizeClientEventId(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string trimmed = raw.Trim();
            return trimmed.Length > 64 ? trimmed[..64] : trimmed;
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        {
            return ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
        }

        private static DateTime NormalizeUtc(DateTime? value)
        {
            if (!value.HasValue) return DateTime.UtcNow;
            var dt = value.Value;
            return dt.Kind switch
            {
                DateTimeKind.Utc => dt,
                DateTimeKind.Local => dt.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            };
        }
    }
}
