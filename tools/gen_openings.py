# -*- coding: utf-8 -*-
"""把 tools/openings_data.py 里的定式内容生成三份产物（单一来源，避免两端手抄）：

    data/openings.json          规范化的数据（留档、给其它工具用）
    src/Core/OpeningsData.cs    桌面版用的 C# 数据（扁平节点表 + 子节点下标）
    mobile/web/openings.js      网页/手机版用的 JS 数据（同样的结构）

用法： python tools/gen_openings.py
"""
import io
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from openings_data import OPENINGS   # noqa: E402
from openings_more import GUIDES, NOTES, MORE_WHITE, MORE_BLACK   # noqa: E402

# 白方开局排在一起、黑方开局排在一起；每个开局都挂上"讲解"（难度/计划/常见错误）
_WHITE = [o for o in OPENINGS if o["side"] == "w"] + MORE_WHITE
_BLACK = [o for o in OPENINGS if o["side"] == "b"] + MORE_BLACK
ALL = _WHITE + _BLACK
for _op in ALL:
    _g = GUIDES.get(_op["id"])
    if _g is None:
        raise SystemExit("开局 " + _op["id"] + " 缺少讲解（GUIDES）")
    _op["level"] = _g["level"]
    _op["plans"] = _g["plans"]
    _op["pitfalls"] = _g["pitfalls"]


def build_tree(lines, op_id=""):
    """把若干条"完整变化线"合并成一棵树：共同前缀合并。

    变化名挂在这条线**真正分岔出来**的那一手上：先合并整棵树，再回看每条线的路径，
    找到路径上第一个"有兄弟"的节点（也就是别人走别处的那个岔口）——名字挂那里最准确。
    只有一条线的开局没有岔口，名字就标在这条线的第一手上（表示"整条就是这个变化"）。
    """
    root = []
    paths = []
    for line in lines:
        siblings = root
        path = []
        for (san, comment) in line["moves"]:
            node = None
            for n in siblings:
                if n["san"] == san:
                    node = n
                    break
            if node is None:
                node = {"san": san, "comment": comment, "name": "", "note": "", "next": []}
                siblings.append(node)
            path.append(node)
            siblings = node["next"]
        paths.append((line, path))

    # 统计每个节点在同一父节点下的兄弟数
    sibcount = {}

    def count(nodes):
        for n in nodes:
            sibcount[id(n)] = len(nodes)
            count(n["next"])

    count(root)

    for (line, path) in paths:
        vname = line.get("name", "")
        if not vname or not path:
            continue
        # 和"最相近的那条线"（共同前缀最长的）比较，把名字挂在两者分道扬镳的那一手上
        target = None
        best = -1
        for (other, opath) in paths:
            if other is line:
                continue
            depth = 0
            while depth < len(path) and depth < len(opath) and opath[depth] is path[depth]:
                depth += 1
            if depth < len(path) and depth > best:
                best = depth
                target = path[depth]
        if target is None:
            target = path[0]           # 单线开局：名字标在整条线上
        if not target["name"]:
            target["name"] = vname
            target["note"] = NOTES.get((op_id, vname), "")
    return root


def flatten(opening):
    """把一个开局的树压成扁平节点表，返回 (nodes, root_indices)。
    nodes[i] = {"san", "comment", "name", "kids":[下标]}"""
    nodes = []

    def walk(node):
        idx = len(nodes)
        nodes.append({"san": node["san"], "comment": node.get("comment", ""),
                      "name": node.get("name", ""), "note": node.get("note", ""), "kids": []})
        for child in node["next"]:
            nodes[idx]["kids"].append(walk(child))
        return idx

    root = [walk(n) for n in build_tree(opening["lines"], opening["id"])]
    return nodes, root


