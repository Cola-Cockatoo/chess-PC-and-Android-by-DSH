/*
 * 手机版引擎自检（Node 运行）：perft、增量状态一致性、SAN、AI 解析、20 档难度
 * 用法： node mobile/test/engine_test.js [--quick]
 */
'use strict';

var C = require('../web/engine.js');
var quick = process.argv.indexOf('--quick') >= 0;

var checks = 0, failures = 0;
function check(ok, what) {
    checks++;
    if (!ok) { failures++; console.log('  [FAIL] ' + what); }
}
function info(s) { console.log(s); }
function isLegal(board, m) {
    var legal = board.generateLegalMoves();
    for (var i = 0; i < legal.length; i++) if (C.sameMove(legal[i], m)) return true;
    return false;
}

// ---------------- perft ----------------
function perft(board, depth) {
    if (depth === 0) return 1;
    var buf = new Int32Array(256);
    var n = board.generateMoves(buf, false);
    var nodes = 0;
    for (var i = 0; i < n; i++) {
        if (!board.tryMake(buf[i])) continue;
        nodes += depth === 1 ? 1 : perft(board, depth - 1);
        board.unmakeMove();
    }
    return nodes;
}

function perftLegal(board, depth) {
    if (depth === 0) return 1;
    var moves = board.generateLegalMoves();
    var nodes = 0;
    for (var i = 0; i < moves.length; i++) {
        board.makeMove(moves[i]);
        nodes += depth === 1 ? 1 : perftLegal(board, depth - 1);
        board.unmakeMove();
    }
    return nodes;
}

function perftCase(name, fen, expected, maxDepth) {
    var t0 = Date.now();
    for (var d = 1; d <= maxDepth && d <= expected.length; d++) {
        var b = new C.Board().loadFen(fen);
        var n = perft(b, d);
        check(n === expected[d - 1], name + ' perft(' + d + ') = ' + n + '，期望 ' + expected[d - 1]);
    }
    for (var d2 = 1; d2 <= Math.min(3, maxDepth); d2++) {
        var b1 = new C.Board().loadFen(fen), b2 = new C.Board().loadFen(fen);
        var a = perft(b1, d2), c = perftLegal(b2, d2);
        check(a === c, name + ' 两条生成路径不一致 depth ' + d2 + ': ' + a + ' vs ' + c);
    }
    info('  ' + name + ' ok (' + (Date.now() - t0) + ' ms)');
}

info('=== 手机版引擎自检 ===');
info('');
info('--- perft ---');
perftCase('startpos', C.START_FEN, [20, 400, 8902, 197281, 4865609], quick ? 4 : 5);
perftCase('kiwipete', 'r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1', [48, 2039, 97862, 4085603], quick ? 3 : 4);
perftCase('pos3', '8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1', [14, 191, 2812, 43238, 674624], 5);
perftCase('pos4', 'r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1', [6, 264, 9467, 422333], quick ? 3 : 4);
perftCase('pos5', 'rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 0 1', [44, 1486, 62379, 2103487], quick ? 3 : 4);
perftCase('pos6', 'r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 1', [46, 2079, 89890, 3894594], quick ? 3 : 4);

