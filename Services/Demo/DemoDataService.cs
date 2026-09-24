using Microsoft.EntityFrameworkCore;
using ZokaAnalytics.Data;
using ZokaAnalytics.Models;

namespace ZokaAnalytics.Services.Demo;

public class DemoDataService : IDemoDataService
{
    private const int TeamIdBase = 700_000;
    private const int MatchIdBase = 800_000;

    private readonly ApplicationDbContext _db;
    private readonly ILogger<DemoDataService> _logger;
    private readonly Random _rng = new(42);

    public DemoDataService(ApplicationDbContext db, ILogger<DemoDataService> logger)
    {
        _db = db;
        _logger = logger;
    }

    private static readonly Dictionary<int, string[]> LeagueTeams = new()
    {
        [203] = new[]
        {
            "Galatasaray", "Fenerbahce", "Besiktas", "Trabzonspor", "Samsunspor",
            "Basaksehir", "Goztepe", "Eyupspor", "Konyaspor", "Kocaelispor",
            "Genclerbirligi", "Alanyaspor", "Kasimpasa", "Rizespor", "Gaziantep FK",
            "Erzurumspor", "Amedspor", "Corum FK"
        },
        [39] = new[]
        {
            "Manchester City", "Arsenal", "Liverpool", "Aston Villa", "Tottenham",
            "Chelsea", "Newcastle", "Manchester United", "West Ham", "Brighton",
            "Wolves", "Fulham", "Bournemouth", "Crystal Palace", "Brentford",
            "Everton", "Nottingham Forest", "Burnley"
        },
        [140] = new[]
        {
            "Real Madrid", "Barcelona", "Girona", "Atletico Madrid", "Athletic Club",
            "Real Sociedad", "Real Betis", "Valencia", "Villarreal", "Getafe",
            "Alaves", "Osasuna", "Sevilla", "Las Palmas", "Rayo Vallecano",
            "Mallorca", "Celta Vigo", "Cadiz"
        },
        [135] = new[]
        {
            "Inter", "Milan", "Juventus", "Atalanta", "Bologna",
            "Roma", "Lazio", "Fiorentina", "Napoli", "Torino",
            "Monza", "Genoa", "Lecce", "Udinese", "Cagliari",
            "Verona", "Empoli", "Frosinone"
        },
        [78] = new[]
        {
            "Bayer Leverkusen", "Bayern Munich", "Stuttgart", "RB Leipzig", "Borussia Dortmund",
            "Eintracht Frankfurt", "Hoffenheim", "Freiburg", "Augsburg", "Werder Bremen",
            "Wolfsburg", "Union Berlin", "Borussia M.Gladbach", "Mainz", "Bochum",
            "Heidenheim", "Koln", "Darmstadt"
        },
        [61] = new[]
        {
            "Paris Saint-Germain", "Monaco", "Brest", "Lille", "Nice",
            "Lens", "Marseille", "Rennes", "Lyon", "Reims",
            "Toulouse", "Montpellier", "Strasbourg", "Nantes", "Le Havre",
            "Metz", "Lorient", "Clermont"
        }
    };

