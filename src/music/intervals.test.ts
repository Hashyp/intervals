import { describe, expect, it } from "vitest";
import {
  DEFAULT_INTERVAL_IDS,
  createQuestion,
  midiToFrequency,
  midiToNoteName,
} from "./intervals";

describe("interval question generation", () => {
  it("creates an ascending question with the expected semitone distance", () => {
    const question = createQuestion(
      {
        enabledIntervalIds: ["M3"],
        mode: "ascending",
      },
      () => 0,
    );

    expect(question.interval.id).toBe("M3");
    expect(question.targetMidi - question.rootMidi).toBe(4);
    expect(question.mode).toBe("ascending");
  });

  it("creates a descending question with the expected semitone distance", () => {
    const question = createQuestion(
      {
        enabledIntervalIds: ["P5"],
        mode: "descending",
      },
      () => 0,
    );

    expect(question.interval.id).toBe("P5");
    expect(question.rootMidi - question.targetMidi).toBe(7);
    expect(question.mode).toBe("descending");
  });

  it("keeps generated notes in a comfortable training range", () => {
    for (const intervalId of DEFAULT_INTERVAL_IDS) {
      const question = createQuestion(
        {
          enabledIntervalIds: [intervalId],
          mode: "mixed",
        },
        () => 0.99,
      );

      expect(question.rootMidi).toBeGreaterThanOrEqual(48);
      expect(question.rootMidi).toBeLessThanOrEqual(84);
      expect(question.targetMidi).toBeGreaterThanOrEqual(48);
      expect(question.targetMidi).toBeLessThanOrEqual(84);
    }
  });
});

describe("midi helpers", () => {
  it("converts A4 to 440 Hz", () => {
    expect(midiToFrequency(69)).toBe(440);
  });

  it("formats note names with octave numbers", () => {
    expect(midiToNoteName(60)).toBe("C4");
    expect(midiToNoteName(70)).toBe("Bb4");
  });
});