// ---------------- 增量状态一致性 ----------------
info('');
info('--- 一致性（哈希 / 增量评估 / 悔棋）---');
(function () {
    var seed = 12345;
    function rnd() { seed = (seed * 1103515245 + 12345) & 0x7fffffff; return seed / 0x7fffffff; }
    var games = 60, plies = 0;
    for (var g = 0; g < games; g++) {
        var b = new C.Board().loadFen(C.START_FEN);
        var fen0 = b.toFen(), key0 = { lo: b.keyLo, hi: b.keyHi };
        var played = [];
        for (var p = 0; p < 200; p++) {
            var moves = b.generateLegalMoves();
            if (moves.length === 0) break;
            var beforeFen = b.toFen(), beforeSide = b.side;
            var m = moves[Math.floor(rnd() * moves.length) % moves.length];
            b.makeMove(m);
            played.push(m);
            plies++;

            var rk = b.recomputeKey();
            if (rk.lo !== b.keyLo || rk.hi !== b.keyHi) { check(false, 'zobrist 不一致 ' + C.moveToUci(m) + ' ' + b.toFen()); break; }
            var mw = b.matPst[C.WHITE], mb = b.matPst[C.BLACK], ph = b.phase, pf = Array.prototype.slice.call(b.pawnFile);
            b.recomputeIncremental();
            if (b.matPst[C.WHITE] !== mw || b.matPst[C.BLACK] !== mb || b.phase !== ph) {
                check(false, '增量评估不一致 ' + C.moveToUci(m) + ' ' + b.toFen()); break;
            }
            for (var i = 0; i < pf.length; i++) {
                if (pf[i] !== b.pawnFile[i]) { check(false, '兵列统计不一致 ' + C.moveToUci(m)); break; }
            }
            b.unmakeMove();
            var rk2 = b.recomputeKey();
            if (b.toFen() !== beforeFen || rk2.lo !== b.keyLo || rk2.hi !== b.keyHi || b.side !== beforeSide) {
                check(false, '悔一步不一致 ' + C.moveToUci(m) + '\n    got      ' + b.toFen() + '\n    expected ' + beforeFen);
                break;
            }
            b.recomputeIncremental();
            b.makeMove(m);
        }
        for (var k = played.length - 1; k >= 0; k--) b.unmakeMove();
        check(b.toFen() === fen0, '全部悔棋恢复 FEN（第 ' + g + ' 局）');
        check(b.keyLo === key0.lo && b.keyHi === key0.hi, '全部悔棋恢复哈希（第 ' + g + ' 局）');
    }
    info('  随机对局步数: ' + plies + ' ok');
})();

// ---------------- SAN ----------------
info('');
info('--- SAN ---');
(function () {
    var b = new C.Board().loadFen(C.START_FEN);
    check(b.san(C.MV(C.parseSq('e2'), C.parseSq('e4'), 0, C.MF.DoublePush)) === 'e4', 'SAN e4');
    check(b.san(C.MV(C.parseSq('g1'), C.parseSq('f3'), 0, 0)) === 'Nf3', 'SAN Nf3');
    var b3 = new C.Board().loadFen('r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4');
    check(b3.san(C.MV(C.parseSq('e1'), 6, 0, C.MF.Castle)) === 'O-O', 'SAN O-O');
    var b4 = new C.Board().loadFen('8/P7/8/8/8/8/8/k3K3 w - - 0 1');
    check(b4.san(C.MV(C.parseSq('a7'), C.parseSq('a8'), C.PT.Queen, 0)) === 'a8=Q+', 'SAN a8=Q+ got ' + b4.san(C.MV(C.parseSq('a7'), C.parseSq('a8'), C.PT.Queen, 0)));
    var b5 = new C.Board().loadFen('6k1/5ppp/8/8/8/8/8/R6K w - - 0 1');
    check(b5.san(C.MV(C.parseSq('a1'), C.parseSq('a8'), 0, 0)) === 'Ra8#', 'SAN Ra8# got ' + b5.san(C.MV(C.parseSq('a1'), C.parseSq('a8'), 0, 0)));
    check(new C.Board().loadFen('8/8/8/8/8/8/8/K6k w - - 0 1').isInsufficientMaterial(), 'K vs K 子力不足');
    check(new C.Board().loadFen('8/8/8/8/8/8/8/K5Bk w - - 0 1').isInsufficientMaterial(), 'K+B vs K 子力不足');
    check(!new C.Board().loadFen('8/8/8/8/8/8/8/K4R1k w - - 0 1').isInsufficientMaterial(), 'K+R vs K 子力充足');
    info('  ok');
})();

