/*
 * 手机版界面：canvas 棋盘 + 触摸交互 + 三个界面（主页 / 难度 / 对局）+ AI 解析面板
 * 依赖 engine.js（规则与引擎）与 pieces.js（棋子精灵图，base64）
 */
(function () {
    'use strict';

    var C = window.Chess;
    var WHITE = C.WHITE, BLACK = C.BLACK;
    var ANALYSIS_DEPTH = 5, ANALYSIS_MS = 350;     // 手机端解析档位（比桌面轻一些）
    var AI_MIN_THINK_MS = 450;                     // 电脑最少"思考"时间，避免瞬间落子

    // ---------------- 元素 ----------------
    function $(id) { return document.getElementById(id); }
    var screens = { home: $('screen-home'), difficulty: $('screen-difficulty'), game: $('screen-game'),
        openings: $('screen-openings'), study: $('screen-study'),
        review: $('screen-review') };
    var canvas = $('board'), ctx = canvas.getContext('2d');
    var chart = $('chart'), cctx = chart.getContext('2d');
    var sheet = new Image();

    // ---------------- 状态 ----------------
    var app = {
        screen: 'home',
        mode: 'ai',                 // ai | pvp
        skill: null,
        game: null,
        humanSide: WHITE,
        aiThinking: false,
        aiTimer: null,
        selected: -1,
        targets: [],
        lastMove: 0,
        analysisOn: false,
        showControl: false,         // 是否叠加显示控制格（双方攻击范围）
        analyses: [],               // 按 ply 索引
        annotations: [],            // 按手数索引
        analysisQueue: [],
        analysisRunning: false,
        analysisGen: 0,
        promoResolve: null
    };

    var AI_SIDE = BLACK, HUMAN_SIDE = WHITE;

    // ---------------- 通用 ----------------
    function showScreen(name) {
        app.screen = name;
        for (var k in screens) {
            if (screens.hasOwnProperty(k)) screens[k].classList.toggle('active', k === name);
        }
        if (name === 'game') resizeAll();
    }

    function setText(id, text) { var el = $(id); if (el) el.textContent = text; }

    // 棋子颜色（与桌面版一致的配色）
    var SYM_COLOR = {
        '!!': '#1B7F79', '!': '#4E7C6A', '!?': '#7A935C', '?!': '#C09635', '?': '#D2762F', '??': '#C25B4E', '…': '#8E8B84'
    };
    function symColor(sym) { return SYM_COLOR[sym] || '#8E8B84'; }

    // ---------------- 棋盘绘制 ----------------
    var boardSize = 0, cell = 0;

    /** 真正的布局视口宽度（headless 下 window.innerWidth 可能偏大，取两者较小值最稳） */
    function viewportWidth() {
        var a = window.innerWidth || 99999;
        var b = document.documentElement ? (document.documentElement.clientWidth || 99999) : 99999;
        return Math.min(a, b);
    }

    function layoutBoard() {
        var wrap = document.querySelector('.board-wrap');
        var area = document.querySelector('.board-area');
        if (!wrap || !area) return;
        var cs = window.getComputedStyle(area);
        var pad = (parseFloat(cs.paddingLeft) || 0) + (parseFloat(cs.paddingRight) || 0);
        var availW = area.clientWidth - pad;
        // 高度以 .game-body 为准：它现在是"棋盘 + 下方面板"的整体滚动容器，
        // 棋盘先占满第一屏，往下滚动即可看到走棋记录与 AI 解析。
        var body = document.querySelector('.game-body');
        var bodyH = body ? body.clientHeight : 0;
        var availH = (bodyH > 60 ? bodyH : area.clientHeight) - 12;
        var vw = viewportWidth();
        var size = Math.min(availW, Math.max(availH, 160));
        if (window.__dbg) {
            var gs = document.getElementById('screen-game');
            var tb = document.querySelector('#screen-game .topbar');
            var apx = document.getElementById('app');
            document.title = 'vw=' + vw + ' vh=' + window.innerHeight + ' appH=' + (apx ? apx.clientHeight : -1) +
                ' screenH=' + (gs ? gs.clientHeight : -1) + ' topbarH=' + (tb ? tb.clientHeight : -1) +
                ' areaW=' + area.clientWidth + ' availH=' + availH + ' size=' + Math.floor(size);
        }
        if (!(size > 0)) size = Math.max(160, vw - 12);
        if (vw > 0 && size > vw - 8) size = vw - 8;
        size = Math.max(120, Math.floor(size));
        if (size === boardSize) return;
        boardSize = size;
        cell = size / 8;
        var dpr = window.devicePixelRatio || 1;
        canvas.style.width = size + 'px';
        canvas.style.height = size + 'px';
        canvas.width = Math.round(size * dpr);
        canvas.height = Math.round(size * dpr);
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        drawBoard();
    }

    function resizeAll() {
        layoutBoard();
        layoutChart();
    }

    function rectOf(sq, whiteBottom) {
        var r = C.rankOf(sq), f = C.fileOf(sq);
        var col = whiteBottom ? f : 7 - f;
        var row = whiteBottom ? 7 - r : r;
        return { x: col * cell, y: row * cell, w: cell, h: cell };
    }

    function squareAt(px, py, whiteBottom) {
        var col = Math.floor(px / cell), row = Math.floor(py / cell);
        if (col < 0 || col > 7 || row < 0 || row > 7) return -1;
        var f = whiteBottom ? col : 7 - col;
        var r = whiteBottom ? 7 - row : row;
        return C.sqIndex(f, r);
    }

    // 「控制格」四色：黑方控制 → 黑；白方控制 → 白；双方 → 浅灰；都没 → 浅绿（棋盘主色）
    var CONTROL_COLORS = { black: '#33322E', white: '#FFFFFF', both: '#D8D6D0', none: '#A7B69D' };

    function controlColor(hasWhite, hasBlack) {
        if (hasWhite && hasBlack) return CONTROL_COLORS.both;
        if (hasBlack) return CONTROL_COLORS.black;
        if (hasWhite) return CONTROL_COLORS.white;
        return CONTROL_COLORS.none;
    }

    function drawBoard() {
        if (!app.game || boardSize === 0) return;
        var b = app.game.position;
        var whiteBottom = whiteBottomNow();
        ctx.clearRect(0, 0, boardSize, boardSize);

        // 「控制格」视图：整块棋盘按"谁控制这一格"重新上色，覆盖原来的深浅格
        var controlView = !!app.showControl;
        var wm = 0, bm = 0;
        if (controlView) {
            wm = b.controlledSquares(WHITE);
            bm = b.controlledSquares(BLACK);
        }
        for (var r = 0; r < 8; r++) {
            for (var f = 0; f < 8; f++) {
                var sq = C.sqIndex(f, r);
                var rc = rectOf(sq, whiteBottom);
                if (controlView) {
                    ctx.fillStyle = controlColor(C.controlHas(wm, f, r), C.controlHas(bm, f, r));
                } else {
                    ctx.fillStyle = ((f + r) % 2 !== 0) ? '#F1ECE2' : '#A7B69D';
                }
                ctx.fillRect(rc.x, rc.y, rc.w, rc.h);
            }
        }
        if (controlView) {
            // 淡网格线：同色的相邻格否则会连成一片
            ctx.strokeStyle = 'rgba(110,108,102,0.16)';
            ctx.lineWidth = 1;
            for (var gi = 1; gi < 8; gi++) {
                ctx.beginPath(); ctx.moveTo(gi * cell, 0); ctx.lineTo(gi * cell, boardSize); ctx.stroke();
                ctx.beginPath(); ctx.moveTo(0, gi * cell); ctx.lineTo(boardSize, gi * cell); ctx.stroke();
            }
        }

        // 上一步高亮（控制格视图下会干扰色块辨识，故不画）
        if (app.lastMove && !controlView) {
            ctx.fillStyle = 'rgba(78,124,106,0.18)';
            var a = rectOf(C.mvFrom(app.lastMove), whiteBottom);
            var c = rectOf(C.mvTo(app.lastMove), whiteBottom);
            ctx.fillRect(a.x, a.y, a.w, a.h);
            ctx.fillRect(c.x, c.y, c.w, c.h);
        }
        // 被将军
        if (b.inCheck(b.side)) {
            var k = b.kingSq[b.side];
            if (k || b.sq[0] === C.mkPiece(C.PT.King, b.side)) {
                var kr = rectOf(k, whiteBottom);
                ctx.fillStyle = 'rgba(194,91,78,0.35)';
                ctx.fillRect(kr.x, kr.y, kr.w, kr.h);
            }
        }
        // 选中
        if (app.selected >= 0) {
            var sr = rectOf(app.selected, whiteBottom);
            ctx.fillStyle = 'rgba(78,124,106,0.32)';
            ctx.fillRect(sr.x, sr.y, sr.w, sr.h);
        }
        // 可走点
        for (var i = 0; i < app.targets.length; i++) {
            var to = C.mvTo(app.targets[i]);
            var tr = rectOf(to, whiteBottom);
            var isCap = b.sq[to] !== 0 || (C.mvFlags(app.targets[i]) & C.MF.EnPassant);
            if (isCap) {
                ctx.strokeStyle = 'rgba(60,98,83,0.55)';
                ctx.lineWidth = Math.max(2, cell * 0.055);
                ctx.beginPath();
                var inset = cell * 0.08;
                ctx.arc(tr.x + tr.w / 2, tr.y + tr.h / 2, tr.w / 2 - inset, 0, Math.PI * 2);
                ctx.stroke();
            } else {
                ctx.fillStyle = 'rgba(60,98,83,0.35)';
                ctx.beginPath();
                ctx.arc(tr.x + tr.w / 2, tr.y + tr.h / 2, cell * 0.155, 0, Math.PI * 2);
                ctx.fill();
            }
        }

        // 棋子（精灵图）
        if (sheet.complete && sheet.naturalWidth > 0) {
            for (var rr = 0; rr < 8; rr++) {
                for (var ff = 0; ff < 8; ff++) {
                    var s2 = C.sqIndex(ff, rr);
                    var p = b.sq[s2];
                    if (p === 0) continue;
                    var rc2 = rectOf(s2, whiteBottom);
                    var type = C.typeOf(p), color = C.colorOf(p);
                    var sx = (type - 1) * 128, sy = (color === WHITE ? 0 : 1) * 128;
                    var pad = cell * 0.06;
                    if (controlView && color === BLACK) {
                        // 黑棋在"黑方控制"的黑格上会看不清 —— 加一层白色光晕
                        ctx.shadowColor = '#FFFFFF';
                        ctx.shadowBlur = cell * 0.10;
                    }
                    ctx.drawImage(sheet, sx, sy, 128, 128, rc2.x + pad, rc2.y + pad, rc2.w - pad * 2, rc2.h - pad * 2);
                    if (controlView && color === BLACK) {
                        ctx.shadowColor = 'transparent';
                        ctx.shadowBlur = 0;
                    }
                }
            }
        }


        // 坐标
        ctx.fillStyle = 'rgba(60,98,83,0.55)';
        ctx.font = Math.max(9, Math.round(cell * 0.19)) + 'px system-ui, sans-serif';
        ctx.textAlign = 'left'; ctx.textBaseline = 'top';
        for (var i2 = 0; i2 < 8; i2++) {
            var file = whiteBottom ? i2 : 7 - i2;
            ctx.fillText(String.fromCharCode(97 + file), i2 * cell + 3, boardSize - cell + 3);
            var rank = whiteBottom ? 7 - i2 : i2;
            ctx.fillText(String(rank + 1), 3, i2 * cell + 3);
        }
    }

    function whiteBottomNow() {
        if (app.mode === 'pvp') return app.game.position.side === WHITE;
        return app.humanSide === WHITE;    // 人机对战：己方始终在下方
    }

    // ---------------- 触摸交互 ----------------
    // 说明：不用 Pointer Events（需要 Chrome 55+），改用 touchstart + mousedown，
    // 老机型上的 WebView 也能正常落子。
    var lastTouchAt = 0;
    function onBoardDown(ev) {
        if (ev.type === 'mousedown' && (Date.now() - lastTouchAt) < 700) return;   // 触摸后的兼容鼠标事件，忽略
        if (ev.type === 'touchstart') {
            lastTouchAt = Date.now();
            if (ev.preventDefault) ev.preventDefault();
        }
        var src = (ev.touches && ev.touches.length) ? ev.touches[0] : ev;
        if (!app.game || app.game.isOver() || app.aiThinking) return;
        var rect = canvas.getBoundingClientRect();
        var x = (src.clientX - rect.left), y = (src.clientY - rect.top);
        var sq = squareAt(x, y, whiteBottomNow());
        if (sq < 0) return;
        var b = app.game.position;
        if (app.mode === 'ai' && b.side !== app.humanSide) return;

        var p = b.sq[sq];
        if (p !== 0 && C.colorOf(p) === b.side) {
            if (app.selected === sq) { app.selected = -1; app.targets = []; }
            else { app.selected = sq; app.targets = app.game.legalMovesFrom(sq); }
            drawBoard();
            return;
        }
        if (app.selected >= 0) {
            for (var i = 0; i < app.targets.length; i++) {
                if (C.mvTo(app.targets[i]) === sq) {
                    var from = app.selected;
                    var cands = [];
                    for (var k = 0; k < app.targets.length; k++) {
                        if (C.mvTo(app.targets[k]) === sq) cands.push(app.targets[k]);
                    }
                    app.selected = -1; app.targets = [];
                    if (cands.length > 1) {
                        askPromotion(b.side).then(function (piece) {
                            var chosen = cands[0];
                            for (var j = 0; j < cands.length; j++) if (C.mvPromo(cands[j]) === piece) chosen = cands[j];
                            applyMove(chosen);
                        });
                    } else {
                        applyMove(cands[0]);
                    }
                    return;
                }
            }
        }
        app.selected = -1; app.targets = [];
        drawBoard();
    }
    canvas.addEventListener('touchstart', onBoardDown, false);
    canvas.addEventListener('mousedown', onBoardDown, false);

    // ---------------- 对局流程 ----------------
    function startGame(mode, skill) {
        hideReviewButton();
        app.mode = mode;
        app.skill = skill;
        app.game = new C.ChessGame();
        if (mode !== 'ai') app.humanSide = WHITE;      // 人机对战沿用难度页选的执子
        app.aiThinking = false;
        app.selected = -1; app.targets = []; app.lastMove = 0;
        app.analyses = []; app.annotations = []; app.analysisQueue = [];
        app.analysisGen++;
        if (mode === 'ai') {
            setText('gameTitle', '人机对战');
            setText('gameSub', 'ELO ' + skill.elo + ' · ' + skill.tier + ' · ' + (app.humanSide === WHITE ? '你执白先行' : '你执黑后行'));
            $('btnAnalysis').style.display = '';
        } else {
            setText('gameTitle', '人人对战（本地）');
            setText('gameSub', '每走一步自动翻转棋盘');
            $('btnAnalysis').style.display = 'none';
            app.analysisOn = false;
            $('analysis').classList.remove('on');
            $('btnAnalysis').classList.remove('on');
            $('btnAnalysis').textContent = 'AI 解析 · 关';
        }
        showScreen('game');
        updateHud();
        renderMoves();
        drawBoard();
        if (app.analysisOn) requestAnalysis(app.game.plyCount());
        if (mode === 'ai' && app.game.position.side !== app.humanSide) startAiTurn();
    }

    function applyMove(m) {
        if (!app.game.play(m)) return;
        app.lastMove = m;
        app.selected = -1; app.targets = [];
        updateHud();
        renderMoves();
        drawBoard();

        // AI 解析（若开启）：先给这一手定级，再排当前局面的分析
        if (app.analysisOn) {
            var idx = app.game.plyCount() - 1;
            var pa = app.analyses[idx];
            if (pa) {
                var mover = (idx % 2 === 0) ? WHITE : BLACK;
                app.annotations[idx] = C.Analyzer.judge(pa, app.game.moveHistory[idx], mover);
            }
            requestAnalysis(idx);
            requestAnalysis(app.game.plyCount());
            renderAnalysis();
            renderMoves();
        }

        if (app.game.isOver()) { showResult(); return; }
        if (app.mode === 'ai' && app.game.position.side !== app.humanSide) startAiTurn();
    }

    function startAiTurn() {
        if (app.aiThinking || app.game.isOver()) return;
        app.aiThinking = true;
        updateHud();
        var dots = 0;
        var ticker = setInterval(function () {
            if (!app.aiThinking) return;
            dots = (dots + 1) % 4;
            setText('turnText', '电脑思考中' + new Array(dots + 1).join('.'));
        }, 300);
        // 先让界面把"思考中"画出来，再跑同步搜索（WebView 里没有 Worker，用一次让步来避免白屏）
        setTimeout(function () {
            var snapshot = app.game.position.clone();
            var eng = new C.Engine();
            var rng = mulberry32((Date.now() ^ (app.game.plyCount() * 2654435761)) >>> 0);
            var t0 = Date.now();
            var result = null;
            try {
                result = eng.search(snapshot, app.game.keysLo, app.game.keysHi, app.skill, rng);
            } catch (e) { result = null; }
            var wait = AI_MIN_THINK_MS - (Date.now() - t0);
            if (wait > 0) {
                setTimeout(function () { finishAi(result); }, wait);
            } else {
                finishAi(result);
            }
            function finishAi(res) {
                clearInterval(ticker);
                app.aiThinking = false;
                if (!res) { updateHud(); return; }
                var legal = app.game.legalMoves();
                var chosen = null;
                for (var i = 0; i < legal.length; i++) if (C.sameMove(legal[i], res.move)) { chosen = legal[i]; break; }
                if (!chosen && legal.length > 0) chosen = legal[0];
                if (!chosen) { updateHud(); return; }
                applyMove(chosen);
            }
        }, 40);
    }

    function mulberry32(a) {
        return function () {
            a |= 0; a = (a + 0x6D2B79F5) | 0;
            var t = Math.imul(a ^ (a >>> 15), 1 | a);
            t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
            return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
        };
    }

    function updateHud() {
        var b = app.game.position;
        $('turnDot').className = 'dot ' + (b.side === WHITE ? 'white' : 'black');
        var turn;
        if (app.aiThinking) turn = '电脑思考中…';
        else if (app.game.isOver()) {
            turn = app.game.state === C.GS.Checkmate
                ? (app.game.winner === WHITE ? '白方获胜' : '黑方获胜') : '和棋';
        } else {
            turn = b.side === WHITE ? '白方走棋' : '黑方走棋';
            if (app.mode === 'ai') turn += (b.side === app.humanSide) ? '（你）' : '（电脑）';
        }
        setText('turnText', turn);
        setText('roundText', '第 ' + b.fullmove + ' 回合');
        var st = '', cls = 'status';
        if (app.game.isOver()) { st = app.game.stateReason(); if (app.game.state === C.GS.Checkmate) cls += ' check'; }
        else if (!app.aiThinking && b.inCheck(b.side)) { st = '将军！'; cls += ' check'; }
        var el = $('statusText');
        el.textContent = st;
        el.className = cls;
        $('btnUndo').disabled = app.aiThinking || app.game.plyCount() === 0;
    }

    function showResult() {
        updateHud();
        var title, sub;
        if (app.game.state === C.GS.Checkmate) {
            title = app.mode === 'ai'
                ? (app.game.winner === app.humanSide ? '你赢了' : '电脑获胜')
                : (app.game.winner === WHITE ? '白方获胜' : '黑方获胜');
            sub = '将杀 · 共 ' + Math.ceil(app.game.plyCount() / 2) + ' 回合';
        } else {
            title = '和棋';
            sub = app.game.stateReason();
        }
        setText('statusText', title + ' · ' + sub);
        $('statusText').className = 'status check';
        renderMoves();
        showReviewButton();
    }

    function renderMoves() {
        var box = $('moves');
        var sans = app.game.sanHistory;
        if (sans.length === 0) { box.innerHTML = '<div class="empty">对局尚未开始</div>'; return; }
        var rows = Math.ceil(sans.length / 2);
        var html = '';
        for (var i = 0; i < rows; i++) {
            var last = (i === rows - 1);
            html += '<div class="mv' + (last ? ' cur' : '') + '">';
            html += '<span class="no">' + (i + 1) + '.</span>';
            html += '<span class="san">' + sans[i * 2] + '</span>';
            html += '<span class="sym" style="color:' + symOf(i * 2) + '">' + symText(i * 2) + '</span>';
            if (i * 2 + 1 < sans.length) {
                html += '<span class="san2">' + sans[i * 2 + 1] + '</span>';
                html += '<span class="sym2" style="color:' + symOf(i * 2 + 1) + '">' + symText(i * 2 + 1) + '</span>';
            }
            html += '</div>';
        }
        box.innerHTML = html;
        box.scrollTop = box.scrollHeight;
    }

    function symText(i) {
        if (!app.analysisOn) return '';
        var ma = app.annotations[i];
        if (!ma || !ma.ready) return '…';
        return ma.grade.symbol;
    }
    function symOf(i) {
        var ma = app.annotations[i];
        if (!ma || !ma.ready) return '#8E8B84';
        return symColor(ma.grade.symbol);
    }

    // ---------------- AI 解析 ----------------
    function requestAnalysis(ply) {
        if (!app.analysisOn) return;
        if (ply < 0 || ply > app.game.plyCount()) return;
        if (app.analyses[ply]) return;
        for (var i = 0; i < app.analysisQueue.length; i++) if (app.analysisQueue[i] === ply) return;
        app.analysisQueue.push(ply);
        pumpAnalysis();
    }

    function pumpAnalysis() {
        if (app.analysisRunning || app.analysisQueue.length === 0 || !app.analysisOn) return;
        app.analysisRunning = true;
        var ply = app.analysisQueue.shift();
        var gen = app.analysisGen;
        // 让界面先画完再算，避免卡顿感
        setTimeout(function () {
            var pa = null;
            try {
                var b = new C.Board().loadFen(app.game.keysLo.length ? C.START_FEN : C.START_FEN);
                b = new C.Board().loadFen(C.START_FEN);
                var lo = [b.keyLo], hi = [b.keyHi];
                for (var i = 0; i < ply; i++) {
                    if (!b.tryMake(app.game.moveHistory[i])) { b = null; break; }
                    lo.push(b.keyLo); hi.push(b.keyHi);
                }
                if (b) {
                    pa = C.Analyzer.analyze(b, lo, hi, C.SkillTable.analysisProfile(ANALYSIS_DEPTH, ANALYSIS_MS),
                        Math.random, new C.Engine());
                    pa.ply = ply;
                }
            } catch (e) { pa = null; }
            app.analysisRunning = false;
            if (gen !== app.analysisGen) { pumpAnalysis(); return; }
            if (pa) {
                app.analyses[ply] = pa;
                if (ply < app.game.plyCount()) {
                    var mover = (ply % 2 === 0) ? WHITE : BLACK;
                    app.annotations[ply] = C.Analyzer.judge(pa, app.game.moveHistory[ply], mover);
                }
                renderAnalysis();
                if (app.analysisOn) renderMoves();
            }
            pumpAnalysis();
        }, 30);
    }

    function currentAnalysis() {
        var cur = app.game.plyCount();
        if (app.analyses[cur]) return app.analyses[cur];
        for (var i = cur; i >= 0; i--) if (app.analyses[i]) return app.analyses[i];
        return null;
    }

    function renderAnalysis() {
        if (!app.analysisOn) return;
        var pa = currentAnalysis();
        var rate = pa ? C.Analyzer.whiteWinRate(pa) : 50;
        var cp = pa ? C.Analyzer.whiteCp(pa) : 0;
        setText('anRate', Math.round(rate) + '%');
        setText('anEval', pa ? C.AnalysisMath.formatScore(cp) : '--');
        $('anEval').style.color = cp > 30 ? '#3C6253' : (cp < -30 ? '#C25B4E' : '#8E8B84');
        var pending = app.analysisQueue.length + (app.analysisRunning ? 1 : 0);
        setText('anCap', '全局胜率变化' + (pending > 0 ? ' · 分析中 ' + pending : ''));

        // 每一手的评价（全部列出来，最新的在最后，列表自动滚到底部）
        var n = app.annotations.length, html = '';
        for (var i = 0; i < n; i++) {
            var ma = app.annotations[i];
            var san = app.game.sanHistory[i] || '';
            html += '<div class="g"><span class="no">' + (Math.floor(i / 2) + 1) + (i % 2 === 0 ? '.' : '…') + '</span>';
            html += '<span class="san">' + san + '</span>';
            if (!ma || !ma.ready) {
                html += '<span class="sym" style="color:#8E8B84">…</span><span class="loss">分析中</span>';
            } else {
                html += '<span class="sym" style="color:' + symColor(ma.grade.symbol) + '">' + ma.grade.symbol + '</span>';
                html += '<span class="loss">' + (ma.lossCp <= 5 ? '最佳' : '-' + (ma.lossCp / 100).toFixed(2)) + '</span>';
            }
            html += '</div>';
        }
        var gbox = $('grades');
        gbox.innerHTML = html || '<div class="g"><span class="loss">走一步棋后这里会显示评价</span></div>';
        gbox.scrollTop = gbox.scrollHeight;

        drawChart();
    }

    function renderLegend() {
        var th = ['唯一好棋', '≤0.15', '≤0.50', '≤1.20', '≤3.00', '>3.00'];
        // 表头行：列宽与数据行一致，含义与桌面版相同
        var html = '<div class="lg lg-head"><span class="sym">符号</span><span class="en">English</span>' +
            '<span class="cn">中文</span><span class="th">损失(兵)</span></div>';
        for (var i = 0; i < C.Annotations.All.length; i++) {
            var a = C.Annotations.All[i];
            html += '<div class="lg"><span class="sym" style="color:' + symColor(a.symbol) + '">' + a.symbol + '</span>' +
                '<span class="en">' + a.english + '</span><span class="cn">' + a.chinese + '</span>' +
                '<span class="th">' + th[i] + '</span></div>';
        }
        $('legend').innerHTML = html;
    }

    // ---------------- 胜率曲线 ----------------
    var chartSize = { w: 0, h: 0 };
    function layoutChart() {
        if (!chart) return;
        var w = chart.clientWidth || 260, h = chart.clientHeight || 84;
        var dpr = window.devicePixelRatio || 1;
        chart.width = Math.round(w * dpr);
        chart.height = Math.round(h * dpr);
        cctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        chartSize = { w: w, h: h };
        drawChart();
    }

    function drawChart() {
        if (!app.analysisOn || chartSize.w === 0) return;
        var w = chartSize.w, h = chartSize.h;
        cctx.clearRect(0, 0, w, h);
        cctx.fillStyle = '#FAF9F6';
        cctx.fillRect(0, 0, w, h);
        cctx.strokeStyle = '#E4E0D8';
        cctx.lineWidth = 1;
        cctx.strokeRect(0.5, 0.5, w - 1, h - 1);

        var midY = h / 2;
        cctx.strokeStyle = '#D8D4CB';
        cctx.setLineDash([4, 4]);
        cctx.beginPath(); cctx.moveTo(1, midY); cctx.lineTo(w - 1, midY); cctx.stroke();
        cctx.setLineDash([]);

        var last = 0;
        for (var i = 0; i < app.analyses.length; i++) { if (!app.analyses[i]) break; last = i; }
        if (last === 0 && !app.analyses[0]) {
            cctx.fillStyle = '#8E8B84';
            cctx.font = '12px system-ui, sans-serif';
            cctx.textAlign = 'center'; cctx.textBaseline = 'middle';
            cctx.fillText('分析中…', w / 2, h / 2);
            return;
        }
        var span = Math.max(12, last);
        var pts = [];
        for (var k = 0; k <= last; k++) {
            if (!app.analyses[k]) break;
            var px = 2 + (w - 4) * (k / span);
            var py = h - 2 - (h - 4) * (C.Analyzer.whiteWinRate(app.analyses[k]) / 100);
            pts.push([px, py]);
        }
        // 曲线与 50% 线之间的填充
        for (var s = 0; s + 1 < pts.length; s++) {
            fillSeg(pts[s], pts[s + 1], midY);
        }
        cctx.strokeStyle = '#4E7C6A';
        cctx.lineWidth = 1.8;
        cctx.beginPath();
        cctx.moveTo(pts[0][0], pts[0][1]);
        for (var p = 1; p < pts.length; p++) cctx.lineTo(pts[p][0], pts[p][1]);
        cctx.stroke();
        var lp = pts[pts.length - 1];
        cctx.fillStyle = '#3C6253';
        cctx.beginPath(); cctx.arc(lp[0], lp[1], 3.2, 0, Math.PI * 2); cctx.fill();
    }

    function fillSeg(a, b, midY) {
        function quad(p1, p2, above) {
            cctx.fillStyle = above ? 'rgba(78,124,106,0.28)' : 'rgba(194,91,78,0.22)';
            cctx.beginPath();
            cctx.moveTo(p1[0], p1[1]); cctx.lineTo(p2[0], p2[1]);
            cctx.lineTo(p2[0], midY); cctx.lineTo(p1[0], midY);
            cctx.closePath(); cctx.fill();
        }
        var aAbove = a[1] <= midY, bAbove = b[1] <= midY;
        if (aAbove === bAbove) { quad(a, b, aAbove); return; }
        var t = (midY - a[1]) / (b[1] - a[1]);
        var cross = [a[0] + t * (b[0] - a[0]), midY];
        if (aAbove) { quad(a, cross, true); quad(cross, b, false); }
        else { quad(a, cross, false); quad(cross, b, true); }
    }

    // ---------------- 升变 ----------------
    function askPromotion(color) {
        return new Promise(function (resolve) {
            var row = $('promoRow');
            row.innerHTML = '';
            var types = [C.PT.Queen, C.PT.Rook, C.PT.Bishop, C.PT.Knight];
            types.forEach(function (t) {
                var btn = document.createElement('button');
                var cv = document.createElement('canvas');
                cv.width = 116; cv.height = 116;
                var c2 = cv.getContext('2d');
                c2.drawImage(sheet, (t - 1) * 128, (color === WHITE ? 0 : 1) * 128, 128, 128, 6, 6, 104, 104);
                btn.appendChild(cv);
                btn.onclick = function () {
                    $('promo').classList.remove('on');
                    resolve(t);
                };
                row.appendChild(btn);
            });
            $('promo').classList.add('on');
        });
    }

    // ---------------- 界面事件 ----------------
    function hintText() {
        return app.humanSide === WHITE
            ? '点击任意档位开始对局 · 你执白先行'
            : '点击任意档位开始对局 · 你执黑后行';
    }

    /** 难度页选择执白 / 执黑（人机对战用；人人对战忽略）。 */
    function setHumanSide(side) {
        app.humanSide = (side === BLACK) ? BLACK : WHITE;
        if ($('sideWhite')) $('sideWhite').className = 'btn side-btn' + (app.humanSide === WHITE ? ' on' : '');
        if ($('sideBlack')) $('sideBlack').className = 'btn side-btn' + (app.humanSide === BLACK ? ' on' : '');
        if ($('diffHint')) setText('diffHint', hintText());
    }

    function buildDifficulty() {
        var tiles = $('tiles');
        tiles.innerHTML = '';
        C.SkillTable.levels.forEach(function (lv) {
            var d = document.createElement('div');
            d.className = 'tile';
            d.innerHTML = '<div class="tile-inner"><div class="elo">' + lv.elo +
                '</div><div class="tier">' + lv.tier + '</div></div>';
            d.addEventListener('click', function () {
                setText('diffHint', 'ELO ' + lv.elo + ' · ' + lv.tier + ' — ' + lv.desc);
                startGame('ai', lv);
            });
            tiles.appendChild(d);
        });
        setHumanSide(app.humanSide);
    }

    function bind() {
        $('btnAiPlay').onclick = function () { showScreen('difficulty'); };
        $('btnPvpPlay').onclick = function () { app.analysisOn = false; startGame('pvp', null); };
        $('btnDiffBack').onclick = function () { showScreen('home'); };
        $('btnBack').onclick = function () { showScreen('home'); };
        if ($('sideWhite')) $('sideWhite').onclick = function () { setHumanSide(WHITE); };
        if ($('sideBlack')) $('sideBlack').onclick = function () { setHumanSide(BLACK); };
        $('btnNew').onclick = function () {
            app.game = new C.ChessGame();
            app.lastMove = 0; app.selected = -1; app.targets = [];
            app.analyses = []; app.annotations = []; app.analysisQueue = []; app.analysisGen++;
            updateHud(); renderMoves(); renderAnalysis(); drawBoard();
            if (app.analysisOn) requestAnalysis(0);
            // 执黑时新局同样由电脑先走
            if (app.mode === 'ai' && app.game.position.side !== app.humanSide) startAiTurn();
        };
        $('btnControl').onclick = function () {
            app.showControl = !app.showControl;
            var cb = $('btnControl');
            cb.textContent = app.showControl ? '控制格 · 开' : '控制格 · 关';
            cb.className = 'btn' + (app.showControl ? ' on' : '');
            drawBoard();
        };
        $('btnUndo').onclick = function () {
            if (app.aiThinking || app.game.plyCount() === 0) return;
            var n = (app.mode === 'ai') ? 2 : 1;
            if (n > app.game.plyCount()) n = app.game.plyCount();
            app.game.undoPlies(n);
            if (app.mode === 'ai' && app.game.plyCount() > 0 && app.game.position.side !== app.humanSide) app.game.undoLast();
            var keep = app.game.plyCount();
            app.analyses = app.analyses.slice(0, keep + 1);
            app.annotations = app.annotations.slice(0, keep);
            app.analysisQueue = [];
            app.analysisGen++;
            app.lastMove = keep > 0 ? app.game.moveHistory[keep - 1] : 0;
            app.selected = -1; app.targets = [];
            updateHud(); renderMoves(); renderAnalysis(); drawBoard();
            if (app.analysisOn) requestAnalysis(keep);
            // 执黑时悔棋回到开局，该轮到电脑走，补上触发以免卡住
            if (app.mode === 'ai' && app.game.position.side !== app.humanSide) startAiTurn();
        };
        $('btnAnalysis').onclick = function () {
            app.analysisOn = !app.analysisOn;
            var btn = $('btnAnalysis');
            btn.textContent = app.analysisOn ? 'AI 解析 · 开' : 'AI 解析 · 关';
            btn.classList.toggle('on', app.analysisOn);
            $('analysis').classList.toggle('on', app.analysisOn);
            if (app.analysisOn) {
                renderLegend();
                requestAnalysis(0);
                var cur = app.game.plyCount();
                for (var i = 1; i <= cur; i++) requestAnalysis(i);
                renderAnalysis();
                layoutChart();
                renderMoves();
            } else {
                renderMoves();
            }
            resizeAll();
        };
        window.addEventListener('resize', function () {
            setTimeout(function () { resizeAll(); layoutChart(); }, 60);
        });
        window.addEventListener('orientationchange', function () {
            setTimeout(function () { resizeAll(); layoutChart(); }, 200);
            setTimeout(function () { resizeAll(); layoutChart(); }, 600);
        });
        ['(orientation: landscape)', '(orientation: portrait)'].forEach(function (qy) {
            var mq = window.matchMedia ? window.matchMedia(qy) : null;
            if (!mq) return;
            var handler = function () { setTimeout(function () { resizeAll(); layoutChart(); }, 120); };
            if (mq.addEventListener) mq.addEventListener('change', handler);
            else if (mq.addListener) mq.addListener(handler);
        });
    }

    // ---------------- 启动 ----------------
    /*
     * 调试入口（供自动化截图/回归使用，正常使用不会触发）：
     *   index.html?demo=difficulty          直接打开难度页
     *   index.html?demo=game&elo=1200       直接开一局人机（可带 moves=e4,e5,... 预走若干步）
     *   index.html?demo=game&analysis=1     同时打开 AI 解析
     *   index.html?demo=pvp                 人人对战
     */
    function runDemo() {
        var q = {};
        location.search.replace(/^\?/, '').split('&').forEach(function (kv) {
            if (!kv) return;
            var p = kv.split('=');
            q[decodeURIComponent(p[0])] = decodeURIComponent(p[1] || '');
        });
        if (!q.demo) return false;
        window.__dbg = (q.dbg === '1');
        // ?w=393&h=851：把界面放进一个模拟手机尺寸的框里
        // （无头浏览器窗口有最小宽度限制，无法把窗口直接设成手机宽度）
        if (q.w) {
            var fw = parseInt(q.w, 10), fh = parseInt(q.h, 10) || Math.round(fw * 2.16);
            document.body.style.display = 'flex';
            document.body.style.alignItems = 'center';
            document.body.style.justifyContent = 'center';
            document.body.style.background = '#DCDAD4';
            var box = document.getElementById('app');
            box.style.position = 'relative';
            box.style.top = 'auto'; box.style.left = 'auto'; box.style.right = 'auto'; box.style.bottom = 'auto';
            box.style.width = fw + 'px';
            box.style.height = fh + 'px';
            box.style.flex = '0 0 auto';
            box.style.borderRadius = '10px';
            box.style.overflow = 'hidden';
            box.style.boxShadow = '0 4px 18px rgba(0,0,0,.16)';
        }
        if (q.demo === 'difficulty') { showScreen('difficulty'); return true; }
        if (q.demo === 'study') {
            // 截图/自检用：打开定式列表或某个开局，并可先走若干步
            var ops = (window.ChessStudy && window.ChessStudy.openings) || [];
            var pick = null;
            for (var i = 0; i < ops.length; i++) if (ops[i].id === (q.opening || 'italian')) pick = ops[i];
            window.ChessStudy.openList();
            if (pick && q.list !== '1') window.ChessStudy.openStudy(pick);
            if (q.guide === '1') window.ChessStudy.toggleGuide(true);
            if (q.dump === '1') {
                var s = window.ChessStudy.current();
                document.title = s ? ('opp=' + s.oppSan + '|my=' + s.mySan + '|why=' + s.why.length +
                    '|prompt=' + s.prompt) : 'none';
            }
            var steps = parseInt(q.steps || '0', 10) || 0;
            for (var k = 0; k < steps; k++) {
                var s = window.ChessStudy.current();
                if (!s) break;
                if (s.isStudentTurn()) window.ChessStudy.playCandidate(0);
                if (!s.isStudentTurn()) { s.playOpponent(); window.ChessStudy.refresh(); }
            }
            return true;
        }
        if (q.demo === 'review') {
            var rg = new C.ChessGame();
            var rline = ['e4', 'e5', 'Qh5', 'Nc6', 'Qxf7+', 'Kxf7'];
            for (var ri = 0; ri < rline.length; ri++) {
                var rl = rg.legalMoves(), rmv = 0;
                for (var rk = 0; rk < rl.length; rk++) if (rg.position.san(rl[rk]) === rline[ri]) { rmv = rl[rk]; break; }
                if (!rmv) break;
                rg.play(rmv);
            }
            app.mode = 'ai'; app.humanSide = WHITE;
            startReview(rg, 'ai', WHITE);
            if (q.analysis === '1' || q.control === '1') {
                setTimeout(function () {
                    if (q.analysis === '1' && rvEl('btnReviewAnalysis')) rvEl('btnReviewAnalysis').click();
                    if (q.control === '1' && rvEl('btnReviewControl')) rvEl('btnReviewControl').click();
                }, 300);
            }
            return true;
        }
        if (q.demo === 'home') return true;
        if (q.demo === 'difficulty-black') { showScreen('difficulty'); setHumanSide(BLACK); return true; }
        if (q.demo !== 'game' && q.demo !== 'pvp') return false;
        var mode = (q.demo === 'pvp') ? 'pvp' : 'ai';
        if (q.side === 'b') setHumanSide(BLACK);
        startGame(mode, C.SkillTable.forElo(parseInt(q.elo, 10) || 1200));
        if (mode === 'ai' && q.analysis === '1') $('btnAnalysis').click();
        if (q.control === '1') $('btnControl').click();      // 截图用：直接打开控制格
        var moves = (q.moves || '').split(',').filter(function (s) { return s.length > 0; });
        moves.forEach(function (san) {
            if (app.aiThinking) return;
            var legal = app.game.legalMoves();
            for (var i = 0; i < legal.length; i++) {
                if (app.game.position.san(legal[i]) === san) { applyMove(legal[i]); return; }
            }
        });
        return true;
    }

    // ---------------- iOS：提示"添加到主屏幕" ----------------
    // 在 iPhone/iPad 的 Safari 里打开时给一次提示；已经添加到主屏幕（独立运行）就不再显示。
    function showIosHintIfNeeded() {
        var el = $('iosHint');
        if (!el) return;
        var ua = navigator.userAgent || '';
        var isIOS = /iPad|iPhone|iPod/.test(ua) ||
            (ua.indexOf('Macintosh') >= 0 && 'ontouchend' in document);   // iPadOS 13+ 伪装成 Mac
        var standalone = (window.navigator.standalone === true) ||
            (window.matchMedia && window.matchMedia('(display-mode: standalone)').matches);
        if (isIOS && !standalone) el.className = 'ios-hint on';
    }

    function init() {
        sheet.onload = function () { drawBoard(); };
        sheet.src = PIECE_SHEET;
        // 主页装饰：把"马"画进一个 canvas
        var logo = $('logo');
        var lc = document.createElement('canvas');
        lc.width = 168; lc.height = 168; lc.style.width = '84px'; lc.style.height = '84px';
        lc.style.opacity = '0.55';
        logo.appendChild(lc);
        var lctx = lc.getContext('2d');
        var draw = function () { lctx.drawImage(sheet, 1 * 128, 0, 128, 128, 0, 0, 168, 168); };
        if (sheet.complete) draw(); else sheet.onload = function () { draw(); drawBoard(); };
        buildDifficulty();
        bind();
        if (window.ChessStudy) window.ChessStudy.init();
        bindReview();
        showScreen('home');
        showIosHintIfNeeded();
        runDemo();
        window.addEventListener('load', function () {
            resizeAll(); layoutChart();
            setTimeout(function () { resizeAll(); layoutChart(); }, 60);
            setTimeout(function () { resizeAll(); layoutChart(); }, 350);
            setTimeout(function () { resizeAll(); layoutChart(); }, 900);
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();

    // Android 返回键：先关弹窗 → 再回主页 → 都没有才退出应用
    window.__onAndroidBack = function () {
        var promo = document.getElementById('promo');
        if (promo && promo.className.indexOf('on') >= 0) {
            promo.className = 'promo';
            return 'handled';
        }
        if (app.screen === 'game' || app.screen === 'difficulty') {
            showScreen('home');
            return 'handled';
        }
        return 'exit';
    };


    // ---------------- 复盘（网页版） ----------------
    var rv = { review: null, lesson: null, timer: null, canvas: null, ctx: null, size: 0, cell: 0,
               building: false, done: 0, total: 0, selected: -1, lastMove: 0, controlOn: false, analysisOn: false };

    function rvEl(id) { return document.getElementById(id); }

    function startReview(game, mode, humanSide) {
        if (!game || !game.moveHistory || game.moveHistory.length === 0) return;
        rv.review = null; rv.lesson = null; rv.building = true; rv.done = 0;
        rv.total = game.moveHistory.length; rv.selected = -1; rv.lastMove = 0;
        rv.controlOn = false; rv.analysisOn = false;
        setText('btnReviewControl', '控制格 · 关');
        setText('btnReviewAnalysis', 'AI 解析 · 关');
        rvEl('btnReviewControl').className = 'btn small';
        rvEl('btnReviewAnalysis').className = 'btn small';
        rvEl('rvStrip').style.display = 'none';
        showScreen('review');
        layoutReviewBoard();
        renderReview();
        setTimeout(function () {
            try {
                rv.review = ChessReview.build(game, mode, humanSide, function (d, t) {
                    rv.done = d; rv.total = t; renderReview();
                });
            } catch (e) { rv.review = null; }
            rv.building = false;
            if (rv.review) {
                rv.lesson = new ChessReview.Lesson(rv.review);
                syncReviewBoard(); renderReview(); reviewAuto();
            } else renderReview();
        }, 40);
    }

    function stopReviewTimer() { if (rv.timer) { clearTimeout(rv.timer); rv.timer = null; } }

    function reviewAuto() {
        stopReviewTimer();
        if (!rv.lesson || rv.lesson.mode !== 'auto') { renderReview(); return; }
        rv.timer = setTimeout(function () {
            rv.timer = null;
            if (!rv.lesson || rv.lesson.mode !== 'auto') return;
            var go = rv.lesson.advance();
            syncReviewBoard(); renderReview();
            if (go && rv.lesson.mode === 'auto') reviewAuto();
        }, ChessReview.Speed);
    }

    function rvGame() {
        if (!rv.review || !rv.lesson) return null;
        var g = rv.review.gameAt(rv.lesson.displayPly());
        if (rv.lesson.mode === 'correct' && rv.lesson.quizMove) g.play(rv.lesson.quizMove);
        return g;
    }

    function syncReviewBoard() {
        if (!rvGame()) return;
        var L = rv.lesson;
        rv.lastMove = (L.mode === 'correct' && L.quizMove) ? L.quizMove
            : (L.displayPly() > 0 ? rv.review.moves[L.displayPly() - 1] : 0);
        rv.selected = -1;
        paintReviewBoard();
    }

    function layoutReviewBoard() {
        if (!rv.canvas) {
            rv.canvas = rvEl('reviewBoard');
            if (!rv.canvas) return;
            rv.ctx = rv.canvas.getContext('2d');
            bindReviewCanvas();
        }
        var area = document.querySelector('#screen-review .board-area');
        if (!area) return;
        var size = Math.max(180, Math.min(area.clientWidth - 12, viewportWidth() - 12));
        var maxH = Math.max(180, Math.round((window.innerHeight || 0) * 0.46));
        if (size > maxH) size = maxH;
        rv.size = Math.floor(size); rv.cell = rv.size / 8;
        var dpr = window.devicePixelRatio || 1;
        rv.canvas.width = Math.round(rv.size * dpr);
        rv.canvas.height = Math.round(rv.size * dpr);
        rv.canvas.style.width = rv.size + 'px';
        rv.canvas.style.height = rv.size + 'px';
        rv.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    }

    function rvRect(sq, whiteBottom) {
        var f = C.fileOf(sq), r = C.rankOf(sq);
        return { x: (whiteBottom ? f : 7 - f) * rv.cell, y: (whiteBottom ? 7 - r : r) * rv.cell,
                 w: rv.cell, h: rv.cell };
    }

    function paintReviewBoard() {
        var g = rvGame();
        if (!g || !rv.ctx || rv.size === 0) return;
        var ctx = rv.ctx, b = g.position;
        var whiteBottom = app.mode === 'ai' ? (app.humanSide === WHITE) : (b.side === WHITE);
        ctx.clearRect(0, 0, rv.size, rv.size);
        var wm = 0, bm = 0;
        if (rv.controlOn) { wm = b.controlledSquares(WHITE); bm = b.controlledSquares(BLACK); }
        for (var r = 0; r < 8; r++) {
            for (var f = 0; f < 8; f++) {
                var rc = rvRect(C.sqIndex(f, r), whiteBottom);
                ctx.fillStyle = rv.controlOn
                    ? controlColor(C.controlHas(wm, f, r), C.controlHas(bm, f, r))
                    : (((f + r) % 2 !== 0) ? '#F1ECE2' : '#A7B69D');
                ctx.fillRect(rc.x, rc.y, rc.w, rc.h);
            }
        }
        if (rv.lastMove) {
            ctx.fillStyle = 'rgba(78,124,106,0.30)';
            var a = rvRect(C.mvFrom(rv.lastMove), whiteBottom), c = rvRect(C.mvTo(rv.lastMove), whiteBottom);
            ctx.fillRect(a.x, a.y, a.w, a.h); ctx.fillRect(c.x, c.y, c.w, c.h);
        }
        if (rv.selected >= 0) {
            var sr = rvRect(rv.selected, whiteBottom);
            ctx.fillStyle = 'rgba(78,124,106,0.32)';
            ctx.fillRect(sr.x, sr.y, sr.w, sr.h);
        }
        if (ChessUI.sheetReady()) {
            for (var rr = 0; rr < 8; rr++) {
                for (var ff = 0; ff < 8; ff++) {
                    var sq = C.sqIndex(ff, rr), p = b.sq[sq];
                    if (p === 0) continue;
                    var rc2 = rvRect(sq, whiteBottom), pad = rv.cell * 0.06;
                    ChessUI.drawPiece(ctx, C.typeOf(p), C.colorOf(p),
                        rc2.x + pad, rc2.y + pad, rc2.w - pad * 2, rc2.h - pad * 2);
                }
            }
        }
        if (rv.lesson && rv.lesson.mode === 'quiz') {
            ctx.strokeStyle = 'rgba(60,98,83,0.75)';
            ctx.lineWidth = Math.max(2, rv.cell * 0.03);
            ctx.strokeRect(1, 1, rv.size - 2, rv.size - 2);
        }
    }

    function bindReviewCanvas() {
        function tap(cx, cy) {
            if (!rv.lesson || rv.lesson.mode !== 'quiz') return;
            var rect = rv.canvas.getBoundingClientRect();
            var x = cx - rect.left, y = cy - rect.top;
            if (x < 0 || y < 0 || x > rv.size || y > rv.size) return;
            var g0 = rvGame();
            var whiteBottom = app.mode === 'ai' ? (app.humanSide === WHITE) : (g0.position.side === WHITE);
            var f = whiteBottom ? Math.floor(x / rv.cell) : 7 - Math.floor(x / rv.cell);
            var rr = whiteBottom ? 7 - Math.floor(y / rv.cell) : Math.floor(y / rv.cell);
            var sq = C.sqIndex(f, rr);
            if (rv.selected < 0) {
                var mine = false, l0 = g0.legalMoves();
                for (var i = 0; i < l0.length; i++) if (C.mvFrom(l0[i]) === sq) mine = true;
                if (mine) { rv.selected = sq; paintReviewBoard(); }
                return;
            }
            var mv = 0, legal = g0.legalMoves();
            for (var k = 0; k < legal.length; k++)
                if (C.mvFrom(legal[k]) === rv.selected && C.mvTo(legal[k]) === sq) { mv = legal[k]; break; }
            rv.selected = -1;
            if (!mv) { paintReviewBoard(); return; }
            rv.lesson.submit(mv);
            syncReviewBoard(); renderReview();
            if (rv.lesson.mode === 'correct') stopReviewTimer();
        }
        rv.canvas.addEventListener('touchstart', function (ev) {
            if (ev.preventDefault) ev.preventDefault();
            var t = (ev.touches && ev.touches.length) ? ev.touches[0] : ev;
            tap(t.clientX, t.clientY);
        }, false);
        rv.canvas.addEventListener('mousedown', function (ev) { tap(ev.clientX, ev.clientY); }, false);
    }

    function renderReview() {
        if (!rv.canvas) layoutReviewBoard();
        var L = rv.lesson, box = rvEl('reviewLog');
        if (rv.building) {
            setText('rvProgress', '正在准备复盘… ' + Math.min(rv.done, rv.total) + ' / ' + rv.total);
            if (box) box.innerHTML = '<div class="ln hint">正在给这盘棋逐步打分…</div>';
            return;
        }
        if (!L) { setText('rvProgress', '复盘准备失败'); return; }

        var sub = L.progressText();
        if (L.mode === 'quiz') sub += ' · 该你走了';
        else if (L.mode === 'key' || L.mode === 'correct') sub += ' · 点「继续」听讲解';
        else if (L.mode === 'done') sub += ' · 已讲完';
        setText('rvProgress', sub);

        var strip = rvEl('rvStrip');
        if (rv.analysisOn) {
            var s = L.currentStep();
            if (s) {
                var rate = Math.round(C.AnalysisMath.winRate(s.evalCp));
                strip.style.display = 'flex';
                strip.innerHTML = '<span>白方胜率 <b>' + rate + '%</b></span><span>上一步 ' + s.san +
                    (s.grade ? ' · ' + s.grade.symbol + s.grade.chinese : '') + '</span>';
            }
        } else strip.style.display = 'none';

        if (box) {
            var wait = (L.mode === 'key' || L.mode === 'correct');
            var html = '';
            for (var i = 0; i < L.log.length; i++) {
                var cls = 'ln';
                if (i === L.log.length - 1 && wait) cls += ' last';
                if (L.log[i].indexOf('给个提示') === 0) cls += ' hint';
                html += '<div class="' + cls + '">' +
                    String(L.log[i]).replace(/[&<>]/g, function (c) {
                        return c === '&' ? '&amp;' : (c === '<' ? '&lt;' : '&gt;');
                    }) + '</div>';
            }
            box.innerHTML = html;
            box.scrollTop = box.scrollHeight;
        }

        var btn = rvEl('btnReviewContinue');
        btn.disabled = !(L.mode === 'auto' || L.mode === 'key' || L.mode === 'correct' || L.mode === 'done');
        btn.textContent = L.mode === 'done' ? '再来一遍'
            : (L.mode === 'correct' ? '继续讲解 ▶' : (L.mode === 'key' ? '继续 ▶' : '下一步 ▶'));
        rvEl('btnReviewPrev').disabled = !L.canStepBack();
        rvEl('btnReviewNext').disabled = !L.canJump(1);
        paintReviewBoard();
    }

    function showReviewButton() {
        var r = $('btnReview'), u = $('btnUndo');
        if (r) r.style.display = '';
        if (u) u.style.display = 'none';
    }

    function hideReviewButton() {
        var r = $('btnReview'), u = $('btnUndo');
        if (r) r.style.display = 'none';
        if (u) u.style.display = '';
    }

    function jumpReview(dir) {
        var L = rv.lesson;
        if (!L) return;
        stopReviewTimer();
        if (!L.jump(dir)) return;
        syncReviewBoard(); renderReview();
    }

    function bindReview() {
        if (!rvEl('btnReview')) return;
        rvEl('btnReview').onclick = function () {
            if (!app.game || !app.game.isOver()) return;
            startReview(app.game, app.mode === 'pvp' ? 'pvp' : 'ai', app.humanSide);
        };
        rvEl('btnReviewBack').onclick = function () { stopReviewTimer(); showScreen('game'); };
        rvEl('btnReviewRestart').onclick = function () {
            stopReviewTimer();
            if (!rv.review) return;
            rv.lesson = new ChessReview.Lesson(rv.review);
            syncReviewBoard(); renderReview(); reviewAuto();
        };
        rvEl('btnReviewContinue').onclick = function () {
            var L = rv.lesson;
            if (!L) return;
            if (L.mode === 'done') { rvEl('btnReviewRestart').click(); return; }
            if (L.mode === 'key' || L.mode === 'correct') {
                L.cont(); syncReviewBoard(); renderReview(); reviewAuto(); return;
            }
            if (L.mode === 'auto') {
                L.advance(); syncReviewBoard(); renderReview();
                if (L.mode === 'auto') reviewAuto(); else stopReviewTimer();
            }
        };
        rvEl('btnReviewPrev').onclick = function () {
            var L = rv.lesson;
            if (!L) return;
            stopReviewTimer();
            if (!L.stepBack()) return;
            syncReviewBoard();
            renderReview();
        };
        rvEl('btnReviewNext').onclick = function () { jumpReview(1); };
        rvEl('btnReviewControl').onclick = function () {
            rv.controlOn = !rv.controlOn;
            setText('btnReviewControl', rv.controlOn ? '控制格 · 开' : '控制格 · 关');
            rvEl('btnReviewControl').className = 'btn small' + (rv.controlOn ? ' on' : '');
            paintReviewBoard();
        };
        rvEl('btnReviewAnalysis').onclick = function () {
            rv.analysisOn = !rv.analysisOn;
            setText('btnReviewAnalysis', rv.analysisOn ? 'AI 解析 · 开' : 'AI 解析 · 关');
            rvEl('btnReviewAnalysis').className = 'btn small' + (rv.analysisOn ? ' on' : '');
            renderReview();
        };
        window.addEventListener('resize', function () {
            if (app.screen === 'review') { layoutReviewBoard(); paintReviewBoard(); }
        });
    }

    // 给「定式学习」模块（study.js）用的接口
    window.ChessUI = {
        showScreen: showScreen,
        currentScreen: function () { return app.screen; },
        viewportWidth: viewportWidth,
        sheetReady: function () { return sheet.complete && sheet.naturalWidth > 0; },
        drawPiece: function (g, type, color, x, y, w, h) {
            var sx = (type - 1) * 128, sy = (color === WHITE ? 0 : 1) * 128;
            g.drawImage(sheet, sx, sy, 128, 128, x, y, w, h);
        }
    };

    // 便于调试
    window.__chessApp = app;
})();
