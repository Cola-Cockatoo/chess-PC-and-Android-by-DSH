import SwiftUI
import WebKit

/// 加载打包在 App 里的国际象棋页面（www/index.html）。
struct WebView: UIViewRepresentable {

    func makeUIView(context: Context) -> WKWebView {
        let config = WKWebViewConfiguration()
        config.allowsInlineMediaPlayback = true
        config.mediaTypesRequiringUserActionForPlayback = []

        let webView = WKWebView(frame: .zero, configuration: config)
        let bg = UIColor(red: 0.965, green: 0.957, blue: 0.941, alpha: 1.0)
        webView.isOpaque = false
        webView.backgroundColor = bg
        webView.scrollView.backgroundColor = bg
        webView.scrollView.bounces = false
        // 页面自己管安全区，别让 WebView 再插一段内边距
        webView.scrollView.contentInsetAdjustmentBehavior = .never
        webView.allowsBackForwardNavigationGestures = false
        webView.allowsLinkPreview = false
        webView.scrollView.showsVerticalScrollIndicator = false
        webView.scrollView.showsHorizontalScrollIndicator = false

        // 只读本地文件：把访问范围限制在 www 目录内
        let bundle = Bundle.main
        var fileURL: URL? = bundle.url(forResource: "index", withExtension: "html", subdirectory: "www")
        if fileURL == nil {
            // 兜底：万一 www 被平铺到了 bundle 根目录
            fileURL = bundle.url(forResource: "index", withExtension: "html")
        }
        if let url = fileURL {
            webView.loadFileURL(url, allowingReadAccessTo: url.deletingLastPathComponent())
        }
        return webView
    }

    func updateUIView(_ uiView: WKWebView, context: Context) {
        // 旋转屏幕由页面的 resize 处理接管，这里不需要做任何事
    }
}