// ---------------- 控制格（攻击范围） ----------------
info('');
info('--- 控制格 ---');
(function () {
    function has(mask, file, rank) { return C.controlHas(mask, file.charCodeAt(0) - 97, rank - 1); }

    var b = new C.Board().loadFen(C.START_FEN);
    var w = b.controlledSquares(C.WHITE), k = b.controlledSquares(C.BLACK);
    check(has(w, 'a', 3) && has(w, 'h', 3) && has(w, 'b', 3) && has(w, 'g', 3), '开局白兵控制 a3/b3/g3/h3');
    check(!has(w, 'a', 4) && !has(w, 'd', 4), '开局白兵不控制正前方');
    check(has(k, 'a', 6) && has(k, 'h', 6) && has(k, 'b', 6) && has(k, 'g', 6), '开局黑兵控制 a6/b6/g6/h6');

    // 兵斜吃
    b = new C.Board().loadFen('8/8/8/8/3P4/8/8/8 w - - 0 1');
    w = b.controlledSquares(C.WHITE);
    check(has(w, 'c', 5) && has(w, 'e', 5) && !has(w, 'd', 5), '白兵 d4 控制 c5/e5，不控制 d5');

    // 射线被挡即停
    b = new C.Board().loadFen('8/8/8/8/8/P7/8/R7 w - - 0 1');
    w = b.controlledSquares(C.WHITE);
    check(has(w, 'a', 2) && has(w, 'a', 3) && !has(w, 'a', 4), '白车 a1 控制 a2/a3，被己方兵挡住');

    // 后：直线 + 斜线
    b = new C.Board().loadFen('8/8/8/8/3Q4/8/8/8 w - - 0 1');
    w = b.controlledSquares(C.WHITE);
    check(has(w, 'd', 8) && has(w, 'd', 1) && has(w, 'a', 4) && has(w, 'h', 4), '白后 d4 控制十字四端');
    check(has(w, 'a', 1) && has(w, 'h', 8) && has(w, 'g', 1) && has(w, 'a', 7), '白后 d4 控制斜线四端');

    // 王的一步，不含自己站的格子
    b = new C.Board().loadFen('8/8/8/8/8/8/4K3/8 w - - 0 1');
    w = b.controlledSquares(C.WHITE);
    check(has(w, 'd', 1) && has(w, 'd', 3) && has(w, 'f', 3) && has(w, 'f', 1) && !has(w, 'e', 2), '白王 e2 控制周围 8 格');

    // 位序与桌面版一致：a1 = 最低位、h8 = 最高位
    b = new C.Board().loadFen('8/8/8/8/8/8/8/R6R w - - 0 1');
    w = b.controlledSquares(C.WHITE);
    check(has(w, 'b', 1) && has(w, 'g', 1) && has(w, 'a', 7) && has(w, 'h', 7), '两车控制整条 1 横排与 a/h 直线');
    check(C.controlHas({ lo: 1, hi: 0 }, 0, 0) && C.controlHas({ lo: 0, hi: 0x80000000 }, 7, 7), 'a1=最低位、h8=最高位');

    // 与 isSquareAttacked 完全一致 + 与桌面版同一套判定：随机对局逐步抽查
    var rng = 20260202, mm = 0, plies = 0;
    var g = new C.ChessGame();
    function rnd(n) { rng = (rng * 1103515245 + 12345) & 0x7fffffff; return rng % n; }
    for (var ply = 0; ply < 300; ply++) {
        var pos = g.position;
        var mw = pos.controlledSquares(C.WHITE), mb = pos.controlledSquares(C.BLACK);
        for (var f = 0; f < 8; f++) {
            for (var r = 0; r < 8; r++) {
                var sq = C.sqIndex(f, r);
                if (C.controlHas(mw, f, r) !== pos.isSquareAttacked(sq, C.WHITE)) mm++;
                if (C.controlHas(mb, f, r) !== pos.isSquareAttacked(sq, C.BLACK)) mm++;
            }
        }
        var ms = g.legalMoves();
        if (ms.length === 0) break;
        g.play(ms[rnd(ms.length)]);
        plies++;
    }
    check(plies > 80, '控制格一致性抽查走了 ' + plies + ' 步');
    check(mm === 0, '控制格掩码与 isSquareAttacked 完全一致（不符 ' + mm + ' 处）');
    info('  ok');
})();

