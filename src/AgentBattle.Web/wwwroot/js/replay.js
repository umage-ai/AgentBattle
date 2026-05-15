function replay(battleId) {
  return {
    battleId,
    events: [],
    idx: 0,
    playing: false,
    speed: 1,
    godView: true,
    showStrengths: true,
    sound: true,
    loaded: false,

    seats: [],
    community: [],
    pot: 0,
    currentSeat: null,
    holeCards: {},
    revealedSeats: new Set(),

    // Hand-level meta
    buttonSeat: null,
    sbSeat: null,
    bbSeat: null,

    // Streaming reasoning entries (one per agent_thoughts event, in order seen)
    thoughtsStream: [],

    // Visual transient state
    toast: { visible: false, kind: '', icon: '', who: '', text: '', amount: '' },
    _toastTimer: null,
    flashSeat: null,
    _flashTimer: null,
    potBumping: false,
    _potTimer: null,
    winnerSeats: [],
    _winnerTimer: null,

    // Stack deltas for hand_ended
    _lastStacks: {},

    _timer: null,

    async load() {
      const res = await fetch(`/Battles/Replay/${this.battleId}?handler=events`);
      this.events = await res.json();
      this.rebuild(true);
      this.loaded = true;
    },

    togglePlay() {
      if (window.Sfx) Sfx.resume();
      this.playing = !this.playing;
      if (this.playing) this._tick();
      else clearTimeout(this._timer);
    },
    _tick() {
      if (!this.playing) return;
      if (this.idx < this.events.length - 1) {
        this.idx++;
        this._stepForward(this.events[this.idx]);
        this._timer = setTimeout(() => this._tick(), 700 / this.speed);
      } else {
        this.playing = false;
      }
    },
    prev() {
      if (this.idx > 0) { this.idx--; this.rebuild(true); }
    },
    next() {
      if (this.idx < this.events.length - 1) {
        this.idx++;
        this._stepForward(this.events[this.idx]);
      }
    },

    // Full re-derive from scratch. Used by scrubbing and prev.
    rebuild(silent) {
      this.seats = [];
      this.community = [];
      this.pot = 0;
      this.currentSeat = null;
      this.holeCards = {};
      this.revealedSeats = new Set();
      this.thoughtsStream = [];
      this.buttonSeat = this.sbSeat = this.bbSeat = null;
      this.winnerSeats = [];
      this._lastStacks = {};

      for (let i = 0; i <= this.idx && i < this.events.length; i++) {
        this._applyEvent(this.events[i], /*silent*/ true);
      }
      if (!silent) this._playSoundFor(this.events[this.idx]);
    },

    // Forward step from play/next — drive animations & sounds.
    _stepForward(e) {
      this._applyEvent(e, false);
      this._playSoundFor(e);
    },

    _applyEvent(e, silent) {
      switch (e.t) {
        case 'battle_started':
          this.seats = e.agents.map(a => ({
            seat: a.seat,
            displayName: a.display_name,
            agentId: a.id,
            stack: 1000,
            currentBet: 0,
            hasFolded: false,
            delta: 0
          }));
          break;

        case 'hand_started':
          this.community = [];
          this.pot = 0;
          this.holeCards = {};
          this.revealedSeats = new Set();
          this.buttonSeat = e.button_seat;
          this.sbSeat = e.sb_seat;
          this.bbSeat = e.bb_seat;
          this.winnerSeats = [];
          this.seats.forEach(s => { s.currentBet = 0; s.hasFolded = false; s.delta = 0; });
          this._lastStacks = Object.fromEntries(this.seats.map(s => [s.seat, s.stack]));
          break;

        case 'hole_cards_dealt':
          (e.deals || []).forEach(d => this.holeCards[d.seat] = d.cards);
          break;

        case 'community_dealt':
          (e.cards || []).forEach(c => this.community.push(c));
          if (!silent) this._bumpPot();
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

        case 'agent_thoughts': {
          // Append-or-update entry keyed by (handNo, seat, attempt) so re-derives don't duplicate.
          const seatObj = this.seats.find(s => s.seat === e.seat);
          const author = seatObj?.displayName ?? `Seat ${e.seat}`;
          const id = `${e.hand_no}-${e.seat}-${e.attempt ?? 0}-${this.idx}-${e.ts}`;
          this.thoughtsStream.push({
            id,
            seat: e.seat,
            handNo: e.hand_no,
            author,
            street: this._streetForCurrent(),
            text: (e.text || '').trim(),
            action: null
          });
          break;
        }

        case 'agent_action': {
          // Attach action to the most recent thought from the same seat for this hand.
          const t = [...this.thoughtsStream].reverse()
            .find(x => x.seat === e.seat && x.handNo === e.hand_no);
          if (t) t.action = this._actionSummary(e);
          if (e.action === 'fold') {
            const s = this.seats.find(x => x.seat === e.seat);
            if (s) s.hasFolded = true;
          }
          if (!silent) {
            this._flashSeat(e.seat);
            this._showToast(e);
            if (['raise', 'call', 'post'].includes(e.action) || e.amount) this._bumpPot();
          }
          break;
        }

        case 'agent_note': {
          const seatObj = this.seats.find(s => s.seat === e.seat);
          const author = seatObj?.displayName ?? `Seat ${e.seat}`;
          const id = `note-${e.hand_no}-${e.seat}-${e.ts}`;
          this.thoughtsStream.push({
            id,
            seat: e.seat,
            handNo: e.hand_no,
            author,
            street: 'note',
            text: '📝 ' + (e.text || '').trim(),
            action: { label: 'private note', verb: '', kind: 'note', amount: '' }
          });
          break;
        }

        case 'agent_action_rejected':
          // Show a brief retry note next to the latest thought for that seat.
          const tr = [...this.thoughtsStream].reverse()
            .find(x => x.seat === e.seat && x.handNo === e.hand_no);
          if (tr && !tr.action) tr.action = { label: 'rejected', kind: 'fold', verb: `(retry: ${e.reason})`, amount: '' };
          break;

        case 'showdown':
          (e.reveals || []).forEach(r => {
            this.revealedSeats.add(r.seat);
            this.holeCards[r.seat] = r.cards || this.holeCards[r.seat];
          });
          break;

        case 'hand_ended': {
          const stacks = e.stacks || {};
          // Compute delta vs hand-start so we can show floating +/- chips.
          const winners = [];
          for (const [seatStr, chips] of Object.entries(stacks)) {
            const seat = Number(seatStr);
            const local = this.seats.find(x => x.seat === seat);
            if (!local) continue;
            const before = this._lastStacks[seat] ?? local.stack;
            const d = chips - before;
            local.stack = chips;
            local.delta = d;
            if (d > 0) winners.push(seat);
          }
          this.currentSeat = null;
          if (!silent && winners.length > 0) {
            this._highlightWinners(winners);
          }
          break;
        }

        case 'battle_ended':
          this.currentSeat = null;
          if (!silent) this._highlightBattleWinner(e);
          break;
      }
    },

    _streetForCurrent() {
      if (this.community.length === 0) return 'preflop';
      if (this.community.length === 3) return 'flop';
      if (this.community.length === 4) return 'turn';
      if (this.community.length === 5) return 'river';
      return '—';
    },

    _actionSummary(e) {
      const map = {
        fold:   { label: 'Folds.',  verb: 'FOLD',  kind: 'fold' },
        check:  { label: 'Checks.', verb: 'CHECK', kind: 'check' },
        call:   { label: 'Calls.',  verb: 'CALL',  kind: 'call' },
        raise:  { label: 'Raises.', verb: 'RAISE', kind: 'raise' },
        all_in: { label: 'All-in!', verb: 'ALL-IN', kind: 'all_in' },
        post:   { label: 'Posts blind.', verb: 'POST', kind: 'call' }
      };
      const m = map[e.action] || { label: e.action, verb: e.action.toUpperCase(), kind: 'call' };
      return { ...m, amount: e.amount ?? '' };
    },

    _showToast(e) {
      const seat = this.seats.find(s => s.seat === e.seat);
      const who = seat ? seat.displayName : `Seat ${e.seat}`;
      const icons = { fold: '✕', call: '→', check: '✓', raise: '↑', all_in: '★', post: '·' };
      const verbs = { fold: 'folds', call: 'calls', check: 'checks', raise: 'raises to', all_in: 'all-in', post: 'posts' };
      this.toast = {
        visible: true,
        kind: e.action,
        icon: icons[e.action] || '·',
        who,
        text: verbs[e.action] || e.action,
        amount: e.amount ? String(e.amount) : ''
      };
      clearTimeout(this._toastTimer);
      this._toastTimer = setTimeout(() => { this.toast.visible = false; }, Math.max(700, 1400 / this.speed));
    },

    _flashSeat(seat) {
      this.flashSeat = seat;
      clearTimeout(this._flashTimer);
      this._flashTimer = setTimeout(() => { this.flashSeat = null; }, 700);
    },

    _bumpPot() {
      this.potBumping = true;
      clearTimeout(this._potTimer);
      this._potTimer = setTimeout(() => { this.potBumping = false; }, 360);
    },

    _highlightWinners(seats) {
      this.winnerSeats = seats;
      clearTimeout(this._winnerTimer);
      this._winnerTimer = setTimeout(() => { this.winnerSeats = []; }, 1600);
    },

    _highlightBattleWinner(e) {
      // BattleEnded ranking is sorted by chips descending in the runtime, but be safe.
      const ranking = e.ranking || [];
      if (ranking.length === 0) return;
      const top = ranking.reduce((a, b) => (b.chips > a.chips ? b : a));
      // Find which seat this agent occupies.
      const seat = this.seats.find(s => s.agentId === top.agent_id || s.displayName === top.agent_id);
      if (seat) this._highlightWinners([seat.seat]);
    },

    _playSoundFor(e) {
      if (!this.sound || !window.Sfx) return;
      Sfx.enabled = this.sound;
      Sfx.resume();
      switch (e?.t) {
        case 'hand_started':      Sfx.handStart(); break;
        case 'hole_cards_dealt':  (e.deals || []).forEach((_, i) => setTimeout(() => Sfx.deal(), i * 60)); break;
        case 'community_dealt':   (e.cards || []).forEach((_, i) => setTimeout(() => Sfx.deal(), i * 90)); break;
        case 'agent_action':
          if (e.action === 'fold')                       Sfx.fold();
          else if (e.action === 'check')                 Sfx.check();
          else if (e.action === 'call' || e.action === 'post') Sfx.chip();
          else if (e.action === 'raise')                 Sfx.raise();
          else if (e.action === 'all_in')                Sfx.allIn();
          break;
        case 'showdown':          Sfx.showdown(); break;
        case 'battle_ended':      Sfx.victory(); break;
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
      return `${this.idx + 1}/${this.events.length} · ${e.t}`;
    },

    currentSeatName() {
      if (this.currentSeat == null) return '';
      const s = this.seats.find(x => x.seat === this.currentSeat);
      return s ? s.displayName : '';
    },

    positionTag(seat) {
      if (seat === this.buttonSeat) return 'btn';
      if (seat === this.sbSeat && this.sbSeat !== this.buttonSeat) return 'sb';
      if (seat === this.bbSeat && this.bbSeat !== this.buttonSeat && this.bbSeat !== this.sbSeat) return 'bb';
      return '';
    },

    suitSymbol(s) {
      switch (s) {
        case 's': return '♠';
        case 'h': return '♥';
        case 'd': return '♦';
        case 'c': return '♣';
        default: return '';
      }
    },

    displayRank(r) {
      if (r === 'T') return '10';
      if (r === '?') return '';
      return r;
    },

    _strengthsForVisibleSeats() {
      if (!window.HandEval) return [];
      const out = [];
      for (const seat of this.seats) {
        const cards = this.holeCards[seat.seat];
        if (!cards || cards.length < 2) continue;
        const visible = this.godView || this.revealedSeats.has(seat.seat);
        if (!visible) continue;
        const all = [...cards, ...this.community];
        const ev = window.HandEval.evaluate(all);
        out.push({
          seat: seat.seat,
          displayName: seat.displayName,
          category: ev.category,
          categoryName: ev.categoryName,
          description: ev.description,
          tiebreak: ev.tiebreak,
          folded: seat.hasFolded,
          isLeader: false
        });
      }
      out.sort((a, b) => {
        if (a.folded !== b.folded) return a.folded ? 1 : -1;
        return -window.HandEval.compare(a, b);
      });
      if (out.length > 0 && !out[0].folded) out[0].isLeader = true;
      return out;
    },

    get rankedStrengths() {
      return this._strengthsForVisibleSeats();
    },

    strengthFor(seatId) {
      const all = this._strengthsForVisibleSeats();
      const s = all.find(x => x.seat === seatId);
      if (!s) return null;
      return { label: s.description, isLeader: s.isLeader };
    }
  };
}
