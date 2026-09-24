namespace ZokaAnalytics.Services.Markets;

public record ScoreLine(int HomeGoals, int AwayGoals, double Probability)
{
    public string Text => $"{HomeGoals}-{AwayGoals}";
}

public static class PoissonMarkets
{
    private const int MaxGoals = 8;

    public static double Pmf(int k, double lambda)
    {
        if (lambda <= 0) return k == 0 ? 1 : 0;

        double result = Math.Exp(-lambda);
        for (var i = 1; i <= k; i++)
            result *= lambda / i;

        return result;
    }

    public static double[,] Matrix(double xgHome, double xgAway)
    {
        var matrix = new double[MaxGoals + 1, MaxGoals + 1];
        double total = 0;

        for (var h = 0; h <= MaxGoals; h++)
        {
            for (var a = 0; a <= MaxGoals; a++)
            {
                matrix[h, a] = Pmf(h, xgHome) * Pmf(a, xgAway);
                total += matrix[h, a];
            }
        }

        if (total > 0)
        {
            for (var h = 0; h <= MaxGoals; h++)
                for (var a = 0; a <= MaxGoals; a++)
                    matrix[h, a] /= total;
        }

        return matrix;
    }

    public static List<ScoreLine> TopScores(double xgHome, double xgAway, int count = 6)
    {
        var matrix = Matrix(xgHome, xgAway);
        var lines = new List<ScoreLine>();

        for (var h = 0; h <= MaxGoals; h++)
            for (var a = 0; a <= MaxGoals; a++)
                if (matrix[h, a] > 0.001)
                    lines.Add(new ScoreLine(h, a, matrix[h, a]));

        return lines.OrderByDescending(l => l.Probability).Take(count).ToList();
    }

    public static double OverProbability(double xgHome, double xgAway, double line)
    {
        var matrix = Matrix(xgHome, xgAway);
        double under = 0;

        for (var h = 0; h <= MaxGoals; h++)
            for (var a = 0; a <= MaxGoals; a++)
                if (h + a < line)
                    under += matrix[h, a];

        return Math.Clamp(1 - under, 0, 1);
    }

    public static double BttsProbability(double xgHome, double xgAway)
        => Math.Clamp((1 - Pmf(0, xgHome)) * (1 - Pmf(0, xgAway)), 0, 1);

    public static double ProbabilityFor(
        Models.BetType selection, double xgHome, double xgAway,
        double homeWin, double draw, double awayWin)
        => selection switch
        {
            Models.BetType.HomeWin => homeWin,
            Models.BetType.Draw => draw,
            Models.BetType.AwayWin => awayWin,
            Models.BetType.DoubleChance1X => homeWin + draw,
            Models.BetType.DoubleChanceX2 => draw + awayWin,
            Models.BetType.DoubleChance12 => homeWin + awayWin,
            Models.BetType.Over15 => OverProbability(xgHome, xgAway, 1.5),
            Models.BetType.Under15 => 1 - OverProbability(xgHome, xgAway, 1.5),
            Models.BetType.Over25 => OverProbability(xgHome, xgAway, 2.5),
            Models.BetType.Under25 => 1 - OverProbability(xgHome, xgAway, 2.5),
            Models.BetType.Over35 => OverProbability(xgHome, xgAway, 3.5),
            Models.BetType.Under35 => 1 - OverProbability(xgHome, xgAway, 3.5),
            Models.BetType.BttsYes => BttsProbability(xgHome, xgAway),
            Models.BetType.BttsNo => 1 - BttsProbability(xgHome, xgAway),
            _ => 0
        };
}