// 极简版学习会话（只为测试用，逻辑与 study.js 一致）
function StudySessionForTest(op) {
    this.op = op;
    this.game = new C.ChessGame();
    this.next = op.root.slice();
    this.done = false;
    this.side = op.side === 'w' ? C.WHITE : C.BLACK;
}
StudySessionForTest.prototype.isStudentTurn = function () {
    return this.game.position.side === this.side;
};
StudySessionForTest.prototype.cands = function () {
    var out = [], legal = this.game.legalMoves();
    for (var i = 0; i < this.next.length; i++) {
        var nd = this.op.nodes[this.next[i]];
        for (var k = 0; k < legal.length; k++) {
            if (this.game.position.san(legal[k]) === nd.san) { out.push(this.next[i]); break; }
        }
    }
    return out;
};
StudySessionForTest.prototype.candidateCount = function () { return this.cands().length; };
StudySessionForTest.prototype.playIdx = function (idx) {
    var nd = this.op.nodes[idx], legal = this.game.legalMoves();
    for (var k = 0; k < legal.length; k++) {
        if (this.game.position.san(legal[k]) === nd.san) {
            if (!this.game.play(legal[k])) return false;
            this.next = nd.kids.slice();
            if (this.next.length === 0) this.done = true;
            return true;
        }
    }
    return false;
};
StudySessionForTest.prototype.playFirst = function () {
    var cs = this.cands();
    return cs.length ? this.playIdx(cs[0]) : false;
};
StudySessionForTest.prototype.playOpponent = function () { return this.playFirst(); };

// ---------------- AI 解析 ----------------
info('');
info('--- AI 解析 ---');
(function () {
    check(Math.abs(C.AnalysisMath.winRate(0) - 50) < 0.001, 'winRate(0)=50');
    check(C.AnalysisMath.winRate(300) > C.AnalysisMath.winRate(100), 'winRate 单调');
    check(C.AnalysisMath.toCp(C.MATE_SCORE - 1) === 1999, '将杀折算 1999');
    check(C.AnalysisMath.formatScore(135) === '+1.35', 'formatScore +1.35');
    check(C.AnalysisMath.formatScore(C.MATE_SCORE - 1) === '将杀', 'formatScore 将杀');

    check(C.Analyzer.classify(0, true, false) === C.Annotations.Good, 'loss 0 -> !');
    check(C.Analyzer.classify(0, true, true) === C.Annotations.Brilliant, 'only move -> !!');
    check(C.Analyzer.classify(30, false, false) === C.Annotations.Interesting, 'loss 30 -> !?');
    check(C.Analyzer.classify(80, false, false) === C.Annotations.Dubious, 'loss 80 -> ?!');
    check(C.Analyzer.classify(200, false, false) === C.Annotations.Mistake, 'loss 200 -> ?');
    check(C.Analyzer.classify(500, false, false) === C.Annotations.Blunder, 'loss 500 -> ??');
    check(C.Annotations.All.length === 6 && C.Annotations.All[0] === C.Annotations.Brilliant &&
        C.Annotations.All[5] === C.Annotations.Blunder, '符号表 优->劣');

    var profile = C.SkillTable.analysisProfile(6, 600);
    var rng = (function () { var s = 4242; return function () { s = (s * 1103515245 + 12345) & 0x7fffffff; return s / 0x7fffffff; }; })();
    var b = new C.Board().loadFen('4k3/8/8/3q4/8/8/8/3QK3 w - - 0 1');
    var legal = b.generateLegalMoves();
    var pa = C.Analyzer.analyze(b, null, null, profile, rng, new C.Engine());
    check(!pa.terminal, '分析出结果');
    check(pa.moves.length === legal.length, '分析覆盖全部合法着法 (' + pa.moves.length + '/' + legal.length + ')');
    check(pa.depth >= 3, '分析深度 >= 3 (' + pa.depth + ')');
    check(pa.elapsedMs <= profile.timeMs + 1500, '分析不超时 (' + pa.elapsedMs + 'ms)');
    var sorted = true;
    for (var i = 1; i < pa.scores.length; i++) if (pa.scores[i] > pa.scores[i - 1]) sorted = false;
    check(sorted, '根节点分数降序');
    check(pa.bestScoreCp > 700, '最佳着法白吃后 (+' + pa.bestScoreCp + ')');
    var qxd5 = null, qd2 = null;
    for (var k = 0; k < legal.length; k++) {
        var s = b.san(legal[k]);
        if (s === 'Qxd5') qxd5 = legal[k];
        if (s === 'Qd2') qd2 = legal[k];
    }
    check(qxd5 !== null, '找到 Qxd5');
    check(pa.bestMove === qxd5, '最佳着法是 Qxd5');
    var g1 = C.Analyzer.judge(pa, qxd5, C.WHITE);
    check(g1.ready && g1.grade === C.Annotations.Brilliant, '白吃后 -> !! (' + g1.grade.symbol + ')');
    var g2 = C.Analyzer.judge(pa, qd2, C.WHITE);
    check(g2.ready && g2.grade.rank >= C.Annotations.Mistake.rank, '放过黑后 -> 差评 (' + g2.grade.symbol + ', 损失 ' + g2.lossCp + ')');
    var mated = C.Analyzer.analyze(new C.Board().loadFen('7k/6Q1/6K1/8/8/8/8/8 b - - 0 1'), null, null, profile, rng, new C.Engine());
    check(mated.terminal && mated.bestScoreCp < -C.MATE_THRESHOLD, '将杀局面 -> terminal');
    var sp = C.Analyzer.analyze(new C.Board().loadFen(C.START_FEN), null, null, profile, rng, new C.Engine());
    check(sp.moves.length === 20, '开局 20 个着法');
    check(Math.abs(C.Analyzer.whiteCp(sp)) < 150, '开局接近均势 (' + C.Analyzer.whiteCp(sp) + 'cp)');
    info('  开局: ' + sp.moves.length + ' 着法, 深度 ' + sp.depth + ', 白方 ' + C.Analyzer.whiteCp(sp) + 'cp, 胜率 ' +
        C.Analyzer.whiteWinRate(sp).toFixed(1) + '%, ' + sp.elapsedMs + 'ms');
})();

