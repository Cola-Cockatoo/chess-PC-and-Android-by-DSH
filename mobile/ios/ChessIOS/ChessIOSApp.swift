import SwiftUI

/// 国际象棋 · iOS 外壳
///
/// 和安卓版同一个思路：界面与引擎都是打包进来的 HTML5（www/index.html），
/// 外面套一个 WKWebView。好处是三个平台的规则、AI、界面完全一致，改一处全都受益。
///
/// 这里刻意不做两件事：
///   1. 不加任何 JS 桥（不做 addJavascriptInterface 那种注入）—— 页面完全自包含，不需要与原生通信；
///   2. 不联网：Info.plist 里禁用了任意加载，页面也只读本地文件。
@main
struct ChessIOSApp: App {
    var body: some Scene {
        WindowGroup {
            ContentView()
        }
    }
}

struct ContentView: View {
    /// 与页面同一个底色，避免启动瞬间闪白/闪黑
    private let bg = Color(red: 0.965, green: 0.957, blue: 0.941)

    var body: some View {
        ZStack {
            bg.ignoresSafeArea()
            // 不加 ignoresSafeArea：WebView 落在安全区内，刘海/灵动岛与底部小白条都不会挡到内容
            WebView()
        }
    }
}
