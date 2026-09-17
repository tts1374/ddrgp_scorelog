using System.Globalization;

namespace DDRGpScoreViewer.Models;

public sealed record FlareSkillPlayInput(
    string PlayId,
    string PlayedAt,
    string SongId,
    string ChartId,
    string ClearType,
    string? FlareRank);

public sealed record FlareSkillChartInput(
    string ChartId,
    string SongId,
    string Title,
    string Version,
    string PlayStyle,
    string Difficulty,
    int Level,
    bool IsRemoved);

public sealed record FlareSkillChartResult(
    string PlayId,
    string PlayedAt,
    string ChartId,
    string SongTitle,
    string Difficulty,
    int Level,
    string FlareRank,
    int FlareSkill,
    int Position = 0)
{
    public string PositionDisplay => Position.ToString(CultureInfo.CurrentCulture);
    public string DifficultyShortDisplay => Difficulty switch
    {
        "BEGINNER" => "BGN",
        "BASIC" => "BAS",
        "DIFFICULT" => "DIF",
        "EXPERT" => "EXP",
        "CHALLENGE" => "CHA",
        _ => Difficulty,
    };

    public string LevelDisplay => $"Lv.{Level}";
    public string FlareRankDisplay => $"FLARE {FlareRank}";
    public string FlareSkillDisplay => FlareSkill.ToString("N0", CultureInfo.CurrentCulture);
    public string FlareBadgeGroup => FlareRank;
}

public sealed record FlareSkillCategoryResult(
    string Name,
    string VersionRange,
    IReadOnlyList<FlareSkillChartResult> TopCharts,
    FlareSkillChartResult? RunnerUp,
    int Total)
{
    public string CountDisplay => $"{TopCharts.Count} / 30 TARGET";
    public string TotalDisplay => Total.ToString("N0", CultureInfo.CurrentCulture);
    public bool HasRunnerUp => RunnerUp is not null;
}

public sealed record FlareSkillRankResult(
    string MainRank,
    string SubRank,
    int Threshold,
    string? NextMainRank,
    string? NextSubRank,
    int? NextThreshold)
{
    public string RankDisplay => string.IsNullOrEmpty(SubRank)
        ? MainRank
        : $"{MainRank} {SubRank}";

    public string NextRankDisplay => NextThreshold is null
        ? Localization.Get("最高ランク")
        : string.IsNullOrEmpty(NextSubRank)
            ? NextMainRank ?? "—"
            : $"{NextMainRank} {NextSubRank}";

    public string JapaneseDisplay => MainRank switch
    {
        "NONE" => "なし",
        "MERCURY" => "水星",
        "VENUS" => "金星",
        "EARTH" => "地球",
        "MARS" => "火星",
        "JUPITER" => "木星",
        "SATURN" => "土星",
        "URANUS" => "天王星",
        "NEPTUNE" => "海王星",
        "SUN" => "太陽",
        "WORLD" => "世界",
        _ => MainRank,
    };

    public string NextRankThresholdDisplay => NextThreshold is null
        ? Localization.Get("最高ランク")
        : $"{NextRankDisplay} / {NextThreshold.Value:N0}";

    public double Progress(int total)
    {
        if (NextThreshold is null)
        {
            return 100d;
        }

        var range = NextThreshold.Value - Threshold;
        return range <= 0
            ? 0d
            : Math.Clamp((total - Threshold) * 100d / range, 0d, 100d);
    }

    public int? PointsToNext(int total) => NextThreshold is null
        ? null
        : Math.Max(0, NextThreshold.Value - total);

    public string PointsToNextDisplay(int total) => PointsToNext(total) is not int remaining
        ? Localization.Get("到達済み")
        : Localization.Format("あと {0:N0}", remaining);
}