// ---------------- 20 档难度 ----------------
info('');
info('--- 20 档难度 ---');
(function () {
    var fens = [
        C.START_FEN,
        'r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 4 4',
        '8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1',
        '6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1'
    ];
    var rng = (function () { var s = 777; return function () { s = (s * 1103515245 + 12345) & 0x7fffffff; return s / 0x7fffffff; }; })();
    for (var t = 0; t < C.SkillTable.levels.length; t++) {
        var skill = C.SkillTable.levels[t];
        var worst = 0;
        for (var f = 0; f < fens.length; f++) {
            var b = new C.Board().loadFen(fens[f]);
            var eng = new C.Engine();
            var t0 = Date.now();
            var r = eng.search(b, null, null, skill, rng);
            var ms = Date.now() - t0;
            check(r !== null, 'ELO ' + skill.elo + ' 有结果');
            if (!r) continue;
            check(isLegal(b, r.move), 'ELO ' + skill.elo + ' 着法合法 ' + C.moveToUci(r.move));
            if (ms > worst) worst = ms;
            check(ms < skill.timeMs + 4000, 'ELO ' + skill.elo + ' 不超时 (' + ms + 'ms)');
        }
        info('  ELO ' + skill.elo + ' (' + skill.tier + ') 深度<=' + skill.maxDepth + ' 时间<=' + skill.timeMs + 'ms  最慢 ' + worst + 'ms');
    }
    var mate = new C.Board().loadFen('7k/6Q1/6K1/8/8/8/8/8 b - - 0 1');
    check(!mate.hasLegalMove(), '将杀局面无着法');
    check(new C.Engine().search(mate, null, null, C.SkillTable.forElo(2000), rng) === null, '将杀局面返回 null');
    var m1 = new C.Board().loadFen('7k/8/6K1/8/8/8/8/Q7 w - - 0 1');
    var r1 = new C.Engine().search(m1, null, null, C.SkillTable.forElo(2000), rng);
    check(r1 !== null && m1.san(r1.move).indexOf('#') >= 0, '能找到一步杀：' + (r1 ? m1.san(r1.move) : 'null'));
})();

info('');

