import { useCallback, useEffect, useRef, useState } from "react";
import * as Tone from "tone";
import type { IntervalQuestion } from "../music/intervals";
import { midiToNoteName } from "../music/intervals";

type AudioState = "idle" | "loading" | "playing" | "error";
export type InstrumentId = "guitar" | "piano";

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
  const samplersRef = useRef<Partial<Record<InstrumentId, Tone.Sampler>>>({});
  const [audioState, setAudioState] = useState<AudioState>("idle");

  const getSampler = useCallback((instrument: InstrumentId) => {
    if (!samplersRef.current[instrument]) {
      samplersRef.current[instrument] = new Tone.Sampler(
        INSTRUMENT_SAMPLER_CONFIG[instrument],
      ).toDestination();
    }

    return samplersRef.current[instrument];
  }, []);

  const play = useCallback(
    async (question: IntervalQuestion, instrument: InstrumentId) => {
      try {
        setAudioState("loading");
        await Tone.start();
        const sampler = getSampler(instrument);
        await Tone.loaded();

        setAudioState("playing");
        const now = Tone.now() + 0.08;
        const rootNote = midiToNoteName(question.rootMidi);
        const targetNote = midiToNoteName(question.targetMidi);

        for (const activeSampler of Object.values(samplersRef.current)) {
          activeSampler?.releaseAll();
        }

        sampler.releaseAll();

        if (question.mode === "harmonic") {
          sampler.triggerAttackRelease([rootNote, targetNote], 1.8, now, 0.95);
        } else {
          sampler.triggerAttackRelease(rootNote, 1.35, now, 0.95);
          sampler.triggerAttackRelease(targetNote, 1.35, now + 1.05, 0.95);
        }

        window.setTimeout(() => {
          setAudioState("idle");
        }, question.mode === "harmonic" ? 2100 : 2550);
      } catch {
        setAudioState("error");
      }
    },
    [getSampler],
  );

  useEffect(() => {
    return () => {
      for (const sampler of Object.values(samplersRef.current)) {
        sampler?.dispose();
      }
      samplersRef.current = {};
    };
  }, []);

  return {
    audioState,
    play,
  };
}
