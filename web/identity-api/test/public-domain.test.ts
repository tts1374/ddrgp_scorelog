import { describe, expect, it } from "vitest";
import {
  calculateFlareSkill,
  completedFlareSkill,
  flareCategory,
  flareSkillRank,
  scoreRank,
  type FlareSkillCandidate,
} from "../src/public-domain";

describe("public Player domain rules", () => {
  it("matches every score to the Desktop RANK boundary", () => {
    expect([
      [1_000_000, "AAA"], [990_000, "AAA"], [989_999, "AA+"], [950_000, "AA+"],
      [949_999, "AA"], [900_000, "AA"], [899_999, "AA-"], [890_000, "AA-"],
      [889_999, "A+"], [850_000, "A+"], [849_999, "A"], [800_000, "A"],
      [799_999, "A-"], [790_000, "A-"], [789_999, "B+"], [750_000, "B+"],
      [749_999, "B"], [700_000, "B"], [699_999, "B-"], [690_000, "B-"],
      [689_999, "C+"], [650_000, "C+"], [649_999, "C"], [600_000, "C"],
      [599_999, "C-"], [590_000, "C-"], [589_999, "D+"], [550_000, "D+"],
      [549_999, "D"], [0, "D"],
    ].map(([score]) => scoreRank(score as number))).toEqual([
      "AAA", "AAA", "AA+", "AA+", "AA", "AA", "AA-", "AA-", "A+", "A+",
      "A", "A", "A-", "A-", "B+", "B+", "B", "B", "B-", "B-", "C+", "C+",
      "C", "C", "C-", "C-", "D+", "D+", "D", "D",
    ]);
  });

  it("matches every value in the Desktop Lv x FLARE completion table", () => {
    const expected = [
      [153, 162, 171, 179, 188, 197, 205, 214, 223, 232],
      [164, 173, 182, 192, 201, 210, 220, 229, 238, 248],
      [180, 190, 200, 210, 221, 231, 241, 251, 261, 272],
      [196, 207, 218, 229, 240, 251, 262, 273, 284, 296],
      [217, 229, 241, 254, 266, 278, 291, 303, 315, 328],
      [243, 257, 271, 285, 299, 312, 326, 340, 354, 368],
      [270, 285, 300, 316, 331, 346, 362, 377, 392, 408],
      [307, 324, 342, 359, 377, 394, 411, 429, 446, 464],
      [355, 375, 395, 415, 435, 455, 475, 495, 515, 536],
      [424, 448, 472, 496, 520, 544, 568, 592, 616, 640],
      [492, 520, 548, 576, 604, 632, 660, 688, 716, 744],
      [540, 571, 601, 632, 663, 693, 724, 754, 785, 816],
      [577, 610, 643, 675, 708, 741, 773, 806, 839, 872],
      [609, 644, 678, 713, 747, 782, 816, 851, 885, 920],
      [636, 672, 708, 744, 780, 816, 852, 888, 924, 960],
      [657, 694, 731, 768, 806, 843, 880, 917, 954, 992],
      [673, 711, 749, 787, 825, 863, 901, 939, 977, 1016],
      [689, 728, 767, 806, 845, 884, 923, 962, 1001, 1040],
      [704, 744, 784, 824, 864, 904, 944, 984, 1024, 1064],
    ];
    const ranks = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "EX"] as const;
    expect(expected.map((row, level) => row.map((_, rank) => completedFlareSkill(level + 1, ranks[rank]!))))
      .toEqual(expected);
    expect(() => completedFlareSkill(20, "EX")).toThrow(RangeError);
  });

  it("matches Desktop category mappings and TOTAL rank thresholds", () => {
    expect(flareCategory("DDR X3 VS 2ndMIX")).toBe("CLASSIC");
    expect(flareCategory("DanceDanceRevolution A")).toBe("WHITE");
    expect(flareCategory("DanceDanceRevolution WORLD")).toBe("GOLD");
    expect(flareCategory("unknown")).toBeNull();

    const thresholds = [
      ["NONE", null, 0], ["NONE", "+", 500], ["NONE", "++", 1000], ["NONE", "+++", 1500],
      ["MERCURY", null, 2000], ["MERCURY", "+", 3000], ["MERCURY", "++", 4000], ["MERCURY", "+++", 5000],
      ["VENUS", null, 6000], ["VENUS", "+", 7000], ["VENUS", "++", 8000], ["VENUS", "+++", 9000],
      ["EARTH", null, 10000], ["EARTH", "+", 11500], ["EARTH", "++", 13000], ["EARTH", "+++", 14500],
      ["MARS", null, 16000], ["MARS", "+", 18000], ["MARS", "++", 20000], ["MARS", "+++", 22000],
      ["JUPITER", null, 24000], ["JUPITER", "+", 26500], ["JUPITER", "++", 29000], ["JUPITER", "+++", 31500],
      ["SATURN", null, 34000], ["SATURN", "+", 36750], ["SATURN", "++", 39500], ["SATURN", "+++", 42250],
      ["URANUS", null, 45000], ["URANUS", "+", 48750], ["URANUS", "++", 52500], ["URANUS", "+++", 56250],
      ["NEPTUNE", null, 60000], ["NEPTUNE", "+", 63750], ["NEPTUNE", "++", 67500], ["NEPTUNE", "+++", 71250],
      ["SUN", null, 75000], ["SUN", "+", 78750], ["SUN", "++", 82500], ["SUN", "+++", 86250],
      ["WORLD", null, 90000],
    ] as const;
    for (const [index, [main, sub, total]] of thresholds.entries()) {
      expect(flareSkillRank(total)).toEqual({ main, sub });
      expect(flareSkillRank(total + 1)).toEqual({ main, sub });
      if (index > 0) {
        const [previousMain, previousSub] = thresholds[index - 1]!;
        expect(flareSkillRank(total - 1)).toEqual({ main: previousMain, sub: previousSub });
      }
    }
  });

  it("selects category Top30 with deterministic title, difficulty, and chart tie-breaks", () => {
    const candidates: FlareSkillCandidate[] = Array.from({ length: 33 }, (_, index) => ({
      chart_id: `chart_${String(index).padStart(2, "0")}`,
      title: index >= 29 ? "Tie" : `Song ${String(index).padStart(2, "0")}`,
      difficulty: index === 31 ? "BASIC" : index === 32 ? "BEGINNER" : "EXPERT",
      level: 19,
      version: "DanceDanceRevolution WORLD",
      flare_rank: "EX",
    }));
    const result = calculateFlareSkill(candidates);
    const gold = result.categories.find((category) => category.category === "GOLD")!;
    expect(gold.target_count).toBe(30);
    expect(gold.targets.at(-1)?.chart_id).toBe("chart_32");
    expect(gold.targets.some((target) => target.chart_id === "chart_31")).toBe(false);
    expect(gold.total).toBe(30 * 1064);
    expect(result.total).toBe(gold.total);
  });

  it("returns TOTAL 0 / NONE for no eligible public Best", () => {
    expect(calculateFlareSkill([])).toMatchObject({ total: 0, rank: { main: "NONE", sub: null } });
    expect(flareSkillRank(499)).toEqual({ main: "NONE", sub: null });
    expect(flareSkillRank(500)).toEqual({ main: "NONE", sub: "+" });
    expect(flareSkillRank(90_000)).toEqual({ main: "WORLD", sub: null });
  });
});
