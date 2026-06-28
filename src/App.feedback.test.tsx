import { fireEvent, render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";

const audioMock = vi.hoisted(() => ({
  audioState: "idle",
  play: vi.fn(),
  playBetweenNotes: vi.fn(),
  stop: vi.fn(),
}));

vi.mock("./audio/useIntervalAudio", () => ({
  useIntervalAudio: () => audioMock,
}));

describe("App feedback", () => {
  beforeEach(() => {
    audioMock.audioState = "idle";
    audioMock.play.mockClear();
    audioMock.playBetweenNotes.mockClear();
    audioMock.stop.mockClear();
    window.history.replaceState(
      {},
      "",
      "/?mode=ascending&intervals=M3&instrument=guitar",
    );
  });

  it("shows the audio error status text without success styling", () => {
    audioMock.audioState = "error";

    render(<App />);

    const status = screen.getByRole("status");

    expect(status).toHaveTextContent(
      "Audio could not start. Click play again or check browser audio permissions.",
    );
    expect(status).toHaveClass("feedback", "is-error");
  });

  it("shows correct feedback and marks the correct answer", () => {
    render(<App />);

    const answers = within(screen.getByLabelText("Interval answers"));
    const correctAnswer = answers.getByRole("button", {
      name: /major third/i,
    });

    fireEvent.click(correctAnswer);

    const status = screen.getByRole("status");

    expect(status).toHaveTextContent("Correct — Major third. four half steps.");
    expect(status).toHaveClass("feedback", "is-correct");
    expect(correctAnswer).toHaveClass("is-correct");
    expect(
      screen.queryByRole("button", { name: /play all notes between/i }),
    ).not.toBeInTheDocument();
  });

  it("shows wrong feedback and answer classes", () => {
    render(<App />);

    const answers = within(screen.getByLabelText("Interval answers"));
    const correctAnswer = answers.getByRole("button", {
      name: /major third/i,
    });
    const wrongAnswer = answers.getByRole("button", {
      name: /minor second/i,
    });
    const mutedAnswer = answers.getByRole("button", {
      name: /perfect unison/i,
    });

    fireEvent.click(wrongAnswer);

    const status = screen.getByRole("status");

    expect(status).toHaveTextContent(
      "Not yet. The answer was Major third. Try “Play all notes between” to hear every half step.",
    );
    expect(status).toHaveClass("feedback", "is-wrong");
    expect(correctAnswer).toHaveClass("is-correct");
    expect(wrongAnswer).toHaveClass("is-wrong");
    expect(mutedAnswer).toHaveClass("is-faded");
    expect(
      screen.getByRole("button", { name: /play all notes between/i }),
    ).toBeInTheDocument();
  });

  it("calls playBetweenNotes from the wrong-answer remediation button", () => {
    render(<App />);

    const answers = within(screen.getByLabelText("Interval answers"));

    fireEvent.click(
      answers.getByRole("button", {
        name: /minor second/i,
      }),
    );
    fireEvent.click(
      screen.getByRole("button", { name: /play all notes between/i }),
    );

    expect(audioMock.playBetweenNotes).toHaveBeenCalledTimes(1);
    expect(audioMock.playBetweenNotes.mock.calls[0]?.[0]).toMatchObject({
      interval: {
        id: "M3",
      },
    });
    expect(audioMock.playBetweenNotes.mock.calls[0]?.[1]).toBe("guitar");
  });
});