// ---------------- 定式学习：数据完整性与每条变化是否走得通 ----------------
info('');
info('--- 定式学习 ---');
(function () {
    global.window = global.window || {};
    require('../web/openings.js');
    var ops = global.window.ChessOpenings;
    check(ops && ops.length >= 12, '定式数量 ' + (ops ? ops.length : 0) + ' 个');

    var totalNodes = 0, branchPoints = 0, studentBranches = 0, leaves = 0, paths = 0;
    var bad = [];

    function walk(op, nodes, line, depth) {
        if (!nodes || nodes.length === 0) {
            paths++;
            return;
        }
        var student = (op.side === 'w') === (depth % 2 === 0);
        if (student && nodes.length > 1) studentBranches++;
        for (var i = 0; i < nodes.length; i++) {
            var nd = op.nodes[nodes[i]];
            // 用"这条线走到这里"的局面验证这一步能走
            var g = new C.ChessGame();
            var ok = true;
            for (var k = 0; k < line.length; k++) {
                var mv = findSan(g.position, line[k]);
                if (!mv || !g.play(mv)) { ok = false; break; }
            }
            var m = ok ? findSan(g.position, nd.san) : null;
            if (!m) {
                bad.push(op.name + '：' + line.join(' ') + (line.length ? ' ' : '') + nd.san);
                continue;
            }
            if (!nd.comment) bad.push(op.name + ' ' + nd.san + ' 缺少点评');
            line.push(nd.san);
            walk(op, nd.kids, line, depth + 1);
            line.pop();
        }
    }

    function findSan(pos, san) {
        var legal = pos.generateLegalMoves();
        for (var i = 0; i < legal.length; i++) if (pos.san(legal[i]) === san) return legal[i];
        return null;
    }

    for (var i = 0; i < ops.length; i++) {
        var op = ops[i];
        totalNodes += op.nodes.length;
        check(op.name && op.side && op.eco && op.summary, op.name + ' 字段齐全');
        check(op.level && op.level.length > 0, op.name + ' 有难度定位：' + op.level);
        check(op.plans && op.plans.length >= 2, op.name + ' 有典型计划 ' + (op.plans ? op.plans.length : 0) + ' 条');
        check(op.pitfalls && op.pitfalls.length >= 1, op.name + ' 有常见错误 ' + (op.pitfalls ? op.pitfalls.length : 0) + ' 条');
        check(op.root.length > 0, op.name + ' 有第一步');
        for (var k = 0; k < op.nodes.length; k++) {
            if (op.nodes[k].kids.length > 1) branchPoints++;
            if (op.nodes[k].kids.length === 0) leaves++;
        }
        walk(op, op.root, [], 0);

        // 用 StudySession 走一遍主线，确认能推进到结束
        var s = new StudySessionForTest(op);
        var guard = 0;
        while (!s.done && guard++ < 200) {
            if (s.isStudentTurn()) {
                if (!s.playFirst()) break;
            } else if (!s.playOpponent()) break;
        }
        check(s.done, op.name + ' 主线能走到结束（' + guard + ' 步）');
    }

    check(bad.length === 0, '所有变化都合法且带点评（问题 ' + bad.length + ' 处）' + (bad.length ? '：' + bad[0] : ''));
    check(totalNodes > 200, '节点总数 ' + totalNodes);
    check(leaves >= 15, '变化结尾 ' + leaves + ' 个');
    check(branchPoints >= 10, '分支点 ' + branchPoints + ' 个');
    check(studentBranches >= 8, '学员可选的分支 ' + studentBranches + ' 个（会显示多个箭头）');
    check(ops.length >= 24, '开局数已达 ' + ops.length + ' 个');
    var namedNodes = 0, noted = 0;
    for (var q = 0; q < ops.length; q++) {
        for (var w = 0; w < ops[q].nodes.length; w++) {
            var nd2 = ops[q].nodes[w];
            if (nd2.name && nd2.name.length > 0) {
                namedNodes++;
                if (nd2.note && nd2.note.length > 0) noted++;
            }
        }
    }
    check(namedNodes >= 48, '有命名的变化 ' + namedNodes + ' 个（少了说明有名字没挂到岔口上）');
    check(noted === namedNodes, '每个命名变化都有说明（' + noted + '/' + namedNodes + '）');

    // 分支处的箭头数：英国式 1.c4 e5 之后应当有 2 个
    (function () {
        var en = null;
        for (var i = 0; i < ops.length; i++) if (ops[i].id === 'english') en = ops[i];
        check(!!en, '找到英国式开局');
        if (!en) return;
        var s = new StudySessionForTest(en);
        check(s.candidateCount() === 1, '英国式第一步 1 个候选');
        s.playFirst();
        s.playOpponent();
        check(s.candidateCount() === 2, 'e5 之后出现 2 个候选（实际 ' + s.candidateCount() + '）');
    })();
    info('  ok');
})();

info('checks: ' + checks + ', failures: ' + failures);
info(failures === 0 ? 'ALL TESTS PASSED' : 'TESTS FAILED');
process.exit(failures === 0 ? 0 : 1);
