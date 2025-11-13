using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IDV_Backend.Contracts.Audit;
using IDV_Backend.Models;
using IDV_Backend.Models.Audit;
using IDV_Backend.Repositories.Audit;

namespace UserTest.Repositories.Audit
{
    /// <summary>
    /// Test-only extension overloads that
    /// - supply CancellationToken.None
    /// - convert DateTimeOffset? -> DateTime? where needed
    /// </summary>
    internal static class AuditRepositoryTestExtensions
    {
        // ---------- Template audit (TemplateAuditLog) ----------

        public static Task<int> CountTemplateAuditLogsAsync(
            this AuditRepository repo,
            DateTimeOffset? fromDate,
            DateTimeOffset? toDate)
            => repo.CountTemplateAuditLogsAsync(fromDate, toDate, CancellationToken.None);

        // Forwarders that just add CancellationToken.None (no "Raw" helpers required)
        public static async Task<IEnumerable<ActionCountDto>> GetTopActionCountsAsync(
            this AuditRepository repo,
            DateTimeOffset? fromDate,
            DateTimeOffset? toDate,
            int top)
        {
            var result = await repo.GetTopActionCountsAsync(fromDate, toDate, top, CancellationToken.None);
            return result.Select(x => new ActionCountDto { Action = x.Action, Count = x.Count });
        }

        public static async Task<IEnumerable<UserActivityDto>> GetTopActiveUsersAsync(
            this AuditRepository repo,
            DateTimeOffset? fromDate,
            DateTimeOffset? toDate,
            int top)
        {
            var result = await repo.GetTopActiveUsersAsync(fromDate, toDate, top, CancellationToken.None);
            return result.Select(x => new UserActivityDto
            {
                UserId = x.UserId,
                UserDisplayName = x.UserDisplayName ?? string.Empty,
                ActionCount = x.Count
            });
        }

        public static async Task<IEnumerable<TemplateActivityDto>> GetTopModifiedTemplatesAsync(
            this AuditRepository repo,
            DateTimeOffset? fromDate,
            DateTimeOffset? toDate,
            int top)
        {
            var result = await repo.GetTopModifiedTemplatesAsync(fromDate, toDate, top, CancellationToken.None);
            return result.Select(x => new TemplateActivityDto
            {
                TemplateId = x.TemplateId,
                ActionCount = x.Count
            });
        }

        // Legacy tests sometimes call with named arg "actionContains". Keep mapping overloads:
        public static Task<(IReadOnlyList<TemplateAuditLog> rows, int total)> GetTemplateAuditLogsAsync(
            this AuditRepository repo,
            long templateId,
            int page,
            int pageSize,
            DateTimeOffset? fromDate,
            DateTimeOffset? toDate,
            string? actionContains,
            long? userId)
            => repo.GetTemplateAuditLogsAsync(templateId, page, pageSize, fromDate, toDate, action: actionContains, userId, CancellationToken.None);

        // Same signature but using "action" (so tests that already migrated still compile without ct)
        public static Task<(IReadOnlyList<TemplateAuditLog> rows, int total)> GetUserTemplateAuditLogsAsync(
            this AuditRepository repo,
            long userId,
            int page,
            int pageSize,
            DateTimeOffset? fromDate,
            DateTimeOffset? toDate,
            string? action,
            long? templateId)
            => repo.GetUserTemplateAuditLogsAsync(userId, page, pageSize, fromDate, toDate, action, templateId, CancellationToken.None);

        public static Task<(IReadOnlyList<TemplateAuditLog> rows, int total)> SearchTemplateAuditLogsAsync(
            this AuditRepository repo,
            int page,
            int pageSize,
            DateTimeOffset? fromDate,
            DateTimeOffset? toDate,
            string? actionContains,
            long? userId,
            long? templateId)
            => repo.SearchTemplateAuditLogsAsync(page, pageSize, fromDate, toDate, action: actionContains, userId, templateId, CancellationToken.None);

        // ---------- Generic audit (AuditLog) expects DateTime? ----------

        public static Task<(IReadOnlyList<AuditLog> logs, int totalCount)> SearchAuditLogsAsync(
            this AuditRepository repo,
            string? entityName = null,
            long? entityId = null,
            long? userId = null,
            string? action = null,
            DateTimeOffset? fromDate = null,
            DateTimeOffset? toDate = null,
            int page = 1,
            int pageSize = 50)
            => repo.SearchAuditLogsAsync(
                entityName,
                entityId,
                userId,
                action,
                fromDate?.UtcDateTime,
                toDate?.UtcDateTime,
                page,
                pageSize,
                CancellationToken.None);

        public static Task<(IReadOnlyList<AuditLog> logs, int totalCount)> GetEntityAuditTrailAsync(
            this AuditRepository repo,
            string entityName,
            long entityId,
            int page = 1,
            int pageSize = 50)
            => repo.GetEntityAuditTrailAsync(entityName, entityId, page, pageSize, CancellationToken.None);

        public static Task<(IReadOnlyList<AuditLog> logs, int totalCount)> GetUserAuditTrailAsync(
            this AuditRepository repo,
            long userId,
            int page = 1,
            int pageSize = 50,
            DateTimeOffset? fromDate = null,
            DateTimeOffset? toDate = null)
            => repo.GetUserAuditTrailAsync(
                userId,
                page,
                pageSize,
                fromDate?.UtcDateTime,
                toDate?.UtcDateTime,
                CancellationToken.None);
    }
}