public sealed record FlareSkillStyleResult(
    string PlayStyle,
    FlareSkillCategoryResult Classic,
    FlareSkillCategoryResult White,
    FlareSkillCategoryResult Gold,
    int Total,
    FlareSkillRankResult Rank,
    int AbnormalExclusionCount,
    int UnknownStyleAbnormalExclusionCount)
{
    public string TotalDisplay => Total.ToString("N0", CultureInfo.CurrentCulture);
    public int TotalAbnormalExclusionCount =>
        AbnormalExclusionCount + UnknownStyleAbnormalExclusionCount;
    public string AbnormalExclusionDisplay => UnknownStyleAbnormalExclusionCount == 0
        ? Localization.Format("算出対象外: {0:N0}件", AbnormalExclusionCount)
        : Localization.Format(
            "算出対象外: {0:N0}件（style不明: {1:N0}件）",
            TotalAbnormalExclusionCount,
            UnknownStyleAbnormalExclusionCount);
    public string PointsToNextDisplay => Rank.PointsToNextDisplay(Total);
}

public sealed record FlareSkillData(
    FlareSkillStyleResult Single,
    FlareSkillStyleResult Double)
{
    public static FlareSkillData Empty { get; } = FlareSkillCalculator.Calculate([], []);
}

public static class FlareSkillCalculator
{
    private const int CategoryLimit = 30;

    // BEMANIWiki DDR WORLD フレアスキル表（2026-09-17確認）。列は FLARE I..IX, EX。
    // https://bemaniwiki.com/index.php?DanceDanceRevolution+WORLD/%E3%83%95%E3%83%AC%E3%82%A2%E3%82%B9%E3%82%AD%E3%83%AB
    // Runtimeでは外部サイトへアクセスせず、この完成値だけを使用する。
    private static readonly int[,] CompletedValues =
    {
        { 153, 162, 171, 179, 188, 197, 205, 214, 223, 232 },
        { 164, 173, 182, 192, 201, 210, 220, 229, 238, 248 },
        { 180, 190, 200, 210, 221, 231, 241, 251, 261, 272 },
        { 196, 207, 218, 229, 240, 251, 262, 273, 284, 296 },
        { 217, 229, 241, 254, 266, 278, 291, 303, 315, 328 },
        { 243, 257, 271, 285, 299, 312, 326, 340, 354, 368 },
        { 270, 285, 300, 316, 331, 346, 362, 377, 392, 408 },
        { 307, 324, 342, 359, 377, 394, 411, 429, 446, 464 },
        { 355, 375, 395, 415, 435, 455, 475, 495, 515, 536 },
        { 424, 448, 472, 496, 520, 544, 568, 592, 616, 640 },
        { 492, 520, 548, 576, 604, 632, 660, 688, 716, 744 },
        { 540, 571, 601, 632, 663, 693, 724, 754, 785, 816 },
        { 577, 610, 643, 675, 708, 741, 773, 806, 839, 872 },
        { 609, 644, 678, 713, 747, 782, 816, 851, 885, 920 },
        { 636, 672, 708, 744, 780, 816, 852, 888, 924, 960 },
        { 657, 694, 731, 768, 806, 843, 880, 917, 954, 992 },
        { 673, 711, 749, 787, 825, 863, 901, 939, 977, 1016 },
        { 689, 728, 767, 806, 845, 884, 923, 962, 1001, 1040 },
        { 704, 744, 784, 824, 864, 904, 944, 984, 1024, 1064 },
    };

