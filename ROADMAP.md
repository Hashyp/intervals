# Roadmap

## Recommended Next Features

### 1. Adaptive Practice

Track incorrect answers and bias future questions toward weak intervals.

Why it matters:

- highest learning value
- turns the app from a static quiz into a training tool
- makes practice feel personal instead of random

Suggested implementation:

- store per-interval correct and incorrect counts
- increase selection weight for frequently missed intervals
- show a short "focus intervals" section in the progress area

### 2. Reference Songs / Mnemonic Mode

Attach a familiar musical example to each interval.

Why it matters:

- helps users build long-term memory for interval recognition
- gives beginners a practical mental anchor

Suggested implementation:

- add one short reference example per interval
- show the example only after answering or in study mode
- keep the copy compact and musician-friendly

### 3. Replay Variants

Add more focused listening controls.

Suggested controls:

- replay same interval
- play root only
- play target only
- switch between melodic and harmonic replay

Why it matters:

- helps users isolate what they hear
- reduces frustration when the sound is unclear

### 4. Instrument / Timbre Choice

Keep guitar, then add alternatives.

Suggested options:

- guitar
- piano
- pure tone

Why it matters:

- some users learn faster on simpler timbres
- lets the app support both beginner and musician workflows

### 5. Per-Interval Statistics

Show progress by interval, not only global score.

Suggested metrics:

- accuracy per interval
- most missed intervals
- recent improvement
- strongest intervals

Why it matters:

- makes weaknesses visible
- pairs naturally with adaptive practice

### 6. Difficulty Presets

Provide quick training presets.

Suggested presets:

- Beginner: `m2 M2 m3 M3 P4 P5 P8`
- Triads: `m3 M3 P5`
- Advanced: all intervals

Why it matters:

- easier onboarding
- faster setup for repeat practice

### 7. Answer-After-Replay Mode

Require the user to hear the interval before answering.

Why it matters:

- encourages deliberate listening
- reduces fast guessing

Suggested implementation:

- disable answers until first playback
- optional stricter mode: require two listens

### 8. Register Control

Let users choose pitch range.

Suggested options:

- low
- middle
- high

Why it matters:

- interval color changes by register
- useful for instrument-specific ear training

### 9. Keyboard Shortcuts

Add fast controls for repeated drilling.

Suggested shortcuts:

- `Space`: replay
- number keys: answer
- `N`: next interval

Why it matters:

- faster practice loop
- better desktop usability

### 10. Daily Practice Mode

Offer a short fixed session.

Suggested format:

- 10, 20, or 30 questions
- summary screen at the end
- streak or completion tracking later

Why it matters:

- helps build a habit
- gives users a clear stopping point

## Best Next Single Feature

If only one feature should be built next, choose:

**Adaptive Practice with Per-Interval Statistics**

Reason:

- strongest learning impact
- builds directly on the current app
- creates a foundation for smarter training modes later