    public async Task<DemoSeedResult> GenerateAsync(bool reset = false, CancellationToken ct = default)
    {
        if (reset)
        {
            await _db.Predictions.ExecuteDeleteAsync(ct);
            await _db.CouponItems.ExecuteDeleteAsync(ct);
            await _db.H2HRecords.ExecuteDeleteAsync(ct);
            await _db.TeamStatistics.ExecuteDeleteAsync(ct);
            await _db.Matches.ExecuteDeleteAsync(ct);
            await _db.Teams.Where(t => t.Id >= TeamIdBase).ExecuteDeleteAsync(ct);
            _logger.LogInformation("Demo verisi sifirlandi.");
        }

        if (await _db.Matches.AnyAsync(m => m.Id >= MatchIdBase, ct))
            return new DemoSeedResult(0, 0, 0, "Demo verisi zaten var. Sifirlamak icin ?reset=true kullan.");

        var leagues = await _db.Leagues.Where(l => LeagueTeams.Keys.Contains(l.Id)).ToListAsync(ct);
        if (leagues.Count == 0)
            return new DemoSeedResult(0, 0, 0, "Lig bulunamadi. Once uygulamayi bir kez calistir (seeder ligleri ekler).");

        var currentSeason = leagues.First().CurrentSeason;
        var strengths = new Dictionary<int, double>();
        var teamCount = 0;
        var matchId = MatchIdBase;
        var allMatches = new List<Match>();

        foreach (var league in leagues)
        {
            var names = LeagueTeams[league.Id];

            for (var i = 0; i < names.Length; i++)
            {
                var teamId = TeamIdBase + league.Id * 100 + i;

                var rating = 2.05 - (i / (double)(names.Length - 1)) * 1.30;
                strengths[teamId] = rating;

                _db.Teams.Add(new Team
                {
                    Id = teamId,
                    Name = names[i],
                    Code = names[i].Length >= 3 ? names[i][..3].ToUpper() : names[i].ToUpper(),
                    Country = league.Country,
                    VenueName = names[i] + " Stadium",
                    VenueCity = league.Country,
                    LastSyncedAtUtc = DateTime.UtcNow
                });

                teamCount++;
            }
        }

        await _db.SaveChangesAsync(ct);

        foreach (var league in leagues)
        {
            var names = LeagueTeams[league.Id];
            var teamIds = Enumerable.Range(0, names.Length)
                .Select(i => TeamIdBase + league.Id * 100 + i)
                .ToList();

            for (var seasonOffset = 1; seasonOffset >= 0; seasonOffset--)
            {
                var season = currentSeason - seasonOffset;
                var seasonStart = new DateTime(season, 8, 10, 18, 0, 0, DateTimeKind.Utc);
                var fixtures = BuildDoubleRoundRobin(teamIds);

                for (var round = 0; round < fixtures.Count; round++)
                {
                    var roundDate = seasonStart.AddDays(round * 7);

                    foreach (var (homeId, awayId) in fixtures[round])
                    {
                        var kickoff = roundDate
                            .AddDays(_rng.Next(0, 3))
                            .AddHours(_rng.Next(-4, 4));

                        var match = new Match
                        {
                            Id = matchId++,
                            LeagueId = league.Id,
                            Season = season,
                            Round = $"Hafta {round + 1}",
                            HomeTeamId = homeId,
                            AwayTeamId = awayId,
                            KickoffUtc = kickoff,
                            VenueName = names[teamIds.IndexOf(homeId)] + " Stadium",
                            UpdatedAtUtc = DateTime.UtcNow
                        };

                        if (kickoff < DateTime.UtcNow.AddHours(-2))
                        {
                            SimulateResult(match, strengths[homeId], strengths[awayId]);
                        }
                        else if (kickoff < DateTime.UtcNow.AddDays(21))
                        {
                            match.Status = MatchStatus.Scheduled;
                            AssignOdds(match, strengths[homeId], strengths[awayId]);
                        }
                        else
                        {
                            continue;
                        }

                        allMatches.Add(match);
                    }
                }
            }
        }

        const int batchSize = 500;
        for (var i = 0; i < allMatches.Count; i += batchSize)
        {
            _db.Matches.AddRange(allMatches.Skip(i).Take(batchSize));
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
        }

        var statCount = await RebuildStatisticsAsync(ct);

        _logger.LogInformation("Demo verisi olusturuldu: {Teams} takim, {Matches} mac.", teamCount, allMatches.Count);

        return new DemoSeedResult(
            teamCount,
            allMatches.Count,
            statCount,
            "Demo verisi hazir. Simdi /dev/train ve /dev/predict calistir.");
    }

    private static List<List<(int Home, int Away)>> BuildDoubleRoundRobin(List<int> teams)
    {
        var list = new List<int>(teams);
        if (list.Count % 2 != 0) list.Add(-1);

        var rounds = new List<List<(int, int)>>();
        var count = list.Count;

        for (var round = 0; round < count - 1; round++)
        {
            var pairs = new List<(int, int)>();

            for (var i = 0; i < count / 2; i++)
            {
                var home = list[i];
                var away = list[count - 1 - i];
                if (home == -1 || away == -1) continue;

                pairs.Add(round % 2 == 0 ? (home, away) : (away, home));
            }

            rounds.Add(pairs);

            var last = list[^1];
            list.RemoveAt(list.Count - 1);
            list.Insert(1, last);
        }

        var secondHalf = rounds.Select(r => r.Select(p => (p.Item2, p.Item1)).ToList()).ToList();
        rounds.AddRange(secondHalf);

        return rounds;
    }

