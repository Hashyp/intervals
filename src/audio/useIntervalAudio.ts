import { useCallback, useEffect, useRef, useState } from "react";
import type * as Tone from "tone";
import type { IntervalQuestion } from "../music/intervals";
import { midiToNoteName } from "../music/intervals";

type AudioState = "idle" | "loading" | "playing" | "error";
export type InstrumentId = "guitar" | "piano";
type ToneModule = typeof import("tone");

const GUITAR_SAMPLE_URLS = {
  C4: "c4.mp3",
  Db4: "db4.mp3",
  D4: "d4.mp3",
  Eb4: "eb4.mp3",
  E4: "e4.mp3",
  F4: "f4.mp3",
  Gb4: "gb4.mp3",
  G4: "g4.mp3",
  Ab4: "ab4.mp3",
  A4: "a4.mp3",
  Bb4: "bb4.mp3",
  B4: "b4.mp3",
  C5: "c5.mp3",
} as const;

const PIANO_SAMPLE_URLS = {
  A0: "A0.mp3",
  C1: "C1.mp3",
  "D#1": "Ds1.mp3",
  "F#1": "Fs1.mp3",
  A1: "A1.mp3",
  C2: "C2.mp3",
  "D#2": "Ds2.mp3",
  "F#2": "Fs2.mp3",
  A2: "A2.mp3",
  C3: "C3.mp3",
  "D#3": "Ds3.mp3",
  "F#3": "Fs3.mp3",
  A3: "A3.mp3",
  C4: "C4.mp3",
  "D#4": "Ds4.mp3",
  "F#4": "Fs4.mp3",
  A4: "A4.mp3",
  C5: "C5.mp3",
  "D#5": "Ds5.mp3",
  "F#5": "Fs5.mp3",
  A5: "A5.mp3",
  C6: "C6.mp3",
  "D#6": "Ds6.mp3",
  "F#6": "Fs6.mp3",
  A6: "A6.mp3",
  C7: "C7.mp3",
  "D#7": "Ds7.mp3",
  "F#7": "Fs7.mp3",
  A7: "A7.mp3",
  C8: "C8.mp3",
} as const;

type SamplerConfig = {
  baseUrl: string;
  release: number;
  volume: number;
  urls: Record<string, string>;
};

const INSTRUMENT_SAMPLER_CONFIG: Record<InstrumentId, SamplerConfig> = {
  guitar: {
    urls: GUITAR_SAMPLE_URLS,
    baseUrl: `${import.meta.env.BASE_URL}samples/guitar/`,
    release: 1.1,
    volume: -5,
  },
  piano: {
    urls: PIANO_SAMPLE_URLS,
    baseUrl: `${import.meta.env.BASE_URL}samples/piano/`,
    release: 1.15,
    volume: -8,
  },
};

