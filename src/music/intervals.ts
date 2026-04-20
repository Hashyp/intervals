export type PlayMode = "ascending" | "descending" | "harmonic";
export type TrainingMode = PlayMode | "mixed";

export type IntervalId =
  | "P1"
  | "m2"
  | "M2"
  | "m3"
  | "M3"
  | "P4"
  | "TT"
  | "P5"
  | "m6"
  | "M6"
  | "m7"
  | "M7"
  | "P8";

export type IntervalDefinition = {
  id: IntervalId;
  name: string;
  shortName: string;
  semitones: number;
  hint: string;
};

export type IntervalQuestion = {
  id: string;
  interval: IntervalDefinition;
  rootMidi: number;
  targetMidi: number;
  mode: PlayMode;
};

export type QuestionOptions = {
  enabledIntervalIds: readonly IntervalId[];
  mode: TrainingMode;
};

export const INTERVALS: readonly IntervalDefinition[] = [
  {
    id: "P1",
    name: "Perfect unison",
    shortName: "Unison",
    semitones: 0,
    hint: "same note",
  },
  {
    id: "m2",
    name: "Minor second",
    shortName: "m2",
    semitones: 1,
    hint: "one half step",
  },
  {
    id: "M2",
    name: "Major second",
    shortName: "M2",
    semitones: 2,
    hint: "two half steps",
  },
  {
    id: "m3",
    name: "Minor third",
    shortName: "m3",
    semitones: 3,
    hint: "three half steps",
  },
  {
    id: "M3",
    name: "Major third",
    shortName: "M3",
    semitones: 4,
    hint: "four half steps",
  },
  {
    id: "P4",
    name: "Perfect fourth",
    shortName: "P4",
    semitones: 5,
    hint: "five half steps",
  },
  {
    id: "TT",
    name: "Tritone",
    shortName: "TT",
    semitones: 6,
    hint: "six half steps",
  },
  {
    id: "P5",
    name: "Perfect fifth",
    shortName: "P5",
    semitones: 7,
    hint: "seven half steps",
  },
  {
    id: "m6",
    name: "Minor sixth",
    shortName: "m6",
    semitones: 8,
    hint: "eight half steps",
  },
  {
    id: "M6",
    name: "Major sixth",
    shortName: "M6",
    semitones: 9,
    hint: "nine half steps",
  },
  {
    id: "m7",
    name: "Minor seventh",
    shortName: "m7",
    semitones: 10,
    hint: "ten half steps",
  },
  {
    id: "M7",
    name: "Major seventh",
    shortName: "M7",
    semitones: 11,
    hint: "eleven half steps",
  },
  {
    id: "P8",
    name: "Perfect octave",
    shortName: "Octave",
    semitones: 12,
    hint: "same name, higher or lower",
  },
];

export const DEFAULT_INTERVAL_IDS: readonly IntervalId[] = [
  "m2",
  "M2",
  "m3",
  "M3",
  "P4",
  "P5",
  "P8",
];

const NOTE_NAMES = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"];
const PLAYABLE_MIN_MIDI = 48;
const PLAYABLE_MAX_MIDI = 84;
const ROOT_MIN_MIDI = 52;
const ROOT_MAX_MIDI = 72;
const REAL_MODES: readonly PlayMode[] = ["ascending", "descending", "harmonic"];

export function midiToFrequency(midi: number): number {
  return 440 * 2 ** ((midi - 69) / 12);
}

export function midiToNoteName(midi: number): string {
  const octave = Math.floor(midi / 12) - 1;
  const pitchClass = ((midi % 12) + 12) % 12;

  return `${NOTE_NAMES[pitchClass]}${octave}`;
}

export function createQuestion(
  options: QuestionOptions,
  random: () => number = Math.random,
): IntervalQuestion {
  const enabledIntervals = INTERVALS.filter((interval) =>
    options.enabledIntervalIds.includes(interval.id),
  );
  const intervalPool = enabledIntervals.length > 0 ? enabledIntervals : INTERVALS;
  const interval = pickOne(intervalPool, random);
  const mode =
    options.mode === "mixed" ? pickOne(REAL_MODES, random) : options.mode;
  const rootCandidates = getRootCandidates(interval.semitones, mode);
  const rootMidi = pickOne(rootCandidates, random);
  const targetMidi =
    mode === "descending" ? rootMidi - interval.semitones : rootMidi + interval.semitones;

  return {
    id: `${Date.now()}-${rootMidi}-${targetMidi}-${interval.id}-${mode}`,
    interval,
    rootMidi,
    targetMidi,
    mode,
  };
}

function getRootCandidates(semitones: number, mode: PlayMode): number[] {
  const candidates: number[] = [];

  for (let rootMidi = ROOT_MIN_MIDI; rootMidi <= ROOT_MAX_MIDI; rootMidi += 1) {
    const targetMidi =
      mode === "descending" ? rootMidi - semitones : rootMidi + semitones;

    if (targetMidi >= PLAYABLE_MIN_MIDI && targetMidi <= PLAYABLE_MAX_MIDI) {
      candidates.push(rootMidi);
    }
  }

  return candidates;
}

function pickOne<T>(items: readonly T[], random: () => number): T {
  const index = Math.min(Math.floor(random() * items.length), items.length - 1);

  return items[index];
}

