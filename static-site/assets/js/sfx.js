// Lightweight, synth-only sound effects via Web Audio API.
// No asset files — every sound is generated on the fly so the page stays small.
(function () {
  const Sfx = {
    enabled: true,
    _ctx: null,
    _lastPlay: 0,

    _ensure() {
      if (this._ctx) return this._ctx;
      const AC = window.AudioContext || window.webkitAudioContext;
      if (!AC) return null;
      this._ctx = new AC();
      return this._ctx;
    },

    // Tiny gain ramp envelope to avoid clicks. duration in seconds.
    _envelope(gain, peak, attack, hold, release) {
      const ctx = this._ctx;
      const t0 = ctx.currentTime;
      gain.gain.setValueAtTime(0.0001, t0);
      gain.gain.exponentialRampToValueAtTime(peak, t0 + attack);
      gain.gain.setValueAtTime(peak, t0 + attack + hold);
      gain.gain.exponentialRampToValueAtTime(0.0001, t0 + attack + hold + release);
      return t0 + attack + hold + release;
    },

    _tone({ freq = 440, type = 'sine', peak = 0.15, attack = 0.005, hold = 0.04, release = 0.08, sweepTo = null }) {
      const ctx = this._ensure();
      if (!ctx || !this.enabled) return;
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.type = type;
      osc.frequency.setValueAtTime(freq, ctx.currentTime);
      if (sweepTo != null) {
        osc.frequency.exponentialRampToValueAtTime(sweepTo, ctx.currentTime + attack + hold + release);
      }
      osc.connect(gain).connect(ctx.destination);
      const stop = this._envelope(gain, peak, attack, hold, release);
      osc.start();
      osc.stop(stop + 0.02);
    },

    _noise({ peak = 0.12, attack = 0.002, hold = 0.02, release = 0.06, lowpass = 4000 }) {
      const ctx = this._ensure();
      if (!ctx || !this.enabled) return;
      const buffer = ctx.createBuffer(1, ctx.sampleRate * 0.2, ctx.sampleRate);
      const data = buffer.getChannelData(0);
      for (let i = 0; i < data.length; i++) data[i] = Math.random() * 2 - 1;
      const src = ctx.createBufferSource();
      src.buffer = buffer;
      const filter = ctx.createBiquadFilter();
      filter.type = 'lowpass';
      filter.frequency.value = lowpass;
      const gain = ctx.createGain();
      src.connect(filter).connect(gain).connect(ctx.destination);
      const stop = this._envelope(gain, peak, attack, hold, release);
      src.start();
      src.stop(stop + 0.02);
    },

    // Public API ------------------------------------------------------

    // Browsers require a user gesture to resume audio. Wire this to first click.
    resume() {
      const ctx = this._ensure();
      if (ctx && ctx.state === 'suspended') ctx.resume();
    },

    // Soft riffle of a card sliding off the deck.
    deal() {
      this._noise({ peak: 0.08, hold: 0.01, release: 0.05, lowpass: 3200 });
      this._tone({ freq: 1800, type: 'triangle', peak: 0.04, hold: 0.005, release: 0.05, sweepTo: 900 });
    },

    // Chip clack — short noise burst + ping.
    chip() {
      this._noise({ peak: 0.10, hold: 0.008, release: 0.04, lowpass: 2200 });
      this._tone({ freq: 2600, type: 'square', peak: 0.05, hold: 0.005, release: 0.03 });
    },

    // Several chips for a bigger bet.
    chipStack(count = 3) {
      const n = Math.max(1, Math.min(5, count));
      for (let i = 0; i < n; i++) {
        setTimeout(() => this.chip(), i * 65);
      }
    },

    // Quiet click for check.
    check() {
      this._tone({ freq: 380, type: 'sine', peak: 0.08, hold: 0.01, release: 0.05 });
    },

    // Soft thud + slide for fold.
    fold() {
      this._noise({ peak: 0.10, hold: 0.02, release: 0.12, lowpass: 800 });
      this._tone({ freq: 240, type: 'sine', peak: 0.06, hold: 0.04, release: 0.12, sweepTo: 140 });
    },

    // Big sweep for all-in / raise.
    raise() {
      this._tone({ freq: 320, type: 'sawtooth', peak: 0.08, hold: 0.04, release: 0.16, sweepTo: 720 });
      setTimeout(() => this.chipStack(3), 60);
    },

    allIn() {
      this._tone({ freq: 220, type: 'square', peak: 0.10, hold: 0.05, release: 0.22, sweepTo: 920 });
      setTimeout(() => this.chipStack(5), 50);
    },

    // Triumphant ping for showdown.
    showdown() {
      const seq = [523.25, 659.25, 783.99]; // C5, E5, G5
      seq.forEach((f, i) => setTimeout(() => {
        this._tone({ freq: f, type: 'triangle', peak: 0.10, hold: 0.06, release: 0.18 });
      }, i * 110));
    },

    // Long major triad for the battle winner.
    victory() {
      const seq = [523.25, 659.25, 783.99, 1046.5];
      seq.forEach((f, i) => setTimeout(() => {
        this._tone({ freq: f, type: 'triangle', peak: 0.12, hold: 0.10, release: 0.30 });
      }, i * 130));
    },

    // Hand starting — quick rising chime.
    handStart() {
      this._tone({ freq: 660, type: 'sine', peak: 0.06, hold: 0.02, release: 0.10, sweepTo: 1100 });
    }
  };

  window.Sfx = Sfx;
})();