export function useIntervalAudio() {
  const toneRef = useRef<ToneModule | null>(null);
  const samplersRef = useRef<Partial<Record<InstrumentId, Tone.Sampler>>>({});
  const playbackTimeoutRef = useRef<number | null>(null);
  const playbackTokenRef = useRef(0);
  const [audioState, setAudioState] = useState<AudioState>("idle");

  const loadTone = useCallback(async () => {
    if (!toneRef.current) {
      toneRef.current = await import("tone");
    }

    return toneRef.current;
  }, []);

  const clearPlaybackTimeout = useCallback(() => {
    if (playbackTimeoutRef.current !== null) {
      window.clearTimeout(playbackTimeoutRef.current);
      playbackTimeoutRef.current = null;
    }
  }, []);

  const releaseAllSamplers = useCallback(() => {
    for (const sampler of Object.values(samplersRef.current)) {
      sampler?.releaseAll();
    }
  }, []);

  const beginPlayback = useCallback(() => {
    playbackTokenRef.current += 1;
    clearPlaybackTimeout();
    releaseAllSamplers();

    return playbackTokenRef.current;
  }, [clearPlaybackTimeout, releaseAllSamplers]);

  const isCurrentPlayback = useCallback((token: number) => {
    return playbackTokenRef.current === token;
  }, []);

  const finishPlaybackAfter = useCallback(
    (durationMs: number, token: number) => {
      clearPlaybackTimeout();
      playbackTimeoutRef.current = window.setTimeout(() => {
        if (!isCurrentPlayback(token)) {
          return;
        }

        setAudioState("idle");
        playbackTimeoutRef.current = null;
      }, durationMs);
    },
    [clearPlaybackTimeout, isCurrentPlayback],
  );

  const stop = useCallback(() => {
    playbackTokenRef.current += 1;
    clearPlaybackTimeout();
    releaseAllSamplers();
    setAudioState("idle");
  }, [clearPlaybackTimeout, releaseAllSamplers]);

  const getSampler = useCallback(
    (tone: ToneModule, instrument: InstrumentId) => {
      if (!samplersRef.current[instrument]) {
        samplersRef.current[instrument] = new tone.Sampler(
          INSTRUMENT_SAMPLER_CONFIG[instrument],
        ).toDestination();
      }

      return samplersRef.current[instrument];
    },
    [],
  );

  const play = useCallback(
    async (question: IntervalQuestion, instrument: InstrumentId) => {
      const token = beginPlayback();

      try {
        setAudioState("loading");
        const tone = await loadTone();
        if (!isCurrentPlayback(token)) {
          return;
        }

        await tone.start();
        if (!isCurrentPlayback(token)) {
          return;
        }

        const sampler = getSampler(tone, instrument);
        await tone.loaded();
        if (!isCurrentPlayback(token)) {
          return;
        }

        setAudioState("playing");
        const now = tone.now() + 0.08;
        const rootNote = midiToNoteName(question.rootMidi);
        const targetNote = midiToNoteName(question.targetMidi);

        releaseAllSamplers();

        if (question.mode === "harmonic") {
          sampler.triggerAttackRelease([rootNote, targetNote], 1.8, now, 0.95);
        } else {
          sampler.triggerAttackRelease(rootNote, 1.35, now, 0.95);
          sampler.triggerAttackRelease(targetNote, 1.35, now + 1.05, 0.95);
        }

        finishPlaybackAfter(question.mode === "harmonic" ? 2100 : 2550, token);
      } catch {
        if (isCurrentPlayback(token)) {
          setAudioState("error");
        }
      }
    },
    [
      beginPlayback,
      finishPlaybackAfter,
      getSampler,
      isCurrentPlayback,
      loadTone,
      releaseAllSamplers,
    ],
  );

  const playBetweenNotes = useCallback(
    async (question: IntervalQuestion, instrument: InstrumentId) => {
      const token = beginPlayback();

      try {
        setAudioState("loading");
        const tone = await loadTone();
        if (!isCurrentPlayback(token)) {
          return;
        }

        await tone.start();
        if (!isCurrentPlayback(token)) {
          return;
        }

        const sampler = getSampler(tone, instrument);
        await tone.loaded();
        if (!isCurrentPlayback(token)) {
          return;
        }

        setAudioState("playing");
        const now = tone.now() + 0.08;
        const midiNotes = getMidiNotesBetween(
          question.rootMidi,
          question.targetMidi,
        );

        releaseAllSamplers();

        const stepDuration = 0.66;
        const stepSpacing = 0.38;

        midiNotes.forEach((midi, index) => {
          sampler.triggerAttackRelease(
            midiToNoteName(midi),
            stepDuration,
            now + index * stepSpacing,
            0.94,
          );
        });

        const totalDurationMs = Math.round(
          (midiNotes.length * stepSpacing + 0.8) * 1000,
        );
        finishPlaybackAfter(totalDurationMs, token);
      } catch {
        if (isCurrentPlayback(token)) {
          setAudioState("error");
        }
      }
    },
    [
      beginPlayback,
      finishPlaybackAfter,
      getSampler,
      isCurrentPlayback,
      loadTone,
      releaseAllSamplers,
    ],
  );

  useEffect(() => {
    return () => {
      playbackTokenRef.current += 1;
      clearPlaybackTimeout();
      for (const sampler of Object.values(samplersRef.current)) {
        sampler?.dispose();
      }
      samplersRef.current = {};
    };
  }, [clearPlaybackTimeout]);

  return {
    audioState,
    play,
    playBetweenNotes,
    stop,
  };
}

function getMidiNotesBetween(fromMidi: number, toMidi: number) {
  if (fromMidi === toMidi) {
    return [fromMidi];
  }

  const step = toMidi > fromMidi ? 1 : -1;
  const notes: number[] = [];

  for (let midi = fromMidi; ; midi += step) {
    notes.push(midi);
    if (midi === toMidi) {
      break;
    }
  }

  return notes;
}
