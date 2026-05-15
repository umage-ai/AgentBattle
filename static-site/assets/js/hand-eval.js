// Client-side 7-card Texas Hold'em hand evaluator.
// Port of AgentBattle.Poker.Mcp.Engine.HandEvaluator. Returns {category, categoryName, tiebreak, description}.
// Cards are 2-char strings: "As", "Td", "7h", "2c". Rank chars: 2-9, T, J, Q, K, A. Suit chars: c, d, h, s.
(function (root) {
  const RANK_VAL = { '2': 2, '3': 3, '4': 4, '5': 5, '6': 6, '7': 7, '8': 8, '9': 9, 'T': 10, 'J': 11, 'Q': 12, 'K': 13, 'A': 14 };
  const RANK_NAME = { 2: '2', 3: '3', 4: '4', 5: '5', 6: '6', 7: '7', 8: '8', 9: '9', 10: 'Ten', 11: 'Jack', 12: 'Queen', 13: 'King', 14: 'Ace' };

  const CATEGORIES = [
    'HighCard', 'Pair', 'TwoPair', 'ThreeOfAKind', 'Straight',
    'Flush', 'FullHouse', 'FourOfAKind', 'StraightFlush'
  ];
  const CATEGORY_LABEL = {
    'HighCard': 'High card', 'Pair': 'Pair', 'TwoPair': 'Two pair', 'ThreeOfAKind': 'Three of a kind',
    'Straight': 'Straight', 'Flush': 'Flush', 'FullHouse': 'Full house', 'FourOfAKind': 'Four of a kind',
    'StraightFlush': 'Straight flush'
  };

  function categoryRank(c) { return CATEGORIES.indexOf(c); }

  function parseCard(s) {
    return { rank: RANK_VAL[s[0]], suit: s[1] };
  }

  function combinations(arr, k) {
    const out = [];
    const n = arr.length;
    if (k > n) return out;
    const idx = Array.from({ length: k }, (_, i) => i);
    while (true) {
      out.push(idx.map(i => arr[i]));
      let p = k - 1;
      while (p >= 0 && idx[p] === n - k + p) p--;
      if (p < 0) return out;
      idx[p]++;
      for (let i = p + 1; i < k; i++) idx[i] = idx[i - 1] + 1;
    }
  }

  function isStraight(descRanks) {
    if (new Set(descRanks).size !== 5) return { ok: false };
    // Wheel: A,5,4,3,2 → high=5
    if (descRanks[0] === 14 && descRanks[1] === 5 && descRanks[2] === 4 && descRanks[3] === 3 && descRanks[4] === 2)
      return { ok: true, high: 5 };
    for (let i = 1; i < descRanks.length; i++)
      if (descRanks[i] !== descRanks[i - 1] - 1) return { ok: false };
    return { ok: true, high: descRanks[0] };
  }

  function scoreFive(five) {
    const ranks = five.map(c => c.rank).sort((a, b) => b - a);
    const isFlush = new Set(five.map(c => c.suit)).size === 1;
    const straight = isStraight(ranks);
    const groupsMap = {};
    ranks.forEach(r => groupsMap[r] = (groupsMap[r] || 0) + 1);
    const groups = Object.entries(groupsMap)
      .map(([r, c]) => ({ rank: +r, count: c }))
      .sort((a, b) => b.count - a.count || b.rank - a.rank);

    if (isFlush && straight.ok) return { category: 'StraightFlush', tiebreak: [straight.high], description: `Straight flush, ${RANK_NAME[straight.high]} high` };
    if (groups[0].count === 4) return { category: 'FourOfAKind', tiebreak: [groups[0].rank, groups[1].rank], description: `Four of a kind, ${RANK_NAME[groups[0].rank]}s` };
    if (groups[0].count === 3 && groups[1].count === 2) return { category: 'FullHouse', tiebreak: [groups[0].rank, groups[1].rank], description: `Full house, ${RANK_NAME[groups[0].rank]}s over ${RANK_NAME[groups[1].rank]}s` };
    if (isFlush) return { category: 'Flush', tiebreak: ranks.slice(), description: `Flush, ${RANK_NAME[ranks[0]]} high` };
    if (straight.ok) return { category: 'Straight', tiebreak: [straight.high], description: `Straight, ${RANK_NAME[straight.high]} high` };
    if (groups[0].count === 3) return { category: 'ThreeOfAKind', tiebreak: [groups[0].rank, groups[1].rank, groups[2].rank], description: `Three of a kind, ${RANK_NAME[groups[0].rank]}s` };
    if (groups[0].count === 2 && groups[1].count === 2) return { category: 'TwoPair', tiebreak: [groups[0].rank, groups[1].rank, groups[2].rank], description: `Two pair, ${RANK_NAME[groups[0].rank]}s and ${RANK_NAME[groups[1].rank]}s` };
    if (groups[0].count === 2) return { category: 'Pair', tiebreak: [groups[0].rank, groups[1].rank, groups[2].rank, groups[3].rank], description: `Pair of ${RANK_NAME[groups[0].rank]}s` };
    return { category: 'HighCard', tiebreak: ranks.slice(), description: `${RANK_NAME[ranks[0]]} high` };
  }

  function compare(a, b) {
    const dc = categoryRank(a.category) - categoryRank(b.category);
    if (dc !== 0) return dc;
    const len = Math.min(a.tiebreak.length, b.tiebreak.length);
    for (let i = 0; i < len; i++) {
      const diff = a.tiebreak[i] - b.tiebreak[i];
      if (diff !== 0) return diff;
    }
    return 0;
  }

  function evaluate(cardStrings) {
    if (!cardStrings || cardStrings.length < 5) {
      // Pre-flop / partial board — describe the hole-card situation.
      return preflopSummary(cardStrings || []);
    }
    const cards = cardStrings.map(parseCard);
    const combos = combinations(cards, 5);
    let best = null;
    for (const c of combos) {
      const s = scoreFive(c);
      if (best === null || compare(s, best) > 0) best = s;
    }
    return {
      category: best.category,
      categoryName: CATEGORY_LABEL[best.category],
      tiebreak: best.tiebreak,
      description: best.description
    };
  }

  function preflopSummary(cardStrings) {
    if (cardStrings.length !== 2) {
      return { category: null, categoryName: '—', tiebreak: [], description: 'incomplete board' };
    }
    const a = parseCard(cardStrings[0]);
    const b = parseCard(cardStrings[1]);
    const high = Math.max(a.rank, b.rank);
    const low = Math.min(a.rank, b.rank);
    const suited = a.suit === b.suit ? 's' : 'o';
    const label = a.rank === b.rank
      ? `Pair of ${RANK_NAME[a.rank]}s`
      : `${RANK_NAME[high]}–${RANK_NAME[low]} ${suited === 's' ? 'suited' : 'offsuit'}`;
    return { category: null, categoryName: 'Preflop', tiebreak: [high, low], description: label };
  }

  root.HandEval = { evaluate, compare };
})(window);
