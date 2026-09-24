using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ZokaAnalytics.Configuration;
using ZokaAnalytics.Data;
using ZokaAnalytics.Models;
using ZokaAnalytics.Services.FotMob;

namespace ZokaAnalytics.Services.Sync;

public class SyncRunner : ISyncRunner
{
    private readonly ApplicationDbContext _db;
    private readonly IFootballDataClient _api;
    private readonly SyncSettings _settings;
    private readonly ILogger<SyncRunner> _logger;

    public SyncRunner(
        ApplicationDbContext db,
        IFootballDataClient api,
        IOptions<SyncSettings> settings,
        ILogger<SyncRunner> logger)
    {
        _db = db;
        _api = api;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SyncSummary> RunFullSyncAsync(CancellationToken ct = default)
    {
        var startedAt = DateTime.UtcNow;
        var results = new List<SyncStepResult>();

        var leagues = await _db.Leagues
            .Where(l => l.IsTracked)
            .OrderBy(l => l.DisplayOrder)
            .ToListAsync(ct);

        if (leagues.Count == 0)
            _logger.LogWarning("Takip edilen lig yok.");

        results.Add(await RunStepAsync("1-Ligler", () => SyncLeagueDetailsAsync(leagues, ct), ct));
        results.Add(await RunStepAsync("2-GecmisFikstur", () => SyncLeagueMatchesAsync(leagues, ct), ct));
        results.Add(await RunStepAsync("3-BuSezonGecmis", () => BackfillCurrentSeasonAsync(leagues, ct), ct));
        results.Add(await RunStepAsync("4-YaklasanMaclar", () => SyncUpcomingByDateAsync(leagues, ct), ct));
        results.Add(await RunStepAsync("5-CanliSkorlar", () => RefreshLiveMatchesAsync(ct), ct));
        results.Add(await RunStepAsync("6-Istatistikler", () => RebuildStatisticsAsync(ct), ct));
        results.Add(await RunStepAsync("7-H2H", () => RebuildH2HAsync(ct), ct));

        foreach (var league in leagues)
            league.LastSyncedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return new SyncSummary(startedAt, DateTime.UtcNow, _api.CallsUsed, results);
    }

    private async Task<SyncStepResult> RunStepAsync(string step, Func<Task<int>> action, CancellationToken ct)
    {
        var log = new SyncLog { Step = step, Status = SyncStatus.Running, StartedAtUtc = DateTime.UtcNow };
        _db.SyncLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        try
        {
            var count = await action();

            log.Status = SyncStatus.Success;
            log.ItemsProcessed = count;
            log.ApiCallsUsed = _api.CallsUsed;
            log.CompletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Sync adimi tamam: {Step} ({Count} kayit)", step, count);
            return new SyncStepResult(step, true, count, null);
        }
        catch (Exception ex)
        {
            log.Status = SyncStatus.Failed;
            log.Message = ex.Message;
            log.CompletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            _logger.LogError(ex, "Sync adimi basarisiz: {Step}", step);
            return new SyncStepResult(step, false, 0, ex.Message);
        }
    }

    private async Task<int> SyncLeagueDetailsAsync(List<League> leagues, CancellationToken ct)
    {
        var updated = 0;

        foreach (var league in leagues)
        {
            var detail = await _api.GetLeagueDetailAsync(league.Id, ct);
            if (detail is null) continue;

            league.Name = string.IsNullOrWhiteSpace(detail.Name) ? league.Name : detail.Name;
            league.Country = detail.Country ?? league.Country;
            league.LogoUrl = FotMobImages.LeagueLogo(league.Id);

            if (detail.SeasonYear.HasValue)
                league.CurrentSeason = detail.SeasonYear.Value;

            updated++;
        }

        await _db.SaveChangesAsync(ct);
        return updated;
    }

    private async Task<int> SyncLeagueMatchesAsync(List<League> leagues, CancellationToken ct)
    {
        var processed = 0;

        foreach (var league in leagues)
        {
            var items = await _api.GetLeagueMatchesAsync(league.Id, ct);
            if (items.Count == 0) continue;

            await EnsureTeamsExistAsync(items, ct);

            var seasons = new Dictionary<int, int> { [league.Id] = league.CurrentSeason };
            processed += await UpsertMatchesAsync(items, league.Id, seasons, ct);
        }

        return processed;
    }

    private async Task<int> BackfillCurrentSeasonAsync(List<League> leagues, CancellationToken ct)
    {
        if (leagues.Count == 0) return 0;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var seasonStart = new DateOnly(SeasonResolver.FromKickoff(DateTime.UtcNow), 8, 1);
        var earliest = today.AddDays(-_settings.BackfillDays);
        var from = seasonStart > earliest ? seasonStart : earliest;

        return await ScanDatesAsync(leagues, from, today.DayNumber - from.DayNumber, ct);
    }

    private async Task<int> SyncUpcomingByDateAsync(List<League> leagues, CancellationToken ct)
    {
        if (leagues.Count == 0) return 0;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await ScanDatesAsync(leagues, today, _settings.UpcomingDays, ct);
    }

    private async Task<int> ScanDatesAsync(
        List<League> leagues, DateOnly from, int dayCount, CancellationToken ct)
    {
        var seasons = leagues.ToDictionary(l => l.Id, l => l.CurrentSeason);
        var trackedIds = seasons.Keys.ToHashSet();
        var processed = 0;

        for (var offset = 0; offset <= dayCount; offset++)
        {
            if (_api.BudgetExceeded)
            {
                _logger.LogWarning("Cagri butcesi doldu, {Date} tarihinde duruldu.", from.AddDays(offset));
                break;
            }

            var date = from.AddDays(offset);
            var items = await _api.GetMatchesByDateAsync(date, ct);
            if (items.Count == 0) continue;

            var relevant = items
                .Where(m => m.LeagueId.HasValue
                            && m.LeagueId.Value <= int.MaxValue
                            && trackedIds.Contains((int)m.LeagueId.Value))
                .ToList();

            if (relevant.Count == 0) continue;

            _logger.LogInformation("{Date}: {Relevant}/{Total} mac takip edilen ligde.",
                date, relevant.Count, items.Count);

            await EnsureTeamsExistAsync(relevant, ct);
            processed += await UpsertMatchesAsync(relevant, null, seasons, ct);
        }

        return processed;
    }

    private async Task<int> UpsertMatchesAsync(
        List<FmMatch> items, int? forcedLeagueId, Dictionary<int, int> seasons, CancellationToken ct)
    {
        var valid = items
            .Where(i => i.Id > 0 && i.Id <= int.MaxValue
                        && i.Home.Id is > 0 and <= int.MaxValue
                        && i.Away.Id is > 0 and <= int.MaxValue
                        && i.Status.UtcTime.HasValue)
            .ToList();

        if (valid.Count == 0) return 0;

        var ids = valid.Select(i => (int)i.Id).ToList();
        var existing = await _db.Matches.Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
        var knownLeagueIds = await _db.Leagues.Select(l => l.Id).ToListAsync(ct);

        var upserted = 0;

        foreach (var item in valid)
        {
            var leagueId = forcedLeagueId ?? (int?)item.LeagueId;
            if (leagueId is null || !knownLeagueIds.Contains(leagueId.Value)) continue;

            var matchId = (int)item.Id;
            var alreadySettled = existing.TryGetValue(matchId, out var cached)
                                 && cached.Status == MatchStatus.Finished
                                 && cached.HomeGoals is not null
                                 && item.Status.Finished;

            if (alreadySettled) continue;

            if (cached is null)
            {
                cached = new Match { Id = matchId };
                _db.Matches.Add(cached);
                existing[matchId] = cached;
            }

            cached.LeagueId = leagueId.Value;

            cached.Season = SeasonResolver.FromKickoff(item.Status.UtcTime!.Value.UtcDateTime);

            cached.Round = item.RoundLabel;
            cached.HomeTeamId = (int)item.Home.Id;
            cached.AwayTeamId = (int)item.Away.Id;
            cached.KickoffUtc = item.Status.UtcTime!.Value.UtcDateTime;
            cached.Status = MapStatus(item.Status);
            cached.ElapsedMinutes = ParseElapsed(item.Status.LiveTime?.Short);

            if (item.Status.Started)
            {
                cached.HomeGoals = item.Home.Score;
                cached.AwayGoals = item.Away.Score;
            }

            cached.UpdatedAtUtc = DateTime.UtcNow;
            upserted++;
        }

        await _db.SaveChangesAsync(ct);
        return upserted;
    }

    private async Task EnsureTeamsExistAsync(List<FmMatch> items, CancellationToken ct)
    {
        var refs = items
            .SelectMany(m => new[] { m.Home, m.Away })
            .Where(t => t.Id is > 0 and <= int.MaxValue)
            .GroupBy(t => t.Id)
            .Select(g => g.First())
            .ToList();

        if (refs.Count == 0) return;

        var ids = refs.Select(t => (int)t.Id).ToList();
        var existingIds = await _db.Teams.Where(t => ids.Contains(t.Id)).Select(t => t.Id).ToListAsync(ct);

        var missing = refs.Where(t => !existingIds.Contains((int)t.Id)).ToList();
        if (missing.Count == 0) return;

        foreach (var dto in missing)
        {
            var teamId = (int)dto.Id;
            _db.Teams.Add(new Team
            {
                Id = teamId,
                Name = string.IsNullOrWhiteSpace(dto.LongName) ? dto.Name : dto.LongName!,
                LogoUrl = FotMobImages.TeamLogo(teamId),
                LastSyncedAtUtc = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> RefreshLiveMatchesAsync(CancellationToken ct = default)
    {
        var items = await _api.GetLiveMatchesAsync(ct);
        if (items.Count == 0) return 0;

        var trackedLeagueIds = await _db.Leagues.Where(l => l.IsTracked).Select(l => l.Id).ToListAsync(ct);

        var relevant = items
            .Where(i => i.LeagueId.HasValue && trackedLeagueIds.Contains((int)i.LeagueId.Value))
            .ToList();

        if (relevant.Count == 0) return 0;

        var ids = relevant.Where(i => i.Id <= int.MaxValue).Select(i => (int)i.Id).ToList();
        var matches = await _db.Matches.Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);

        var updated = 0;

        foreach (var item in relevant)
        {
            if (item.Id > int.MaxValue) continue;
            if (!matches.TryGetValue((int)item.Id, out var match)) continue;

            match.Status = MapStatus(item.Status);
            match.ElapsedMinutes = ParseElapsed(item.Status.LiveTime?.Short);
            match.HomeGoals = item.Home.Score;
            match.AwayGoals = item.Away.Score;
            match.UpdatedAtUtc = DateTime.UtcNow;

            updated++;
        }

        await _db.SaveChangesAsync(ct);
        return updated;
    }

    private async Task<int> RebuildStatisticsAsync(CancellationToken ct)
    {
        var finished = await _db.Matches
            .Where(m => m.Status == MatchStatus.Finished && m.HomeGoals != null && m.AwayGoals != null)
            .OrderBy(m => m.KickoffUtc)
            .Select(m => new
            {
                m.LeagueId, m.Season, m.HomeTeamId, m.AwayTeamId,
                HomeGoals = m.HomeGoals!.Value, AwayGoals = m.AwayGoals!.Value
            })
            .AsNoTracking()
            .ToListAsync(ct);

        if (finished.Count == 0) return 0;

        var stats = new Dictionary<(int, int, int), TeamStatistic>();
        var forms = new Dictionary<(int, int, int), List<char>>();

        void Apply(int leagueId, int season, int teamId, int gf, int ga, bool isHome)
        {
            var key = (leagueId, season, teamId);

            if (!stats.TryGetValue(key, out var stat))
            {
                stat = new TeamStatistic { LeagueId = leagueId, Season = season, TeamId = teamId };
                stats[key] = stat;
                forms[key] = new List<char>();
            }

            stat.PlayedTotal++;
            stat.GoalsForTotal += gf;
            stat.GoalsAgainstTotal += ga;

            if (isHome)
            {
                stat.PlayedHome++; stat.GoalsForHome += gf; stat.GoalsAgainstHome += ga;
            }
            else
            {
                stat.PlayedAway++; stat.GoalsForAway += gf; stat.GoalsAgainstAway += ga;
            }

            if (ga == 0) stat.CleanSheets++;
            if (gf == 0) stat.FailedToScore++;

            char result;
            if (gf > ga)
            {
                result = 'W'; stat.WinsTotal++; stat.Points += 3;
                if (isHome) stat.WinsHome++; else stat.WinsAway++;
            }
            else if (gf == ga)
            {
                result = 'D'; stat.DrawsTotal++; stat.Points += 1;
                if (isHome) stat.DrawsHome++; else stat.DrawsAway++;
            }
            else
            {
                result = 'L'; stat.LossesTotal++;
                if (isHome) stat.LossesHome++; else stat.LossesAway++;
            }

            var form = forms[key];
            form.Add(result);
            if (form.Count > 5) form.RemoveAt(0);
        }

        foreach (var m in finished)
        {
            Apply(m.LeagueId, m.Season, m.HomeTeamId, m.HomeGoals, m.AwayGoals, true);
            Apply(m.LeagueId, m.Season, m.AwayTeamId, m.AwayGoals, m.HomeGoals, false);
        }

        foreach (var (key, stat) in stats)
        {
            stat.Form = new string(forms[key].ToArray());
            stat.AvgGoalsFor = stat.PlayedTotal > 0 ? (double)stat.GoalsForTotal / stat.PlayedTotal : 0;
            stat.AvgGoalsAgainst = stat.PlayedTotal > 0 ? (double)stat.GoalsAgainstTotal / stat.PlayedTotal : 0;
            stat.UpdatedAtUtc = DateTime.UtcNow;
        }

        foreach (var group in stats.Values.GroupBy(s => (s.LeagueId, s.Season)))
        {
            var ranked = group
                .OrderByDescending(s => s.Points)
                .ThenByDescending(s => s.GoalsForTotal - s.GoalsAgainstTotal)
                .ThenByDescending(s => s.GoalsForTotal)
                .ToList();

            for (var i = 0; i < ranked.Count; i++)
                ranked[i].Rank = i + 1;
        }

        await _db.TeamStatistics.ExecuteDeleteAsync(ct);
        _db.ChangeTracker.Clear();

        _db.TeamStatistics.AddRange(stats.Values);
        await _db.SaveChangesAsync(ct);

        return stats.Count;
    }

    private async Task<int> RebuildH2HAsync(CancellationToken ct)
    {
        var horizon = DateTime.UtcNow.AddDays(_settings.UpcomingDays);

        var upcomingPairs = await _db.Matches
            .Where(m => m.KickoffUtc >= DateTime.UtcNow && m.KickoffUtc <= horizon)
            .Select(m => new { m.HomeTeamId, m.AwayTeamId })
            .Distinct()
            .ToListAsync(ct);

        if (upcomingPairs.Count == 0) return 0;

        var processed = 0;

        foreach (var pair in upcomingPairs)
        {
            var a = Math.Min(pair.HomeTeamId, pair.AwayTeamId);
            var b = Math.Max(pair.HomeTeamId, pair.AwayTeamId);

            var history = await _db.Matches
                .Where(m => m.Status == MatchStatus.Finished
                            && m.HomeGoals != null && m.AwayGoals != null
                            && ((m.HomeTeamId == a && m.AwayTeamId == b)
                                || (m.HomeTeamId == b && m.AwayTeamId == a)))
                .OrderByDescending(m => m.KickoffUtc)
                .Take(20)
                .AsNoTracking()
                .ToListAsync(ct);

            var record = await _db.H2HRecords.FirstOrDefaultAsync(h => h.TeamAId == a && h.TeamBId == b, ct);
            if (record is null)
            {
                record = new H2HRecord { TeamAId = a, TeamBId = b };
                _db.H2HRecords.Add(record);
            }

            record.TotalMatches = history.Count;
            record.TeamAWins = record.Draws = record.TeamBWins = 0;
            record.TeamAGoals = record.TeamBGoals = 0;

            int over25 = 0, btts = 0, totalGoals = 0;

            foreach (var m in history)
            {
                var aGoals = m.HomeTeamId == a ? m.HomeGoals!.Value : m.AwayGoals!.Value;
                var bGoals = m.HomeTeamId == a ? m.AwayGoals!.Value : m.HomeGoals!.Value;

                record.TeamAGoals += aGoals;
                record.TeamBGoals += bGoals;

                if (aGoals > bGoals) record.TeamAWins++;
                else if (aGoals < bGoals) record.TeamBWins++;
                else record.Draws++;

                var sum = aGoals + bGoals;
                totalGoals += sum;
                if (sum > 2) over25++;
                if (aGoals > 0 && bGoals > 0) btts++;
            }

            record.AvgTotalGoals = history.Count > 0 ? (double)totalGoals / history.Count : 0;
            record.Over25Rate = history.Count > 0 ? (double)over25 / history.Count : 0;
            record.BttsRate = history.Count > 0 ? (double)btts / history.Count : 0;
            record.LastMeetingUtc = history.FirstOrDefault()?.KickoffUtc;
            record.UpdatedAtUtc = DateTime.UtcNow;

            processed++;
        }

        await _db.SaveChangesAsync(ct);
        return processed;
    }

    private static MatchStatus MapStatus(FmStatus status)
    {
        if (status.Cancelled) return MatchStatus.Cancelled;
        if (status.Finished) return MatchStatus.Finished;

        if (status.Ongoing || status.Started)
        {
            var label = status.LiveTime?.Short ?? string.Empty;
            if (label.Contains("HT", StringComparison.OrdinalIgnoreCase))
                return MatchStatus.HalfTime;

            return MatchStatus.Live;
        }

        return MatchStatus.Scheduled;
    }

    private static int? ParseElapsed(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;

        var digits = new string(label.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var minutes) ? minutes : null;
    }
}
