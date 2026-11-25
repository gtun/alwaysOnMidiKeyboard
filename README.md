# AlwaysOn Keys

A tiny tray app that makes the AKAI MPK mini MK3 playable any time. Press a key and
you hear it. There's no DAW and no setup. In FFXIV the same keys play your bard's
performance mode.

- **Small:** a ~60 KB exe with nothing to install. It uses the .NET Framework that ships
  with Windows.
- **Idle cost is zero:** MIDI input is driven by the driver's callbacks, so the app doesn't
  poll. After 30 s of silence the audio stream stops (it restarts in under 1 ms) and the
  app uses no CPU. A 2.5 s timer checks whether the keyboard has been plugged in or
  unplugged.
- **Low latency:** the app uses Windows' low-latency audio mode where the driver
  supports it. On this PC that's a 2.7 ms engine period with about 6 ms of audio queued.
- **Playing cost is small:** around 0.5 % of one CPU core while holding 7-note chords.
  Keeping the stream running during short pauses costs about 0.4 %.

## Install / run

The built exe is at `%LOCALAPPDATA%\AlwaysOnKeys\AlwaysOnKeys.exe`. Double-click it and
a small keyboard icon appears in the system tray. Turn on **Start with Windows** from its
menu so you can forget about it.

Icon colours: **teal** means connected, **purple** means forced FFXIV mode, **grey**
means the keyboard isn't connected (or is paused or busy).

Click the icon (left or right) to open the menu:

| Menu | What it does |
|---|---|
| Instrument | Pick one of 16 sounds |
| Mode | **Auto** sends FFXIV keys while the game window is focused and plays sound otherwise. You can also force either one. |
| Pause | Releases the keyboard so a DAW can use it. Windows only lets one app open a MIDI input at a time. |
| Start with Windows | Adds or removes the app from your login programs (current user only) |
| Open / Reload settings | Edit `AlwaysOnKeys.ini` (next to the exe), then reload |

Unplugging and replugging the keyboard is handled automatically.

## Instruments

Grand Piano, Electric Piano, Drawbar Organ, String Ensemble, Warm Pad, Brass, Flute,
Nylon Guitar, Harp, Marimba, Vibraphone, Music Box, Crystal Bells, Synth Lead,
Synth Bass, Chiptune. The **pads play a drum kit** (kick, snare, hats, toms, clap,
cymbals).

You can switch instruments from the keyboard too: press **PROG CHANGE**, then a pad
(pads 1–8 select instruments 1–8, or 9–16 on pad bank B).

## Controls (local sound)

| Control | Effect |
|---|---|
| Joystick left/right | Pitch bend ±2 semitones |
| Joystick up | Vibrato |
| Knob 1 | Volume |
| Knob 2 | Tone. Centre is neutral, left is darker, right is brighter. |
| Knob 3 | Reverb amount |
| Knob 4 | Release: how long notes ring after you let go |
| Knob 5 | Compressor. It evens out soft and hard key presses and tames loud peaks. At 0 it's off, halfway is noticeably tighter, and at full every note plays at the same level. |
| Sustain pedal | Sustain |

The MPK's knobs report absolute positions (0–127), and they all read 0 after the
keyboard powers up. The first touch of a knob therefore jumps its setting to where
the knob actually is. The app remembers where you last left each knob
(`KnobValues` in the settings), so your sound is the same after a restart until you
touch a knob again.

## FFXIV mode

FFXIV plays **one note at a time** across 3 octaves (C3–C6). In Auto mode, keys go to
the game whenever the FFXIV window is focused. Open performance mode with your
instrument first, otherwise the key presses act as normal game keys.

| Control | Effect in game |
|---|---|
| Keys | Play notes. The most recent key you pressed wins. Middle C on the keyboard is the game's middle C. Notes outside the game's range are moved up or down by octaves until they fit. |
| **Pad 1 (hold)** | −1 octave, only while held |
| **Pad 2 (hold)** | +1 octave, only while held |
| Joystick left/right | Step bend: re-plays the held note 1–2 semitones down or up and snaps back when you let go |
| Joystick up | Trill between the held note and the note a whole step above. The further you push, the faster it goes. |

### About the Oct − / Oct + buttons

Those two buttons are handled **inside the keyboard itself** and send no MIDI
message. Each press shifts the notes the keyboard sends, and its LED shows the shift.
No app on the PC can tell when they are pressed or released, so they can't be made to
work only while held. That's why **pads 1 and 2** do hold-to-shift instead (pads send a
press and a release). The Oct buttons still work as usual, and in FFXIV mode the app
follows whatever octave they've set. To use different pads, change `OctaveDownPad`
and `OctaveUpPad` in the settings file. The menu's status line shows the last note
number received, which helps you find a pad's number.

### Keybinds

The defaults assume FFXIV's standard one-octave performance keybinds:

```
C  C# D  D# E  F  F# G  G# A  A# B  C
Q  2  W  3  E  R  5  T  6  Y  7  U  I        +1 octave = Shift, -1 octave = Ctrl
```

Check these in-game under **Keybind → Performance** and change `NoteKeys`,
`OctaveUpKey` and `OctaveDownKey` in the settings to match. If you assigned all 37
notes to their own keys, set `Layout=full` and list them in `FullKeys`, from the lowest
C to the highest. Combos like `Ctrl+Q` are allowed.

### Troubleshooting

- **Sound feels late (local mode):** the tray menu's "Audio:" line shows the mode in use.
  If it says `standard` instead of `low-latency`, your audio driver doesn't support the
  smaller period. Bluetooth headphones add 100–200 ms that no app can remove, so use
  wired output for playing. If you hear crackles, raise `BufferMs` in the settings.

- **No notes in game:** if FFXIV runs as administrator, Windows blocks key presses
  from non-admin apps. Run AlwaysOn Keys as administrator too, or stop running the game
  as admin.
- **Fast repeated notes get swallowed:** raise `RetriggerGapMs`, for example to 25.
- **Octave jumps sometimes land in the wrong octave:** raise `ModifierGapMs`, for
  example to 10.
- FFXIV also has its own built-in MIDI input. It can't use the keyboard while this
  app holds it, so use **Pause** if you'd rather use the game's own MIDI support.
- Sending key presses to the game from an outside tool is a grey area under the FFXIV
  terms of service. This app only turns your own live key presses into key presses;
  it doesn't automate anything.

## Building

The app is written in plain C# 5 and compiled with the `csc.exe` that ships with Windows.

- From Windows: run `build.cmd`. The output is `dist\AlwaysOnKeys.exe`.
- From WSL: `./build.sh --install` builds it and copies it to `%LOCALAPPDATA%\AlwaysOnKeys`.
- Self-test: `AlwaysOnKeys.exe --selftest [demo.wav]` checks MIDI and audio, renders
  every instrument offline and writes `selftest.txt`. `--selftest --knobs` measures what
  each knob does.

| Source file | Contents |
|---|---|
| `src/Synth.cs` | Instrument models, drums, reverb |
| `src/Audio.cs` | WASAPI output that sleeps when idle |
| `src/Midi.cs` | winmm MIDI input |
| `src/Ffxiv.cs` | Note-to-keybind mapping, hold-octave pads, step bend and trill |
| `src/App.cs` | Tray menu, routing, self-test |
| `src/Config.cs` | INI settings and log |