    private void SimulateResult(Match match, double homeRating, double awayRating)
    {
        var xgHome = Math.Clamp(homeRating * 0.95 + 0.35, 0.3, 4.0);
        var xgAway = Math.Clamp(awayRating * 0.78, 0.25, 3.5);

        var homeGoals = SamplePoisson(xgHome);
        var awayGoals = SamplePoisson(xgAway);

        match.HomeGoals = homeGoals;
        match.AwayGoals = awayGoals;
        match.HomeHalftimeGoals = homeGoals > 0 ? _rng.Next(0, homeGoals + 1) : 0;
        match.AwayHalftimeGoals = awayGoals > 0 ? _rng.Next(0, awayGoals + 1) : 0;
        match.Status = MatchStatus.Finished;

        AssignOdds(match, homeRating, awayRating);
    }

    private void AssignOdds(Match match, double homeRating, double awayRating)
    {
        var diff = homeRating - awayRating + 0.35;
        var homeProb = 1.0 / (1.0 + Math.Exp(-diff * 1.5));
        var drawProb = 0.30 - Math.Abs(diff) * 0.06;
        drawProb = Math.Clamp(drawProb, 0.10, 0.32);

        var remaining = 1.0 - drawProb;
        var h = homeProb * remaining;
        var a = remaining - h;

        const double margin = 1.06;
        match.OddsHome = Math.Round((decimal)(1.0 / Math.Max(h, 0.03) / margin), 2);
        match.OddsDraw = Math.Round((decimal)(1.0 / drawProb / margin), 2);
        match.OddsAway = Math.Round((decimal)(1.0 / Math.Max(a, 0.03) / margin), 2);
    }

    private int SamplePoisson(double lambda)
    {
        var l = Math.Exp(-lambda);
        var k = 0;
        var p = 1.0;

        do
        {
            k++;
            p *= _rng.NextDouble();
        } while (p > l && k < 12);

        return k - 1;
    }

    private async Task<int> RebuildStatisticsAsync(CancellationToken ct)
    {
        _db.ChangeTracker.Clear();

        var finished = await _db.Matches
            .Where(m => m.Status == MatchStatus.Finished && m.HomeGoals != null && m.AwayGoals != null)
            .Select(m => new
            {
                m.LeagueId, m.Season, m.HomeTeamId, m.AwayTeamId,
                HomeGoals = m.HomeGoals!.Value, AwayGoals = m.AwayGoals!.Value, m.KickoffUtc
            })
            .AsNoTracking()
            .ToListAsync(ct);

        var stats = new Dictionary<(int League, int Season, int Team), TeamStatistic>();
        var formTracker = new Dictionary<(int, int, int), List<char>>();

        foreach (var m in finished.OrderBy(m => m.KickoffUtc))
        {
            Apply(m.LeagueId, m.Season, m.HomeTeamId, m.HomeGoals, m.AwayGoals, true);
            Apply(m.LeagueId, m.Season, m.AwayTeamId, m.AwayGoals, m.HomeGoals, false);
        }

        void Apply(int leagueId, int season, int teamId, int gf, int ga, bool isHome)
        {
            var key = (leagueId, season, teamId);

            if (!stats.TryGetValue(key, out var stat))
            {
                stat = new TeamStatistic { LeagueId = leagueId, Season = season, TeamId = teamId };
                stats[key] = stat;
                formTracker[key] = new List<char>();
            }

            stat.PlayedTotal++;
            stat.GoalsForTotal += gf;
            stat.GoalsAgainstTotal += ga;

            if (isHome)
            {
                stat.PlayedHome++;
                stat.GoalsForHome += gf;
                stat.GoalsAgainstHome += ga;
            }
            else
            {
                stat.PlayedAway++;
                stat.GoalsForAway += gf;
                stat.GoalsAgainstAway += ga;
            }

            if (ga == 0) stat.CleanSheets++;
            if (gf == 0) stat.FailedToScore++;

            char result;
            if (gf > ga)
            {
                result = 'W';
                stat.WinsTotal++;
                stat.Points += 3;
                if (isHome) stat.WinsHome++; else stat.WinsAway++;
            }
            else if (gf == ga)
            {
                result = 'D';
                stat.DrawsTotal++;
                stat.Points += 1;
                if (isHome) stat.DrawsHome++; else stat.DrawsAway++;
            }
            else
            {
                result = 'L';
                stat.LossesTotal++;
                if (isHome) stat.LossesHome++; else stat.LossesAway++;
            }

            var form = formTracker[key];
            form.Add(result);
            if (form.Count > 5) form.RemoveAt(0);
        }

        foreach (var (key, stat) in stats)
        {
            stat.Form = new string(formTracker[key].ToArray());
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

        _db.TeamStatistics.AddRange(stats.Values);
        await _db.SaveChangesAsync(ct);

        return stats.Count;
    }
}