    private static readonly string[] FlareRanks =
        ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "EX"];

    private static readonly IReadOnlyDictionary<string, int> FlareRankIndexes =
        FlareRanks.Select((rank, index) => (rank, index))
            .ToDictionary(item => item.rank, item => item.index, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, int> DifficultyOrder =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["BEGINNER"] = 0,
            ["BASIC"] = 1,
            ["DIFFICULT"] = 2,
            ["EXPERT"] = 3,
            ["CHALLENGE"] = 4,
        };

    private static readonly IReadOnlyDictionary<string, string> VersionCategories =
        BuildVersionCategories();

    // DDR WORLD公式フレアスキルランク表（2026-09-17確認）。
    // https://p.eagate.573.jp/game/ddr/ddrworld/event/flare.html
    private static readonly (string Main, string Sub, int Threshold)[] RankThresholds =
    {
        ("NONE", "", 0), ("NONE", "+", 500), ("NONE", "++", 1000), ("NONE", "+++", 1500),
        ("MERCURY", "", 2000), ("MERCURY", "+", 3000), ("MERCURY", "++", 4000), ("MERCURY", "+++", 5000),
        ("VENUS", "", 6000), ("VENUS", "+", 7000), ("VENUS", "++", 8000), ("VENUS", "+++", 9000),
        ("EARTH", "", 10000), ("EARTH", "+", 11500), ("EARTH", "++", 13000), ("EARTH", "+++", 14500),
        ("MARS", "", 16000), ("MARS", "+", 18000), ("MARS", "++", 20000), ("MARS", "+++", 22000),
        ("JUPITER", "", 24000), ("JUPITER", "+", 26500), ("JUPITER", "++", 29000), ("JUPITER", "+++", 31500),
        ("SATURN", "", 34000), ("SATURN", "+", 36750), ("SATURN", "++", 39500), ("SATURN", "+++", 42250),
        ("URANUS", "", 45000), ("URANUS", "+", 48750), ("URANUS", "++", 52500), ("URANUS", "+++", 56250),
        ("NEPTUNE", "", 60000), ("NEPTUNE", "+", 63750), ("NEPTUNE", "++", 67500), ("NEPTUNE", "+++", 71250),
        ("SUN", "", 75000), ("SUN", "+", 78750), ("SUN", "++", 82500), ("SUN", "+++", 86250),
        ("WORLD", "", 90000),
    };

    public static int GetCompletedValue(int level, string flareRank)
    {
        if (level is < 1 or > 19 || !FlareRankIndexes.TryGetValue(flareRank, out var rankIndex))
        {
            throw new ArgumentOutOfRangeException();
        }

        return CompletedValues[level - 1, rankIndex];
    }

    public static FlareSkillRankResult GetRank(int total)
    {
        var index = 0;
        for (var candidate = 1; candidate < RankThresholds.Length; candidate++)
        {
            if (RankThresholds[candidate].Threshold > total)
            {
                break;
            }
            index = candidate;
        }

        var current = RankThresholds[index];
        var next = index + 1 < RankThresholds.Length
            ? RankThresholds[index + 1]
            : ((string Main, string Sub, int Threshold)?)null;
        return new FlareSkillRankResult(
            current.Main,
            current.Sub,
            current.Threshold,
            next?.Main,
            next?.Sub,
            next?.Threshold);
    }

    public static FlareSkillData Calculate(
        IEnumerable<FlareSkillPlayInput> plays,
        IEnumerable<FlareSkillChartInput> charts)
    {
        var chartMap = charts.ToDictionary(chart => chart.ChartId, StringComparer.Ordinal);
        var valid = new List<(FlareSkillChartResult Chart, string PlayStyle, string Category)>();
        var singleAbnormalExclusionCount = 0;
        var doubleAbnormalExclusionCount = 0;
        var unknownStyleAbnormalExclusionCount = 0;

        void CountAbnormal(string? playStyle)
        {
            if (playStyle == "SINGLE")
            {
                singleAbnormalExclusionCount++;
            }
            else if (playStyle == "DOUBLE")
            {
                doubleAbnormalExclusionCount++;
            }
            else
            {
                unknownStyleAbnormalExclusionCount++;
            }
        }

        foreach (var play in plays)
        {
            if (play.FlareRank is null || string.Equals(play.ClearType, "FAILED", StringComparison.Ordinal))
            {
                continue;
            }

            if (!chartMap.TryGetValue(play.ChartId, out var chart))
            {
                CountAbnormal(playStyle: null);
                continue;
            }

            if (chart.SongId != play.SongId ||
                chart.IsRemoved ||
                chart.Level is < 1 or > 19 ||
                chart.PlayStyle is not ("SINGLE" or "DOUBLE") ||
                !DifficultyOrder.ContainsKey(chart.Difficulty))
            {
                CountAbnormal(chart.PlayStyle);
                continue;
            }
            if (!FlareRankIndexes.TryGetValue(play.FlareRank, out var rankIndex) ||
                !VersionCategories.TryGetValue(chart.Version, out var category))
            {
                CountAbnormal(chart.PlayStyle);
                continue;
            }

            valid.Add((
                new FlareSkillChartResult(
                    play.PlayId,
                    play.PlayedAt,
                    chart.ChartId,
                    chart.Title,
                    chart.Difficulty,
                    chart.Level,
                    play.FlareRank,
                    CompletedValues[chart.Level - 1, rankIndex]),
                chart.PlayStyle,
                category));
        }

        return new FlareSkillData(
            BuildStyle(
                "SINGLE",
                valid,
                singleAbnormalExclusionCount,
                unknownStyleAbnormalExclusionCount),
            BuildStyle(
                "DOUBLE",
                valid,
                doubleAbnormalExclusionCount,
                unknownStyleAbnormalExclusionCount));
    }

    private static FlareSkillStyleResult BuildStyle(
        string playStyle,
        IReadOnlyList<(FlareSkillChartResult Chart, string PlayStyle, string Category)> candidates,
        int abnormalExclusionCount,
        int unknownStyleAbnormalExclusionCount)
    {
        var chartBests = candidates
            .Where(candidate => candidate.PlayStyle == playStyle)
            .GroupBy(candidate => candidate.Chart.ChartId, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(candidate => candidate.Chart.FlareSkill)
                .ThenByDescending(candidate => FlareRankIndexes[candidate.Chart.FlareRank])
                .ThenByDescending(candidate => ParsePlayedAt(candidate.Chart.PlayedAt))
                .ThenBy(candidate => candidate.Chart.PlayId, StringComparer.Ordinal)
                .First())
            .ToArray();

        var classic = BuildCategory("CLASSIC", "1st〜X3 VS 2ndMIX", chartBests);
        var white = BuildCategory("WHITE", "2013〜A", chartBests);
        var gold = BuildCategory("GOLD", "A20〜WORLD", chartBests);
        var total = classic.Total + white.Total + gold.Total;
        return new FlareSkillStyleResult(
            playStyle,
            classic,
            white,
            gold,
            total,
            GetRank(total),
            abnormalExclusionCount,
            unknownStyleAbnormalExclusionCount);
    }

    private static FlareSkillCategoryResult BuildCategory(
        string name,
        string versionRange,
        IEnumerable<(FlareSkillChartResult Chart, string PlayStyle, string Category)> candidates)
    {
        var ordered = candidates
            .Where(candidate => candidate.Category == name)
            .Select(candidate => candidate.Chart)
            .OrderByDescending(chart => chart.FlareSkill)
            .ThenBy(chart => chart.SongTitle, StringComparer.Ordinal)
            .ThenBy(chart => DifficultyOrder[chart.Difficulty])
            .ThenBy(chart => chart.ChartId, StringComparer.Ordinal)
            .ToArray();
        var top = ordered.Take(CategoryLimit)
            .Select((chart, index) => chart with { Position = index + 1 })
            .ToArray();
        var runnerUp = ordered.Skip(CategoryLimit).FirstOrDefault() is { } next
            ? next with { Position = CategoryLimit + 1 }
            : null;
        return new FlareSkillCategoryResult(
            name,
            versionRange,
            top,
            runnerUp,
            top.Sum(chart => chart.FlareSkill));
    }

    private static DateTimeOffset ParsePlayedAt(string value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out var timestamp)
            ? timestamp
            : DateTimeOffset.MinValue;

    private static IReadOnlyDictionary<string, string> BuildVersionCategories()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var version in new[]
        {
            "DDR 1st", "DDR 2ndMIX", "DDR 3rdMIX", "DDR 4thMIX", "DDR 5thMIX",
            "DDRMAX", "DDRMAX2", "DDR EXTREME", "DDR SuperNOVA", "DDR SuperNOVA 2",
            "DDR X", "DDR X2", "DDR X3 VS 2ndMIX",
        })
        {
            result[version] = "CLASSIC";
        }
        foreach (var version in new[]
        {
            "DanceDanceRevolution (2013)", "DanceDanceRevolution (2014)",
            "DanceDanceRevolution A",
        })
        {
            result[version] = "WHITE";
        }
        foreach (var version in new[]
        {
            "DanceDanceRevolution A20", "DanceDanceRevolution A20 PLUS",
            "DanceDanceRevolution A20 PL US", "DanceDanceRevolution A3",
            "DanceDanceRevolution WORLD",
        })
        {
            result[version] = "GOLD";
        }
        return result;
    }
}
