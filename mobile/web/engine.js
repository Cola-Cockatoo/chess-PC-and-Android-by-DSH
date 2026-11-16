/*
 * 国际象棋核心（手机版）—— 由桌面版 C# 引擎逐段移植而来
 *
 * 与桌面版保持一致的实现：
 *   - 0x88 棋盘表示、Zobrist 双 32 位哈希、增量评估
 *   - 伪合法走法生成 + 试走判合法、王车易位、吃过路兵、兵升变
 *   - Alpha-Beta + 静态搜索 + 置换表 + 空着裁剪 + 杀手着法 + LMR
 *   - ELO 100~2000 共 20 档难度（深度/时间/失误率/候选窗口）
 *   - AI 解析：着法分级（!! ! !? ?! ? ??）、胜率换算、局面分析
 *
 * 走法用整数打包，避免搜索中产生大量对象：
 *   from(8) | to(8)<<8 | promo(4)<<16 | flags(4)<<20
 */
(function (root, factory) {
    var api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    if (root) root.Chess = api;
})(typeof self !== 'undefined' ? self : (typeof globalThis !== 'undefined' ? globalThis : this), function () {
    'use strict';

    // ------------------------------------------------------------------
    // 常量与工具
    // ------------------------------------------------------------------
    var PT = { None: 0, Pawn: 1, Knight: 2, Bishop: 3, Rook: 4, Queen: 5, King: 6 };
    var MF = { None: 0, EnPassant: 1, Castle: 2, DoublePush: 4 };
    var CR = { WK: 1, WQ: 2, BK: 4, BQ: 8 };
    var WHITE = 1, BLACK = 2;

    var KNIGHT_OFF = [33, 31, 18, 14, -33, -31, -18, -14];
    var BISHOP_OFF = [17, 15, -17, -15];
    var ROOK_OFF = [16, -16, 1, -1];
    var KING_OFF = [17, 16, 15, 1, -1, -15, -16, -17];

    function typeOf(p) { return p & 7; }
    function colorOf(p) { return p >> 3; }
    function mkPiece(type, color) { return type | (color << 3); }
    function opp(c) { return c === WHITE ? BLACK : WHITE; }
    function onBoard(s) { return (s & 0x88) === 0; }
    function rankOf(s) { return s >> 4; }
    function fileOf(s) { return s & 7; }
    function sqIndex(f, r) { return r * 16 + f; }
    function sqName(s) { return String.fromCharCode(97 + fileOf(s)) + (rankOf(s) + 1); }
    function parseSq(str) { return sqIndex(str.charCodeAt(0) - 97, str.charCodeAt(1) - 49); }

    function MV(from, to, promo, flags) {
        return (from | (to << 8) | ((promo || 0) << 16) | ((flags || 0) << 20)) | 0;
    }
    function mvFrom(m) { return m & 0xFF; }
    function mvTo(m) { return (m >> 8) & 0xFF; }
    function mvPromo(m) { return (m >> 16) & 0xF; }
    function mvFlags(m) { return (m >> 20) & 0xF; }
    var PROMO_LETTER = { 2: 'n', 3: 'b', 4: 'r', 5: 'q' };
    function moveToUci(m) {
        return sqName(mvFrom(m)) + sqName(mvTo(m)) + (mvPromo(m) ? PROMO_LETTER[mvPromo(m)] : '');
    }
    function sameMove(a, b) {
        return a === b || (mvFrom(a) === mvFrom(b) && mvTo(a) === mvTo(b) && mvPromo(a) === mvPromo(b));
    }

    var START_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

    // ------------------------------------------------------------------
    // Zobrist（双 32 位，避免 BigInt）
    // ------------------------------------------------------------------
    var ZP_LO = new Int32Array(128 * 24), ZP_HI = new Int32Array(128 * 24);
    var ZC_LO = new Int32Array(16), ZC_HI = new Int32Array(16);
    var ZE_LO = new Int32Array(128), ZE_HI = new Int32Array(128);
    var ZS_LO = 0, ZS_HI = 0;

    (function initZobrist() {
        var s = 20240517 >>> 0;
        function rnd() {
            s ^= s << 13; s >>>= 0;
            s ^= s >>> 17;
            s ^= s << 5; s >>>= 0;
            return s | 0;
        }
        for (var i = 0; i < 128; i++) {
            for (var p = 1; p < 24; p++) {
                ZP_LO[i * 24 + p] = rnd();
                ZP_HI[i * 24 + p] = rnd();
            }
        }
        for (var c = 1; c < 16; c++) { ZC_LO[c] = rnd(); ZC_HI[c] = rnd(); }
        for (var e = 0; e < 128; e++) { ZE_LO[e] = rnd(); ZE_HI[e] = rnd(); }
        ZS_LO = rnd(); ZS_HI = rnd();
    })();

    // ------------------------------------------------------------------
    // 评估：子力价值 + 位置表 + 兵型 + 双象 + 王的阶段插值
    // ------------------------------------------------------------------
    var PIECE_VALUE = [0, 100, 320, 335, 500, 950, 0];

    var PST_PAWN = [
        0, 0, 0, 0, 0, 0, 0, 0,
        50, 50, 50, 50, 50, 50, 50, 50,
        10, 10, 20, 30, 30, 20, 10, 10,
        5, 5, 10, 25, 25, 10, 5, 5,
        0, 0, 0, 20, 20, 0, 0, 0,
        5, -5, -10, 0, 0, -10, -5, 5,
        5, 10, 10, -20, -20, 10, 10, 5,
        0, 0, 0, 0, 0, 0, 0, 0];

    var PST_KNIGHT = [
        -50, -40, -30, -30, -30, -30, -40, -50,
        -40, -20, 0, 0, 0, 0, -20, -40,
        -30, 0, 10, 15, 15, 10, 0, -30,
        -30, 5, 15, 20, 20, 15, 5, -30,
        -30, 0, 15, 20, 20, 15, 0, -30,
        -30, 5, 10, 15, 15, 10, 5, -30,
        -40, -20, 0, 5, 5, 0, -20, -40,
        -50, -40, -30, -30, -30, -30, -40, -50];

    var PST_BISHOP = [
        -20, -10, -10, -10, -10, -10, -10, -20,
        -10, 0, 0, 0, 0, 0, 0, -10,
        -10, 0, 5, 10, 10, 5, 0, -10,
        -10, 5, 5, 10, 10, 5, 5, -10,
        -10, 0, 10, 10, 10, 10, 0, -10,
        -10, 10, 10, 10, 10, 10, 10, -10,
        -10, 5, 0, 0, 0, 0, 5, -10,
        -20, -10, -10, -10, -10, -10, -10, -20];

    var PST_ROOK = [
        0, 0, 0, 0, 0, 0, 0, 0,
        5, 10, 10, 10, 10, 10, 10, 5,
        -5, 0, 0, 0, 0, 0, 0, -5,
        -5, 0, 0, 0, 0, 0, 0, -5,
        -5, 0, 0, 0, 0, 0, 0, -5,
        -5, 0, 0, 0, 0, 0, 0, -5,
        -5, 0, 0, 0, 0, 0, 0, -5,
        0, 0, 0, 5, 5, 0, 0, 0];

    var PST_QUEEN = [
        -20, -10, -10, -5, -5, -10, -10, -20,
        -10, 0, 0, 0, 0, 0, 0, -10,
        -10, 0, 5, 5, 5, 5, 0, -10,
        -5, 0, 5, 5, 5, 5, 0, -5,
        0, 0, 5, 5, 5, 5, 0, -5,
        -10, 5, 5, 5, 5, 5, 0, -10,
        -10, 0, 5, 0, 0, 0, 0, -10,
        -20, -10, -10, -5, -5, -10, -10, -20];

    var PST_KING_MID = [
        -30, -40, -40, -50, -50, -40, -40, -30,
        -30, -40, -40, -50, -50, -40, -40, -30,
        -30, -40, -40, -50, -50, -40, -40, -30,
        -30, -40, -40, -50, -50, -40, -40, -30,
        -20, -30, -30, -40, -40, -30, -30, -20,
        -10, -20, -20, -20, -20, -20, -20, -10,
        20, 20, 0, 0, 0, 0, 20, 20,
        20, 30, 10, 0, 0, 10, 30, 20];

    var PST_KING_END = [
        -50, -40, -30, -20, -20, -30, -40, -50,
        -30, -20, -10, 0, 0, -10, -20, -30,
        -30, -10, 20, 30, 30, 20, -10, -30,
        -30, -10, 30, 40, 40, 30, -10, -30,
        -30, -10, 30, 40, 40, 30, -10, -30,
        -30, -10, 20, 30, 30, 20, -10, -30,
        -30, -30, 0, 0, 0, 0, -30, -30,
        -50, -30, -30, -30, -30, -30, -30, -50];

    var MATE_SCORE = 30000;
    var MATE_THRESHOLD = 29000;
    var INFINITY_SCORE = 100000;

    function pstIndex(sq, color) {
        var r = rankOf(sq), f = fileOf(sq);
        return color === WHITE ? (7 - r) * 8 + f : r * 8 + f;
    }

    function pstValue(type, sq, color) {
        var i = pstIndex(sq, color);
        switch (type) {
            case PT.Pawn: return PST_PAWN[i];
            case PT.Knight: return PST_KNIGHT[i];
            case PT.Bishop: return PST_BISHOP[i];
            case PT.Rook: return PST_ROOK[i];
            case PT.Queen: return PST_QUEEN[i];
        }
        return 0;
    }

    function kingPst(sq, color, phase) {
        var i = pstIndex(sq, color);
        var mid = PST_KING_MID[i], end = PST_KING_END[i];
        if (phase > 24) phase = 24;
        if (phase < 0) phase = 0;
        return ((mid * phase + end * (24 - phase)) / 24) | 0;
    }

    // ------------------------------------------------------------------
    // 局面
    // ------------------------------------------------------------------
    function Board() {
        this.sq = new Int8Array(128);
        this.matPst = new Int32Array(3);
        this.kingSq = new Int32Array(3);
        this.pieceCount = new Int32Array(24);
        this.pawnFile = new Int32Array(24);
        this.undoStack = new Int32Array(10 * 1024);
        this.nullStack = new Int32Array(4 * 128);
        this.clear();
    }

    Board.prototype.clear = function () {
        this.sq.fill(0);
        this.matPst.fill(0);
        this.kingSq.fill(0);
        this.pieceCount.fill(0);
        this.pawnFile.fill(0);
        this.phase = 0;
        this.side = WHITE;
        this.castling = 0;
        this.ep = -1;
        this.halfmove = 0;
        this.fullmove = 1;
        this.keyLo = 0; this.keyHi = 0;
        this.stackTop = 0;
        this.nullTop = 0;
    };

    Board.prototype.clone = function () {
        var b = Object.create(Board.prototype);
        b.sq = new Int8Array(this.sq);
        b.matPst = new Int32Array(this.matPst);
        b.kingSq = new Int32Array(this.kingSq);
        b.pieceCount = new Int32Array(this.pieceCount);
        b.pawnFile = new Int32Array(this.pawnFile);
        b.undoStack = new Int32Array(10 * 1024);
        b.nullStack = new Int32Array(4 * 128);
        b.phase = this.phase;
        b.side = this.side;
        b.castling = this.castling;
        b.ep = this.ep;
        b.halfmove = this.halfmove;
        b.fullmove = this.fullmove;
        b.keyLo = this.keyLo; b.keyHi = this.keyHi;
        b.stackTop = 0;
        b.nullTop = 0;
        return b;
    };

    Board.prototype.addPiece = function (s, p) {
        this.sq[s] = p;
        var i = s * 24 + p;
        this.keyLo ^= ZP_LO[i]; this.keyHi ^= ZP_HI[i];
        var t = typeOf(p), c = colorOf(p);
        this.pieceCount[p]++;
        if (t === PT.King) {
            this.kingSq[c] = s;
        } else {
            this.matPst[c] += PIECE_VALUE[t] + pstValue(t, s, c);
            if (t === PT.Knight || t === PT.Bishop) this.phase += 1;
            else if (t === PT.Rook) this.phase += 2;
            else if (t === PT.Queen) this.phase += 4;
        }
        if (t === PT.Pawn) this.pawnFile[(c << 3) | fileOf(s)]++;
    };

    Board.prototype.removePiece = function (s) {
        var p = this.sq[s];
        if (p === 0) return;
        var i = s * 24 + p;
        this.keyLo ^= ZP_LO[i]; this.keyHi ^= ZP_HI[i];
        var t = typeOf(p), c = colorOf(p);
        this.pieceCount[p]--;
        if (t === PT.King) {
            this.kingSq[c] = 0;
        } else {
            this.matPst[c] -= PIECE_VALUE[t] + pstValue(t, s, c);
            if (t === PT.Knight || t === PT.Bishop) this.phase -= 1;
            else if (t === PT.Rook) this.phase -= 2;
            else if (t === PT.Queen) this.phase -= 4;
        }
        if (t === PT.Pawn) this.pawnFile[(c << 3) | fileOf(s)]--;
        this.sq[s] = 0;
    };

    Board.prototype.movePiece = function (from, to) {
        var p = this.sq[from];
        this.removePiece(from);
        this.addPiece(to, p);
    };

    Board.prototype.recomputeKey = function () {
        var lo = 0, hi = 0;
        for (var i = 0; i < 128; i++) {
            if ((i & 0x88) !== 0) continue;
            var p = this.sq[i];
            if (p === 0) continue;
            lo ^= ZP_LO[i * 24 + p]; hi ^= ZP_HI[i * 24 + p];
        }
        lo ^= ZC_LO[this.castling]; hi ^= ZC_HI[this.castling];
        if (this.ep >= 0) { lo ^= ZE_LO[this.ep]; hi ^= ZE_HI[this.ep]; }
        if (this.side === BLACK) { lo ^= ZS_LO; hi ^= ZS_HI; }
        return { lo: lo | 0, hi: hi | 0 };
    };

    Board.prototype.recomputeIncremental = function () {
        this.matPst.fill(0); this.kingSq.fill(0);
        this.pieceCount.fill(0); this.pawnFile.fill(0);
        this.phase = 0;
        for (var i = 0; i < 128; i++) {
            if ((i & 0x88) !== 0) continue;
            var p = this.sq[i];
            if (p === 0) continue;
            var t = typeOf(p), c = colorOf(p);
            this.pieceCount[p]++;
            if (t === PT.King) this.kingSq[c] = i;
            else {
                this.matPst[c] += PIECE_VALUE[t] + pstValue(t, i, c);
                if (t === PT.Knight || t === PT.Bishop) this.phase += 1;
                else if (t === PT.Rook) this.phase += 2;
                else if (t === PT.Queen) this.phase += 4;
            }
            if (t === PT.Pawn) this.pawnFile[(c << 3) | fileOf(i)]++;
        }
    };

    Board.prototype.loadFen = function (fen) {
        this.clear();
        var parts = fen.trim().split(/\s+/);
        var rows = parts[0], rank = 7, file = 0;
        for (var i = 0; i < rows.length; i++) {
            var ch = rows[i];
            if (ch === '/') { rank--; file = 0; continue; }
            if (ch >= '1' && ch <= '8') { file += ch.charCodeAt(0) - 48; continue; }
            var t = PT.None;
            switch (ch.toLowerCase()) {
                case 'p': t = PT.Pawn; break;
                case 'n': t = PT.Knight; break;
                case 'b': t = PT.Bishop; break;
                case 'r': t = PT.Rook; break;
                case 'q': t = PT.Queen; break;
                case 'k': t = PT.King; break;
            }
            var c = (ch === ch.toUpperCase()) ? WHITE : BLACK;
            if (t !== PT.None && rank >= 0 && rank < 8 && file >= 0 && file < 8) {
                this.addPiece(sqIndex(file, rank), mkPiece(t, c));
            }
            file++;
        }
        this.side = (parts.length > 1 && parts[1] === 'b') ? BLACK : WHITE;
        this.castling = 0;
        if (parts.length > 2) {
            var cs = parts[2];
            if (cs.indexOf('K') >= 0) this.castling |= CR.WK;
            if (cs.indexOf('Q') >= 0) this.castling |= CR.WQ;
            if (cs.indexOf('k') >= 0) this.castling |= CR.BK;
            if (cs.indexOf('q') >= 0) this.castling |= CR.BQ;
        }
        this.ep = (parts.length > 3 && parts[3] !== '-') ? parseSq(parts[3]) : -1;
        this.halfmove = (parts.length > 4) ? (parseInt(parts[4], 10) || 0) : 0;
        this.fullmove = (parts.length > 5) ? (parseInt(parts[5], 10) || 1) : 1;
        var k = this.recomputeKey();
        this.keyLo = k.lo; this.keyHi = k.hi;
        return this;
    };

    Board.prototype.toFen = function () {
        var out = '';
        for (var rank = 7; rank >= 0; rank--) {
            var empty = 0;
            for (var file = 0; file < 8; file++) {
                var p = this.sq[sqIndex(file, rank)];
                if (p === 0) { empty++; continue; }
                if (empty > 0) { out += empty; empty = 0; }
                var letter = '?';
                switch (typeOf(p)) {
                    case PT.Pawn: letter = 'p'; break;
                    case PT.Knight: letter = 'n'; break;
                    case PT.Bishop: letter = 'b'; break;
                    case PT.Rook: letter = 'r'; break;
                    case PT.Queen: letter = 'q'; break;
                    case PT.King: letter = 'k'; break;
                }
                out += colorOf(p) === WHITE ? letter.toUpperCase() : letter;
            }
            if (empty > 0) out += empty;
            if (rank > 0) out += '/';
        }
        out += this.side === WHITE ? ' w ' : ' b ';
        var cs = '';
        if (this.castling & CR.WK) cs += 'K';
        if (this.castling & CR.WQ) cs += 'Q';
        if (this.castling & CR.BK) cs += 'k';
        if (this.castling & CR.BQ) cs += 'q';
        out += (cs === '' ? '-' : cs);
        out += ' ' + (this.ep >= 0 ? sqName(this.ep) : '-');
        out += ' ' + this.halfmove + ' ' + this.fullmove;
        return out;
    };

    Board.prototype.isSquareAttacked = function (s, byColor) {
        var i, t, p, dir;
        if (byColor === WHITE) {
            if (onBoard(s - 15) && this.sq[s - 15] === mkPiece(PT.Pawn, WHITE)) return true;
            if (onBoard(s - 17) && this.sq[s - 17] === mkPiece(PT.Pawn, WHITE)) return true;
        } else {
            if (onBoard(s + 15) && this.sq[s + 15] === mkPiece(PT.Pawn, BLACK)) return true;
            if (onBoard(s + 17) && this.sq[s + 17] === mkPiece(PT.Pawn, BLACK)) return true;
        }
        var kn = mkPiece(PT.Knight, byColor);
        for (i = 0; i < 8; i++) {
            t = s + KNIGHT_OFF[i];
            if (onBoard(t) && this.sq[t] === kn) return true;
        }
        var kg = mkPiece(PT.King, byColor);
        for (i = 0; i < 8; i++) {
            t = s + KING_OFF[i];
            if (onBoard(t) && this.sq[t] === kg) return true;
        }
        for (i = 0; i < 4; i++) {
            dir = BISHOP_OFF[i];
            t = s + dir;
            while (onBoard(t)) {
                p = this.sq[t];
                if (p !== 0) {
                    if (colorOf(p) === byColor && (typeOf(p) === PT.Bishop || typeOf(p) === PT.Queen)) return true;
                    break;
                }
                t += dir;
            }
        }
        for (i = 0; i < 4; i++) {
            dir = ROOK_OFF[i];
            t = s + dir;
            while (onBoard(t)) {
                p = this.sq[t];
                if (p !== 0) {
                    if (colorOf(p) === byColor && (typeOf(p) === PT.Rook || typeOf(p) === PT.Queen)) return true;
                    break;
                }
                t += dir;
            }
        }
        return false;
    };

    Board.prototype.inCheck = function (color) {
        if (color === undefined) color = this.side;
        var k = this.kingSq[color];
        if (k === 0 && this.sq[0] !== mkPiece(PT.King, color)) return false;
        return this.isSquareAttacked(k, opp(color));
    };

    /**
     * 某一方"控制"（攻击范围）的全部格子。用两个 32 位整数拼成 64 位掩码：
     * 位序 = rank * 8 + file（低位在前），第 0 位是 a1 —— 与桌面版完全一致。
     * 用 lo/hi 两个数是为了避免 JS 位运算只有 32 位的问题。
     */
    Board.prototype.controlledSquares = function (byColor) {
        var lo = 0, hi = 0;
        for (var r = 0; r < 8; r++) {
            for (var f = 0; f < 8; f++) {
                if (!this.isSquareAttacked(sqIndex(f, r), byColor)) continue;
                var bit = r * 8 + f;
                if (bit < 32) lo = (lo | (1 << bit)) >>> 0;
                else hi = (hi | (1 << (bit - 32))) >>> 0;
            }
        }
        return { lo: lo, hi: hi };
    };

    /** 控制格掩码里的某一位（rank * 8 + file）是否置位。 */
    function controlHas(mask, file, rank) {
        var bit = rank * 8 + file;
        if (bit < 32) return (mask.lo & (1 << bit)) !== 0;
        return (mask.hi & (1 << (bit - 32))) !== 0;
    }

    Board.prototype.addPawnMove = function (out, n, from, to, promoRank, flags) {
        if (rankOf(to) === promoRank) {
            out[n++] = MV(from, to, PT.Queen, flags);
            out[n++] = MV(from, to, PT.Rook, flags);
            out[n++] = MV(from, to, PT.Bishop, flags);
            out[n++] = MV(from, to, PT.Knight, flags);
        } else {
            out[n++] = MV(from, to, 0, flags);
        }
        return n;
    };

    /** 生成伪合法走法，写入 out，返回个数 */
    Board.prototype.generateMoves = function (out, capturesOnly) {
        var us = this.side, them = opp(us), n = 0;
        for (var s = 0; s < 128; s++) {
            if ((s & 0x88) !== 0) continue;
            var piece = this.sq[s];
            if (piece === 0 || colorOf(piece) !== us) continue;
            var t = typeOf(piece), i, to, target, dir, d;
            switch (t) {
                case PT.Pawn:
                    dir = us === WHITE ? 16 : -16;
                    var startRank = us === WHITE ? 1 : 6;
                    var promoRank = us === WHITE ? 7 : 0;
                    to = s + dir;
                    if (!capturesOnly && onBoard(to) && this.sq[to] === 0) {
                        n = this.addPawnMove(out, n, s, to, promoRank, MF.None);
                        if (rankOf(s) === startRank) {
                            var to2 = to + dir;
                            if (onBoard(to2) && this.sq[to2] === 0) out[n++] = MV(s, to2, 0, MF.DoublePush);
                        }
                    }
                    for (d = -1; d <= 1; d += 2) {
                        to = s + dir + d;
                        if (!onBoard(to)) continue;
                        target = this.sq[to];
                        if (target !== 0) {
                            if (colorOf(target) === them) n = this.addPawnMove(out, n, s, to, promoRank, MF.None);
                        } else if (to === this.ep) {
                            out[n++] = MV(s, to, 0, MF.EnPassant);
                        }
                    }
                    break;
                case PT.Knight:
                    for (i = 0; i < 8; i++) {
                        to = s + KNIGHT_OFF[i];
                        if (!onBoard(to)) continue;
                        target = this.sq[to];
                        if (target !== 0 && colorOf(target) === us) continue;
                        if (capturesOnly && target === 0) continue;
                        out[n++] = MV(s, to, 0, MF.None);
                    }
                    break;
                case PT.Bishop:
                case PT.Rook:
                case PT.Queen:
                    var offs = t === PT.Bishop ? BISHOP_OFF : (t === PT.Rook ? ROOK_OFF : KING_OFF);
                    var cnt = t === PT.Queen ? 8 : 4;
                    for (i = 0; i < cnt; i++) {
                        dir = offs[i];
                        to = s + dir;
                        while (onBoard(to)) {
                            target = this.sq[to];
                            if (target === 0) {
                                if (!capturesOnly) out[n++] = MV(s, to, 0, MF.None);
                            } else {
                                if (colorOf(target) === them) out[n++] = MV(s, to, 0, MF.None);
                                break;
                            }
                            to += dir;
                        }
                    }
                    break;
                case PT.King:
                    for (i = 0; i < 8; i++) {
                        to = s + KING_OFF[i];
                        if (!onBoard(to)) continue;
                        target = this.sq[to];
                        if (target !== 0 && colorOf(target) === us) continue;
                        if (capturesOnly && target === 0) continue;
                        out[n++] = MV(s, to, 0, MF.None);
                    }
                    if (!capturesOnly) n = this.generateCastles(out, n, us);
                    break;
            }
        }
        return n;
    };

    Board.prototype.generateCastles = function (out, n, us) {
        var E1 = sqIndex(4, 0), H1 = sqIndex(7, 0), A1 = sqIndex(0, 0);
        var E8 = sqIndex(4, 7), H8 = sqIndex(7, 7), A8 = sqIndex(0, 7);
        if (us === WHITE) {
            if ((this.castling & CR.WK) && this.sq[E1] === mkPiece(PT.King, WHITE) && this.sq[H1] === mkPiece(PT.Rook, WHITE) &&
                this.sq[5] === 0 && this.sq[6] === 0 &&
                !this.isSquareAttacked(E1, BLACK) && !this.isSquareAttacked(5, BLACK)) {
                out[n++] = MV(E1, 6, 0, MF.Castle);
            }
            if ((this.castling & CR.WQ) && this.sq[E1] === mkPiece(PT.King, WHITE) && this.sq[A1] === mkPiece(PT.Rook, WHITE) &&
                this.sq[1] === 0 && this.sq[2] === 0 && this.sq[3] === 0 &&
                !this.isSquareAttacked(E1, BLACK) && !this.isSquareAttacked(3, BLACK)) {
                out[n++] = MV(E1, 2, 0, MF.Castle);
            }
        } else {
            if ((this.castling & CR.BK) && this.sq[E8] === mkPiece(PT.King, BLACK) && this.sq[H8] === mkPiece(PT.Rook, BLACK) &&
                this.sq[117] === 0 && this.sq[118] === 0 &&
                !this.isSquareAttacked(E8, WHITE) && !this.isSquareAttacked(117, WHITE)) {
                out[n++] = MV(E8, 118, 0, MF.Castle);
            }
            if ((this.castling & CR.BQ) && this.sq[E8] === mkPiece(PT.King, BLACK) && this.sq[A8] === mkPiece(PT.Rook, BLACK) &&
                this.sq[113] === 0 && this.sq[114] === 0 && this.sq[115] === 0 &&
                !this.isSquareAttacked(E8, WHITE) && !this.isSquareAttacked(115, WHITE)) {
                out[n++] = MV(E8, 114, 0, MF.Castle);
            }
        }
        return n;
    };

    Board.prototype.generateLegalMoves = function () {
        var buf = new Int32Array(256);
        var n = this.generateMoves(buf, false);
        var legal = [];
        for (var i = 0; i < n; i++) {
            if (this.tryMake(buf[i])) {
                legal.push(buf[i]);
                this.unmakeMove();
            }
        }
        return legal;
    };

    Board.prototype.hasLegalMove = function () {
        var buf = new Int32Array(256);
        var n = this.generateMoves(buf, false);
        for (var i = 0; i < n; i++) {
            if (this.tryMake(buf[i])) {
                this.unmakeMove();
                return true;
            }
        }
        return false;
    };

    Board.prototype.makeMove = function (m) {
        var top = this.stackTop * 10, u = this.undoStack;
        var from = mvFrom(m), to = mvTo(m), promo = mvPromo(m), flags = mvFlags(m);
        var piece = this.sq[from], t = typeOf(piece), c = colorOf(piece);
        var captured = 0, capturedSq = -1;

        u[top + 0] = m;
        u[top + 1] = 0;
        u[top + 2] = -1;
        u[top + 3] = this.castling;
        u[top + 4] = this.ep;
        u[top + 5] = this.halfmove;
        u[top + 6] = this.fullmove;
        u[top + 7] = this.keyLo;
        u[top + 8] = this.keyHi;
        this.stackTop++;

        if (flags & MF.EnPassant) {
            capturedSq = to + (c === WHITE ? -16 : 16);
            captured = this.sq[capturedSq];
            this.removePiece(capturedSq);
        } else if (this.sq[to] !== 0) {
            capturedSq = to;
            captured = this.sq[to];
            this.removePiece(to);
        }
        u[top + 1] = captured;
        u[top + 2] = capturedSq;

        this.movePiece(from, to);
        if (promo !== 0) {
            this.removePiece(to);
            this.addPiece(to, mkPiece(promo, c));
        }

        if (flags & MF.Castle) {
            var rf, rt;
            if (to === 6) { rf = sqIndex(7, 0); rt = sqIndex(5, 0); }
            else if (to === 2) { rf = sqIndex(0, 0); rt = sqIndex(3, 0); }
            else if (to === 118) { rf = sqIndex(7, 7); rt = sqIndex(5, 7); }
            else { rf = sqIndex(0, 7); rt = sqIndex(3, 7); }
            this.movePiece(rf, rt);
        }

        var nc = this.castling;
        if (t === PT.King) {
            nc &= (c === WHITE) ? ~(CR.WK | CR.WQ) : ~(CR.BK | CR.BQ);
        }
        if (from === sqIndex(0, 0) || to === sqIndex(0, 0)) nc &= ~CR.WQ;
        if (from === sqIndex(7, 0) || to === sqIndex(7, 0)) nc &= ~CR.WK;
        if (from === sqIndex(0, 7) || to === sqIndex(0, 7)) nc &= ~CR.BQ;
        if (from === sqIndex(7, 7) || to === sqIndex(7, 7)) nc &= ~CR.BK;
        if (nc !== this.castling) {
            this.keyLo ^= ZC_LO[this.castling]; this.keyHi ^= ZC_HI[this.castling];
            this.keyLo ^= ZC_LO[nc]; this.keyHi ^= ZC_HI[nc];
            this.castling = nc;
        }

        if (this.ep >= 0) {
            this.keyLo ^= ZE_LO[this.ep]; this.keyHi ^= ZE_HI[this.ep];
            this.ep = -1;
        }
        if (flags & MF.DoublePush) {
            this.ep = (from + to) >> 1;
            this.keyLo ^= ZE_LO[this.ep]; this.keyHi ^= ZE_HI[this.ep];
        }

        if (t === PT.Pawn || captured !== 0) this.halfmove = 0; else this.halfmove++;
        if (c === BLACK) this.fullmove++;

        this.side = opp(c);
        this.keyLo ^= ZS_LO; this.keyHi ^= ZS_HI;
    };

    Board.prototype.unmakeMove = function () {
        if (this.stackTop === 0) return;
        this.stackTop--;
        var top = this.stackTop * 10, u = this.undoStack;
        var m = u[top + 0];
        var from = mvFrom(m), to = mvTo(m), promo = mvPromo(m), flags = mvFlags(m);

        if (flags & MF.Castle) {
            var rf, rt;
            if (to === 6) { rf = sqIndex(7, 0); rt = sqIndex(5, 0); }
            else if (to === 2) { rf = sqIndex(0, 0); rt = sqIndex(3, 0); }
            else if (to === 118) { rf = sqIndex(7, 7); rt = sqIndex(5, 7); }
            else { rf = sqIndex(0, 7); rt = sqIndex(3, 7); }
            this.movePiece(rt, rf);
        }

        var piece = this.sq[to], c = colorOf(piece);
        if (promo !== 0) {
            this.removePiece(to);
            this.addPiece(to, mkPiece(PT.Pawn, c));
        }
        this.movePiece(to, from);

        var captured = u[top + 1], capturedSq = u[top + 2];
        if (capturedSq >= 0 && captured !== 0) this.addPiece(capturedSq, captured);

        this.castling = u[top + 3];
        this.ep = u[top + 4];
        this.halfmove = u[top + 5];
        this.fullmove = u[top + 6];
        this.keyLo = u[top + 7];
        this.keyHi = u[top + 8];
        this.side = c;      // 悔棋后轮到刚才走子的一方
    };

    Board.prototype.tryMake = function (m) {
        var mover = colorOf(this.sq[mvFrom(m)]);
        this.makeMove(m);
        if (this.isSquareAttacked(this.kingSq[mover], opp(mover))) {
            this.unmakeMove();
            return false;
        }
        return true;
    };

    Board.prototype.makeNull = function () {
        var top = this.nullTop * 4, u = this.nullStack;
        u[top + 0] = this.ep;
        u[top + 1] = this.halfmove;
        u[top + 2] = this.keyLo;
        u[top + 3] = this.keyHi;
        this.nullTop++;
        if (this.ep >= 0) { this.keyLo ^= ZE_LO[this.ep]; this.keyHi ^= ZE_HI[this.ep]; this.ep = -1; }
        this.halfmove++;
        this.side = opp(this.side);
        this.keyLo ^= ZS_LO; this.keyHi ^= ZS_HI;
    };

    Board.prototype.unmakeNull = function () {
        if (this.nullTop === 0) return;
        this.nullTop--;
        var top = this.nullTop * 4, u = this.nullStack;
        this.side = opp(this.side);
        this.ep = u[top + 0];
        this.halfmove = u[top + 1];
        this.keyLo = u[top + 2];
        this.keyHi = u[top + 3];
    };

    // ------------------------------------------------------------------
    // 记谱（SAN）
    // ------------------------------------------------------------------
    var PIECE_LETTER = { 1: 'P', 2: 'N', 3: 'B', 4: 'R', 5: 'Q', 6: 'K' };

    Board.prototype.san = function (m) {
        var out = '';
        var from = mvFrom(m), to = mvTo(m), promo = mvPromo(m), flags = mvFlags(m);
        var capture = this.sq[to] !== 0 || (flags & MF.EnPassant);
        if (flags & MF.Castle) {
            out = (to === 6 || to === 118) ? 'O-O' : 'O-O-O';
        } else {
            var t = typeOf(this.sq[from]);
            if (t === PT.Pawn) {
                if (capture) out += String.fromCharCode(97 + fileOf(from)) + 'x';
                out += sqName(to);
                if (promo !== 0) out += '=' + PIECE_LETTER[promo];
            } else {
                out += PIECE_LETTER[t];
                var legal = this.generateLegalMoves();
                // 标准 SAN 消歧（与桌面版同一套规则）：先比同列，再比同行，都撞才写完整格子名
                var sameFile = false, sameRank = false, other = false;
                for (var i = 0; i < legal.length; i++) {
                    var o = legal[i];
                    if (mvTo(o) !== to || mvFrom(o) === from) continue;
                    if (typeOf(this.sq[mvFrom(o)]) !== t) continue;
                    other = true;
                    if (fileOf(mvFrom(o)) === fileOf(from)) sameFile = true;
                    if (rankOf(mvFrom(o)) === rankOf(from)) sameRank = true;
                }
                if (other) {
                    if (!sameFile) out += String.fromCharCode(97 + fileOf(from));
                    else if (!sameRank) out += (rankOf(from) + 1);
                    else out += sqName(from);
                }
                if (capture) out += 'x';
                out += sqName(to);
            }
        }
        if (this.tryMake(m)) {
            var oppCheck = this.inCheck(this.side);
            var oppHasMove = this.hasLegalMove();
            if (oppCheck && !oppHasMove) out += '#';
            else if (oppCheck) out += '+';
            this.unmakeMove();
        }
        return out;
    };

    Board.prototype.isInsufficientMaterial = function () {
        var knights = 0, bishops = 0, others = 0, bishopColor = -1, sameColor = true;
        for (var i = 0; i < 128; i++) {
            if ((i & 0x88) !== 0) continue;
            var p = this.sq[i];
            if (p === 0) continue;
            var t = typeOf(p);
            if (t === PT.King) continue;
            if (t === PT.Knight) { knights++; continue; }
            if (t === PT.Bishop) {
                bishops++;
                var col = (rankOf(i) + fileOf(i)) % 2;
                if (bishopColor < 0) bishopColor = col;
                else if (bishopColor !== col) sameColor = false;
                continue;
            }
            others++;
        }
        if (others > 0) return false;
        if (bishops === 0 && knights <= 1) return true;
        if (knights === 0 && bishops === 1) return true;
        if (knights === 0 && bishops >= 2 && sameColor) return true;
        return false;
    };

    // ------------------------------------------------------------------
    // 静态评估
    // ------------------------------------------------------------------
    function evaluate(b) {
        var white = b.matPst[WHITE], black = b.matPst[BLACK];
        white += kingPst(b.kingSq[WHITE], WHITE, b.phase);
        black += kingPst(b.kingSq[BLACK], BLACK, b.phase);
        if (b.pieceCount[mkPiece(PT.Bishop, WHITE)] >= 2) white += 32;
        if (b.pieceCount[mkPiece(PT.Bishop, BLACK)] >= 2) black += 32;
        for (var f = 0; f < 8; f++) {
            var wp = b.pawnFile[(WHITE << 3) | f], bp = b.pawnFile[(BLACK << 3) | f];
            if (wp > 1) white -= 12 * (wp - 1);
            if (bp > 1) black -= 12 * (bp - 1);
        }
        var score = white - black;
        if (b.side === BLACK) score = -score;
        return score + 8;
    }

    // ------------------------------------------------------------------
    // 难度档位（ELO 100 ~ 2000，每 100 分一档）
    // ------------------------------------------------------------------
    var SKILL_DEPTH = [1, 1, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 5, 5, 5, 6, 6, 7, 8, 10];
    var SKILL_TIME = [120, 150, 200, 250, 300, 350, 400, 450, 500, 550, 600, 700, 800, 900, 1000, 1150, 1300, 1450, 1600, 1800];
    var SKILL_BLUNDER = [0.85, 0.66, 0.50, 0.38, 0.30, 0.24, 0.19, 0.15, 0.12, 0.09, 0.07, 0.055, 0.045, 0.035, 0.028, 0.020, 0.014, 0.009, 0.004, 0.0];
    var SKILL_MARGIN = [400, 330, 270, 220, 185, 155, 132, 115, 100, 86, 72, 60, 50, 42, 35, 28, 20, 12, 6, 0];
    var SKILL_NOISE = [120, 100, 85, 72, 60, 50, 42, 36, 30, 25, 20, 16, 13, 10, 8, 6, 4, 2, 1, 0];

    function tierName(elo) {
        if (elo <= 300) return '入门';
        if (elo <= 600) return '初级';
        if (elo <= 900) return '中级';
        if (elo <= 1200) return '进阶';
        if (elo <= 1500) return '高手';
        if (elo <= 1700) return '专家';
        if (elo <= 1900) return '大师';
        return '特级大师';
    }

    function tierDesc(elo) {
        if (elo <= 300) return '随手乱走，经常送子';
        if (elo <= 600) return '只看一步，会漏战术';
        if (elo <= 900) return '懂基本吃子与兑换';
        if (elo <= 1200) return '有简单战术意识';
        if (elo <= 1500) return '能算两三步的战术';
        if (elo <= 1700) return '攻防均衡，失误很少';
        if (elo <= 1900) return '算度较深，善于抓漏';
        return '全力搜索，几乎不失误';
    }

    var SkillTable = {
        MinElo: 100,
        MaxElo: 2000,
        Step: 100,
        levels: (function () {
            var list = [];
            for (var i = 0; i < 20; i++) {
                list.push({
                    elo: 100 + i * 100,
                    tier: tierName(100 + i * 100),
                    desc: tierDesc(100 + i * 100),
                    maxDepth: SKILL_DEPTH[i],
                    timeMs: SKILL_TIME[i],
                    blunderPct: SKILL_BLUNDER[i],
                    marginCp: SKILL_MARGIN[i],
                    noiseCp: SKILL_NOISE[i],
                    forceAccurate: false
                });
            }
            return list;
        })(),
        forElo: function (elo) {
            var idx = Math.floor((elo - 100) / 100);
            if (idx < 0) idx = 0;
            if (idx > 19) idx = 19;
            return this.levels[idx];
        },
        /** 分析档位：全力搜索、根节点取精确分 */
        analysisProfile: function (maxDepth, timeMs) {
            return {
                elo: 0, tier: '分析', desc: 'AI 解析',
                maxDepth: maxDepth, timeMs: timeMs,
                blunderPct: 0, marginCp: 0, noiseCp: 0, forceAccurate: true
            };
        }
    };

    // ------------------------------------------------------------------
    // 搜索引擎
    // ------------------------------------------------------------------
    var TTBITS = 17, TTSIZE = 1 << TTBITS, TTMASK = TTSIZE - 1;
    var MAX_PLY = 64;
    var MOVE_BUF = 256;

    function Engine() {
        this.ttLo = new Int32Array(TTSIZE);
        this.ttHi = new Int32Array(TTSIZE);
        this.ttMove = new Int32Array(TTSIZE);
        this.ttScore = new Int32Array(TTSIZE);
        this.ttDepth = new Int8Array(TTSIZE);
        this.ttFlag = new Int8Array(TTSIZE);
        this.ttUsed = new Uint8Array(TTSIZE);
        this.moves = [];
        this.caps = [];
        this.scores = [];
        for (var i = 0; i < MAX_PLY; i++) {
            this.moves.push(new Int32Array(MOVE_BUF));
            this.caps.push(new Int32Array(MOVE_BUF));
            this.scores.push(new Int32Array(MOVE_BUF));
        }
        this.killers = new Int32Array(MAX_PLY * 2);
        this.pathLo = new Int32Array(4096);
        this.pathHi = new Int32Array(4096);
        this.pathCount = 0;
        this.nodes = 0;
        this.stop = false;
        this.timeLimit = 0;
    }

    Engine.prototype.clearTable = function () {
        this.ttUsed.fill(0);
        this.ttDepth.fill(0);
    };

    Engine.prototype.now = function () {
        return (typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now();
    };

    Engine.prototype.timeUp = function () {
        if (this.stop) return true;
        if ((this.nodes & 511) === 0 && this.now() - this.startTime >= this.timeLimit) this.stop = true;
        return this.stop;
    };

    /** 当前局面是否在之前的路径中出现过（path 的最后一项是当前局面本身） */
    Engine.prototype.isRepetition = function (b) {
        for (var i = this.pathCount - 2; i >= 0; i--) {
            if (this.pathLo[i] === b.keyLo && this.pathHi[i] === b.keyHi) return true;
        }
        return false;
    };

    Engine.prototype.search = function (position, historyLo, historyHi, skill, rng) {
        var b = this;
        this.board = position;
        this.skill = skill;
        this.rng = rng || Math.random;
        this.nodes = 0;
        this.stop = false;
        this.timeLimit = skill.timeMs;
        this.startTime = this.now();
        this.pathCount = 0;
        this.killers.fill(0);
        if (historyLo && historyHi) {
            for (var i = 0; i < historyLo.length && this.pathCount < 4000; i++) {
                this.pathLo[this.pathCount] = historyLo[i];
                this.pathHi[this.pathCount] = historyHi[i];
                this.pathCount++;
            }
        }

        var rootMoves = position.generateLegalMoves();
        if (rootMoves.length === 0) return null;

        var n = rootMoves.length;
        var moves = rootMoves.slice();
        var scores = new Int32Array(n);
        var iterScores = new Int32Array(n);
        var completed = 0, bestScore = 0, i, d;

        for (d = 1; d <= skill.maxDepth; d++) {
            var accurate = (skill.marginCp > 0 || skill.noiseCp > 0 || skill.blunderPct > 0 || skill.forceAccurate);
            var alpha = -INFINITY_SCORE, localBest = -INFINITY_SCORE, aborted = false;
            for (i = 0; i < n; i++) {
                var m = moves[i];
                position.tryMake(m);
                this.pathLo[this.pathCount] = position.keyLo;
                this.pathHi[this.pathCount] = position.keyHi;
                this.pathCount++;
                var sc = accurate
                    ? -this.alphaBeta(d - 1, -INFINITY_SCORE, INFINITY_SCORE, 1)
                    : -this.alphaBeta(d - 1, -INFINITY_SCORE, -alpha, 1);
                this.pathCount--;
                position.unmakeMove();
                if (this.stop) { aborted = true; break; }
                iterScores[i] = sc;
                if (sc > localBest) localBest = sc;
                if (sc > alpha) alpha = sc;
            }
            if (aborted) break;
            for (i = 0; i < n; i++) scores[i] = iterScores[i];
            completed = d;
            bestScore = localBest;
            this.sortRoot(moves, scores, n);
            if (bestScore > MATE_THRESHOLD - 1000) break;
            if ((this.now() - this.startTime) * 3 > this.timeLimit) break;
        }

        var chosen = this.chooseMove(moves, scores, n);
        var roots = [];
        for (i = 0; i < n; i++) roots.push({ move: moves[i], score: completed > 0 ? scores[i] : 0 });

        var resultScore = 0;
        for (i = 0; i < n; i++) {
            if (sameMove(moves[i], chosen)) { resultScore = scores[i]; break; }
        }
        return {
            move: chosen,
            score: resultScore,
            depth: completed,
            nodes: this.nodes,
            elapsedMs: Math.round(this.now() - this.startTime),
            roots: roots,
            complete: completed > 0
        };
    };

    Engine.prototype.sortRoot = function (moves, scores, n) {
        for (var i = 1; i < n; i++) {
            var m = moves[i], s = scores[i], j = i - 1;
            while (j >= 0 && scores[j] < s) { moves[j + 1] = moves[j]; scores[j + 1] = scores[j]; j--; }
            moves[j + 1] = m; scores[j + 1] = s;
        }
    };

    Engine.prototype.chooseMove = function (moves, scores, n) {
        if (n <= 0) return 0;
        if (n === 1) return moves[0];
        var sk = this.skill;
        if (sk.blunderPct > 0 && this.rng() < sk.blunderPct) {
            return moves[Math.floor(this.rng() * n) % n];
        }
        if (sk.marginCp <= 0 && sk.noiseCp <= 0) return moves[0];

        var noisy = new Int32Array(n), best = -InfinityScoreMin(), i;
        for (i = 0; i < n; i++) {
            var v = scores[i];
            if (sk.noiseCp > 0) v += Math.floor(this.rng() * (2 * sk.noiseCp + 1)) - sk.noiseCp;
            noisy[i] = v;
            if (v > best) best = v;
        }
        var count = 0;
        for (i = 0; i < n; i++) if (noisy[i] >= best - sk.marginCp) count++;
        if (count <= 1) return moves[0];
        var pick = Math.floor(this.rng() * count) % count;
        for (i = 0; i < n; i++) {
            if (noisy[i] >= best - sk.marginCp) {
                if (pick === 0) return moves[i];
                pick--;
            }
        }
        return moves[0];
    };

    function InfinityScoreMin() { return -2147483000; }

    Engine.prototype.scoreMoves = function (b, moves, scores, n, ply, ttMove) {
        var killers = this.killers, k0 = killers[ply * 2], k1 = killers[ply * 2 + 1];
        for (var i = 0; i < n; i++) {
            var m = moves[i], s = 0;
            if (ttMove && sameMove(m, ttMove)) {
                s = 1000000;
            } else {
                var to = mvTo(m), from = mvFrom(m);
                var victim = b.sq[to];
                if (mvFlags(m) & MF.EnPassant) victim = mkPiece(PT.Pawn, opp(b.side));
                if (victim !== 0) {
                    s = 100000 + PIECE_VALUE[typeOf(victim)] * 16 - PIECE_VALUE[typeOf(b.sq[from])];
                } else if (mvPromo(m) !== 0) {
                    s = 90000 + PIECE_VALUE[mvPromo(m)];
                } else if (k0 && sameMove(m, k0)) {
                    s = 80000;
                } else if (k1 && sameMove(m, k1)) {
                    s = 79000;
                }
            }
            scores[i] = s;
        }
    };

    Engine.prototype.alphaBeta = function (depth, alpha, beta, ply) {
        var b = this.board;
        if (this.stop) return 0;
        if (ply > 0) {
            if (b.halfmove >= 100 || this.isRepetition(b)) return 0;
        }
        var inCheck = b.inCheck();
        if (inCheck) depth++;
        if (depth <= 0) return this.quiescence(alpha, beta, ply);
        if (ply >= MAX_PLY - 2) return evaluate(b);

        this.nodes++;
        if (this.timeUp()) return 0;

        var alphaOrig = alpha;
        var idx = (b.keyLo & TTMASK) >>> 0;
        var ttMove = 0;
        if (this.ttUsed[idx] && this.ttLo[idx] === b.keyLo && this.ttHi[idx] === b.keyHi) {
            ttMove = this.ttMove[idx];
            if (this.ttDepth[idx] >= depth) {
                var sc = this.ttScore[idx];
                if (sc > MATE_THRESHOLD) sc -= ply;
                else if (sc < -MATE_THRESHOLD) sc += ply;
                var fl = this.ttFlag[idx];
                if (fl === 0) return sc;
                if (fl === 1 && sc >= beta) return sc;
                if (fl === 2 && sc <= alpha) return sc;
            }
        }

        // 空着裁剪
        if (!inCheck && ply > 0 && depth >= 3 && beta < MATE_THRESHOLD) {
            var nonPawn = b.pieceCount[mkPiece(PT.Knight, b.side)] + b.pieceCount[mkPiece(PT.Bishop, b.side)] +
                b.pieceCount[mkPiece(PT.Rook, b.side)] + b.pieceCount[mkPiece(PT.Queen, b.side)];
            if (nonPawn > 0) {
                b.makeNull();
                this.pathLo[this.pathCount] = b.keyLo; this.pathHi[this.pathCount] = b.keyHi; this.pathCount++;
                var nsc = -this.alphaBeta(depth - 3, -beta, -beta + 1, ply + 1);
                this.pathCount--;
                b.unmakeNull();
                if (this.stop) return 0;
                if (nsc >= beta) return nsc > MATE_THRESHOLD ? beta : nsc;
            }
        }

        var buf = this.moves[ply], scores = this.scores[ply];
        var n = b.generateMoves(buf, false);
        this.scoreMoves(b, buf, scores, n, ply, ttMove);

        var best = -INFINITY_SCORE, bestMove = 0, legal = 0;
        for (var i = 0; i < n; i++) {
            var bi = i;
            for (var j = i + 1; j < n; j++) if (scores[j] > scores[bi]) bi = j;
            if (bi !== i) {
                var tm = buf[i]; buf[i] = buf[bi]; buf[bi] = tm;
                var ts = scores[i]; scores[i] = scores[bi]; scores[bi] = ts;
            }
            var m = buf[i];
            var quiet = mvPromo(m) === 0 && (mvFlags(m) & MF.EnPassant) === 0 && b.sq[mvTo(m)] === 0;
            if (!b.tryMake(m)) continue;
            legal++;
            this.pathLo[this.pathCount] = b.keyLo; this.pathHi[this.pathCount] = b.keyHi; this.pathCount++;
            var sc2;
            var reduce = depth >= 3 && legal > 3 && quiet && !inCheck;
            if (reduce) {
                sc2 = -this.alphaBeta(depth - 2, -alpha - 1, -alpha, ply + 1);
                if (!this.stop && sc2 > alpha) sc2 = -this.alphaBeta(depth - 1, -beta, -alpha, ply + 1);
            } else {
                sc2 = -this.alphaBeta(depth - 1, -beta, -alpha, ply + 1);
            }
            this.pathCount--;
            b.unmakeMove();
            if (this.stop) return 0;
            if (sc2 > best) { best = sc2; bestMove = m; }
            if (sc2 > alpha) alpha = sc2;
            if (alpha >= beta) {
                if (quiet) {
                    var k0 = this.killers[ply * 2];
                    if (!k0 || !sameMove(k0, m)) {
                        this.killers[ply * 2 + 1] = k0;
                        this.killers[ply * 2] = m;
                    }
                }
                break;
            }
        }

        if (legal === 0) return inCheck ? -MATE_SCORE + ply : 0;

        var flag = best <= alphaOrig ? 2 : (best >= beta ? 1 : 0);
        var storeScore = best;
        if (best > MATE_THRESHOLD) storeScore = best + ply;
        else if (best < -MATE_THRESHOLD) storeScore = best - ply;
        if (!this.ttUsed[idx] || this.ttLo[idx] !== b.keyLo || this.ttHi[idx] !== b.keyHi ||
            this.ttDepth[idx] <= depth || flag === 0) {
            this.ttUsed[idx] = 1;
            this.ttLo[idx] = b.keyLo; this.ttHi[idx] = b.keyHi;
            this.ttScore[idx] = storeScore;
            this.ttDepth[idx] = depth;
            this.ttFlag[idx] = flag;
            this.ttMove[idx] = bestMove;
        }
        return best;
    };

    Engine.prototype.quiescence = function (alpha, beta, ply) {
        var b = this.board;
        if (this.stop) return 0;
        this.nodes++;
        if (this.timeUp()) return 0;

        var inCheck = b.inCheck();
        var best;
        if (!inCheck) {
            var stand = evaluate(b);
            if (stand >= beta) return stand;
            if (stand > alpha) alpha = stand;
            best = stand;
        } else {
            best = -INFINITY_SCORE;
        }
        if (ply >= MAX_PLY - 2) return inCheck ? evaluate(b) : best;

        var caps = this.caps[ply], scores = this.scores[ply];
        var n = b.generateMoves(caps, !inCheck);
        for (var i = 0; i < n; i++) {
            var m = caps[i];
            var victim = b.sq[mvTo(m)];
            if (mvFlags(m) & MF.EnPassant) victim = mkPiece(PT.Pawn, opp(b.side));
            scores[i] = victim !== 0
                ? PIECE_VALUE[typeOf(victim)] * 16 - PIECE_VALUE[typeOf(b.sq[mvFrom(m)])]
                : 90000 + PIECE_VALUE[mvPromo(m)];
        }

        var legal = 0;
        for (var k = 0; k < n; k++) {
            var bi = k;
            for (var j = k + 1; j < n; j++) if (scores[j] > scores[bi]) bi = j;
            if (bi !== k) {
                var tm = caps[k]; caps[k] = caps[bi]; caps[bi] = tm;
                var ts = scores[k]; scores[k] = scores[bi]; scores[bi] = ts;
            }
            var mv = caps[k];
            if (!b.tryMake(mv)) continue;
            legal++;
            this.pathLo[this.pathCount] = b.keyLo; this.pathHi[this.pathCount] = b.keyHi; this.pathCount++;
            var sc = -this.quiescence(-beta, -alpha, ply + 1);
            this.pathCount--;
            b.unmakeMove();
            if (this.stop) return 0;
            if (sc > best) best = sc;
            if (sc > alpha) alpha = sc;
            if (alpha >= beta) return best;
        }
        if (inCheck && legal === 0) return -MATE_SCORE + ply;
        return best;
    };

    // ------------------------------------------------------------------
    // 评估值 ↔ 胜率、着法分级
    // ------------------------------------------------------------------
    var Annotation = function (symbol, english, chinese, rank) {
        this.symbol = symbol; this.english = english; this.chinese = chinese; this.rank = rank;
    };
    var Annotations = {
        Brilliant: new Annotation('!!', 'Brilliant', '精彩', 0),
        Good: new Annotation('!', 'Good', '好棋', 1),
        Interesting: new Annotation('!?', 'Interesting', '有趣', 2),
        Dubious: new Annotation('?!', 'Dubious', '可疑', 3),
        Mistake: new Annotation('?', 'Mistake', '错着', 4),
        Blunder: new Annotation('??', 'Blunder', '大漏着', 5),
        Pending: new Annotation('…', 'Analyzing', '分析中', 6)
    };
    Annotations.All = [Annotations.Brilliant, Annotations.Good, Annotations.Interesting,
        Annotations.Dubious, Annotations.Mistake, Annotations.Blunder];

    var AnalysisMath = {
        toCp: function (score) {
            if (score > MATE_THRESHOLD) return 2000 - (MATE_SCORE - score);
            if (score < -MATE_THRESHOLD) return -2000 + (MATE_SCORE + score);
            if (score > 2000) return 2000;
            if (score < -2000) return -2000;
            return score;
        },
        winRate: function (cp) {
            var v = this.toCp(cp);
            var w = 50 + 50 * (2 / (1 + Math.exp(-0.00368208 * v)) - 1);
            if (w < 0.5) w = 0.5;
            if (w > 99.5) w = 99.5;
            return w;
        },
        formatScore: function (cp) {
            if (cp > MATE_THRESHOLD) return '将杀';
            if (cp < -MATE_THRESHOLD) return '-将杀';
            var p = cp / 100;
            return (p >= 0 ? '+' : '') + p.toFixed(2);
        }
    };

    var Analyzer = {
        analyze: function (position, historyLo, historyHi, profile, rng, engine) {
            var pa = {
                ply: 0, keyLo: position.keyLo, keyHi: position.keyHi, side: position.side,
                bestScoreCp: 0, bestMove: 0, moves: [], scores: [], terminal: false,
                depth: 0, nodes: 0, elapsedMs: 0
            };
            var legal = position.generateLegalMoves();
            if (legal.length === 0) {
                pa.terminal = true;
                pa.bestScoreCp = position.inCheck(position.side) ? -MATE_SCORE : 0;
                return pa;
            }
            var eng = engine || new Engine();
            var r = eng.search(position, historyLo, historyHi, profile, rng);
            if (!r || !r.roots) {
                pa.terminal = true;
                pa.bestScoreCp = -MATE_SCORE;
                return pa;
            }
            pa.depth = r.depth;
            pa.nodes = r.nodes;
            pa.elapsedMs = r.elapsedMs;
            pa.bestMove = r.move;
            pa.bestScoreCp = r.roots.length > 0 ? r.roots[0].score : r.score;
            for (var i = 0; i < r.roots.length; i++) {
                pa.moves.push(r.roots[i].move);
                pa.scores.push(r.roots[i].score);
            }
            return pa;
        },

        whiteCp: function (pa) { return pa.side === WHITE ? pa.bestScoreCp : -pa.bestScoreCp; },
        whiteWinRate: function (pa) { return AnalysisMath.winRate(this.whiteCp(pa)); },

        isOnlyMove: function (pa) {
            if (pa.scores.length < 2) return true;
            var best = pa.scores[0];
            if (best > MATE_THRESHOLD) return true;
            if (AnalysisMath.toCp(best) - AnalysisMath.toCp(pa.scores[1]) >= 150) return true;
            for (var i = 1; i < pa.scores.length; i++) {
                if (AnalysisMath.toCp(best) - AnalysisMath.toCp(pa.scores[i]) < 150) return false;
            }
            return true;
        },

        classify: function (lossCp, playedBest, onlyMove) {
            if (playedBest && onlyMove) return Annotations.Brilliant;
            if (lossCp <= 15) return Annotations.Good;
            if (lossCp <= 50) return Annotations.Interesting;
            if (lossCp <= 120) return Annotations.Dubious;
            if (lossCp <= 300) return Annotations.Mistake;
            return Annotations.Blunder;
        },

        judge: function (pa, played, moverSide) {
            var ma = { grade: Annotations.Pending, lossCp: 0, scoreCp: 0, bestScoreCp: 0, ready: false };
            if (!pa) return ma;
            ma.ready = true;
            if (pa.terminal) {
                ma.grade = Annotations.Good;
                ma.bestScoreCp = this.whiteCp(pa);
                ma.scoreCp = ma.bestScoreCp;
                return ma;
            }
            var playedScore = null;
            for (var i = 0; i < pa.moves.length; i++) {
                if (sameMove(pa.moves[i], played)) { playedScore = pa.scores[i]; break; }
            }
            if (playedScore === null) { ma.ready = false; return ma; }

            var best = pa.bestScoreCp;
            var loss = AnalysisMath.toCp(best) - AnalysisMath.toCp(playedScore);
            if (loss < 0) loss = 0;
            ma.lossCp = loss;
            ma.bestScoreCp = (moverSide === WHITE) ? best : -best;
            ma.scoreCp = (moverSide === WHITE) ? playedScore : -playedScore;
            var playedBest = playedScore >= best;
            ma.grade = this.classify(loss, playedBest, this.isOnlyMove(pa));
            return ma;
        }
    };

    // ------------------------------------------------------------------
    // 一局棋
    // ------------------------------------------------------------------
    var GS = {
        Playing: 0, Checkmate: 1, Stalemate: 2,
        DrawFiftyMove: 3, DrawRepetition: 4, DrawInsufficientMaterial: 5
    };

    function ChessGame(fen) {
        this.newGame(fen || START_FEN);
    }

    ChessGame.prototype.newGame = function (fen) {
        this.position = new Board().loadFen(fen || START_FEN);
        this.moveHistory = [];
        this.sanHistory = [];
        this.keysLo = [this.position.keyLo];
        this.keysHi = [this.position.keyHi];
        this.state = GS.Playing;
        this.winner = 0;
        this.updateState();
    };

    ChessGame.prototype.isOver = function () { return this.state !== GS.Playing; };
    ChessGame.prototype.plyCount = function () { return this.moveHistory.length; };

    ChessGame.prototype.legalMoves = function () { return this.position.generateLegalMoves(); };

    ChessGame.prototype.legalMovesFrom = function (s) {
        var all = this.legalMoves(), out = [];
        for (var i = 0; i < all.length; i++) if (mvFrom(all[i]) === s) out.push(all[i]);
        return out;
    };

    ChessGame.prototype.isLegal = function (m) {
        var all = this.legalMoves();
        for (var i = 0; i < all.length; i++) if (sameMove(all[i], m)) return true;
        return false;
    };

    ChessGame.prototype.play = function (m) {
        if (this.isOver()) return false;
        var san = this.position.san(m);
        if (!this.position.tryMake(m)) return false;
        this.moveHistory.push(m);
        this.sanHistory.push(san);
        this.keysLo.push(this.position.keyLo);
        this.keysHi.push(this.position.keyHi);
        this.updateState();
        return true;
    };

    ChessGame.prototype.undoLast = function () {
        if (this.moveHistory.length === 0) return;
        this.position.unmakeMove();
        this.moveHistory.pop();
        this.sanHistory.pop();
        this.keysLo.pop();
        this.keysHi.pop();
        this.state = GS.Playing;
        this.winner = 0;
        this.updateState();
    };

    ChessGame.prototype.undoPlies = function (n) {
        for (var i = 0; i < n && this.moveHistory.length > 0; i++) this.undoLast();
    };

    ChessGame.prototype.updateState = function () {
        this.state = GS.Playing;
        this.winner = 0;
        var legal = this.legalMoves();
        if (legal.length === 0) {
            if (this.position.inCheck(this.position.side)) {
                this.state = GS.Checkmate;
                this.winner = opp(this.position.side);
            } else {
                this.state = GS.Stalemate;
            }
            return;
        }
        if (this.position.halfmove >= 100) { this.state = GS.DrawFiftyMove; return; }
        var key = this.position.keyLo, key2 = this.position.keyHi, count = 0;
        for (var i = 0; i < this.keysLo.length; i++) if (this.keysLo[i] === key && this.keysHi[i] === key2) count++;
        if (count >= 3) { this.state = GS.DrawRepetition; return; }
        if (this.position.isInsufficientMaterial()) this.state = GS.DrawInsufficientMaterial;
    };

    ChessGame.prototype.stateText = function () {
        switch (this.state) {
            case GS.Checkmate: return (this.winner === WHITE ? '白方' : '黑方') + '将杀获胜';
            case GS.Stalemate: return '逼和 · 和棋';
            case GS.DrawFiftyMove: return '50 回合规则 · 和棋';
            case GS.DrawRepetition: return '三次重复局面 · 和棋';
            case GS.DrawInsufficientMaterial: return '子力不足 · 和棋';
        }
        if (this.position.inCheck(this.position.side)) {
            return (this.position.side === WHITE ? '白方' : '黑方') + '被将军';
        }
        return '';
    };

    ChessGame.prototype.stateReason = function () {
        switch (this.state) {
            case GS.Checkmate: return '将杀';
            case GS.Stalemate: return '逼和';
            case GS.DrawFiftyMove: return '50 回合规则';
            case GS.DrawRepetition: return '三次重复局面';
            case GS.DrawInsufficientMaterial: return '子力不足';
        }
        return '';
    };

    return {
        PT: PT, MF: MF, CR: CR, WHITE: WHITE, BLACK: BLACK, GS: GS,
        START_FEN: START_FEN,
        MATE_SCORE: MATE_SCORE, MATE_THRESHOLD: MATE_THRESHOLD, INFINITY_SCORE: INFINITY_SCORE,
        PIECE_VALUE: PIECE_VALUE,
        Board: Board, evaluate: evaluate, Engine: Engine,
        SkillTable: SkillTable, Annotations: Annotations, AnalysisMath: AnalysisMath,
        Analyzer: Analyzer, ChessGame: ChessGame,
        MV: MV, mvFrom: mvFrom, mvTo: mvTo, mvPromo: mvPromo, mvFlags: mvFlags,
        moveToUci: moveToUci, sameMove: sameMove,
        controlHas: controlHas, sqIndex: sqIndex,
        sqName: sqName, parseSq: parseSq, sqIndex: sqIndex,
        rankOf: rankOf, fileOf: fileOf, typeOf: typeOf, colorOf: colorOf, mkPiece: mkPiece, opp: opp,
        PIECE_LETTER: PIECE_LETTER
    };
});