def cs_str(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


def js_str(s):
    return ("'" + s.replace("\\", "\\\\").replace("'", "\\'") + "'")


def write_cs(openings, path):
    out = io.StringIO()
    out.write("// 本文件由 tools/gen_openings.py 自动生成，请勿手改；"
              "内容来源：tools/openings_data.py\n")
    out.write("using System;\n\n")
    out.write("namespace Chess\n{\n")
    out.write("    /// <summary>定式里的一个着法节点（扁平存储，子节点用下标引用）。</summary>\n")
    out.write("    public sealed class OpeningMove\n    {\n")
    out.write("        public readonly string San;\n")
    out.write("        public readonly string Comment;\n")
    out.write("        public readonly string VariationName;   // 空串表示没有单独命名\n")
    out.write("        public readonly string Note;            // 这条变化的一句话说明\n")
    out.write("        public readonly int[] Children;\n\n")
    out.write("        public OpeningMove(string san, string comment, string variationName,"
              " string note, int[] children)\n")
    out.write("        {\n            San = san; Comment = comment; VariationName = variationName;"
              " Note = note; Children = children;\n        }\n    }\n\n")
    out.write("    /// <summary>一个定式（开局）的完整数据。</summary>\n")
    out.write("    public sealed class Opening\n    {\n")
    out.write("        public readonly string Id;\n        public readonly string Name;\n")
    out.write("        public readonly string English;\n        public readonly string Side;   // \"w\" 教白方 / \"b\" 教黑方\n")
    out.write("        public readonly string Eco;\n        public readonly string Summary;\n")
    out.write("        public readonly string Level;          // 难度定位\n")
    out.write("        public readonly string[] Plans;        // 典型计划\n")
    out.write("        public readonly string[] Pitfalls;     // 常见错误\n")
    out.write("        public readonly int[] Root;\n        public readonly OpeningMove[] Nodes;\n\n")
    out.write("        public Opening(string id, string name, string english, string side, string eco,\n"
              "                       string summary, string level, string[] plans, string[] pitfalls,\n"
              "                       int[] root, OpeningMove[] nodes)\n        {\n")
    out.write("            Id = id; Name = name; English = english; Side = side;"
              " Eco = eco; Summary = summary;\n")
    out.write("            Level = level; Plans = plans; Pitfalls = pitfalls;"
              " Root = root; Nodes = nodes;\n        }\n    }\n\n")
    out.write("    /// <summary>常用开局与主要变化（含逐步点评）。</summary>\n")
    out.write("    public static class OpeningsData\n    {\n")
    out.write("        private static Opening[] _all;\n\n")
    out.write("        public static Opening[] All()\n        {\n")
    out.write("            if (_all != null) return _all;\n")
    out.write("            _all = new Opening[]\n            {\n")
    for op in openings:
        nodes, root = flatten(op)
        out.write("                new Opening(%s, %s, %s, %s, %s, %s, %s,\n"
                  % (cs_str(op["id"]), cs_str(op["name"]), cs_str(op["en"]),
                     cs_str(op["side"]), cs_str(op.get("eco", "")), cs_str(op["summary"]),
                     cs_str(op.get("level", ""))))
        out.write("                    new string[] { " + ", ".join(cs_str(x) for x in op.get("plans", [])) + " },\n")
        out.write("                    new string[] { " + ", ".join(cs_str(x) for x in op.get("pitfalls", [])) + " },\n")
        out.write("                    new int[] { " + ", ".join(str(i) for i in root) + " },\n")
        out.write("                    new OpeningMove[]\n                    {\n")
        for nd in nodes:
            kids = ", ".join(str(i) for i in nd["kids"])
            out.write("                        new OpeningMove(%s, %s, %s, %s, new int[] { %s }),\n"
                      % (cs_str(nd["san"]), cs_str(nd["comment"]), cs_str(nd["name"]),
                         cs_str(nd["note"]), kids))
        out.write("                    }),\n")
    out.write("            };\n            return _all;\n        }\n    }\n}\n")
    with io.open(path, "w", encoding="utf-8", newline="\r\n") as f:
        f.write("\ufeff")            # C# 源文件带 BOM，与其它源码一致
        f.write(out.getvalue())


def write_js(openings, path):
    out = io.StringIO()
    out.write("// 本文件由 tools/gen_openings.py 自动生成，请勿手改；"
              "内容来源：tools/openings_data.py\n")
    out.write("(function (root) {\n")
    out.write("    'use strict';\n\n")
    out.write("    // 定式数据：nodes 为扁平节点表（kids 是子节点下标），root 是第一步的候选下标\n")
    out.write("    var OPENINGS = [\n")
    for op in openings:
        nodes, root = flatten(op)
        out.write("        {\n")
        out.write("            id: %s, name: %s, en: %s, side: %s, eco: %s,\n"
                  % (js_str(op["id"]), js_str(op["name"]), js_str(op["en"]),
                     js_str(op["side"]), js_str(op.get("eco", ""))))
        out.write("            summary: %s,\n" % js_str(op["summary"]))
        out.write("            level: %s,\n" % js_str(op.get("level", "")))
        out.write("            plans: [%s],\n" % ", ".join(js_str(x) for x in op.get("plans", [])))
        out.write("            pitfalls: [%s],\n" % ", ".join(js_str(x) for x in op.get("pitfalls", [])))
        out.write("            root: [%s],\n" % ", ".join(str(i) for i in root))
        out.write("            nodes: [\n")
        for nd in nodes:
            kids = ", ".join(str(i) for i in nd["kids"])
            out.write("                { san: %s, comment: %s, name: %s, note: %s, kids: [%s] },\n"
                      % (js_str(nd["san"]), js_str(nd["comment"]), js_str(nd["name"]),
                         js_str(nd["note"]), kids))
        out.write("            ]\n        },\n")
    out.write("    ];\n\n")
    out.write("    root.ChessOpenings = OPENINGS;\n")
    out.write("})(typeof window !== 'undefined' ? window : (typeof module === 'object' && module.exports ? module.exports : this));\n")
    with io.open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(out.getvalue())


def write_json(openings, path):
    data = {"version": 1, "openings": []}
    for op in openings:
        nodes, root = flatten(op)
        data["openings"].append({
            "id": op["id"], "name": op["name"], "en": op["en"], "side": op["side"],
            "eco": op.get("eco", ""), "summary": op["summary"],
            "level": op.get("level", ""), "plans": op.get("plans", []), "pitfalls": op.get("pitfalls", []),
            "root": root, "nodes": nodes,
        })
    with io.open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(data, ensure_ascii=False, indent=1))


def main() -> int:
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    os.makedirs(os.path.join(root, "data"), exist_ok=True)
    write_cs(ALL, os.path.join(root, "src", "Core", "OpeningsData.cs"))
    write_js(ALL, os.path.join(root, "mobile", "web", "openings.js"))
    write_json(ALL, os.path.join(root, "data", "openings.json"))

    # 统计
    total = 0
    leaves = 0
    branches = 0
    notes = 0
    for op in ALL:
        nodes, _ = flatten(op)
        total += len(nodes)
        leaves += sum(1 for n in nodes if not n["kids"])
        branches += sum(1 for n in nodes if len(n["kids"]) > 1)
        notes += sum(1 for n in nodes if n["note"])
    print("开局 %d 个（白 %d / 黑 %d），节点 %d 个（变化终点 %d，分支点 %d，带说明的变化 %d）"
          % (len(ALL), len(_WHITE), len(_BLACK), total, leaves, branches, notes))
    print("已生成：src/Core/OpeningsData.cs、mobile/web/openings.js、data/openings.json")
    return 0


if __name__ == "__main__":
    sys.exit(main())
