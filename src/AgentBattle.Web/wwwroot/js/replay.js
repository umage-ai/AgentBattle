function replay(battleId) {
  return {
    battleId,
    events: [],
    idx: 0,
    playing: false,
    speed: 1,
    godView: true,
    loaded: false,

    seats: [],
    community: [],
    pot: 0,
    currentSeat: null,
    holeCards: {},
    lastThoughts: {},
    revealedSeats: new Set(),
    _timer: null,

    async load() {
      const res = await fetch(`/Battles/Replay/${this.battleId}?handler=events`);
      this.events = await res.json();
      this.rebuild();
      this.loaded = true;
    },

    togglePlay() {
      this.playing = !this.playing;
      if (this.playing) this._tick();
      else clearTimeout(this._timer);
    },
    _tick() {
      if (!this.playing) return;
      if (this.idx < this.events.length - 1) {
        this.idx++;
        this.rebuild();
        this._timer = setTimeout(() => this._tick(), 800 / this.speed);
      } else {
        this.playing = false;
      }
    },
    prev() { if (this.idx > 0) { this.idx--; this.rebuild(); } },
    next() { if (this.idx < this.events.length - 1) { this.idx++; this.rebuild(); } },

    rebuild() {
      // Re-derive all visible state from events[0..idx].
      this.seats = [];
      this.community = [];
      this.pot = 0;
      this.currentSeat = null;
      this.holeCards = {};
      this.lastThoughts = {};
      this.revealedSeats = new Set();

      for (let i = 0; i <= this.idx && i < this.events.length; i++) {
        const e = this.events[i];
        this._applyEvent(e);
      }
    },

    _applyEvent(e) {
      switch (e.t) {
        case 'battle_started':
          this.seats = e.agents.map(a => ({
            seat: a.seat,
            displayName: a.display_name,
            stack: 1000,
            currentBet: 0,
            hasFolded: false
          }));
          break;
        case 'hand_started':
          this.community = [];
          this.pot = 0;
          this.holeCards = {};
          this.revealedSeats = new Set();
          this.seats.forEach(s => { s.currentBet = 0; s.hasFolded = false; });
          break;
        case 'hole_cards_dealt':
          (e.deals || []).forEach(d => this.holeCards[d.seat] = d.cards);
          break;
        case 'community_dealt':
          (e.cards || []).forEach(c => this.community.push(c));
          break;
        case 'agent_turn_started': {
          this.currentSeat = e.seat;
          const snap = e.state_snapshot;
          if (snap && snap.seats) {
            snap.seats.forEach(s => {
              const local = this.seats.find(x => x.seat === s.seat);
              if (local) {
                local.stack = s.stack;
                local.currentBet = s.current_bet;
                local.hasFolded = s.has_folded;
              }
            });
          }
          if (snap) this.pot = snap.pot;
          break;
        }
        case 'agent_thoughts':
          this.lastThoughts[e.seat] = { handNo: e.hand_no, text: e.text };
          break;
        case 'agent_action':
          if (e.action === 'fold') {
            const s = this.seats.find(x => x.seat === e.seat);
            if (s) s.hasFolded = true;
          }
          break;
        case 'showdown':
          (e.reveals || []).forEach(r => this.revealedSeats.add(r.seat));
          break;
        case 'hand_ended':
          for (const [seat, chips] of Object.entries(e.stacks)) {
            const local = this.seats.find(x => x.seat === Number(seat));
            if (local) local.stack = chips;
          }
          break;
        case 'battle_ended':
          // No further state changes; final stacks already captured by last hand_ended.
          break;
      }
    },

    cardsForSeat(seat) {
      const cards = this.holeCards[seat];
      if (!cards) return [];
      if (this.godView) return cards;
      if (this.revealedSeats.has(seat)) return cards;
      return ['??', '??'];
    },

    describeCurrent() {
      const e = this.events[this.idx];
      if (!e) return '';
      return `event ${this.idx + 1}/${this.events.length}: ${e.t}`;
    }
  };
}
