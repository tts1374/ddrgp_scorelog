export type PublicStyle = "SP" | "DP";
export type Difficulty =
  | "BEGINNER"
  | "BASIC"
  | "DIFFICULT"
  | "EXPERT"
  | "CHALLENGE";
export type FlareRank = "EX" | "IX" | "VIII" | "VII" | "VI" | "V" | "IV" | "III" | "II" | "I";
export type FlareCategory = "CLASSIC" | "WHITE" | "GOLD";

export const difficultyOrder: Record<Difficulty, number> = {
  BEGINNER: 0,
  BASIC: 1,
  DIFFICULT: 2,
  EXPERT: 3,
  CHALLENGE: 4,
};

const completedValues = [
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
] as const;

const flareRankIndexes: Record<FlareRank, number> = {
  I: 0,
  II: 1,
  III: 2,
  IV: 3,
  V: 4,
  VI: 5,
  VII: 6,
  VIII: 7,
  IX: 8,
  EX: 9,
};

const versionCategories = new Map<string, FlareCategory>([
  ...[
    "DDR 1st", "DDR 2ndMIX", "DDR 3rdMIX", "DDR 4thMIX", "DDR 5thMIX",
    "DDRMAX", "DDRMAX2", "DDR EXTREME", "DDR SuperNOVA", "DDR SuperNOVA 2",
    "DDR X", "DDR X2", "DDR X3 VS 2ndMIX",
  ].map((version) => [version, "CLASSIC"] as const),
  ...[
    "DanceDanceRevolution (2013)", "DanceDanceRevolution (2014)",
    "DanceDanceRevolution A",
  ].map((version) => [version, "WHITE"] as const),
  ...[
    "DanceDanceRevolution A20", "DanceDanceRevolution A20 PLUS",
    "DanceDanceRevolution A20 PL US", "DanceDanceRevolution A3",
    "DanceDanceRevolution WORLD",
  ].map((version) => [version, "GOLD"] as const),
]);

const flareRankThresholds = [
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

export type ScoreRank =
  | "AAA" | "AA+" | "AA" | "AA-" | "A+" | "A" | "A-"
  | "B+" | "B" | "B-" | "C+" | "C" | "C-" | "D+" | "D";

export function scoreRank(score: number): ScoreRank {
  if (score >= 990_000) return "AAA";
  if (score >= 950_000) return "AA+";
  if (score >= 900_000) return "AA";
  if (score >= 890_000) return "AA-";
  if (score >= 850_000) return "A+";
  if (score >= 800_000) return "A";
  if (score >= 790_000) return "A-";
  if (score >= 750_000) return "B+";
  if (score >= 700_000) return "B";
  if (score >= 690_000) return "B-";
  if (score >= 650_000) return "C+";
  if (score >= 600_000) return "C";
  if (score >= 590_000) return "C-";
  if (score >= 550_000) return "D+";
  return "D";
}

export function completedFlareSkill(level: number, flareRank: FlareRank): number {
  const row = completedValues[level - 1];
  if (row === undefined) throw new RangeError("level must be between 1 and 19");
  return row[flareRankIndexes[flareRank]];
}

export function flareCategory(version: string): FlareCategory | null {
  return versionCategories.get(version) ?? null;
}

export function flareSkillRank(total: number): { main: string; sub: "+" | "++" | "+++" | null } {
  let current: readonly [string, "+" | "++" | "+++" | null, number] = flareRankThresholds[0];
  for (const threshold of flareRankThresholds.slice(1)) {
    if (threshold[2] > total) break;
    current = threshold;
  }
  return { main: current[0], sub: current[1] };
}

export interface FlareSkillCandidate {
  chart_id: string;
  title: string;
  difficulty: Difficulty;
  level: number;
  version: string;
  flare_rank: FlareRank;
}

export interface FlareSkillTarget {
  chart_id: string;
  title: string;
  difficulty: Difficulty;
  level: number;
  flare_rank: FlareRank;
  flare_skill: number;
}

function compareText(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0;
}

export function calculateFlareSkill(candidates: FlareSkillCandidate[]) {
  const grouped = new Map<FlareCategory, FlareSkillTarget[]>([
    ["CLASSIC", []], ["WHITE", []], ["GOLD", []],
  ]);
  for (const candidate of candidates) {
    const category = flareCategory(candidate.version);
    if (category === null) continue;
    grouped.get(category)!.push({
      chart_id: candidate.chart_id,
      title: candidate.title,
      difficulty: candidate.difficulty,
      level: candidate.level,
      flare_rank: candidate.flare_rank,
      flare_skill: completedFlareSkill(candidate.level, candidate.flare_rank),
    });
  }
  const categories = (["CLASSIC", "WHITE", "GOLD"] as const).map((category) => {
    const targets = grouped.get(category)!
      .sort((left, right) =>
        right.flare_skill - left.flare_skill ||
        compareText(left.title, right.title) ||
        difficultyOrder[left.difficulty] - difficultyOrder[right.difficulty] ||
        compareText(left.chart_id, right.chart_id),
      )
      .slice(0, 30);
    return {
      category,
      total: targets.reduce((sum, target) => sum + target.flare_skill, 0),
      target_count: targets.length,
      targets,
    };
  });
  const total = categories.reduce((sum, category) => sum + category.total, 0);
  return { total, rank: flareSkillRank(total), categories };
}
