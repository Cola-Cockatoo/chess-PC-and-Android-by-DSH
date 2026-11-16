/*
 * 定式学习（网页版 / 手机版）
 *
 * 与桌面版同一份内容数据（由 tools/gen_openings.py 从 tools/openings_data.py 生成），
 * 流程也一样：棋盘上给出箭头 → 学员照着走 → 程序应一手 → 再给箭头；有分支时给多个箭头让学员选。
 * 每走一步都会显示这一步为什么好。
 */
(function (root) {
    'use strict';

    var C = root.Chess;
    var OPENINGS = root.ChessOpenings || [];
    var AI = root.ChessUI;            // ui.js 暴露的接口

    var REPLY_DELAY_MS = 600;

    // ------------------------------------------------------------------
    // 学习会话：维护当前局面、候选着法、点评
    // ------------------------------------------------------------------
    function StudySession(opening) {
        this.opening = opening;
        this.game = new C.ChessGame();
        this.path = [];               // 走过的节点下标
        this.restart();
    }

    StudySession.prototype.restart = function () {
        this.game = new C.ChessGame();
        this.path = [];
        this.next = this.opening.root.slice();
        this.lastNode = -1;
        this.comment = '';
        this.prompt = '';
        this.variation = '';
        this.variationNote = '';
        this.oppSan = '';            // 对手上一步
        this.oppComment = '';
        this.mySan = '';             // 我上一步
        this.myComment = '';
        this.why = [];               // 该我走：每条变化的理由
        this.finished = false;
    };

    StudySession.prototype.studentSide = function () {
        return this.opening.side === 'w' ? C.WHITE : C.BLACK;
    };

    StudySession.prototype.isStudentTurn = function () {
        return this.game.position.side === this.studentSide();
    };

    /** 当前所有可走的候选：{index, san, move, comment, name, branch} */
    StudySession.prototype.candidates = function () {
        var out = [];
        if (!this.next || this.next.length === 0) return out;
        var legal = this.game.legalMoves();
        for (var i = 0; i < this.next.length; i++) {
            var idx = this.next[i];
            var node = this.opening.nodes[idx];
            var mv = null;
            for (var k = 0; k < legal.length; k++) {
                if (this.game.position.san(legal[k]) === node.san) { mv = legal[k]; break; }
            }
            if (mv === null) continue;
            out.push({
                index: idx, san: node.san, move: mv, comment: node.comment,
                name: node.name || '', branch: this.next.length
            });
        }
        return out;
    };

    /** 走一步（按 from/to，棋盘点击用）。返回这一步的信息，失败返回 null。 */
    StudySession.prototype.playMove = function (from, to) {
        var cs = this.candidates();
        for (var i = 0; i < cs.length; i++) {
            if (C.mvFrom(cs[i].move) === from && C.mvTo(cs[i].move) === to) {
                return this.playIndex(cs[i].index);
            }
        }
        return null;
    };

    StudySession.prototype.playIndex = function (idx) {
        var cs = this.candidates();
        var mine = this.isStudentTurn();       // 走之前先看轮到谁
        for (var i = 0; i < cs.length; i++) {
            if (cs[i].index !== idx) continue;
            if (!this.game.play(cs[i].move)) return null;
            this.lastNode = idx;
            this.path.push(idx);
            this.next = this.opening.nodes[idx].kids.slice();
            if (mine) {
                this.mySan = cs[i].san;
                this.myComment = cs[i].comment;
            } else {
                this.oppSan = cs[i].san;
                this.oppComment = cs[i].comment;
            }
            if (cs[i].name) {
                this.variation = cs[i].name;
                this.variationNote = cs[i].note || '';
            }
            return cs[i];
        }
        return null;
    };

    /** 程序应一手（有多个候选时走第一个）。 */
    StudySession.prototype.playOpponent = function () {
        var cs = this.candidates();
        if (cs.length === 0) return null;
        return this.playIndex(cs[0].index);
    };

    /** 走过的着法（SAN）。 */
    StudySession.prototype.playedSans = function () {
        var out = [];
        for (var i = 0; i < this.path.length; i++) out.push(this.opening.nodes[this.path[i]].san);
        return out;
    };

    // ------------------------------------------------------------------
    // 学习界面
    // ------------------------------------------------------------------
    var st = {
        session: null,
        canvas: null,
        ctx: null,
        size: 0,
        cell: 0,
        selected: -1,
        lastMove: 0,
        waiting: false,
        flash: '',
        timer: null
    };

    function el(id) { return document.getElementById(id); }

    function setText(id, s) { var e = el(id); if (e) e.textContent = s; }

    /** 打开定式列表 */
    function openList() {
        stopReply();
        renderList();
        AI.showScreen('openings');
    }

    function renderList() {
        var box = el('openingList');
        if (!box) return;
        var html = '';
        for (var i = 0; i < OPENINGS.length; i++) {
            var o = OPENINGS[i];
            html += '<div class="op-row" data-index="' + i + '">' +
                '<div class="op-no">' + (i + 1) + '</div>' +
                '<div class="op-main"><div class="op-name">' + o.name +
                '<span class="op-side">' + (o.side === 'w' ? '白' : '黑') + '</span></div>' +
                '<div class="op-sub">' + o.en + ' · ' + o.eco +
                (o.level ? ' · ' + o.level : '') + '</div></div>' +
                '<div class="op-arrow">›</div></div>';
        }
        box.innerHTML = html;
        var rows = box.querySelectorAll('.op-row');
        for (var k = 0; k < rows.length; k++) {
            rows[k].onclick = function () {
                var idx = parseInt(this.getAttribute('data-index'), 10);
                openStudy(OPENINGS[idx]);
            };
        }
    }

    /** 进入某个开局的学习 */
    function openStudy(opening) {
        if (!opening) return;
        st.session = new StudySession(opening);
        st.selected = -1;
        st.lastMove = 0;
        st.waiting = false;
        st.flash = '';
        setText('studyTitle', opening.name);
        setText('studySub', (opening.side === 'w' ? '你执白' : '你执黑') + ' · ' + opening.en + ' · ' + opening.eco);
        setText('studySummary', opening.summary);
        st.guideOn = false;
        toggleGuide(false);
        AI.showScreen('study');
        layoutStudyBoard();
        refreshStudy(true);
    }

    /** 渲染"开局讲解"：难度定位、典型计划、常见错误、主要变化 */
    function renderGuide(op) {
        var box = el('studyGuide');
        if (!box || !op) return;
        var html = '<div class="g-level">难度定位：' + op.level + '</div>';
        html += '<div class="g-sec">典型计划</div><ol class="g-list">';
        for (var i = 0; i < op.plans.length; i++) html += '<li>' + op.plans[i] + '</li>';
        html += '</ol><div class="g-sec">常见错误</div><ul class="g-list">';
        for (var k = 0; k < op.pitfalls.length; k++) html += '<li>' + op.pitfalls[k] + '</li>';
        html += '</ul>';
        var vars = [];
        for (var n = 0; n < op.nodes.length; n++) {
            var nd = op.nodes[n];
            if (nd.name && nd.note) vars.push('<li><b>' + nd.name + '</b>：' + nd.note + '</li>');
        }
        if (vars.length) html += '<div class="g-sec">主要变化</div><ul class="g-list">' + vars.join('') + '</ul>';
        box.innerHTML = html;
    }

    function toggleGuide(show) {
        var box = el('studyGuide');
        if (!box) return;
        st.guideOn = (typeof show === 'boolean') ? show : !st.guideOn;
        box.className = 'study-guide' + (st.guideOn ? ' on' : '');
        var btn = el('btnStudyGuide');
        if (btn) btn.textContent = st.guideOn ? '收起讲解' : '开局讲解';
        if (st.guideOn && st.session) renderGuide(st.session.opening);
    }

    function restartStudy() {
        if (!st.session) return;
        stopReply();
        st.session.restart();
        st.selected = -1;
        st.lastMove = 0;
        st.waiting = false;
        st.flash = '';
        refreshStudy(true);
    }

    function stopReply() {
        if (st.timer) { clearTimeout(st.timer); st.timer = null; }
    }

    /** 推进：刷新箭头、点评、着法列表 */
    function refreshStudy(first) {
        var s = st.session;
        if (!s) return;
        s.comment = s.comment || '';
        s.prompt = '';

        if (!s.next || s.next.length === 0) {
            s.finished = true;
            st.waiting = false;
            s.comment = '这条变化走完了。可以点「重来」再走一遍，或回列表选别的开局。' +
                (s.variation ? '（走的是：' + s.variation + '）' : '');
            paintStudy();
            return;
        }
        s.finished = false;
        var cs = s.candidates();
        if (cs.length === 0) {
            s.comment = '这一步之后没有更多教学变化了。';
            paintStudy();
            return;
        }

        if (s.isStudentTurn()) {
            stopReply();                  // 取消挂起的应手计时器，避免多走一步
            st.waiting = false;
            st.hints = [];
            st.allowed = [];
            for (var i = 0; i < cs.length; i++) {
                st.hints.push({ from: C.mvFrom(cs[i].move), to: C.mvTo(cs[i].move) });
                st.allowed.push(cs[i].move);
            }
            s.why = [];                        // 该我走：把每条变化的理由列出来
            for (var w = 0; w < cs.length; w++) {
                s.why.push({ san: cs[w].san, name: cs[w].name || '', comment: cs[w].comment });
            }
            if (cs.length === 1) {
                s.prompt = '该你走：照着箭头走这一步。';
            } else {
                var names = [];
                for (var k = 0; k < cs.length; k++) if (cs[k].name) names.push(cs[k].name);
                s.prompt = '该你选：这里有 ' + cs.length + ' 条变化（箭头就是它们），选一个走。' +
                    (names.length ? '变化名：' + names.join(' / ') : '');
            }
            paintStudy();
        } else {
            st.waiting = true;
            st.hints = [];
            st.allowed = [];
            s.why = [];
            s.prompt = '对手思考中…';
            paintStudy();
            stopReply();
            st.timer = setTimeout(function () {
                st.timer = null;
                var r = s.playOpponent();
                if (r) {
                    st.lastMove = r.move;
                    s.comment = '';        // 同上
                }
                refreshStudy(false);
            }, REPLY_DELAY_MS);
        }
    }

    /** 棋盘点击 */
    function tapStudy(sq) {
        var s = st.session;
        if (!s || st.waiting || s.finished) return;
        var cs = s.candidates();
        // 先看是不是直接点到了某个箭头的终点
        for (var i = 0; i < cs.length; i++) {
            if (C.mvTo(cs[i].move) === sq) {
                var needFrom = C.mvFrom(cs[i].move);
                if (st.selected === needFrom || st.selected < 0) {
                    applyStudy(needFrom, sq);
                    return;
                }
            }
        }
        // 否则：点自己的子选中它（只允许教学着法的起点）
        var froms = {};
        for (var k = 0; k < cs.length; k++) froms[C.mvFrom(cs[k].move)] = true;
        if (froms[sq]) {
            st.selected = sq;
            paintStudy();
        } else {
            st.selected = -1;
            paintStudy();
        }
    }

    function applyStudy(from, to) {
        var s = st.session;
        var step = s.playMove(from, to);
        if (!step) {
            st.flash = '这一步不在教学变化里，请按箭头走。';
            st.selected = -1;
            paintStudy();
            return;
        }
        st.flash = '';
        st.selected = -1;
        st.lastMove = step.move;
        s.comment = '';                    // 点评由三段式面板呈现，这里不再重复
        refreshStudy(false);
    }

    /** 供截图/自检用：照第 index 个箭头走一步 */
    function playCandidate(index) {
        var s = st.session;
        if (!s) return false;
        var cs = s.candidates();
        if (index < 0 || index >= cs.length) return false;
        applyStudy(C.mvFrom(cs[index].move), C.mvTo(cs[index].move));
        return true;
    }

    // ------------------------------------------------------------------
    // 绘制
    // ------------------------------------------------------------------
    function layoutStudyBoard() {
        var wrap = document.querySelector('#screen-study .study-board-wrap');
        var area = document.querySelector('#screen-study .study-board-area');
        if (!wrap || !area || !st.canvas) return;
        var vw = AI.viewportWidth();
        var availW = area.clientWidth - 12;
        var size = Math.max(160, Math.min(availW, vw - 12));
        // 竖屏给下面的说明留出空间，最多占可视高度的 58%
        var vh = window.innerHeight || 0;
        var maxH = Math.max(160, Math.round(vh * 0.58));
        if (size > maxH) size = maxH;
        st.size = Math.floor(size);
        st.cell = st.size / 8;
        var dpr = window.devicePixelRatio || 1;
        st.canvas.width = Math.round(st.size * dpr);
        st.canvas.height = Math.round(st.size * dpr);
        st.canvas.style.width = st.size + 'px';
        st.canvas.style.height = st.size + 'px';
        st.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

        if (/[?&]sdbg=1/.test(location.search)) {
            var app = document.getElementById('app');
            var sc = document.getElementById('screen-study');
            var body = sc ? sc.querySelector('.game-body') : null;
            var side = sc ? sc.querySelector('.side') : null;
            document.title = 'app=' + (app ? app.clientWidth : -1) +
                ' screen=' + (sc ? sc.clientWidth : -1) +
                ' body=' + (body ? body.clientWidth : -1) +
                ' area=' + area.clientWidth +
                ' side=' + (side ? side.clientWidth : -1) +
                ' canvas=' + st.size + ' vw=' + vw;
        }
    }

    function studyRect(sq, whiteBottom) {
        var f = C.fileOf(sq), r = C.rankOf(sq);
        var col = whiteBottom ? f : 7 - f;
        var row = whiteBottom ? 7 - r : r;
        return { x: col * st.cell, y: row * st.cell, w: st.cell, h: st.cell };
    }

    function paintStudy() {
        var s = st.session;
        if (!s || !st.ctx || st.size === 0) return;
        var g = st.ctx;
        var pos = s.game.position;
        var whiteBottom = s.studentSide() === C.WHITE;

        // 「本步点评」：对手上一步有什么用 / 我上一步在做什么 / 我下一步为什么走这里
        renderPanel(s);
        var sans = s.playedSans();
        var mv = '';
        for (var i = 0; i < sans.length; i++) {
            mv += (i % 2 === 0 ? (i / 2 + 1) + '. ' : '') + sans[i] + (i % 2 === 0 ? '  ' : '\n');
        }
        setText('studyMoves', mv || '（还没走）');
        setText('studyVariation', s.variation
            ? '当前变化：' + s.variation + (s.variationNote ? '\n' + s.variationNote : '')
            : '');

        // 棋盘
        g.clearRect(0, 0, st.size, st.size);
        for (var r = 0; r < 8; r++) {
            for (var f = 0; f < 8; f++) {
                var rc = studyRect(C.sqIndex(f, r), whiteBottom);
                g.fillStyle = ((f + r) % 2 !== 0) ? '#F1ECE2' : '#A7B69D';
                g.fillRect(rc.x, rc.y, rc.w, rc.h);
            }
        }
        if (st.lastMove) {
            g.fillStyle = 'rgba(78,124,106,0.18)';
            var a = studyRect(C.mvFrom(st.lastMove), whiteBottom);
            var b2 = studyRect(C.mvTo(st.lastMove), whiteBottom);
            g.fillRect(a.x, a.y, a.w, a.h);
            g.fillRect(b2.x, b2.y, b2.w, b2.h);
        }
        if (st.selected >= 0) {
            var sr = studyRect(st.selected, whiteBottom);
            g.fillStyle = 'rgba(78,124,106,0.32)';
            g.fillRect(sr.x, sr.y, sr.w, sr.h);
        }
        // 棋子
        if (AI.sheetReady()) {
            for (var rr = 0; rr < 8; rr++) {
                for (var ff = 0; ff < 8; ff++) {
                    var sq = C.sqIndex(ff, rr);
                    var p = pos.sq[sq];
                    if (p === 0) continue;
                    var rc2 = studyRect(sq, whiteBottom);
                    var type = C.typeOf(p), color = C.colorOf(p);
                    var pad = st.cell * 0.06;
                    AI.drawPiece(g, type, color, rc2.x + pad, rc2.y + pad, rc2.w - pad * 2, rc2.h - pad * 2);
                }
            }
        }
        // 箭头（学员要走的着法；多个表示可选）
        var hints = st.hints || [];
        for (var h = 0; h < hints.length; h++) {
            var ra = studyRect(hints[h].from, whiteBottom);
            var rb = studyRect(hints[h].to, whiteBottom);
            drawArrow(g, ra.x + ra.w / 2, ra.y + ra.h / 2, rb.x + rb.w / 2, rb.y + rb.h / 2);
        }
    }

    /** 渲染三段式点评（随走棋自动推进） */
    function renderPanel(s) {
        var box = el('studyPanel');
        if (!box) return;
        if (st.flash) {
            box.innerHTML = '<div class="sp-flash">' + st.flash + '</div>';
            return;
        }
        var html = '';
        if (s.comment) html += '<div class="sp-headline">' + s.comment + '</div>';
        if (s.oppSan) {
            html += '<div class="sp-row"><div class="sp-tag opp">对手上一步</div>' +
                '<div class="sp-body"><b>' + s.oppSan + '</b>' +
                (s.oppComment ? '<div class="sp-txt">' + s.oppComment + '</div>' : '') +
                '</div></div>';
        }
        if (s.mySan) {
            html += '<div class="sp-row"><div class="sp-tag me">你上一步</div>' +
                '<div class="sp-body"><b>' + s.mySan + '</b>' +
                (s.myComment ? '<div class="sp-txt">' + s.myComment + '</div>' : '') +
                '</div></div>';
        }
        if (s.why && s.why.length) {
            html += '<div class="sp-why"><div class="sp-head">' +
                (s.why.length === 1 ? '该你走 · 为什么要走这里' : '该你选 · 每条变化为什么这样走') +
                '</div>';
            for (var i = 0; i < s.why.length; i++) {
                html += '<div class="sp-item"><b>' + (s.why.length === 1 ? '· ' : (i + 1) + '. ') +
                    s.why[i].san + '</b>' + (s.why[i].name ? '（' + s.why[i].name + '）' : '') +
                    '<div class="sp-txt">' + s.why[i].comment + '</div></div>';
            }
            html += '</div>';
        }
        if (s.prompt) html += '<div class="sp-prompt">' + s.prompt + '</div>';
        box.innerHTML = html;
    }

    function drawArrow(g, ax, ay, bx, by) {
        var cell = st.cell;
        // 起点圆
        g.fillStyle = 'rgba(78,124,106,0.28)';
        g.beginPath();
        g.arc(ax, ay, cell * 0.42, 0, Math.PI * 2);
        g.fill();
        // 终点圆环
        g.strokeStyle = 'rgba(60,98,83,0.6)';
        g.lineWidth = Math.max(1.5, cell * 0.03);
        g.beginPath();
        g.arc(bx, by, cell * 0.36, 0, Math.PI * 2);
        g.stroke();

        var dx = bx - ax, dy = by - ay;
        var len = Math.sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        var ux = dx / len, uy = dy / len;
        var cut = cell * 0.34;                 // 终点缩进，箭头尖落在目标格中心
        var ex = bx - ux * cut, ey = by - uy * cut;
        var head = Math.max(6, cell * 0.26);

        g.strokeStyle = 'rgba(60,98,83,0.85)';
        g.lineWidth = Math.max(3, cell * 0.12);
        g.lineCap = 'round';
        g.beginPath();
        g.moveTo(ax, ay);
        g.lineTo(ex, ey);
        g.stroke();

        // 箭头（三角）
        var px = -uy, py = ux;
        g.fillStyle = 'rgba(60,98,83,0.85)';
        g.beginPath();
        g.moveTo(ex + ux * head * 0.55, ey + uy * head * 0.55);
        g.lineTo(ex - ux * head * 0.45 + px * head * 0.42, ey - uy * head * 0.45 + py * head * 0.42);
        g.lineTo(ex - ux * head * 0.45 - px * head * 0.42, ey - uy * head * 0.45 - py * head * 0.42);
        g.closePath();
        g.fill();
    }

    function init() {
        st.canvas = el('studyBoard');
        if (st.canvas) st.ctx = st.canvas.getContext('2d');
        el('btnStudy').onclick = openList;
        el('btnStudyBack').onclick = function () { AI.showScreen('home'); };
        el('btnStudyList').onclick = function () { stopReply(); openList(); };
        el('btnStudyRestart').onclick = restartStudy;
        if (el('btnStudyGuide')) el('btnStudyGuide').onclick = function () { toggleGuide(); };
        if (st.canvas) {
            st.canvas.addEventListener('touchstart', function (ev) {
                if (ev.preventDefault) ev.preventDefault();
                var t = (ev.touches && ev.touches.length) ? ev.touches[0] : ev;
                onTap(t.clientX, t.clientY);
            }, false);
            st.canvas.addEventListener('mousedown', function (ev) {
                onTap(ev.clientX, ev.clientY);
            }, false);
        }
        window.addEventListener('resize', function () {
            if (AI.currentScreen() === 'study') { layoutStudyBoard(); paintStudy(); }
        });
    }

    function onTap(clientX, clientY) {
        if (!st.canvas) return;
        var r = st.canvas.getBoundingClientRect();
        var x = clientX - r.left, y = clientY - r.top;
        if (x < 0 || y < 0 || x > st.size || y > st.size) return;
        var s = st.session;
        if (!s) return;
        var whiteBottom = s.studentSide() === C.WHITE;
        var col = Math.floor(x / st.cell), row = Math.floor(y / st.cell);
        var f = whiteBottom ? col : 7 - col;
        var rr = whiteBottom ? 7 - row : row;
        if (f < 0 || f > 7 || rr < 0 || rr > 7) return;
        tapStudy(C.sqIndex(f, rr));
    }

    root.ChessStudy = {
        init: init,
        openList: openList,
        openStudy: openStudy,
        restart: restartStudy,
        playCandidate: playCandidate,
        current: function () { return st.session; },
        hintCount: function () { return (st.hints || []).length; },
        isWaiting: function () { return st.waiting; },
        refresh: function () { refreshStudy(false); },
        paint: function () { layoutStudyBoard(); paintStudy(); },
        toggleGuide: toggleGuide,
        openings: OPENINGS
    };
})(typeof window !== 'undefined' ? window : this);
