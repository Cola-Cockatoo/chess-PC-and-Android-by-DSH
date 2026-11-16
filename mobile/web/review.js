/*
 * 复盘（网页 / 手机版）—— 与桌面版同一套逻辑：
 *   · 自动走棋（节奏适中）+ 亲切且不重复的解说
 *   · 关键步：跳过开局定式；亏 ≥0.6 兵 / 一手好棋 / 天平翻面；彼此间隔 ≥4 手、最多 4 个
 *   · 臭棋：退回重下；答对后真的把棋走上去、暂停，等「继续」
 *   · 支持上/下一个关键步跳转
 */
(function (root) {
    'use strict';
    var C = root.Chess;

    // ---------- 逐手打分 ----------
    function build(game, mode, humanSide, progress) {
        var moves = game.moveHistory.slice();
        var steps = [];
        var replay = new C.ChessGame();
        var profile = C.SkillTable.analysisProfile(4, 45);
        var rng = new Rng(20260101);
        var evalBefore = 0;

        for (var i = 0; i < moves.length; i++) {
            var mv = moves[i];
            var side = replay.position.side;
            var pa = C.Analyzer.analyze(replay.position, replay.historyLo, replay.historyHi, profile, rng, new C.Engine());
            var ma = C.Analyzer.judge(pa, mv, side);
            steps.push({
                ply: i, move: mv, san: replay.position.san(mv), whiteMoved: side === C.WHITE,
                lossCp: ma.lossCp || 0, grade: ma.grade || C.Annotations.Good,
                bestMove: pa.bestMove, bestSan: pa.bestMove ? replay.position.san(pa.bestMove) : '',
                evalCp: ma.scoreCp || 0, evalBeforeCp: evalBefore,
                isKey: false, isQuiz: false, kind: ''
            });
            evalBefore = ma.scoreCp || 0;
            replay.play(mv);
            if (progress) progress(i + 1, moves.length);
        }

        var r = { steps: steps, moves: moves, mode: mode, humanSide: humanSide, count: steps.length };
        r.isStudentMove = function (s) { return mode === 'pvp' ? true : s.whiteMoved === (humanSide === C.WHITE); };
        r.gameAt = function (ply) {
            var g = new C.ChessGame();
            for (var k = 0; k < ply && k < moves.length; k++) g.play(moves[k]);
            return g;
        };
        r.keyPlies = function () {
            var out = [];
            for (var k = 0; k < steps.length; k++) if (steps[k].isKey) out.push(k);
            return out;
        };
        r.keyCount = function () { return r.keyPlies().length; };
        r.previousKey = function (before) {
            var ks = r.keyPlies(), found = -1;
            for (var k = 0; k < ks.length; k++) if (ks[k] < before) found = ks[k];
            return found;
        };
        r.nextKey = function (after) {
            var ks = r.keyPlies();
            for (var k = 0; k < ks.length; k++) if (ks[k] > after) return ks[k];
            return -1;
        };
        selectKeys(r);
        return r;
    }

    function selectKeys(r) {
        for (var i = 0; i < r.steps.length; i++) {
            var s = r.steps[i];
            if (!r.isStudentMove(s)) continue;
            if (s.ply < 6) continue;                       // 开局定式
            if (!s.bestSan) continue;                      // 没有"更好"可言
            var bestPlayed = C.mvFrom(s.move) === C.mvFrom(s.bestMove) && C.mvTo(s.move) === C.mvTo(s.bestMove);
            var swing = Math.abs(s.evalCp) >= 150 && Math.abs(s.evalBeforeCp) < 120 &&
                s.evalBeforeCp !== 0 && (s.evalCp > 0) !== (s.evalBeforeCp > 0);

            if (s.lossCp >= 60) {
                s.kind = s.lossCp >= 300 ? 'blunder' : 'mistake';
                s.isKey = true;
            } else if (bestPlayed && s.lossCp <= 15 && s.grade === C.Annotations.Brilliant) {
                s.kind = 'brilliant';
                s.isKey = true;
            } else if (swing) {
                s.kind = 'swing';
                s.isKey = true;
            } else if (bestPlayed && s.lossCp <= 15 && s.ply >= 10 && s.ply <= 40 && s.ply % 7 === 0) {
                s.kind = 'quiet';
                s.isKey = true;
            }
        }

        var keys = [];
        for (var k = 0; k < r.steps.length; k++) if (r.steps[k].isKey) keys.push(r.steps[k]);
        keys.sort(function (a, b) { return weight(b) - weight(a); });

        var chosen = [];
        for (var n = 0; n < keys.length && chosen.length < 4; n++) {
            var close = false;
            for (var m = 0; m < chosen.length; m++) if (Math.abs(chosen[m].ply - keys[n].ply) < 4) close = true;
            if (close) continue;
            chosen.push(keys[n]);
        }
        if (!chosen.length) {
            var worst = null;
            for (var w = 0; w < r.steps.length; w++) {
                var t = r.steps[w];
                if (!r.isStudentMove(t) || !t.bestSan) continue;
                if (!worst || t.lossCp > worst.lossCp) worst = t;
            }
            if (worst) { worst.kind = worst.lossCp >= 300 ? 'blunder' : 'mistake'; chosen.push(worst); }
        }
        for (var a = 0; a < r.steps.length; a++) r.steps[a].isKey = false;
        for (var b2 = 0; b2 < chosen.length; b2++) {
            chosen[b2].isKey = true;
            chosen[b2].isQuiz = chosen[b2].kind === 'blunder' || chosen[b2].lossCp >= 200;
        }
    }

    function weight(s) {
        var w = s.lossCp;
        if (s.kind === 'brilliant') w += 800;
        if (s.kind === 'swing') w += 600;
        if (s.kind === 'blunder') w += 400;
        if (s.kind === 'quiet') w -= 200;
        return w;
    }

    function Rng(seed) { this.s = seed >>> 0 || 1; }
    Rng.prototype.next = function () {
        this.s ^= this.s << 13; this.s >>>= 0;
        this.s ^= this.s >>> 17;
        this.s ^= this.s << 5; this.s >>>= 0;
        return this.s / 4294967296;
    };

    // ---------- 解说（短句、具体；宁可不说话） ----------
    function side(white) { return white ? '白' : '黑'; }
    function paws(cp) { return (Math.abs(cp) / 100).toFixed(1); }
    function pieceName(p) {
        switch (C.typeOf(p)) {
            case C.PT.Pawn: return '兵';
            case C.PT.Knight: return '马';
            case C.PT.Bishop: return '象';
            case C.PT.Rook: return '车';
            case C.PT.Queen: return '后';
            case C.PT.King: return '王';
        }
        return '子';
    }

    var SP = {
        intro: function (r) {
            if (!r.count) return '没有棋谱，复盘不了。';
            var who = r.mode === 'pvp' ? '你们' : '你';
            return r.count + ' 手，一起过一遍。关键的几处我会停下来，' + who + '走坏的地方会退回让你重下。';
        },
        plain: function (r, s) {
            var before = r.gameAt(s.ply), b = before.position;
            var captured = b.sq[C.mvTo(s.move)];
            var isCap = captured !== 0 || (C.mvFlags(s.move) & C.MF.EnPassant);
            var castle = (C.mvFlags(s.move) & C.MF.Castle) !== 0;
            var promo = (C.mvPromo(s.move) || 0) !== 0;
            var type = C.typeOf(b.sq[C.mvFrom(s.move)]);
            var after = r.gameAt(s.ply + 1);
            var check = after.position.inCheck(after.position.side);
            var note;
            if (castle) note = '易位，王先安顿好';
            else if (promo) note = '升变！小兵成后';
            else if (isCap && check) note = '吃子还带将军';
            else if (isCap) note = '吃' + pieceName(captured);
            else if (check) note = '将军';
            else if (type === C.PT.King) note = '王挪一格';
            else if (type === C.PT.Pawn) {
                var f = C.fileOf(C.mvTo(s.move)), rk = C.rankOf(C.mvTo(s.move));
                note = ((f === 3 || f === 4) && (rk === 3 || rk === 4)) ? '抢中心' : '推兵';
            }
            else if (type === C.PT.Queen && s.ply < 12) note = '后早出，当心被追';
            else if (type === C.PT.Knight || type === C.PT.Bishop) note = '出子';
            else note = '重子上场';
            if ((note === '推兵' || note === '出子') && s.ply % 2 === 1) note = '';
            return note ? (s.san + '——' + note) : s.san;
        },
        evalLine: function (r, s) {
            var cp = s.evalCp, rate = Math.round(C.AnalysisMath.winRate(cp));
            if (cp > 29000) return '白方胜势。';
            if (cp < -29000) return '黑方胜势。';
            if (rate >= 70) return '白方舒服（' + rate + '%）。';
            if (rate >= 58) return '白方稍好（' + rate + '%）。';
            if (rate > 42) return '均势（' + rate + '%）。';
            if (rate > 30) return '黑方稍好（' + rate + '%）。';
            return '黑方占上风（' + rate + '%）。';
        },
        deep: function (r, s) {
            var loss = (s.lossCp / 100).toFixed(1);
            if (s.kind === 'brilliant') return '这步 ' + s.san + ' 是这盘里最准的一手——别的走法都会松劲。拿住了。';
            if (s.kind === 'swing') return '局面就是从这儿翻的。' + side(s.whiteMoved) + '方这一手之后，主动换边了。';
            if (s.kind === 'quiet') return s.san + ' 不起眼，但很干净：没送、没乱动。';
            if (s.kind === 'blunder' || s.lossCp >= 200)
                return s.san + ' 亏了 ' + loss + ' 个兵。' + (s.bestSan ? '换 ' + s.bestSan + ' 就还稳。' : '');
            return s.san + ' 差了点，约 ' + loss + ' 个兵；' + (s.bestSan ? s.bestSan + ' 更稳。' : '本可以更稳。');
        },
        quizPrompt: function (r, s) { return '换你来：这个局面该走哪步？'; },
        quizRight: function (r, s, played) { return '对，就是 ' + played + '。比实战的 ' + s.san + ' 稳得多。'; },
        quizWrong: function (r, s, played, lossCp, tries) {
            var head = tries <= 1 ? played + ' 不行' : '还是不行';
            if (lossCp > 0) return head + '——要亏 ' + (lossCp / 100).toFixed(1) + ' 个兵。再试。';
            return head + '——这样会让出主动。再试。';
        },
        hint: function (s) { return s.bestSan ? ('提示：走 ' + s.bestSan + '。') : '提示：先动没发挥作用的子。'; },
        backToGame: function (r, s) { return '实战走的是 ' + s.san + '，接着看。'; },
        jumpForward: function (r, s) { return '→ 第 ' + (s.ply + 1) + ' 手 ' + s.san + '。'; },
        jumpBack: function (r, s) { return '← 第 ' + (s.ply + 1) + ' 手 ' + s.san + '。'; },
        outro: function (r) {
            var bl = 0, mi = 0, br = 0;
            for (var i = 0; i < r.steps.length; i++) {
                var s = r.steps[i];
                if (!r.isStudentMove(s)) continue;
                if (s.kind === 'brilliant') br++;
                else if (s.kind === 'blunder') bl++;
                else if (s.kind === 'mistake') mi++;
            }
            var tail;
            if (br > 0 && bl === 0 && mi === 0) tail = br + ' 步很亮，几乎没送东西。';
            else if (bl > 0) tail = '踩了 ' + bl + ' 个坑，记住就值。' + (br > 0 ? '顺便说，' + br + ' 步好棋也不错。' : '');
            else if (mi > 0) tail = '几处小亏，补上就上一档。';
            else tail = '下得很干净。';
            return '复盘结束。' + tail;
        }
    };

    // ---------- 教学状态机 ----------
    function Lesson(review) {
        this.review = review;
        this.rng = new Rng(20260101);
        this.reset();
    }

    Lesson.prototype.reset = function () {
        this.ply = 0;
        this.mode = this.review.count > 0 ? 'auto' : 'done';
        this.tries = 0;
        this.feedback = '';
        this.tick = 0;
        this.log = [];
        this.logPly = [];
        this.quizMove = 0;
        this.quizSan = '';
        this.say(SP.intro(this.review));
    };

    Lesson.prototype.say = function (line) {
        if (!line) return;
        this.log.push(line);
        this.logPly.push(this.ply);
    };
    Lesson.prototype.waiting = function () {
        return this.mode === 'key' || this.mode === 'quiz' || this.mode === 'correct' || this.mode === 'done';
    };
    Lesson.prototype.currentStep = function () {
        var i = this.mode === 'key' ? this.ply - 1 : this.ply;
        return (i >= 0 && i < this.review.count) ? this.review.steps[i] : null;
    };
    Lesson.prototype.displayPly = function () {
        return this.mode === 'quiz' ? this.ply : Math.min(this.ply, this.review.count);
    };

    Lesson.prototype.advance = function () {
        if (this.mode !== 'auto') return false;
        if (this.ply >= this.review.count) { this.finish(); return false; }
        var s = this.review.steps[this.ply];
        if (s.isKey && s.isQuiz) {
            this.mode = 'quiz'; this.tries = 0; this.feedback = '';
            this.quizMove = 0; this.quizSan = '';
            this.say(SP.quizPrompt(this.review, s));
            return false;
        }
        this.ply++;
        this.say(SP.plain(this.review, s));
        this.tick++;
        if (this.tick % 4 === 0) this.say(SP.evalLine(this.review, s));
        if (s.isKey) {
            this.mode = 'key'; this.feedback = '';
            this.say(SP.deep(this.review, s));
            return false;
        }
        return true;
    };

    Lesson.prototype.cont = function () {
        if (this.mode === 'key') { this.mode = 'auto'; this.feedback = ''; return; }
        if (this.mode === 'correct') {
            var s = this.review.steps[this.ply - 1];
            this.quizMove = 0; this.quizSan = '';
            this.mode = 'auto'; this.feedback = '';
            this.say(SP.backToGame(this.review, s));
            this.tick = 0;
        }
    };

    Lesson.prototype.submit = function (move) {
        if (this.mode !== 'quiz') return { verdict: 'ignored', message: '' };
        var s = this.review.steps[this.ply];
        var pre = this.review.gameAt(this.ply);
        var pa = C.Analyzer.analyze(pre.position, pre.historyLo, pre.historyHi,
            C.SkillTable.analysisProfile(4, 45), this.rng, new C.Engine());
        var ma = C.Analyzer.judge(pa, move, pre.position.side);
        this.tries++;
        var played = pre.position.san(move);
        var exact = C.mvFrom(s.bestMove) === C.mvFrom(move) && C.mvTo(s.bestMove) === C.mvTo(move);
        var correct = exact || (ma.lossCp || 0) <= 50;

        if (correct) {
            this.quizMove = move;
            this.quizSan = played;
            this.ply++;
            this.mode = 'correct';
            this.feedback = '对了！';
            var ok = SP.quizRight(this.review, s, played);
            this.say(ok);
            return { verdict: 'correct', message: ok };
        }
        var bad = SP.quizWrong(this.review, s, played, ma.lossCp || 0, this.tries);
        this.feedback = '再想想';
        this.say(bad);
        if (this.tries >= 2) { this.say(SP.hint(s)); this.feedback = '提示已给出'; }
        return { verdict: 'wrong', message: bad };
    };

    /** 回到上一步：把对话逐行还原到那一刻（不追加新句子） */
    Lesson.prototype.stepBack = function () {
        if (this.ply <= 0) return false;
        this.quizMove = 0; this.quizSan = ''; this.tries = 0; this.feedback = '';
        this.ply--;
        this.mode = 'auto';
        this.tick = this.ply;
        var keep = 0;
        while (keep < this.logPly.length && this.logPly[keep] <= this.ply) keep++;
        this.log.length = keep;
        this.logPly.length = keep;
        return true;
    };

    Lesson.prototype.canStepBack = function () { return this.ply > 0; };

    Lesson.prototype.jump = function (dir) {
        var here = this.mode === 'key' ? this.ply - 1 : this.ply;
        var target = dir < 0 ? this.review.previousKey(here) : this.review.nextKey(here);
        if (target < 0) return false;
        var s = this.review.steps[target];
        this.quizMove = 0; this.quizSan = ''; this.tries = 0; this.feedback = '';
        this.ply = s.isQuiz ? target : target + 1;
        this.mode = s.isQuiz ? 'quiz' : 'key';
        this.say(dir < 0 ? SP.jumpBack(this.review, s) : SP.jumpForward(this.review, s));
        this.say(s.isQuiz ? SP.quizPrompt(this.review, s) : SP.deep(this.review, s));
        return true;
    };

    Lesson.prototype.canJump = function (dir) {
        var here = this.mode === 'key' ? this.ply - 1 : this.ply;
        return dir < 0 ? this.review.previousKey(here) >= 0 : this.review.nextKey(here) >= 0;
    };

    Lesson.prototype.finish = function () {
        this.mode = 'done'; this.feedback = '';
        this.say(SP.outro(this.review));
    };

    Lesson.prototype.progressText = function () {
        var seen = 0, total = 0;
        for (var i = 0; i < this.review.steps.length; i++) {
            if (!this.review.steps[i].isKey) continue;
            total++;
            if (i < this.ply) seen++;
        }
        return '第 ' + Math.min(this.ply, this.review.count) + ' / ' + this.review.count +
            ' 手 · 关键步 ' + Math.min(seen, total) + ' / ' + total;
    };

    root.ChessReview = { build: build, Lesson: Lesson, Speed: 1150 };
})(typeof window !== 'undefined' ? window : this);
