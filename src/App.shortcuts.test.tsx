import {
  cleanup,
  fireEvent,
  render,
  screen,
  within,
} from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";
import type { IntervalQuestion } from "./music/intervals";

const audioMock = vi.hoisted(() => ({
  audioState: "idle" as "idle" | "loading" | "playing" | "error",
  play: vi.fn(),
  playBetweenNotes: vi.fn(),
  stop: vi.fn(),
}));

vi.mock("./audio/useIntervalAudio", () => ({
  useIntervalAudio: () => ({
    audioState: audioMock.audioState,
    play: audioMock.play,
    playBetweenNotes: audioMock.playBetweenNotes,
    stop: audioMock.stop,
  }),
}));

describe("App keyboard shortcuts", () => {
  beforeEach(() => {
    audioMock.audioState = "idle";
    vi.clearAllMocks();
    window.history.replaceState({}, "", "/");
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it("plays the current question with Space when audio is idle", () => {
    render(<App />);

    fireEvent.keyDown(window, { code: "Space", key: " " });

    expect(audioMock.play).toHaveBeenCalledTimes(1);
    expect(audioMock.play.mock.calls[0]?.[1]).toBe("guitar");
  });

  it.each(["loading", "playing"] as const)(
    "does not play with Space while audio is %s",
    (audioState) => {
      audioMock.audioState = audioState;
      render(<App />);

      fireEvent.keyDown(window, { code: "Space", key: " " });

      expect(audioMock.play).not.toHaveBeenCalled();
    },
  );

  it("advances to a new question and resets the guess state with N", () => {
    vi.spyOn(Math, "random")
      .mockReturnValueOnce(0)
      .mockReturnValueOnce(0)
      .mockReturnValueOnce(0.99)
      .mockReturnValue(0);
    render(<App />);

    const answerGrid = screen.getByLabelText("Interval answers");
    const answers = within(answerGrid).getAllByRole("button");
    fireEvent.click(answers[0]);

    expect(screen.getByRole("status")).toHaveTextContent(/not yet/i);
    expect(answers[0]).toHaveAttribute("aria-disabled", "true");

    fireEvent.keyDown(window, { code: "KeyN", key: "n" });

    expect(audioMock.stop).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("status")).toHaveTextContent(
      "Press play, listen to the guitar, then choose the interval you hear.",
    );
    for (const answer of within(answerGrid).getAllByRole("button")) {
      expect(answer).not.toHaveAttribute("aria-disabled");
    }

    fireEvent.keyDown(window, { code: "Space", key: " " });
    const nextQuestion = audioMock.play.mock.calls[0]?.[0] as IntervalQuestion;
    expect(nextQuestion.interval.id).toBe("P8");
  });

  it("ignores shortcuts from button, input, textarea, and contenteditable targets", () => {
    render(<App />);

    const input = document.createElement("input");
    const textarea = document.createElement("textarea");
    const editable = document.createElement("div");
    editable.contentEditable = "true";
    Object.defineProperty(editable, "isContentEditable", {
      configurable: true,
      value: true,
    });
    document.body.append(input, textarea, editable);

    const targets = [
      screen.getByRole("button", { name: /play guitar/i }),
      input,
      textarea,
      editable,
    ];

    for (const target of targets) {
      fireEvent.keyDown(target, { code: "Space", key: " " });
      fireEvent.keyDown(target, { code: "KeyN", key: "n" });
    }

    expect(audioMock.play).not.toHaveBeenCalled();
    expect(audioMock.stop).not.toHaveBeenCalled();
  });
});
